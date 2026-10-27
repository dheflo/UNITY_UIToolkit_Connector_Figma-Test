using System.IO;
using UnityEditor;
using UnityEngine;
// UnityEditor has a legacy PackageInfo of its own, so the one we want is aliased rather than imported.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Connector lifecycle. There is no server to start, so this only primes the service before any
    /// command can reach it.
    /// </summary>
    static class FigmaPipelineConnector
    {
        static string s_Version;

        /// <summary>
        /// The package version figma_status reports. Read from package.json so a release cannot leave it
        /// behind, and cached because a domain reload is the only thing that can change it.
        /// </summary>
        public static string Version
        {
            get
            {
                if (s_Version == null)
                {
                    var info = PackageInfo.FindForAssembly(typeof(FigmaPipelineConnector).Assembly);
                    s_Version = info != null ? info.version : "0.0.0"; // embedded under Assets, not installed
                }
                return s_Version;
            }
        }

        [InitializeOnLoadMethod]
        static void Init()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            Prime();
        }

        // Application.dataPath is main-thread only, so the staging directory has to be resolved here
        // rather than on first use by a command that deliberately runs off the main thread.
        static void Prime()
        {
            FigmaConnectorService.Initialize(Application.dataPath);
        }

        internal static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        }
    }
}
