using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Pure diff for property write-back (no Unity, harness-testable): locates each baseline record in
    /// the current document, builds effective props (class rules overlaid by inline), diffs vs baseline.
    /// </summary>
    /// <remarks>
    /// Property values only, including interaction states (see ComputeStates). Structure is one-way:
    /// Figma owns it, so a moved or removed element is skipped and the next import restores it.
    /// </remarks>
    static class FigmaPropertyDiff
    {
        // USS props with a lossless Figma setter (left/top -> x/y, flex/padding -> auto-layout, gap -> itemSpacing).
        static readonly HashSet<string> k_Supported = new HashSet<string>(new[]
        {
            "background-color", "color", "border-radius", "width", "height", "font-size",
            "opacity", "display",
            "padding-left", "padding-right", "padding-top", "padding-bottom",
            "flex-direction", "justify-content", "align-items", "flex-grow",
            "flex-wrap", "align-self", // auto-layout wrap / per-child layoutAlign (stretch|auto)
            "gap",                     // auto-layout itemSpacing, on the container itself
            "left", "top",
            "text-shadow", "filter", "backdrop-filter"
        });

        // Figma refuses layout overrides on nodes INSIDE an instance (sublayer ids contain ';'),
        // so these changes are warned, not queued.
        static readonly HashSet<string> k_InstanceLockedLayout = new HashSet<string>(new[]
        {
            "width", "height", "left", "top",
            "padding-left", "padding-right", "padding-top", "padding-bottom",
            "flex-direction", "justify-content", "align-items", "flex-grow",
            "flex-wrap", "align-self", "gap"
        });

        static bool InstanceLocked(string figmaNodeId, string prop)
            => k_InstanceLockedLayout.Contains(prop)
               && figmaNodeId != null && figmaNodeId.IndexOf(';') >= 0;

        /// <summary>
        /// Parses flat USS rules into { className -> { prop -> value } }. All literal props are kept so
        /// unsupported edits can warn; var(--token) values are dropped (token write-back owns them).
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> ParseRules(string uss)
        {
            var rules = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrEmpty(uss))
                return rules;

            uss = StripComments(uss);
            var i = 0;
            while (true)
            {
                var open = uss.IndexOf('{', i);
                if (open < 0)
                    break;
                var close = uss.IndexOf('}', open + 1);
                if (close < 0)
                    break;

                var selector = uss.Substring(i, open - i).Trim();
                var body = uss.Substring(open + 1, close - open - 1);
                i = close + 1;

                if (!selector.StartsWith(".", StringComparison.Ordinal))
                    continue;
                var className = selector.Substring(1).Trim();
                if (className.Length == 0 || className.IndexOf(' ') >= 0 || className.IndexOf(':') >= 0)
                    continue; // plain single-class rules only; ParseStateRules owns the .cls:hover form

                var props = ParseDecls(body);
                if (props.Count > 0)
                    rules[className] = props;
            }
            return rules;
        }

        /// <summary>
        /// Pseudo-class rules keyed "class:pseudo" (e.g. "button:hover"), the counterpart to ParseRules.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> ParseStateRules(string uss)
        {
            var rules = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrEmpty(uss))
                return rules;

            uss = StripComments(uss);
            var i = 0;
            while (true)
            {
                var open = uss.IndexOf('{', i);
                if (open < 0)
                    break;
                var close = uss.IndexOf('}', open + 1);
                if (close < 0)
                    break;

                var selector = uss.Substring(i, open - i).Trim();
                var body = uss.Substring(open + 1, close - open - 1);
                i = close + 1;

                if (!selector.StartsWith(".", StringComparison.Ordinal) || selector.IndexOf(' ') >= 0)
                    continue;
                var colon = selector.IndexOf(':');
                if (colon <= 1)
                    continue;

                // Recorded even when empty (all var()), so swapping a token for a literal reads as an edit.
                var key = selector.Substring(1, colon - 1).Trim() + selector.Substring(colon).Trim();
                rules[key] = ParseDecls(body);
            }
            return rules;
        }

        // A comment would otherwise glue itself to the next selector, or hide a brace and desync the scan.
        static string StripComments(string uss)
        {
            if (uss.IndexOf("/*", StringComparison.Ordinal) < 0)
                return uss;

            var sb = new StringBuilder(uss.Length);
            var i = 0;
            while (i < uss.Length)
            {
                var start = uss.IndexOf("/*", i, StringComparison.Ordinal);
                if (start < 0)
                {
                    sb.Append(uss, i, uss.Length - i);
                    break;
                }
                sb.Append(uss, i, start - i);
                var end = uss.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                    break; // unterminated comment swallows the rest
                i = end + 2;
            }
            return sb.ToString();
        }

        // Keeps ALL literal props; drops var(--token) (token write-back owns them); expands padding/margin
        // shorthands so an imported shorthand diffs cleanly against UI Builder's longhand edits.
        static Dictionary<string, string> ParseDecls(string body)
        {
            var props = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(body))
                return props;
            foreach (var decl in body.Split(';'))
            {
                var colon = decl.IndexOf(':');
                if (colon < 0)
                    continue;
                var prop = decl.Substring(0, colon).Trim();
                var value = decl.Substring(colon + 1).Trim();
                if (prop.Length == 0 || value.Length == 0)
                    continue;
                if (value.IndexOf("var(", StringComparison.Ordinal) >= 0)
                    continue; // token-bound -> not a property write-back
                if (prop == "padding" || prop == "margin")
                    ExpandBoxShorthand(prop, value, props);
                else
                    props[prop] = value;
            }
            return props;
        }

        // CSS box shorthand (1-4 values, top/right/bottom/left) -> longhands.
        static void ExpandBoxShorthand(string prop, string value, Dictionary<string, string> props)
        {
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 4)
                return;
            var top = parts[0];
            var right = parts.Length >= 2 ? parts[1] : top;
            var bottom = parts.Length >= 3 ? parts[2] : top;
            var left = parts.Length == 4 ? parts[3] : right;
            props[prop + "-top"] = top;
            props[prop + "-right"] = right;
            props[prop + "-bottom"] = bottom;
            props[prop + "-left"] = left;
        }

        /// <summary>
        /// Inline style= overrides from the generated .uxml, keyed by class token. UI Builder writes
        /// Inspector edits as inline style, so the diffed value must be inline-over-class.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> ParseInlineStyles(string uxml)
        {
            var map = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrEmpty(uxml))
                return map;

            XDocument doc;
            try { doc = XDocument.Parse(uxml); }
            catch { return map; } // malformed -> no inline overrides (falls back to class-only)

            foreach (var el in doc.Descendants())
            {
                var style = (string)el.Attribute("style");
                var cls = (string)el.Attribute("class");
                if (string.IsNullOrEmpty(style) || string.IsNullOrEmpty(cls))
                    continue;
                var props = ParseDecls(style);
                if (props.Count == 0)
                    continue;
                foreach (var token in cls.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    map[token] = props;
            }
            return map;
        }

        /// <summary>
        /// Content elements keyed by structural path. The scheme must match FigmaToUxml's emission DFS:
        /// any element except UXML/Style/Template/AttributeOverrides, /-joined 1-based indices.
        /// </summary>
        public static Dictionary<string, (string tag, string classTokens, string inlineStyle)> ParsePaths(string uxml)
        {
            var map = new Dictionary<string, (string tag, string classTokens, string inlineStyle)>();
            if (string.IsNullOrEmpty(uxml))
                return map;

            XDocument doc;
            try { doc = XDocument.Parse(uxml); }
            catch { return map; } // malformed -> no paths (records skip; re-import heals)

            if (doc.Root == null)
                return map;

            void Recurse(XElement parent, string parentPath)
            {
                var sibling = 0;
                foreach (var el in parent.Elements())
                {
                    var name = el.Name.LocalName;
                    if (name == "Style" || name == "Template" || name == "AttributeOverrides")
                        continue; // not content: excluded on both sides
                    sibling++;
                    var path = parentPath.Length == 0 ? sibling.ToString() : parentPath + "/" + sibling;
                    map[path] = (name, (string)el.Attribute("class") ?? string.Empty,
                                 (string)el.Attribute("style") ?? string.Empty);
                    Recurse(el, path);
                }
            }
            Recurse(doc.Root, string.Empty);
            return map;
        }

        /// <summary>
        /// Class-token rules overlaid by inline style= (inline wins, as UITK renders it). Shared by the
        /// baseline builder and Compute so both sides stay symmetric.
        /// </summary>
        public static Dictionary<string, string> EffectiveProps(
            Dictionary<string, Dictionary<string, string>> rules, string classTokens, string inlineStyle)
        {
            var effective = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(classTokens))
                foreach (var token in classTokens.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    if (rules.TryGetValue(token, out var rule))
                        foreach (var kv in rule)
                            effective[kv.Key] = kv.Value;
            foreach (var kv in ParseDecls(inlineStyle))
                effective[kv.Key] = kv.Value;
            return effective;
        }

        /// <summary>
        /// Whether the inline style= declares a non-var value for prop; the master-ack advance leaves such sharers untouched.
        /// </summary>
        public static bool InlineDeclares(string inlineStyle, string prop)
        {
            return ParseDecls(inlineStyle).ContainsKey(prop);
        }

        /// <summary>
        /// Props of a parsed rule as FigmaCssProp[], for recording the import baseline.
        /// </summary>
        public static FigmaCssProp[] ToProps(Dictionary<string, string> rule)
        {
            var list = new List<FigmaCssProp>();
            if (rule != null)
                foreach (var kv in rule)
                    list.Add(new FigmaCssProp { k = kv.Key, v = kv.Value });
            return list.ToArray();
        }

        /// <summary>
        /// Diffs the emitted pseudo-class rules, returning changes aimed at the Figma variant each state
        /// came from. States with no resolvable variant node are warned about instead.
        /// </summary>
        public static List<FigmaPropertyChange> ComputeStates(List<FigmaStateRecord> baseline, string currentUss,
            List<string> warnings = null)
        {
            var changes = new List<FigmaPropertyChange>();
            if (baseline == null || baseline.Count == 0)
                return changes;

            var rules = ParseStateRules(currentUss);
            var emitted = new HashSet<string>(); // "nodeId|prop": one variant can back two pseudo-classes
            foreach (var record in baseline)
            {
                if (record.props == null || string.IsNullOrEmpty(record.className))
                    continue;
                if (!rules.TryGetValue(record.className + record.pseudo, out var current))
                    continue; // rule removed by hand; a re-import restores it

                var was = new Dictionary<string, string>();
                foreach (var prop in record.props)
                    was[prop.k] = prop.v;

                // Iterate what the rule says now, so a freshly added declaration counts as an edit.
                foreach (var kv in current)
                {
                    if (was.TryGetValue(kv.Key, out var before) && ValuesEqual(kv.Key, kv.Value, before))
                        continue;

                    var pushValue = Pushable(kv.Key, kv.Value, warnings);
                    if (pushValue == null)
                        continue;
                    if (string.IsNullOrEmpty(record.figmaNodeId))
                    {
                        Warn(warnings, kv.Key, FigmaSyncWarningReason.StateVariantUnresolved);
                        continue;
                    }
                    if (!emitted.Add(record.figmaNodeId + "|" + kv.Key))
                    {
                        Warn(warnings, kv.Key, FigmaSyncWarningReason.StatesShareVariant);
                        continue;
                    }

                    changes.Add(new FigmaPropertyChange
                    {
                        figmaNodeId = record.figmaNodeId,
                        prop = kv.Key,
                        value = pushValue
                    });
                }
            }
            return changes;
        }

        /// <summary>
        /// Diffs node baselines against the edited document, returning supported prop changes to push.
        /// Records with a structural path locate by path; the rest fall back to the class branch.
        /// </summary>
        public static List<FigmaPropertyChange> Compute(List<FigmaNodeRecord> baseline, string currentUss,
            string currentUxml, List<FigmaComponentRecord> components = null, List<string> warnings = null)
        {
            // Not an early return: a class edit is diffed against the class, so it is found with or
            // without node baselines.
            var changes = new List<FigmaPropertyChange>();
            baseline = baseline ?? new List<FigmaNodeRecord>();

            // Class-edit->master routing: a change sourced from a mapped class rule targets the master once
            // (Figma propagates); everything else targets the instance.
            Dictionary<string, string> masterByClass = null;
            if (components != null)
            {
                foreach (var comp in components)
                    if (!string.IsNullOrEmpty(comp.className) && !string.IsNullOrEmpty(comp.masterNodeId))
                        (masterByClass = masterByClass ?? new Dictionary<string, string>())[comp.className] = comp.masterNodeId;
            }
            var masterEmitted = new HashSet<string>(); // "masterId|prop" -> one change per class edit

            var rules = ParseRules(currentUss);
            Dictionary<string, Dictionary<string, string>> inline = null;                         // for the class branch; parsed lazily
            Dictionary<string, (string tag, string classTokens, string inlineStyle)> paths = null; // for the path branch; parsed lazily
            HashSet<string> broken = null;                                                         // parents whose children moved
            foreach (var node in baseline)
            {
                if (string.IsNullOrEmpty(node.figmaNodeId))
                    continue;

                Dictionary<string, string> effective;
                Dictionary<string, string> inlineVals = null; // path branch: this element's own inline declarations
                string classTokens = null;
                if (!string.IsNullOrEmpty(node.path))
                {
                    paths = paths ?? ParsePaths(currentUxml);
                    broken = broken ?? BrokenParents(baseline, paths, warnings);
                    if (UnderBrokenParent(node.path, broken) || !paths.TryGetValue(node.path, out var el))
                        continue; // structural edit: see the note on structure below
                    inlineVals = ParseDecls(el.inlineStyle);
                    classTokens = el.classTokens;
                    effective = EffectiveProps(rules, el.classTokens, el.inlineStyle);
                }
                else
                {
                    if (string.IsNullOrEmpty(node.cssClass))
                        continue;
                    // Rule via the shared style class; inline via the identity class; no styleClass -> identity fallback.
                    effective = new Dictionary<string, string>();
                    var styleKey = string.IsNullOrEmpty(node.styleClass) ? node.cssClass : node.styleClass;
                    if (rules.TryGetValue(styleKey, out var rule))
                        foreach (var kv in rule)
                            effective[kv.Key] = kv.Value;
                    inline = inline ?? ParseInlineStyles(currentUxml);
                    if (inline.TryGetValue(node.cssClass, out var over))
                        foreach (var kv in over)
                            effective[kv.Key] = kv.Value;
                }
                if (effective.Count == 0)
                    continue;

                var baseMap = new Dictionary<string, string>();
                if (node.props != null)
                    foreach (var p in node.props)
                        if (!string.IsNullOrEmpty(p.k))
                            baseMap[p.k.Trim()] = p.v;

                foreach (var kv in effective)
                {
                    baseMap.TryGetValue(kv.Key, out var baseVal);
                    if (ValuesEqual(kv.Key, kv.Value, baseVal))
                        continue;

                    // Unsupported props warn, never guess; the baseline records everything, so "absent
                    // from baseline" means the user just added it.
                    var pushValue = Pushable(kv.Key, kv.Value, warnings);
                    if (pushValue == null)
                        continue;

                    // Inline declaration -> this element's own edit, target the instance. Class-provided ->
                    // target the in-file master once; no master mapping -> per-instance fan-out.
                    if (inlineVals != null && !inlineVals.ContainsKey(kv.Key) && masterByClass != null)
                    {
                        var masterId = MasterFor(rules, masterByClass, classTokens, kv.Key);
                        if (masterId != null)
                        {
                            if (masterEmitted.Add(masterId + "|" + kv.Key))
                                changes.Add(new FigmaPropertyChange
                                    { figmaNodeId = masterId, prop = kv.Key, value = pushValue });
                            continue; // masters are top-level -> always writable
                        }
                    }
                    // Figma locks layout on instance sublayers: don't queue what it will refuse; warn instead.
                    if (InstanceLocked(node.figmaNodeId, kv.Key))
                    {
                        Warn(warnings, kv.Key, FigmaSyncWarningReason.InstanceLockedLayout);
                        continue;
                    }
                    changes.Add(new FigmaPropertyChange
                        { figmaNodeId = node.figmaNodeId, prop = kv.Key, value = pushValue });
                }
            }

            AddClassEdits(components, rules, masterEmitted, changes, warnings);
            return changes;
        }

        /// <summary>
        /// Edits to a component's own rule, diffed against the rule rather than inferred from a node. A
        /// class maps to a Figma master, so editing it edits the component: that holds whether or not an
        /// instance overrides the property inline, which is why walking nodes cannot see it.
        /// </summary>
        static void AddClassEdits(List<FigmaComponentRecord> components,
            Dictionary<string, Dictionary<string, string>> rules, HashSet<string> masterEmitted,
            List<FigmaPropertyChange> changes, List<string> warnings)
        {
            if (components == null)
                return;

            foreach (var component in components)
            {
                if (string.IsNullOrEmpty(component.masterNodeId)
                    || !rules.TryGetValue(component.className, out var current))
                    continue;

                var baseMap = new Dictionary<string, string>();
                if (component.props != null)
                    foreach (var p in component.props)
                        if (!string.IsNullOrEmpty(p.k))
                            baseMap[p.k.Trim()] = p.v;

                foreach (var kv in current)
                {
                    baseMap.TryGetValue(kv.Key, out var baseVal);
                    if (ValuesEqual(kv.Key, kv.Value, baseVal))
                        continue;
                    var pushValue = Pushable(kv.Key, kv.Value, warnings);
                    if (pushValue == null)
                        continue;
                    // Masters are top-level, so never instance-locked. Shared with the node pass so an
                    // edit visible both ways is sent once.
                    if (masterEmitted.Add(component.masterNodeId + "|" + kv.Key))
                        changes.Add(new FigmaPropertyChange
                            { figmaNodeId = component.masterNodeId, prop = kv.Key, value = pushValue });
                }
            }
        }

        /// <summary>
        /// Parents whose children no longer match what the import emitted, by count, tag or emitted classes.
        /// Positions under them name other nodes, so their records are skipped and reported instead.
        /// </summary>
        internal static HashSet<string> BrokenParents(List<FigmaNodeRecord> baseline,
            Dictionary<string, (string tag, string classTokens, string inlineStyle)> paths, List<string> warnings)
        {
            var broken = new HashSet<string>();
            var expected = new Dictionary<string, int>();
            string doc = null;
            foreach (var node in baseline)
            {
                if (string.IsNullOrEmpty(node.path) || string.IsNullOrEmpty(node.tag))
                    continue;
                doc = doc ?? node.doc;
                var parent = ParentOf(node.path);
                expected.TryGetValue(parent, out var count);
                expected[parent] = Math.Max(count, ChildIndex(node.path));
                if (!paths.TryGetValue(node.path, out var el) || el.tag != node.tag || !HasClasses(el.classTokens, node.classes))
                    broken.Add(parent);
            }
            if (expected.Count == 0)
                return broken;

            var actual = new Dictionary<string, int>();
            foreach (var path in paths.Keys)
            {
                var parent = ParentOf(path);
                actual.TryGetValue(parent, out var count);
                actual[parent] = Math.Max(count, ChildIndex(path));
            }
            foreach (var kv in expected)
            {
                actual.TryGetValue(kv.Key, out var count);
                if (count != kv.Value)
                    broken.Add(kv.Key);
            }

            if (broken.Count > 0)
                Warn(warnings, string.IsNullOrEmpty(doc) ? "screen" : doc, FigmaSyncWarningReason.StructureChanged);
            return broken;
        }

        static bool UnderBrokenParent(string path, HashSet<string> broken)
        {
            if (broken == null || broken.Count == 0)
                return false;
            for (var parent = ParentOf(path); ; parent = ParentOf(parent))
            {
                if (broken.Contains(parent))
                    return true;
                if (parent.Length == 0)
                    return false;
            }
        }

        static string ParentOf(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        static int ChildIndex(string path)
        {
            var slash = path.LastIndexOf('/');
            return int.TryParse(slash < 0 ? path : path.Substring(slash + 1), out var index) ? index : 0;
        }

        // Every class emitted at import is still on the element; classes the user added on top are fine.
        static bool HasClasses(string current, string recorded)
        {
            if (string.IsNullOrEmpty(recorded))
                return true;
            var have = (current ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in recorded.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (Array.IndexOf(have, token) < 0)
                    return false;
            return true;
        }

        // The value to push, or null once the edit has been warned about.
        static string Pushable(string prop, string value, List<string> warnings)
        {
            if (!k_Supported.Contains(prop))
            {
                Warn(warnings, prop, prop.StartsWith("margin", StringComparison.Ordinal)
                    ? FigmaSyncWarningReason.UseContainerGap
                    : FigmaSyncWarningReason.NoFigmaEquivalent);
                return null;
            }
            if (!FigmaEffectValue.IsEffectProp(prop))
                return value;
            var canonical = FigmaEffectValue.Canonical(prop, value);
            if (canonical == null)
                Warn(warnings, prop, FigmaSyncWarningReason.NoFigmaEquivalent);
            return canonical;
        }

        // Entries are "prop|reason", reason spelled as the enum name, so they stay plain strings for the
        // scan cache.
        static void Warn(List<string> warnings, string prop, FigmaSyncWarningReason reason)
        {
            var entry = prop + "|" + reason;
            if (warnings != null && !warnings.Contains(entry))
                warnings.Add(entry);
        }

        /// <summary>
        /// Collapses "prop|reason" entries into one warning per reason, props joined in first-seen order.
        /// </summary>
        public static List<FigmaSyncWarning> AggregateWarnings(List<string> entries)
        {
            var reasons = new List<FigmaSyncWarningReason>();
            var propsByReason = new Dictionary<FigmaSyncWarningReason, List<string>>();
            foreach (var entry in entries)
            {
                var bar = entry.IndexOf('|');
                var prop = bar < 0 ? entry : entry.Substring(0, bar);
                var reason = FigmaSyncWarningReason.NoFigmaEquivalent;
                if (bar >= 0)
                    Enum.TryParse(entry.Substring(bar + 1), out reason);
                if (!propsByReason.TryGetValue(reason, out var props))
                {
                    reasons.Add(reason);
                    propsByReason[reason] = props = new List<string>();
                }
                if (!props.Contains(prop))
                    props.Add(prop);
            }
            var warnings = new List<FigmaSyncWarning>(reasons.Count);
            foreach (var reason in reasons)
                warnings.Add(new FigmaSyncWarning { reason = reason, props = propsByReason[reason].ToArray() });
            return warnings;
        }

        // The in-file master to target for a class-sourced prop change: the element's class token that is a
        // mapped component class AND whose current rule actually provides the prop. Null -> not class-routable.
        static string MasterFor(Dictionary<string, Dictionary<string, string>> rules,
            Dictionary<string, string> masterByClass, string classTokens, string prop)
        {
            if (string.IsNullOrEmpty(classTokens))
                return null;
            foreach (var token in classTokens.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                if (masterByClass.TryGetValue(token, out var masterId)
                    && rules.TryGetValue(token, out var rule) && rule.ContainsKey(prop))
                    return masterId;
            return null;
        }

        static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
                if (!char.IsWhiteSpace(c))
                    sb.Append(c);
            return sb.ToString();
        }

        // Compare two USS values by meaning, not text (colors as rgba, lengths as numbers), so #FFF == rgb(255,
        // 255, 255) and 300px == 300 don't register as edits when a tool (e.g. UI Builder) re-formats the sheet.
        static bool ValuesEqual(string prop, string a, string b)
        {
            if (FigmaEffectValue.IsEffectProp(prop))
            {
                var ea = FigmaEffectValue.Canonical(prop, a);
                var eb = FigmaEffectValue.Canonical(prop, b);
                if (ea != null && eb != null)
                    return ea == eb;
            }
            if (prop == "background-color" || prop == "color")
            {
                var ca = CanonColor(a);
                var cb = CanonColor(b);
                if (ca != null && cb != null)
                    return ca == cb;
            }
            if (TryLength(a, out var na) && TryLength(b, out var nb))
                return na == nb;
            return Normalize(a) == Normalize(b);
        }

        // Colors and scalars canonicalize through FigmaCssValue so this diff and the token diff cannot drift.
        internal static string CanonColor(string value)
        {
            return FigmaCssValue.TryColor(value, out var canonical) ? canonical : null;
        }

        static bool TryLength(string value, out double n)
        {
            n = 0;
            if (string.IsNullOrEmpty(value))
                return false;
            var v = value.Trim();
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(0, v.Length - 2).Trim();
            return TryNum(v, out n);
        }

        static bool TryNum(string s, out double n)
            => double.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n);
    }
}
