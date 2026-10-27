using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Parsed import: flat node array plus parent/child relationships and per-node CSS lookup.
    /// </summary>
    class FigmaImportContext
    {
        public FigmaNode[] Nodes;
        public List<int>[] Children;
        public List<int> Roots;
        public int[] Order;

        FigmaImportSettings m_ResolutionSettings;
        ControlMap.ControlResolution[] m_Resolutions;
        bool[] m_Resolved;

        internal bool TryGetResolution(int index, FigmaImportSettings settings,
            out ControlMap.ControlResolution resolution)
        {
            resolution = default;
            if (m_Resolutions == null || !ReferenceEquals(m_ResolutionSettings, settings)
                || index < 0 || index >= m_Resolutions.Length || !m_Resolved[index])
                return false;
            resolution = m_Resolutions[index];
            return true;
        }

        internal void CacheResolution(int index, FigmaImportSettings settings,
            ControlMap.ControlResolution resolution)
        {
            if (Nodes == null || index < 0 || index >= Nodes.Length)
                return;
            if (m_Resolutions == null || !ReferenceEquals(m_ResolutionSettings, settings))
            {
                m_ResolutionSettings = settings;
                m_Resolutions = new ControlMap.ControlResolution[Nodes.Length];
                m_Resolved = new bool[Nodes.Length];
            }
            m_Resolutions[index] = resolution;
            m_Resolved[index] = true;
        }

        /// <summary>
        /// USS class prefix namespacing generated classes away from hand-authored USS; empty by default.
        /// </summary>
        public string ClassPrefix = string.Empty;

        /// <summary>
        /// Per-node component class, filled by FigmaComponentStyleModel; null for non-component nodes and templated ui:Instance elements.
        /// </summary>
        public string[] SharedClassByIndex;

        /// <summary>
        /// Per-node inline style decls: delta + placement for components, full body for non-components, placement only for templated instances.
        /// </summary>
        public List<(string prop, string value)>[] InlineDeclsByIndex;

        /// <summary>
        /// One render-ready USS rule per distinct component, in first-occurrence order.
        /// </summary>
        public List<FigmaComponentStyleModel.ComponentRule> ComponentRules;

        /// <summary>
        /// Per-node imported-image URI, for a node whose exported PNG a control absorbs as its icon. Bare,
        /// because it lands in a UXML attribute; USS wraps it in url(...) itself.
        /// </summary>
        public string[] ImageUriByIndex;

        public string ImageUri(int index) => ImageUriByIndex?[index];

        public string SharedClass(int index) => SharedClassByIndex?[index];

        public List<(string prop, string value)> InlineDecls(int index) => InlineDeclsByIndex?[index];

        /// <summary>
        /// Generated class name: optional prefix + kebab-cased name, always a valid USS identifier.
        /// </summary>
        public string ClassNameFrom(string rawName)
        {
            var name = Kebab(FigmaPathGuard.SanitizeName(rawName));
            var prefix = string.IsNullOrEmpty(ClassPrefix) ? "" : Kebab(FigmaPathGuard.SanitizeName(ClassPrefix));
            return EnsureIdentifier(prefix.Length == 0 ? name : prefix + "-" + name);
        }

        // Lower-case, single-dash-separated: "CTA / Primary" -> "cta-primary".
        static string Kebab(string sanitized)
        {
            if (string.IsNullOrEmpty(sanitized))
                return sanitized;

            var lower = sanitized.ToLowerInvariant();
            while (lower.Contains("--"))
                lower = lower.Replace("--", "-");
            return lower.Trim('-');
        }

        /// <summary>
        /// USS identifiers can't start with a digit; "3d-icon" -> "_3d-icon".
        /// </summary>
        public static string EnsureIdentifier(string cls)
        {
            if (string.IsNullOrEmpty(cls))
                return "node";
            var c = cls[0];
            var valid = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
            return valid ? cls : "_" + cls;
        }

        public string Css(int index, string key)
        {
            var css = Nodes[index].css;
            if (css == null)
                return null;

            for (var i = 0; i < css.Length; ++i)
            {
                if (css[i].k == key)
                    return css[i].v;
            }

            return null;
        }
    }
}
