using System;
using System.IO;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Captures the most recent plugin push and re-runs it through the connector: push once from Figma,
    /// then iterate headlessly. Also callable via -executeMethod.
    /// </summary>
    static class FigmaConnectorReplay
    {
        /// <summary>
        /// Where the most recent raw push is stashed; Library/ is unimported and gitignored, so writing
        /// on every push is safe. Only the Pipeline adapter writes it, so test imports leave it alone.
        /// </summary>
        public static string CaptureFilePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "UIToolkitConnector", "last-import.json"));

        public static void Capture(string json)
        {
            try
            {
                var path = CaptureFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI Toolkit Connector] could not capture payload for replay: " + e.Message);
            }
        }

        public static void ReplayLast()
        {
            var path = CaptureFilePath;
            if (!File.Exists(path))
            {
                Debug.LogWarning("[UI Toolkit Connector] no captured payload at " + path
                    + "; push once from the Figma plugin, then replay.");
                return;
            }

            var result = FigmaConnectorService.Import(File.ReadAllText(path));
            if (result != null && result.Contains("\"ok\":true"))
                Debug.Log("[UI Toolkit Connector] replayed " + path + " -> " + result);
            else
                Debug.LogError("[UI Toolkit Connector] replay of " + path + " failed -> " + result);
        }
    }
}
