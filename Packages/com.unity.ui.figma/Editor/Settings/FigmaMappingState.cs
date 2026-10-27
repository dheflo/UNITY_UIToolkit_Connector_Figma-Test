using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Which FigmaImportSettings asset is active, stored per project so the mapping travels with it.
    /// </summary>
    [FilePath("ProjectSettings/FigmaMappingState.asset", FilePathAttribute.Location.ProjectFolder)]
    class FigmaMappingState : ScriptableSingleton<FigmaMappingState>
    {
        // Not a valid GUID, so it can never collide with a real one.
        const string k_ClearedGuid = "-";

        [SerializeField] string m_ActiveMappingGuid = string.Empty;

        /// <summary>
        /// The selected mapping asset, or null when unset, cleared, or deleted.
        /// </summary>
        public static FigmaImportSettings ActiveMapping()
        {
            var guid = instance.m_ActiveMappingGuid;
            if (string.IsNullOrEmpty(guid))
                return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<FigmaImportSettings>(path);
        }

        /// <summary>
        /// True when the user explicitly chose no mapping, as opposed to never having chosen. Find() needs
        /// the difference so it does not adopt the first asset it sees after a deliberate clear.
        /// </summary>
        public static bool MappingCleared => instance.m_ActiveMappingGuid == k_ClearedGuid;

        public static void SetActiveMapping(FigmaImportSettings settings)
        {
            instance.m_ActiveMappingGuid = settings == null
                ? k_ClearedGuid
                : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings));
            instance.Save(true);
        }
    }
}
