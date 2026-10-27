using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Project-wide Figma import mapping: font/control tables and a USS class prefix, applied to every
    /// import. Optional - with no asset the pipeline uses built-in defaults. Created from the settings page.
    /// </summary>
    internal class FigmaImportSettings : ScriptableObject, ISerializationCallbackReceiver
    {
        [Header("Mapping")]
        [Tooltip("Figma font family -> project Font/FontAsset for -unity-font-definition.")]
        public List<FontMapping> fontMap = new List<FontMapping>();

        [Tooltip("USS class prefix for generated classes (e.g. \"app\" -> .app-login-button). Empty = no " +
            "prefix. Namespaces generated classes away from your own USS.")]
        public string classPrefix;

        [Tooltip("Figma component/name -> a control. Applied before the built-in name convention.")]
        public List<ControlMapping> controlMap = new List<ControlMapping>();

        /// <summary>
        /// The mapped Font/FontAsset for a Figma family, or null when unmapped (caller falls back).
        /// </summary>
        public Object ResolveFont(string family)
        {
            if (string.IsNullOrEmpty(family) || fontMap == null)
                return null;

            var wanted = family.Trim();
            for (var i = 0; i < fontMap.Count; ++i)
            {
                var entry = fontMap[i];
                if (entry.font != null && !string.IsNullOrEmpty(entry.family)
                    && string.Equals(entry.family.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    return entry.font;
            }

            return null;
        }

        /// <summary>
        /// The first record whose figmaName matches, in list order. Matching ignores punctuation,
        /// spacing and case, the same as the name convention. An empty/"template" target forces a
        /// generated template.
        /// </summary>
        public bool TryResolveControl(string name, out ControlMapping mapping)
        {
            mapping = default;
            if (controlMap == null)
                return false;

            var key = ControlMap.NormalizeName(name);
            if (key == null)
                return false;

            for (var i = 0; i < controlMap.Count; ++i)
            {
                var entry = controlMap[i];
                if (ControlMap.NormalizeName(entry.figmaName) == key)
                {
                    mapping = entry;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The active mapping asset: the explicit selection, else the first one
        /// found, else null (pipeline uses built-in defaults).
        /// </summary>
        public static FigmaImportSettings Find()
        {
            var active = FigmaMappingState.ActiveMapping();
            if (active != null)
                return active;
            if (FigmaMappingState.MappingCleared)
                return null;   // chosen, not merely unset: do not adopt one behind the user's back

            // Sorted so a project holding several mappings always adopts the same one.
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(FigmaImportSettings)))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);

            foreach (var path in paths)
            {
                var settings = AssetDatabase.LoadAssetAtPath<FigmaImportSettings>(path);
                if (settings != null)
                    return settings;
            }
            return null;
        }

        /// <summary>
        /// Creates a mapping asset under Assets/Figma/ and makes it the active mapping.
        /// </summary>
        public static FigmaImportSettings CreateActiveAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Figma"))
                AssetDatabase.CreateFolder("Assets", "Figma");
            var config = CreateInstance<FigmaImportSettings>();
            AssetDatabase.CreateAsset(config, AssetDatabase.GenerateUniqueAssetPath("Assets/Figma/Figma Mapping.asset"));
            AssetDatabase.SaveAssets();
            FigmaMappingState.SetActiveMapping(config);
            return config;
        }

        public void OnBeforeSerialize() { }

        // Pre-row assets stored caption/icon destinations as fields; fold them into Design rows.
        public void OnAfterDeserialize()
        {
            if (controlMap == null)
                return;
            for (var i = 0; i < controlMap.Count; ++i)
            {
                var entry = controlMap[i];
                var caption = MigrateDesignRow(ref entry.bindings, entry.captionAttribute, FigmaBindingSource.DesignText);
                var icon = MigrateDesignRow(ref entry.bindings, entry.iconAttribute, FigmaBindingSource.DesignIcon);
                if (!caption && !icon)
                    continue;
                entry.captionAttribute = string.Empty;
                entry.iconAttribute = string.Empty;
                controlMap[i] = entry;
            }
        }

        static bool MigrateDesignRow(ref PropertyBinding[] bindings, string attribute, FigmaBindingSource source)
        {
            if (string.IsNullOrEmpty(attribute))
                return false;
            var n = bindings?.Length ?? 0;
            for (var i = 0; i < n; ++i)
                if (bindings[i].source == source || bindings[i].uxmlAttribute == attribute)
                    return true; // already routed; just clear the legacy field
            var grown = new PropertyBinding[n + 1];
            for (var i = 0; i < n; ++i)
                grown[i] = bindings[i];
            grown[n] = new PropertyBinding { uxmlAttribute = attribute, source = source, value = string.Empty };
            bindings = grown;
            return true;
        }
    }

    [Serializable]
    struct FontMapping
    {
        [Tooltip("Figma font family, e.g. \"Inter\".")]
        public string family;
        [Tooltip("Project Font or FontAsset.")]
        public Object font;
    }

    [Serializable]
    struct ControlMapping
    {
        [Tooltip("Match by Figma component or node name; punctuation, spacing and case are ignored.")]
        public string figmaName;
        [Tooltip("Target control: a UITK name (\"Button\") or a fully-qualified type " +
            "(\"MyCompany.UI.Button\"). Empty or \"template\" forces a generated template.")]
        public string uitkControl;
        // Legacy caption/icon destinations, folded into DesignText/DesignIcon rows on deserialize.
        // Kept so pre-row assets still read; never written or consulted elsewhere.
        [HideInInspector] public string captionAttribute;
        [HideInInspector] public string iconAttribute;
        [Tooltip("The UXML attributes this mapping emits, each from a fixed value, a Figma component " +
            "property, or the design's extracted text or icon.")]
        public PropertyBinding[] bindings;
    }
}
