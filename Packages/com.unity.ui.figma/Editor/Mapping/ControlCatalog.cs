using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// The UXML controls the mapping drawer can target, and their authorable attributes, discovered
    /// through public reflection over UxmlSerializedData and the UxmlElement/UxmlAttribute attributes.
    /// </summary>
    static class ControlCatalog
    {
        const BindingFlags k_Declared =
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static List<DiscoveredControl> s_Controls;
        static Dictionary<string, Type> s_ByFullName;

        public static IReadOnlyList<DiscoveredControl> Controls()
        {
            EnsureCatalog();
            return s_Controls;
        }

        public static IReadOnlyList<ControlAttrInfo> Attributes(string fullName)
        {
            var list = new List<ControlAttrInfo>();
            if (string.IsNullOrEmpty(fullName))
                return list;

            EnsureCatalog();
            if (!s_ByFullName.TryGetValue(fullName, out var control))
                return list;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            // Walking down from the control means a derived member shadows the base one of the same name.
            for (var t = control; t != null && typeof(VisualElement).IsAssignableFrom(t); t = t.BaseType)
            {
                try
                {
                    foreach (var member in Members(t))
                    {
                        if (!TryDescribe(member, control, out var info) || !seen.Add(info.name))
                            continue;
                        list.Add(info);
                    }
                }
                catch
                {
                    // Unresolvable member types throw on reflection; keep what the other levels gave us.
                }
            }
            return list;
        }

        // Built into locals and published only once complete, so a throw cannot cache a partial catalog.
        static void EnsureCatalog()
        {
            if (s_Controls != null)
                return;

            var controls = new List<DiscoveredControl>();
            var byFullName = new Dictionary<string, Type>(StringComparer.Ordinal);

            void Add(string fullName, Type element)
            {
                if (string.IsNullOrEmpty(fullName) || !byFullName.TryAdd(fullName, element))
                    return;
                var dot = fullName.LastIndexOf('.');
                var ns = dot > 0 ? fullName.Substring(0, dot) : string.Empty;
                controls.Add(new DiscoveredControl
                {
                    ns = ns,
                    tag = dot > 0 ? fullName.Substring(dot + 1) : fullName,
                    fullName = fullName,
                    engine = ns == "UnityEngine.UIElements"
                });
            }

            // Every [UxmlElement] gets a generated nested UxmlSerializedData, so deriving from it finds the
            // authorable controls without touching the registry.
            foreach (var dataType in TypeCache.GetTypesDerivedFrom<UxmlSerializedData>())
            {
                try
                {
                    if (dataType.IsAbstract)
                        continue;
                    var element = dataType.DeclaringType;
                    if (element == null || element.ContainsGenericParameters || !typeof(VisualElement).IsAssignableFrom(element))
                        continue;

                    // The registry registers a renamed element under both keys, so mappings authored against
                    // either one resolve. Matching that here keeps assets portable between the two hosts.
                    var attr = element.GetCustomAttribute<UxmlElementAttribute>();
                    if (attr != null && !string.IsNullOrEmpty(attr.name) && attr.name != element.Name)
                    {
                        Add(string.IsNullOrEmpty(element.Namespace)
                            ? attr.name
                            : element.Namespace + "." + attr.name, element);
                    }
                    Add(element.FullName, element);
                }
                catch
                {
                    // A type from an assembly with unresolvable dependencies; skip it, keep the rest.
                }
            }

            s_ByFullName = byFullName;
            s_Controls = controls;
        }

        static IEnumerable<MemberInfo> Members(Type t)
        {
            foreach (var p in t.GetProperties(k_Declared))
                yield return p;
            foreach (var f in t.GetFields(k_Declared))
                yield return f;
        }

        static bool TryDescribe(MemberInfo member, Type control, out ControlAttrInfo info)
        {
            info = default;
            // A UxmlObject slot carries [UxmlObjectReference] instead, so it never reaches this test.
            var attr = member.GetCustomAttribute<UxmlAttributeAttribute>(false);
            if (attr == null)
                return false;

            var type = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
            var name = string.IsNullOrEmpty(attr.name) ? UxmlName(member.Name) : attr.name;
            if (string.IsNullOrEmpty(name))
                return false;

            info = new ControlAttrInfo
            {
                name = name,
                type = type,
                typeName = type != null ? type.Name : string.Empty,
                defaultValue = string.Empty,
                inherited = member.DeclaringType != control
            };
            return true;
        }

        /// <summary>
        /// Mirrors the UXML source generator's member-name conversion (lowValue -> low-value). Deliberately
        /// naive about acronyms and digits, because the generator is: myURLValue -> my-u-r-l-value.
        /// </summary>
        static string UxmlName(string member)
        {
            if (string.IsNullOrEmpty(member))
                return member;
            var sb = new System.Text.StringBuilder(member.Length + 4);
            for (var i = 0; i < member.Length; i++)
            {
                var c = member[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                        sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
