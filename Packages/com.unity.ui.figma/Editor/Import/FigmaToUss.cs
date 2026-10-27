using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// One interaction-state rule: pseudo-class, rendered body, the declarations behind it (for the
    /// write-back baseline), and the Figma variant node it came from.
    /// </summary>
    struct FigmaStateRule
    {
        public string pseudo;
        public string body;
        public string nodeId;
        public List<(string prop, string value)> decls;
    }

    /// <summary>
    /// Translates Figma's getCSSAsync CSS into USS: allowlist filter, -unity-* renames, shorthand expansion.
    /// Unknown property or value is an import error.
    /// </summary>
    static class FigmaToUss
    {
        static readonly HashSet<string> k_Keep = new HashSet<string>(new[]
        {
            "width", "height", "min-width", "min-height", "max-width", "max-height",
            "background-color", "color", "opacity",
            "-unity-background-image-tint-color",
            "border-radius",
            "border-top-left-radius", "border-top-right-radius",
            "border-bottom-left-radius", "border-bottom-right-radius",
            "border-width", "border-color",
            "border-left-width", "border-right-width", "border-top-width", "border-bottom-width",
            "flex-direction", "flex-wrap", "justify-content", "align-items", "align-self",
            "flex-grow", "flex-shrink", "flex-basis",
#if UITOOLKIT_HAS_USS_GAP
            // USS gained gap late in 6000.7.0a3, so the define is gated on a4 where it is guaranteed.
            // Below that it is an unknown property and the whole stylesheet fails to import.
            "gap", "row-gap", "column-gap",
#endif
            "padding", "padding-left", "padding-right", "padding-top", "padding-bottom",
            "margin", "margin-left", "margin-right", "margin-top", "margin-bottom",
            "left", "top", "right", "bottom",
            "font-size", "letter-spacing"
        });

        /// <summary>
        /// Parent-relative placement, which belongs to where a node sits rather than to what it is.
        /// It stays out of a shared class, because one left/top would stack every instance at the same
        /// spot, and it is the only thing a templated instance carries, because the TemplateContainer is
        /// what sits in the parent. Size is not placement: it is intrinsic, so it is class material.
        /// </summary>
        internal static readonly HashSet<string> k_Placement = new HashSet<string>(new[]
        {
            "position", "left", "top", "right", "bottom"
        });

        // A variant is a tile inside its COMPONENT_SET; the instance it is compared against sits in a
        // screen. How each participates in ITS parent's layout says nothing about the interaction state,
        // so it is excluded from state deltas for the same reason placement is.
        static readonly HashSet<string> k_ParentLayout = new HashSet<string>(new[]
        {
            "flex-grow", "flex-shrink", "flex-basis", "align-self"
        });

        /// <summary>
        /// Renders the USS: one rule per component, filtered to classes the emitted UXML references.
        /// </summary>
        public static string Build(FigmaImportContext context, FigmaImportSettings settings,
            HashSet<string> usedClasses = null)
        {
            var sb = new StringBuilder();
            if (context.ComponentRules != null)
            {
                foreach (var rule in context.ComponentRules)
                {
                    if (usedClasses != null && !usedClasses.Contains(rule.className))
                        continue;

                    if (rule.body.Length > 0)
                        sb.Append('.').Append(rule.className).Append(" {\n").Append(rule.body).Append("}\n\n");

                    if (rule.states != null)
                        foreach (var state in rule.states)
                            sb.Append('.').Append(rule.className).Append(state.pseudo)
                              .Append(" {\n").Append(state.body).Append("}\n\n");
                }
            }
            AppendControlNormalization(sb, context, settings);
            return sb.ToString();
        }

        /// <summary>
        /// Class tokens the emitted UXML references; Build skips rules outside this set.
        /// </summary>
        public static HashSet<string> UsedClasses(IEnumerable<string> uxmlDocs)
        {
            var used = new HashSet<string>();
            if (uxmlDocs == null)
                return used;
            foreach (var doc in uxmlDocs)
            {
                if (string.IsNullOrEmpty(doc))
                    continue;
                foreach (Match m in Regex.Matches(doc, "class=\"([^\"]*)\""))
                    foreach (var token in m.Groups[1].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                        used.Add(token);
            }
            return used;
        }

        // A TextField's themed inner input (bg/border/padding) fights the flat box imported from Figma;
        // flatten it so the outer field's imported styling defines the look.
        static void AppendControlNormalization(StringBuilder sb, FigmaImportContext context, FigmaImportSettings settings)
        {
            var hasTextField = false;
            for (var i = 0; i < context.Nodes.Length; ++i)
                if (ControlMap.Resolve(context, i, settings).tag == "TextField") { hasTextField = true; break; }
            if (!hasTextField)
                return;

            sb.Append(".unity-text-field .unity-base-text-field__input {\n")
                .Append("    background-color: rgba(0, 0, 0, 0);\n")
                // Units are required: a unitless number parses as Float, and StylePropertyReader.ReadLength
                // only handles Keyword or Dimension, so a bare 0 logs "Accessing invalid property".
                .Append("    border-width: 0px;\n")
                .Append("    padding: 0px;\n")
                .Append("}\n\n");
        }

        /// <summary>
        /// A node's full effective declarations: filtered css, background-image, and var() refs.
        /// </summary>
        public static List<(string prop, string value)> BuildDeclarationBodyDecls(FigmaImportContext context,
            int index, Func<string, string> resolveFont, Func<int, string> resolveBackgroundImage)
        {
            var decls = new List<(string prop, string value)>();
            var node = context.Nodes[index];
            var overridden = VarRefProperties(node.varRefs); // props that will be emitted as var(--token)
            Action<string, string> emit = (property, value) => decls.Add((property, value));

            EmitCssDeclarations(emit, node.css, resolveFont, overridden);

            // Exported image/vector fills become background-image; URI resolved outside (needs AssetDatabase).
            var backgroundImage = resolveBackgroundImage != null ? resolveBackgroundImage(index) : null;
            if (!string.IsNullOrEmpty(backgroundImage))
            {
                emit("background-image", backgroundImage);
                emit("-unity-background-scale-mode", "scale-to-fit");
            }

            // A rasterized node's PNG is rendered with its layer effects baked in, so applying the CSS
            // filter as well would draw the shadow or blur twice. backdrop-filter stays: what is behind
            // the element only exists at runtime.
            if (node.raster)
                for (var i = decls.Count - 1; i >= 0; --i)
                    if (decls[i].prop == "filter")
                        decls.RemoveAt(i);

            AppendVarRefs(emit, node.varRefs);
            return decls;
        }

        /// <summary>
        /// Component class body: the master's intrinsic style minus the always-inline placement set.
        /// No background-image (rasters are per-node, stay inline).
        /// </summary>
        public static List<(string prop, string value)> ClassBodyDecls(FigmaCssProp[] masterCss,
            FigmaCssProp[] masterVarRefs, Func<string, string> resolveFont)
        {
            var decls = new List<(string prop, string value)>();
            var overridden = VarRefProperties(masterVarRefs);
            Action<string, string> emit = (property, value) =>
            {
                if (!k_Placement.Contains(property))
                    decls.Add((property, value));
            };
            EmitCssDeclarations(emit, masterCss, resolveFont, overridden);
            AppendVarRefs(emit, masterVarRefs);
            return decls;
        }

        /// <summary>
        /// A component node's inline decls: full effective decls minus what the class body already
        /// provides with the same value. Placement (k_Placement) always stays inline.
        /// </summary>
        public static List<(string prop, string value)> InstanceInlineDecls(FigmaImportContext context, int index,
            List<(string prop, string value)> classBody, Func<string, string> resolveFont,
            Func<int, string> resolveBackgroundImage)
        {
            var bodyVals = new Dictionary<string, string>();
            foreach (var decl in classBody)
                bodyVals[decl.prop] = decl.value; // last-wins, matching USS cascade within one rule

            var result = new List<(string prop, string value)>();
            foreach (var decl in BuildDeclarationBodyDecls(context, index, resolveFont, resolveBackgroundImage))
            {
                if (!k_Placement.Contains(decl.prop)
                    && bodyVals.TryGetValue(decl.prop, out var bodyVal) && bodyVal == decl.value)
                    continue;
                result.Add(decl);
            }
            return result;
        }

        /// <summary>
        /// Templated ui:Instance container inline decls: placement only; the template root inside owns
        /// the styling class, so anything else here would double-render.
        /// </summary>
        public static List<(string prop, string value)> PlacementDecls(FigmaImportContext context, int index,
            Func<string, string> resolveFont)
        {
            var result = new List<(string prop, string value)>();
            foreach (var decl in BuildDeclarationBodyDecls(context, index, resolveFont, null))
            {
                if (k_Placement.Contains(decl.prop))
                    result.Add(decl);
            }
            return result;
        }

        /// <summary>
        /// Declaration list to rule body text (everything between the braces).
        /// </summary>
        /// <summary>
        /// A declaration is concatenated into USS with no escaping available, so a value carrying a
        /// structural character could close the rule and open a selector of its own. Nothing legitimate
        /// needs one, so the declaration is dropped rather than escaped.
        /// </summary>
        internal static bool IsSafeValue(string value)
        {
            return value != null && value.IndexOf(';') < 0 && value.IndexOf('{') < 0
                && value.IndexOf('}') < 0 && value.IndexOf("/*", StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// The var() path skips the allowlist that constrains every other property name, so the name is
        /// checked against the shape of a USS property.
        /// </summary>
        internal static bool IsSafeName(string prop)
        {
            if (string.IsNullOrEmpty(prop))
                return false;
            var start = prop[0] == '-' ? 1 : 0;   // -unity-* properties
            if (start >= prop.Length || !char.IsLetter(prop[start]))
                return false;
            for (var i = start; i < prop.Length; ++i)
                if (!char.IsLetterOrDigit(prop[i]) && prop[i] != '-')
                    return false;
            return true;
        }

        public static string RenderBody(List<(string prop, string value)> decls)
        {
            if (decls == null || decls.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var decl in decls)
                if (IsSafeName(decl.prop) && IsSafeValue(decl.value))
                    sb.Append("    ").Append(decl.prop).Append(": ").Append(decl.value).Append(";\n");
            return sb.ToString();
        }

        /// <summary>
        /// Declaration list to inline style attribute value. XML-escaping is the UXML emitter's job.
        /// </summary>
        public static string RenderInline(List<(string prop, string value)> decls)
        {
            if (decls == null || decls.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var decl in decls)
            {
                if (!IsSafeName(decl.prop) || !IsSafeValue(decl.value))
                    continue;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(decl.prop).Append(": ").Append(decl.value).Append(';');
            }
            return sb.ToString();
        }

        static void EmitCssDeclarations(Action<string, string> emit, FigmaCssProp[] css,
            Func<string, string> resolveFont, HashSet<string> overridden)
        {
            if (css == null)
                return;
            for (var i = 0; i < css.Length; ++i)
                Emit(emit, css[i].k, css[i].v, resolveFont, overridden);
        }

        static List<(string prop, string value)> CssDeclarationList(FigmaCssProp[] css,
            Func<string, string> resolveFont, HashSet<string> overridden)
        {
            var list = new List<(string prop, string value)>();
            EmitCssDeclarations((p, v) => list.Add((p, v)), css, resolveFont, overridden);
            return list;
        }

        /// <summary>
        /// "State" variants mapped to UITK pseudo-classes, carrying only css that differs from the base.
        /// Sorted by pseudo for stable output; first variant per pseudo wins; null when no deltas.
        /// </summary>
        public static List<FigmaStateRule> BuildStateRules(FigmaImportContext context, int index,
            Func<string, string> resolveFont)
        {
            var variants = context.Nodes[index].variants;
            if (variants == null || variants.Length == 0)
                return null;

            var rules = new List<FigmaStateRule>();
            var seen = new HashSet<string>();
            foreach (var variant in variants)
            {
                var pseudo = FigmaPseudoState.Map(variant.state);
                if (string.IsNullOrEmpty(pseudo) || seen.Contains(pseudo))
                    continue; // resting/unknown state, or pseudo already claimed
                var decls = BuildStateDeltaDecls(context, index, variant, resolveFont);
                if (decls.Count == 0)
                    continue;
                seen.Add(pseudo);
                rules.Add(new FigmaStateRule
                {
                    pseudo = pseudo,
                    body = RenderDecls(decls),
                    nodeId = variant.nodeId,
                    decls = decls
                });
            }
            if (rules.Count == 0)
                return null;

            rules.Sort((a, b) => string.CompareOrdinal(a.pseudo, b.pseudo));
            return rules;
        }

        static string RenderDecls(List<(string prop, string value)> decls)
        {
            var sb = new StringBuilder();
            foreach (var decl in decls)
                if (IsSafeName(decl.prop) && IsSafeValue(decl.value))
                    sb.Append("    ").Append(decl.prop).Append(": ").Append(decl.value).Append(";\n");
            return sb.ToString();
        }

        // Each side must use its own var-binding set; sharing the base's empties every themed state's delta.
        // A variant with no varRefs degrades to raw values.
        static List<(string prop, string value)> BuildStateDeltaDecls(FigmaImportContext context, int index,
            FigmaVariantStyle variant, Func<string, string> resolveFont)
        {
            var node = context.Nodes[index];
            var baseVals = new Dictionary<string, string>();
            foreach (var decl in CssDeclarationList(node.css, resolveFont, VarRefProperties(node.varRefs)))
                baseVals[decl.prop] = decl.value;
            AppendVarRefs((prop, value) => baseVals[prop] = value, node.varRefs);

            var variantDecls = CssDeclarationList(variant.css, resolveFont, VarRefProperties(variant.varRefs));
            AppendVarRefs((prop, value) => variantDecls.Add((prop, value)), variant.varRefs);

            var delta = new List<(string prop, string value)>();
            foreach (var decl in variantDecls)
            {
                // A variant's placement and layout participation describe its tile in the
                // COMPONENT_SET, not a state delta.
                if (k_Placement.Contains(decl.prop) || k_ParentLayout.Contains(decl.prop))
                    continue;
                if (baseVals.TryGetValue(decl.prop, out var baseVal) && baseVal == decl.value)
                    continue; // unchanged from the base rule
                delta.Add(decl);
            }
            return delta;
        }

        static void Emit(Action<string, string> emit, string key, string rawValue, Func<string, string> resolveFont,
            HashSet<string> overridden)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(rawValue))
                return;

            key = key.Trim();
            var value = rawValue.Trim().TrimEnd(';').Trim();
            if (value.Length == 0 || !IsSafeValue(value))
                return;

            switch (key)
            {
                case "background":
                    if (IsColor(value) && !overridden.Contains("background-color"))
                        emit("background-color", value);
                    return; // gradients / url() dropped (images are Phase 2)

                case "border":
                    EmitBorder(emit, value, overridden);
                    return; // USS has no `border` shorthand or border-style

                case "flex":
                    EmitFlex(emit, value);
                    return;

                case "text-align":
                    var align = UnityTextAlign(value);
                    if (align != null)
                        emit("-unity-text-align", align);
                    return;

                case "font-family":
                    var font = resolveFont != null ? resolveFont(value) : null;
                    if (font != null)
                        emit("-unity-font-definition", font);
                    return; // unmatched font -> drop (default font)

                case "font-weight":
                    if (IsBold(value))
                        emit("-unity-font-style", "bold");
                    return;

                case "display":
                    if (value == "flex" || value == "inline-flex")
                        emit("display", "flex"); // UITK has only flex; inline-flex maps to it
                    else if (value == "none")
                        emit("display", "none");
                    return; // block/grid/etc. dropped

                case "position":
                    if (value == "relative" || value == "absolute")
                        emit(key, value);
                    return;

                case "white-space":
                    if (value == "normal" || value == "nowrap")
                        emit("white-space", value); // USS supports only normal/nowrap
                    return;

                case "text-shadow":
                    var textShadow = TextShadowValue(value);
                    if (textShadow != null)
                        emit("text-shadow", textShadow);
                    return;

                case "filter":
                    var filter = FilterValue(value);
                    if (filter != null)
                        emit("filter", filter);
                    return;

#if UITOOLKIT_HAS_USS_FILTER
                case "backdrop-filter":
                    // Background blur; USS backdrop-filter (available in 6000.6).
                    var backdrop = BlurFilterValue(value);
                    if (backdrop != null)
                        emit("backdrop-filter", backdrop);
                    return;
#endif
            }

            if (k_Keep.Contains(key) && !overridden.Contains(key))
                emit(key, value);
            // else: unsupported (or var-overridden) -> dropped (the importer errors on unknown property/value)
        }

        // border: <width> [style] <color> -> longhands (USS has no border shorthand or border-style).
        // Skip longhands a Variable binding owns so the shorthand doesn't leak a stale raw duplicate.
        static void EmitBorder(Action<string, string> emit, string value, HashSet<string> overridden)
        {
            var width = ExtractLength(value);
            var color = ExtractColor(value);
            if (width != null && !overridden.Contains("border-width"))
                emit("border-width", width);
            if (color != null && !overridden.Contains("border-color"))
                emit("border-color", color);
        }

        // flex: <grow> [shrink] [basis] -> flex-grow / flex-shrink / flex-basis longhands.
        static void EmitFlex(Action<string, string> emit, string value)
        {
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && IsNumber(parts[0]))
                emit("flex-grow", parts[0]);
            if (parts.Length >= 2 && IsNumber(parts[1]))
                emit("flex-shrink", parts[1]);
            if (parts.Length >= 3)
            {
                var basis = parts[2];
                if (basis == "auto" || basis.EndsWith("px") || basis.EndsWith("%"))
                    emit("flex-basis", basis);
            }
        }

        // Properties with a Figma Variable binding; raw values suppressed so only var(--token) is emitted.
        static HashSet<string> VarRefProperties(FigmaCssProp[] varRefs)
        {
            var set = new HashSet<string>();
            if (varRefs != null)
                foreach (var r in varRefs)
                    if (!string.IsNullOrEmpty(r.k))
                        set.Add(r.k.Trim());
            return set;
        }

        // Emits `property: var(--token);` for each bound variable (name sanitized to match tokens.uss).
        static void AppendVarRefs(Action<string, string> emit, FigmaCssProp[] varRefs)
        {
            if (varRefs == null)
                return;
            for (var i = 0; i < varRefs.Length; ++i)
            {
                var r = varRefs[i];
                if (string.IsNullOrEmpty(r.k) || string.IsNullOrEmpty(r.v))
                    continue;
                var prop = r.k.Trim();
                if (!IsSafeName(prop))
                    continue;
                emit(prop, "var(" + FigmaTokens.SanitizeTokenName(r.v) + ")");
            }
        }

        /// <summary>
        /// One shadow of a box-shadow/text-shadow list. Lengths are canonical "Npx"; spread is null
        /// when absent.
        /// </summary>
        internal struct ShadowParts
        {
            public string x, y, blur, spread, color;
            public bool inset;
        }

        // USS text-shadow draws one shadow; the first segment is Figma's topmost.
        internal static string TextShadowValue(string value)
        {
            foreach (var segment in SplitTopLevel(value))
            {
                if (!TryParseShadow(segment, out var s) || s.inset)
                    continue;
                return s.x + " " + s.y + " " + s.blur + " " + s.color;
            }
            return null;
        }

        /// <summary>
        /// A CSS filter chain (what Figma emits on vector-ish nodes), rebuilt one function at a time.
        /// blur() is USS since 6000.3; drop-shadow() since 6000.6, so it is emitted under the define
        /// only. An unsupported function is skipped, never passed through.
        /// </summary>
        internal static string FilterValue(string value)
        {
            var functions = SplitFilterFunctions(value);
            if (functions == null)
                return null;
            StringBuilder sb = null;
            foreach (var (name, args) in functions)
            {
                var rebuilt = RebuildFilterFunction(name, args);
                if (rebuilt == null)
                    continue;
                sb = sb ?? new StringBuilder();
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(rebuilt);
            }
            return sb != null ? sb.ToString() : null;
        }

        /// <summary>A filter chain as (name, args) pairs; null when malformed.</summary>
        internal static List<(string name, string args)> SplitFilterFunctions(string value)
        {
            var functions = new List<(string name, string args)>();
            var i = 0;
            while (i < value.Length)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    i++;
                    continue;
                }
                var open = value.IndexOf('(', i);
                if (open < 0)
                    return null;
                var depth = 1;
                var j = open + 1;
                while (j < value.Length && depth > 0)
                {
                    if (value[j] == '(')
                        depth++;
                    else if (value[j] == ')')
                        depth--;
                    j++;
                }
                if (depth != 0)
                    return null;
                functions.Add((value.Substring(i, open - i).Trim(), value.Substring(open + 1, j - open - 2)));
                i = j;
            }
            return functions;
        }

        static string RebuildFilterFunction(string name, string args)
        {
            switch (name)
            {
                case "blur":
                    var length = CanonicalLength(args.Trim());
                    return length != null ? "blur(" + length + ")" : null;

#if UITOOLKIT_HAS_USS_FILTER
                case "drop-shadow":
                    // CSS drop-shadow blur is a box-shadow-style radius (2 sigma); USS takes sigma.
                    if (!TryParseShadow(args, out var s) || s.inset || s.spread != null)
                        return null;
                    return "drop-shadow(" + s.x + " " + s.y + " " + HalfLength(s.blur) + " " + s.color + ")";
#endif
            }
            return null;
        }

        // Splits on commas outside parentheses, so rgba(...) stays whole.
        internal static List<string> SplitTopLevel(string value)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(value))
                return parts;
            var depth = 0;
            var start = 0;
            for (var i = 0; i < value.Length; ++i)
            {
                var c = value[i];
                if (c == '(')
                    depth++;
                else if (c == ')' && depth > 0)
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(value.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(value.Substring(start));
            return parts;
        }

        /// <summary>
        /// "[inset] &lt;x&gt; &lt;y&gt; [blur] [spread] &lt;color&gt;". Every token is re-validated and
        /// rebuilt, so no wire text reaches the stylesheet verbatim.
        /// </summary>
        internal static bool TryParseShadow(string segment, out ShadowParts shadow)
        {
            shadow = default;
            if (string.IsNullOrEmpty(segment))
                return false;

            var color = ExtractColor(segment);
            if (color == null || !IsSafeColor(color))
                return false;

            var rest = segment.Remove(segment.IndexOf(color, StringComparison.Ordinal), color.Length);
            var lengths = new List<string>();
            foreach (var token in rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "inset")
                {
                    shadow.inset = true;
                    continue;
                }
                var length = CanonicalLength(token);
                if (length == null)
                    return false;
                lengths.Add(length);
            }
            if (lengths.Count < 2 || lengths.Count > 4)
                return false;

            shadow.x = lengths[0];
            shadow.y = lengths[1];
            shadow.blur = lengths.Count >= 3 ? lengths[2] : "0px";
            shadow.spread = lengths.Count == 4 ? lengths[3] : null;
            shadow.color = color;
            return true;
        }

        // Figma's blur radius is a gaussian sigma in CSS and USS alike, so it passes through
        // unconverted. Anything but a single blur() is refused.
        internal static string BlurFilterValue(string value)
        {
            if (!value.StartsWith("blur(", StringComparison.Ordinal) || value[value.Length - 1] != ')')
                return null;
            var length = CanonicalLength(value.Substring(5, value.Length - 6).Trim());
            return length != null ? "blur(" + length + ")" : null;
        }

        static string HalfLength(string canonical)
        {
            var f = float.Parse(canonical.Substring(0, canonical.Length - 2), CultureInfo.InvariantCulture);
            return (f * 0.5f).ToString("0.###", CultureInfo.InvariantCulture) + "px";
        }

        // A px length (or bare number) reformatted from the parsed float; anything else is refused.
        static string CanonicalLength(string token)
        {
            var t = token.EndsWith("px", StringComparison.Ordinal)
                ? token.Substring(0, token.Length - 2)
                : token;
            if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return null;
            return f.ToString("0.###", CultureInfo.InvariantCulture) + "px";
        }

        // #hex of a valid width, or a color function holding only numeric-list characters.
        internal static bool IsSafeColor(string color)
        {
            if (string.IsNullOrEmpty(color))
                return false;
            if (color[0] == '#')
            {
                var digits = color.Length - 1;
                if (digits != 3 && digits != 4 && digits != 6 && digits != 8)
                    return false;
                for (var i = 1; i < color.Length; ++i)
                    if (!IsHex(color[i]))
                        return false;
                return true;
            }
            var open = color.IndexOf('(');
            if (open < 0 || color[color.Length - 1] != ')')
                return false;
            for (var i = open + 1; i < color.Length - 1; ++i)
            {
                var c = color[i];
                if (!char.IsDigit(c) && c != ' ' && c != ',' && c != '.' && c != '%' && c != '-')
                    return false;
            }
            return true;
        }

        static bool IsColor(string value)
        {
            return value.StartsWith("#") || value.StartsWith("rgb") || value.StartsWith("hsl");
        }

        static bool IsBold(string value)
        {
            if (value == "bold" || value == "bolder")
                return true;
            return int.TryParse(value, out var weight) && weight >= 600;
        }

        static bool IsNumber(string value)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        static string ExtractLength(string value)
        {
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.EndsWith("px") || part.EndsWith("%"))
                    return part;
            }
            return null;
        }

        static string ExtractColor(string value)
        {
            var hash = value.IndexOf('#');
            if (hash >= 0)
            {
                var end = hash + 1;
                while (end < value.Length && IsHex(value[end]))
                    end++;
                return value.Substring(hash, end - hash);
            }

            foreach (var fn in new[] { "rgba(", "rgb(", "hsla(", "hsl(" })
            {
                var idx = value.IndexOf(fn, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var close = value.IndexOf(')', idx);
                    if (close >= 0)
                        return value.Substring(idx, close - idx + 1);
                }
            }

            return null;
        }

        static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        static string UnityTextAlign(string value)
        {
            switch (value)
            {
                case "left": return "middle-left";
                case "center": return "middle-center";
                case "right": return "middle-right";
                case "justify": return "middle-left";
                default: return null;
            }
        }
    }
}
