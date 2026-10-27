using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Resolves a Figma node to exactly one control, first match wins: pluginData tag -> settings
    /// controlMap -> name convention -> type default. A tag wins even on a component instance.
    /// </summary>
    static class ControlMap
    {
        const string k_EngineNamespace = "UnityEngine.UIElements";

        public struct ControlResolution
        {
            public string tag;         // local element name, e.g. "Button"
            public string ns;          // namespace; engine for catalog controls
            public string ussClass;    // canonical unity-* class for engine catalog; "" otherwise
            public string captionAttr; // attribute carrying the caption text; "" = no caption
            public string iconAttr;    // attribute carrying an exported icon; "" = the control has no icon slot
            public bool isControl;     // false only for the VisualElement fallback (-> generated template)
            public bool container;     // keeps children as content; captionAttr then carries the header from the node name
            public bool leaf;          // catalog control that draws itself, so its Figma child layers are dropped
            // Container whose header is drawn as its own content rather than sitting above it, so a lone
            // text inside it is the header. Only Tab: a tab strip is chips, and the page lives elsewhere.
            public bool headerFromContent;
            public PropertyBinding[] bindings; // extra attributes to emit (from the settings controlMap); null = none
        }

        static readonly Dictionary<string, ControlResolution> k_Catalog = BuildCatalog();

        static readonly ControlResolution k_VisualElement = new ControlResolution
            { tag = "VisualElement", ns = k_EngineNamespace, ussClass = "", captionAttr = "", isControl = false, container = true };
        static readonly ControlResolution k_Label = new ControlResolution
            { tag = "Label", ns = k_EngineNamespace, ussClass = "unity-label", captionAttr = "text", isControl = true, leaf = true };

        public static ControlResolution Resolve(FigmaImportContext context, int index, FigmaImportSettings settings)
        {
            if (context.TryGetResolution(index, settings, out var cached))
                return cached;

            var resolution = ResolveUncached(context, index, settings);
            context.CacheResolution(index, settings, resolution);
            return resolution;
        }

        static ControlResolution ResolveUncached(FigmaImportContext context, int index, FigmaImportSettings settings)
        {
            var node = context.Nodes[index];

            // 1. Explicit designer tag (getSharedPluginData) - honored only if it names a real control.
            if (!string.IsNullOrEmpty(node.control))
            {
                var tagged = FromTarget(node.control, null, null, null);
                if (tagged.isControl)
                    return tagged;
            }

            // 2. Settings controlMap. A match wins outright, even when it forces a template.
            if (settings != null && settings.TryResolveControl(node.name, out var mapping))
            {
                DesignRows(mapping.bindings, out var caption, out var icon);
                return FromTarget(mapping.uitkControl, caption, icon, mapping.bindings, allowGlobal: true);
            }

            // 2.5 A tray of Tabs is a TabView however it is named ("Top tray"), so the Tabs inside it
            //     land somewhere that can host them.
            if (IsTabGroup(context, index))
                return k_Catalog["tabview"];

            // 3. Name convention - components/instances only (guessing on plain frames misfires, e.g. "Input &
            //    Button" -> TextField), and never a leaf control on a node with 2+ meaningful children.
            if (node.type == "COMPONENT" || node.type == "INSTANCE")
            {
                var byName = NameConvention(node.name);
                // A layer the control can take as its own icon is not content it would lose, so it does not
                // count against it here.
                var absorbable = IconChild(context, index, byName);
                if (byName.isControl && !(IsLeafControlTag(byName.tag)
                        && MeaningfulChildCount(context, index, absorbable) >= 2))
                    return byName;
            }

            // 4. Type default.
            return node.type == "TEXT" ? k_Label : k_VisualElement;
        }

        /// <summary>
        /// True when the node's designer tag names a real control - the signal for FigmaToUxml to
        /// route a tagged instance here instead of through the component library.
        /// </summary>
        public static bool HasExplicitControl(FigmaNode node)
        {
            return !string.IsNullOrEmpty(node.control) && FromTarget(node.control, null, null, null).isControl;
        }

        /// <summary>
        /// The child a control can take as its own icon, or -1. A control with an icon slot absorbs a
        /// single child that draws no text; more than one is content the control cannot express, and the
        /// caller reports it rather than choosing between them.
        /// </summary>
        public static int IconChild(FigmaImportContext context, int index, ControlResolution resolution)
        {
            if (string.IsNullOrEmpty(resolution.iconAttr))
                return -1;

            var found = -1;
            foreach (var child in context.Children[index])
            {
                if (HasText(context, child) || IsHidden(context, child))
                    continue;
                if (found >= 0)
                    return -1;
                found = child;
            }
            return found;
        }

        // A hidden layer is not content a control absorbs: a Boolean visibility toggle must remove
        // an icon or caption, not feed it.
        internal static bool IsHidden(FigmaImportContext context, int index)
        {
            var css = context.Nodes[index].css;
            if (css == null)
                return false;
            foreach (var p in css)
                if (p.k == "display" && p.v == "none")
                    return true;
            return false;
        }

        static bool HasText(FigmaImportContext context, int index)
        {
            if (context.Nodes[index].type == "TEXT")
                return true;
            foreach (var child in context.Children[index])
                if (HasText(context, child))
                    return true;
            return false;
        }

        // Dots are allowed so this covers a namespace as well as a local name.
        static bool IsXmlName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            if (!char.IsLetter(value[0]) && value[0] != '_')
                return false;
            foreach (var c in value)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                    return false;
            return true;
        }

        // A bare engine name uses the validated catalog (a miss falls back, typo-safe); a fully-qualified
        // name is trusted - foreign, or an engine control the catalog doesn't curate (e.g. IntegerField).
        // A mapping's caption and icon destinations are its Design rows; first of each kind wins.
        static void DesignRows(PropertyBinding[] bindings, out string caption, out string icon)
        {
            caption = null;
            icon = null;
            if (bindings == null)
                return;
            foreach (var b in bindings)
            {
                if (string.IsNullOrEmpty(b.uxmlAttribute))
                    continue;
                if (caption == null && b.source == FigmaBindingSource.DesignText)
                    caption = b.uxmlAttribute;
                else if (icon == null && b.source == FigmaBindingSource.DesignIcon)
                    icon = b.uxmlAttribute;
            }
        }

        // `allowGlobal`: an authored mapping may name a global-namespace control; a tag is honored only
        // when it names a real catalog control, so a stale tag stays ignored.
        static ControlResolution FromTarget(string target, string captionOverride, string iconOverride,
            PropertyBinding[] bindings, bool allowGlobal = false)
        {
            if (string.IsNullOrEmpty(target))
                return k_VisualElement;
            target = target.Trim();
            if (target.Length == 0 || string.Equals(target, "template", StringComparison.OrdinalIgnoreCase))
                return k_VisualElement; // force-template escape hatch

            var dot = target.LastIndexOf('.');
            string ns, local;
            if (dot > 0)
            {
                ns = target.Substring(0, dot);
                local = target.Substring(dot + 1);
            }
            else
            {
                local = target;
                ns = k_EngineNamespace; // bare name -> engine (foreign controls are stored fully-qualified)
            }

            var hasCaption = !string.IsNullOrEmpty(captionOverride);
            var engine = string.IsNullOrEmpty(ns) || ns == k_EngineNamespace;

            if (engine)
            {
                if (k_Catalog.TryGetValue(local.ToLowerInvariant(), out var cat))
                {
                    if (hasCaption)
                        cat.captionAttr = captionOverride.Trim();
                    if (!string.IsNullOrEmpty(iconOverride))
                        cat.iconAttr = iconOverride.Trim();
                    cat.bindings = bindings;
                    return cat;
                }
                if (dot <= 0)
                {
                    if (!allowGlobal)
                        return k_VisualElement;
                    // A bare non-catalog name is a global-namespace control: UXML resolves an unprefixed
                    // element by bare name. A typo surfaces as a UXML import error, visible where a
                    // silent plain element would not be.
                    ns = GlobalNamespace;
                }
                // else: a qualified non-catalog engine control -> trusted below (no canonical ussClass/caption)
            }

            // Emitted into UXML verbatim, so a name that is not an XML name is a mistake, not a control.
            if (!IsXmlName(local) || (ns != GlobalNamespace && !IsXmlName(ns)))
                return k_VisualElement;

            return new ControlResolution
            {
                tag = local,
                ns = ns,
                ussClass = "",
                captionAttr = hasCaption ? captionOverride.Trim() : "",
                iconAttr = string.IsNullOrEmpty(iconOverride) ? "" : iconOverride.Trim(),
                isControl = true,
                bindings = bindings
            };
        }

        /// <summary>
        /// A component named after a control, or in a folder named after one, resolves to it. Exact only:
        /// this is a convenience for the obvious case, and anything looser competes with the settings
        /// mapping, which says the same thing exactly. "Tab Bar" is not a TabView, it is unmapped.
        /// </summary>
        static ControlResolution NameConvention(string name)
        {
            if (string.IsNullOrEmpty(name))
                return k_VisualElement;

            var whole = NormalizeName(name);
            if (whole != null && k_Catalog.TryGetValue(whole, out var byWhole))
                return byWhole;

            // "/" is structural in Figma, not prose: it nests the component in folders, so "Button/Primary"
            // really is a Button. Matching a segment is still exact, just against a part of the name.
            if (name.IndexOf('/') < 0)
                return k_VisualElement;
            foreach (var segment in name.Split('/'))
            {
                var key = NormalizeName(segment);
                if (key != null && k_Catalog.TryGetValue(key, out var bySegment))
                    return bySegment;
            }
            return k_VisualElement;
        }

        // Carries content/structure; RECTANGLE/ELLIPSE/VECTOR are box decoration until image import lands.
        static bool IsMeaningful(FigmaImportContext context, int index)
        {
            switch (context.Nodes[index].type)
            {
                case "TEXT":
                case "FRAME":
                case "GROUP":
                case "COMPONENT":
                case "INSTANCE":
                    return true;
                default:
                    return false;
            }
        }

        static int MeaningfulChildCount(FigmaImportContext context, int index, int excluded = -1)
        {
            var count = 0;
            foreach (var child in context.Children[index])
                if (child != excluded && IsMeaningful(context, child))
                    count++;
            return count;
        }

        // 2+ meaningful children, all Tabs. Cheap name/tag check - no recursive Resolve on deep trees.
        static bool IsTabGroup(FigmaImportContext context, int index)
        {
            var tabs = 0;
            foreach (var child in context.Children[index])
            {
                if (!IsMeaningful(context, child))
                    continue;
                if (!IsTabLike(context.Nodes[child]))
                    return false;
                tabs++;
            }
            return tabs >= 2;
        }

        static bool IsTabLike(FigmaNode node)
        {
            if (string.Equals(node.control, "Tab", StringComparison.OrdinalIgnoreCase))
                return true;
            // Through NameConvention, not a raw compare, so "Tab" and "Tabs/Active" agree with step 3.
            return (node.type == "COMPONENT" || node.type == "INSTANCE") && NameConvention(node.name).tag == "Tab";
        }

        // The catalog's container controls plus the VisualElement fallback; a leaf is anything else.
        static readonly HashSet<string> k_ContainerTags = BuildContainerTags();

        static HashSet<string> BuildContainerTags()
        {
            var set = new HashSet<string>(new[] { k_VisualElement.tag });
            foreach (var resolution in k_Catalog.Values)
                if (resolution.container)
                    set.Add(resolution.tag);
            return set;
        }

        /// <summary>
        /// True when a name contains a control word without being one ("Tab Bar", not "TabView"). Matching
        /// it would be a guess, but saying nothing leaves a plain element unexplained, so the report nudges
        /// toward a tag or a mapping instead.
        /// </summary>
        public static bool NameSuggestsControl(string name)
        {
            if (string.IsNullOrEmpty(name) || NameConvention(name).isControl)
                return false;

            var token = new StringBuilder();
            for (var i = 0; i <= name.Length; ++i)
            {
                var c = i < name.Length ? name[i] : ' ';
                var alnum = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                var camel = token.Length > 0 && c >= 'A' && c <= 'Z' && name[i - 1] >= 'a' && name[i - 1] <= 'z';
                if (alnum && !camel)
                {
                    token.Append(char.ToLowerInvariant(c));
                    continue;
                }
                if (token.Length > 0 && k_Catalog.ContainsKey(token.ToString()))
                    return true;
                token.Clear();
                if (camel)
                    token.Append(char.ToLowerInvariant(c));
            }
            return false;
        }

        /// <summary>
        /// A leaf control owns its content and must not host arbitrary children; public so
        /// FigmaImportReport can flag a leaf tagged onto a container.
        /// </summary>
        public static bool IsLeafControlTag(string tag)
        {
            return !k_ContainerTags.Contains(tag);
        }

        // Node name -> catalog key: alphanumerics only, lowercased, so "Tab View" / "TabView" / "tab-view"
        // all reach "tabview". Nothing is truncated, because matching is exact.
        internal static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var s = name.Trim();
            var sb = new StringBuilder();
            for (var i = 0; i < s.Length; ++i)
            {
                var c = s[i];
                var alnum = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (!alnum)
                    continue; // spacing and punctuation are not part of the name, and never end it
                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>
        /// Sentinel namespace for a global-namespace control, emitted as an unprefixed UXML element.
        /// Never a real namespace: it is not a valid XML name.
        /// </summary>
        public const string GlobalNamespace = "*global*";

        /// <summary>
        /// The catalog tag a bare name reaches ("image" -> "Image"), or null. A bare catalog name means
        /// that engine control; any other bare name is a global-namespace type.
        /// </summary>
        public static string CanonicalCatalogTag(string bareName)
        {
            var key = NormalizeName(bareName);
            return key != null && k_Catalog.TryGetValue(key, out var cat) ? cat.tag : null;
        }

        /// <summary>
        /// Canonical catalog control names (mirrors BuildCatalog), so the controlMap editor
        /// dropdown and the resolver agree on the control set.
        /// </summary>
        public static readonly string[] CatalogControlNames =
        {
            "Button", "Label", "Toggle", "TextField", "DropdownField", "Slider", "SliderInt",
            "ProgressBar", "RadioButton", "HelpBox", "Image",
            "ScrollView", "Foldout", "TabView", "Tab", "GroupBox", "RadioButtonGroup", "ToggleButtonGroup"
        };

        /// <summary>
        /// The caption attribute a catalog control emits ("" for none or unknown/foreign), so the
        /// mapping drawer can exclude it from the extra-property dropdown.
        /// </summary>
        public static string CaptionAttrFor(string controlName)
        {
            if (string.IsNullOrEmpty(controlName))
                return string.Empty;
            return k_Catalog.TryGetValue(controlName.Trim().ToLowerInvariant(), out var r) ? r.captionAttr : string.Empty;
        }

        static Dictionary<string, ControlResolution> BuildCatalog()
        {
            var map = new Dictionary<string, ControlResolution>();

            // The control's own name is always a key, derived rather than repeated, so a catalog entry
            // can never be written with a key no Figma name could produce. Extra keys are aliases.
            void Register(ControlResolution resolution, string[] keys)
            {
                map[NormalizeName(resolution.tag)] = resolution;
                foreach (var key in keys)
                    map[key] = resolution;
            }

            // Leaf control: absorbs a single text child into captionAttr (see FigmaToUxml).
            void Add(string tag, string ussClass, string captionAttr, params string[] keys)
            {
                Register(new ControlResolution
                {
                    tag = tag, ns = k_EngineNamespace, ussClass = ussClass, captionAttr = captionAttr,
                    isControl = true, leaf = true
                }, keys);
            }

            // A leaf with somewhere to put an exported icon, so the layer becomes the control's own icon
            // rather than being dropped as redundant.
            void AddIconLeaf(string tag, string ussClass, string captionAttr, string iconAttr, params string[] keys)
            {
                Register(new ControlResolution
                {
                    tag = tag, ns = k_EngineNamespace, ussClass = ussClass, captionAttr = captionAttr,
                    iconAttr = iconAttr, isControl = true, leaf = true
                }, keys);
            }

            // Container control: keeps its children; headerAttr (if any) is filled from the node name.
            void AddContainer(string tag, string ussClass, string headerAttr, params string[] keys)
            {
                Register(new ControlResolution
                {
                    tag = tag, ns = k_EngineNamespace, ussClass = ussClass, captionAttr = headerAttr,
                    isControl = true, container = true
                }, keys);
            }

            // As above, but a lone text inside is the header rather than content.
            void AddChipContainer(string tag, string ussClass, string headerAttr, params string[] keys)
            {
                Register(new ControlResolution
                {
                    tag = tag, ns = k_EngineNamespace, ussClass = ussClass, captionAttr = headerAttr,
                    isControl = true, container = true, headerFromContent = true
                }, keys);
            }

            AddIconLeaf("Button", "unity-button", "text", "icon-image");
            Add("Label", "unity-label", "text", "text"); // "text": design-system Text components -> Label
            Add("Toggle", "unity-toggle", "", "checkbox");
            Add("TextField", "unity-text-field", "placeholder-text", "input", "inputfield");
            Add("DropdownField", "unity-popup-field", "", "dropdown");
            Add("Slider", "unity-slider", "");
            Add("SliderInt", "unity-slider-int", "");
            Add("ProgressBar", "unity-progress-bar", "", "progress");
            Add("RadioButton", "unity-radio-button", "label");
            Add("HelpBox", "unity-help-box", "");
            Add("Image", "unity-image", "");

            AddContainer("ScrollView", "unity-scroll-view", "", "scroll");
            AddContainer("Foldout", "unity-foldout", "text");
            AddContainer("TabView", "unity-tab-view", "", "tabs");
            AddChipContainer("Tab", "unity-tab", "label");
            AddContainer("GroupBox", "unity-group-box", "text");
            AddContainer("RadioButtonGroup", "unity-radio-button-group", "label", "radiogroup");
            AddContainer("ToggleButtonGroup", "unity-toggle-button-group", "label", "togglegroup");

            return map;
        }
    }
}
