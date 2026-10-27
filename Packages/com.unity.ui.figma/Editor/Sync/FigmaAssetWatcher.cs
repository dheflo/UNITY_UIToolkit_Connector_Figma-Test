using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Untracks a screen when the user deletes or renames its generated .uxml. Edits are surfaced live by
    /// the write-back scans, so there is no watcher-driven enqueue.
    /// </summary>
    class FigmaAssetWatcher : AssetPostprocessor
    {
        static int s_SuppressDepth;

        /// <summary>
        /// Suppresses pruning while an inbound import regenerates assets, so the import's own writes are
        /// not read as user deletions. Counted and scoped, so nested or failed imports cannot strand it.
        /// </summary>
        public static IDisposable SuppressOutboundScope()
        {
            return new SuppressScope();
        }

        static bool Suppressed => Volatile.Read(ref s_SuppressDepth) > 0;

        sealed class SuppressScope : IDisposable
        {
            bool m_Disposed;

            public SuppressScope()
            {
                Interlocked.Increment(ref s_SuppressDepth);
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                Interlocked.Decrement(ref s_SuppressDepth);
            }
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Cheap checks first: this runs on every import, including the one at startup.
            if (Suppressed || (deleted.Length == 0 && movedFrom.Length == 0))
                return;
            if (!TouchesGeneratedScreens(deleted) && !TouchesGeneratedScreens(movedFrom))
                return;

            foreach (var path in deleted)
                PruneIfGeneratedScreen(path);

            // A rename leaves the old entry behind, which auto-push would then resurrect.
            for (var i = 0; i < movedFrom.Length; ++i)
            {
                var from = movedFrom[i];
                var to = i < moved.Length ? moved[i] : string.Empty;
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    PruneIfGeneratedScreen(from);
            }
        }

        static bool TouchesGeneratedScreens(string[] paths)
        {
            foreach (var path in paths)
                if (IsGeneratedScreen(path))
                    return true;
            return false;
        }

        static bool IsGeneratedScreen(string path)
        {
            return path.StartsWith("Assets/Figma/", StringComparison.Ordinal)
                && path.EndsWith(".uxml", StringComparison.Ordinal);
        }

        static void PruneIfGeneratedScreen(string path)
        {
            if (!IsGeneratedScreen(path))
                return;
            var full = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
            var manifestFull = Path.Combine(Path.GetDirectoryName(full), ".figma-manifest.json");
            FigmaManifestIO.PruneScreen(manifestFull, Path.GetFileNameWithoutExtension(path));
        }
    }
}
