using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Unity->Figma token write-back queue: pushed changes the plugin drains via GET /figma/commands and
    /// acks via POST /figma/ack. SessionState-backed so it survives domain reloads.
    /// </summary>
    static class FigmaTokenWriteBack
    {
        const string k_Key = "Unity.UIToolkit.Figma.WriteBackQueue";

        [System.Serializable]
        class Queue
        {
            public List<FigmaTokenChange> changes = new List<FigmaTokenChange>();
        }

        static Queue Load()
        {
            var json = SessionState.GetString(k_Key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new Queue();
            var queue = JsonUtility.FromJson<Queue>(json);
            return queue ?? new Queue();
        }

        static void Save(Queue queue)
        {
            SessionState.SetString(k_Key, JsonUtility.ToJson(queue));
        }

        /// <summary>
        /// Stable ack key, file-qualified so a same Variable id in another Figma file can't collide.
        /// </summary>
        public static string KeyOf(FigmaTokenChange c)
        {
            return c.file + "|" + c.id;
        }

        /// <summary>
        /// Adds changes; latest edit wins per file+variable id.
        /// </summary>
        public static void Enqueue(IEnumerable<FigmaTokenChange> changes)
        {
            var queue = Load();
            foreach (var change in changes)
            {
                var key = KeyOf(change);
                queue.changes.RemoveAll(c => KeyOf(c) == key);
                queue.changes.Add(change);
            }
            Save(queue);
        }

        public static List<FigmaTokenChange> Pending()
        {
            return Load().changes;
        }

        public static int Count => Load().changes.Count;

        /// <summary>
        /// The plugin applied these: advance baselines to the applied values, then drop from the queue.
        /// </summary>
        public static void Ack(IEnumerable<string> keys)
        {
            var set = new HashSet<string>(keys);
            var queue = Load();
            var applied = queue.changes.FindAll(c => set.Contains(KeyOf(c)));
            if (applied.Count > 0)
                FigmaManifestIO.AdvanceTokenBaselines(applied);
            queue.changes.RemoveAll(c => set.Contains(KeyOf(c)));
            Save(queue);
        }

        public static void Clear()
        {
            SessionState.EraseString(k_Key);
        }

        /// <summary>
        /// The pending queue as the JSON GET /figma/commands returns.
        /// </summary>
        public static string PendingJson()
        {
            return JsonUtility.ToJson(new Queue { changes = Pending() });
        }

        static readonly FigmaScanCache<FigmaTokenChange> s_ScanCache = new FigmaScanCache<FigmaTokenChange>();

        /// <summary>
        /// Diffs every folder's tokens.uss against recorded originals WITHOUT enqueuing (window preview).
        /// Cached per folder by manifest + tokens.uss mtime.
        /// </summary>
        public static List<FigmaTokenChange> Scan()
        {
            var changes = new List<FigmaTokenChange>();
            var root = Path.Combine(Application.dataPath, "Figma");
            if (!Directory.Exists(root))
                return changes;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var manifestPath = Path.Combine(dir, ".figma-manifest.json");
                var tokensPath = Path.Combine(dir, "tokens.uss");
                if (!File.Exists(manifestPath) || !File.Exists(tokensPath))
                    continue;

                var sig = File.GetLastWriteTimeUtc(manifestPath).Ticks + "|" + File.GetLastWriteTimeUtc(tokensPath).Ticks;
                changes.AddRange(s_ScanCache.GetOrCompute(dir, sig, () => ComputeFolder(manifestPath, tokensPath)));
            }
            return changes;
        }

        // tokens.uss is folder-shared (file-global Variables): union screens' originals by id, diff once.
        static List<FigmaTokenChange> ComputeFolder(string manifestPath, string tokensPath)
        {
            var manifest = FigmaManifestIO.ReadFile(manifestPath);
            if (manifest?.screens == null)
                return new List<FigmaTokenChange>();

            var tokens = new List<FigmaTokenRecord>();
            var seen = new HashSet<string>();
            foreach (var screen in manifest.screens)
                if (screen?.tokens != null)
                    foreach (var token in screen.tokens)
                        if (seen.Add(token.id))
                            tokens.Add(token);

            if (tokens.Count == 0)
                return new List<FigmaTokenChange>();

            // All changes in a folder belong to one Figma file; stamp it for scoping and the ack key.
            var changes = FigmaTokenDiff.Compute(tokens, File.ReadAllText(tokensPath));
            for (var i = 0; i < changes.Count; i++)
            {
                var c = changes[i];
                c.file = manifest.fileName;
                changes[i] = c;
            }
            return changes;
        }

        /// <summary>
        /// The pushed queue pruned to what's still a live diff (reverted/acked entries drop). Push
        /// snapshots the value, so re-editing a pushed key needs another Push.
        /// </summary>
        public static List<FigmaTokenChange> Reconcile()
        {
            var scan = Scan();
            var live = new HashSet<string>();
            foreach (var c in scan)
                live.Add(KeyOf(c));
            var queue = Load();
            queue.changes.RemoveAll(c => !live.Contains(KeyOf(c)));
            Save(queue);
            return Pending();
        }

        /// <summary>
        /// Pushes the selected detected changes into the queue GET /figma/commands drains.
        /// </summary>
        public static void Push(IEnumerable<string> keys)
        {
            var set = new HashSet<string>(keys);
            Enqueue(Scan().FindAll(c => set.Contains(KeyOf(c))));
        }
    }
}
