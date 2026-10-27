using System;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Everything the connector does, with no transport attached. The Pipeline command layer is a thin
    /// adapter over these calls, and the tests drive them directly.
    /// </summary>
    /// <remarks>
    /// Main thread only, except <see cref="StageImage"/>. Marshaling belongs to the adapter, which is
    /// the part that knows what thread it is on.
    /// </remarks>
    static class FigmaConnectorService
    {
        /// <summary>Returned when the import throws, so the plugin always gets JSON back.</summary>
        public const string k_ImportFailed = "{\"ok\":false,\"error\":\"import failed\"}";

        /// <summary>
        /// Prepares the shared state an adapter needs before serving: currently the image staging area.
        /// </summary>
        public static void Initialize(string dataPath)
        {
            FigmaImageStore.EnsureInitialized(dataPath);
        }

        /// <summary>
        /// Imports a wire payload, returning the JSON result the plugin reads (ok, counts, notes).
        /// Suppresses the outbound watcher so the import's own writes are not read as user edits.
        /// </summary>
        public static string Import(string payloadJson)
        {
            using (FigmaAssetWatcher.SuppressOutboundScope())
            {
                try
                {
                    return FigmaImportPipeline.Import(payloadJson);
                }
                catch (Exception e)
                {
                    Debug.LogError("[UI Toolkit Connector] import failed: " + e);
                    return k_ImportFailed;
                }
            }
        }

        /// <summary>
        /// Stages a rasterized fill under the key the plugin named it with; the next import promotes it
        /// into the project. Plain file IO, no Unity API, so it is safe off the main thread and an adapter
        /// should not marshal it: large uploads would then queue behind editor work.
        /// </summary>
        public static bool StageImage(string key, byte[] png)
        {
            return FigmaImageStore.Stage(key, png);
        }

        /// <summary>
        /// The write-back queues reconciled against the live diff, plus the screen roots the plugin needs
        /// to scope auto-push to this file.
        /// </summary>
        public static FigmaConnectorCommands Drain()
        {
            return new FigmaConnectorCommands
            {
                changes = FigmaTokenWriteBack.Reconcile().ToArray(),
                props = FigmaPropertyWriteBack.Reconcile().ToArray(),
                roots = FigmaManifestIO.TrackedRoots().ToArray()
            };
        }

        /// <summary>
        /// Drops applied changes from the queues and advances their baselines. Unacked changes stay
        /// queued, so a failed apply is retried rather than silently lost.
        /// </summary>
        public static void Ack(string[] tokenKeys, string[] propKeys)
        {
            if (tokenKeys != null)
                FigmaTokenWriteBack.Ack(tokenKeys);
            if (propKeys != null)
                FigmaPropertyWriteBack.Ack(propKeys);
        }

        /// <summary>
        /// How this editor identifies itself to the plugin: a human label plus a stable per-project id.
        /// </summary>
        public static FigmaProjectIdentity Identity(string projectRoot)
        {
            return new FigmaProjectIdentity
            {
                name = new System.IO.DirectoryInfo(projectRoot).Name,
                id = FigmaProjectId.For(projectRoot)
            };
        }
    }

    /// <summary>
    /// One drain of the write-back queues, serialized to the plugin verbatim.
    /// </summary>
    [Serializable]
    struct FigmaConnectorCommands
    {
        public FigmaTokenChange[] changes;
        public FigmaPropertyChange[] props;
        public FigmaRootRef[] roots;
    }

    /// <summary>
    /// The editor's self-description, so the plugin can show which one it is connected to.
    /// </summary>
    struct FigmaProjectIdentity
    {
        public string name;
        public string id;
    }
}
