using UnityEditor;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Window menu entry for the connector. Always present: having the package installed is the opt-in, so
    /// there is no feature flag for the item to track.
    /// </summary>
    static class FigmaConnectorMenu
    {
        [MenuItem("Window/UI Toolkit/Connector for Figma design", false, 1000)]
        static void Open() => FigmaConnectorWindow.Open();
    }
}
