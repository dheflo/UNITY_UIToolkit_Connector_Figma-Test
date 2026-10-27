using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Loads the package's editor stylesheets.
    /// </summary>
    static class FigmaResources
    {
        const string k_Root = "Packages/com.unity.ui.figma/Editor/StyleSheets/";

        public static StyleSheet LoadStyleSheet(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Root + fileName);
        }
    }
}
