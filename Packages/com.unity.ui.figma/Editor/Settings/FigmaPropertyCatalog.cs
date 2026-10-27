using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Component property names and types seen at import, per component, so the mapping drawer can
    /// suggest them instead of relying on free text. Derived data: lives in Library, rebuilt by any
    /// push, capped so it never grows past a few KB.
    /// </summary>
    [FilePath("Library/FigmaPropertyCatalog.asset", FilePathAttribute.Location.ProjectFolder)]
    class FigmaPropertyCatalog : ScriptableSingleton<FigmaPropertyCatalog>
    {
        // Sized for reality, bounded against pathology: subtree attribution can credit a composite with
        // every nested instance's properties, and the catalog spans every Figma file the project imports.
        // Worst case is a few hundred KB in Library; typical is a few KB.
        const int k_MaxComponents = 512;
        const int k_MaxPropsPerComponent = 64;

        [Serializable]
        internal struct CatalogProp
        {
            public string name; // base name, "#id" suffix stripped
            public string type; // "TEXT" | "BOOLEAN"
        }

        [Serializable]
        class Entry
        {
            public string component; // normalized instance name
            public List<CatalogProp> props = new List<CatalogProp>();
        }

        [SerializeField] List<Entry> m_Entries = new List<Entry>();

        [NonSerialized] Dictionary<string, List<CatalogProp>> m_Lookup;

        /// <summary>
        /// Records the import's component properties. Each prop-bearing node credits every enclosing
        /// instance, so a record matching any of those names suggests the props its rows can reach.
        /// </summary>
        public static void Record(FigmaImportContext context)
        {
            Dictionary<string, Dictionary<string, string>> found = null;
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var props = context.Nodes[i].props;
                if (props == null || props.Length == 0)
                    continue;
                for (var walk = i; walk >= 0; walk = context.Nodes[walk].parentIndex)
                {
                    var node = context.Nodes[walk];
                    if (node.type != "INSTANCE" && node.type != "COMPONENT")
                        continue;
                    var key = ControlMap.NormalizeName(node.name);
                    if (key == null)
                        continue;
                    if (found == null)
                        found = new Dictionary<string, Dictionary<string, string>>();
                    if (!found.TryGetValue(key, out var set))
                        found[key] = set = new Dictionary<string, string>();
                    foreach (var p in props)
                    {
                        // Wire hygiene: only the two suggestible types, names at a sane length.
                        if (p.t != "TEXT" && p.t != "BOOLEAN")
                            continue;
                        var baseName = BaseName(p.k);
                        if (baseName != null && baseName.Length <= 100 && set.Count < k_MaxPropsPerComponent)
                            set[baseName] = p.t;
                    }
                }
            }
            if (found != null)
                instance.Apply(found);
        }

        /// <summary>
        /// The properties recorded for a Figma name, or an empty list. Never null.
        /// </summary>
        public static IReadOnlyList<CatalogProp> PropsFor(string figmaName)
        {
            var key = ControlMap.NormalizeName(figmaName);
            if (key == null)
                return Array.Empty<CatalogProp>();
            var lookup = instance.Lookup();
            return lookup.TryGetValue(key, out var props) ? props : (IReadOnlyList<CatalogProp>)Array.Empty<CatalogProp>();
        }

        [NonSerialized] bool m_EvictionLogged;

        void Apply(Dictionary<string, Dictionary<string, string>> found)
        {
            var evicted = 0;
            foreach (var pair in found)
            {
                for (var i = m_Entries.Count - 1; i >= 0; --i)
                    if (m_Entries[i].component == pair.Key)
                        m_Entries.RemoveAt(i);
                // Oldest first; a refreshed component re-enters at the tail.
                if (m_Entries.Count >= k_MaxComponents)
                {
                    m_Entries.RemoveAt(0);
                    evicted++;
                }
                var entry = new Entry { component = pair.Key };
                foreach (var prop in pair.Value)
                    entry.props.Add(new CatalogProp { name = prop.Key, type = prop.Value });
                m_Entries.Add(entry);
            }
            if (evicted > 0 && !m_EvictionLogged)
            {
                m_EvictionLogged = true;
                Debug.Log("[UI Toolkit Connector] The property suggestion catalog is full ("
                    + k_MaxComponents + " components); suggestions for the least recently imported "
                    + "components were dropped. Free-text property names keep working.");
            }
            m_Lookup = null;
            Save(true);
        }

        Dictionary<string, List<CatalogProp>> Lookup()
        {
            if (m_Lookup == null)
            {
                m_Lookup = new Dictionary<string, List<CatalogProp>>(m_Entries.Count);
                foreach (var entry in m_Entries)
                    m_Lookup[entry.component] = entry.props;
            }
            return m_Lookup;
        }

        static string BaseName(string full)
        {
            if (string.IsNullOrEmpty(full))
                return null;
            var hash = full.LastIndexOf('#');
            var name = hash > 0 ? full.Substring(0, hash).Trim() : full.Trim();
            return name.Length == 0 ? null : name;
        }
    }
}
