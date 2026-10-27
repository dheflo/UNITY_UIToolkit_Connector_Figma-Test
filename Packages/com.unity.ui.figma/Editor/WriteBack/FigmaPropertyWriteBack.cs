using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Unity->Figma property write-back queue: pushed node property changes awaiting plugin apply.
    /// Opt-in (a detected edit is queued only on push); SessionState-backed like FigmaTokenWriteBack.
    /// </summary>
    static class FigmaPropertyWriteBack
    {
        const string k_Key = "Unity.UIToolkit.Figma.PropertyWriteBackQueue";

        [System.Serializable]
        class Queue
        {
            public List<FigmaPropertyChange> props = new List<FigmaPropertyChange>();
        }

        static Queue Load()
        {
            var json = SessionState.GetString(k_Key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new Queue();
            return JsonUtility.FromJson<Queue>(json) ?? new Queue();
        }

        static void Save(Queue queue)
        {
            SessionState.SetString(k_Key, JsonUtility.ToJson(queue));
        }

        /// <summary>
        /// Stable ack key: latest edit for a given file+node+property wins. File-qualified so a same node id
        /// in another Figma file can't collide (acking one file's change must not pop another's).
        /// </summary>
        public static string KeyOf(FigmaPropertyChange c)
        {
            return c.file + "|" + c.figmaNodeId + "|" + c.prop;
        }

        public static void Enqueue(IEnumerable<FigmaPropertyChange> changes)
        {
            var queue = Load();
            foreach (var change in changes)
            {
                var key = KeyOf(change);
                queue.props.RemoveAll(c => KeyOf(c) == key);
                queue.props.Add(change);
            }
            Save(queue);
        }

        public static List<FigmaPropertyChange> Pending()
        {
            return Load().props;
        }

        public static int Count => Load().props.Count;

        /// <summary>
        /// The plugin applied these to Figma: advance the baseline to the applied values (so the same edits
        /// stop showing as a diff), then drop them from the queue.
        /// </summary>
        public static void Ack(IEnumerable<string> keys)
        {
            var set = new HashSet<string>(keys);
            var queue = Load();
            var applied = queue.props.FindAll(c => set.Contains(KeyOf(c)));
            if (applied.Count > 0)
                FigmaManifestIO.AdvanceNodeBaselines(applied);
            queue.props.RemoveAll(c => set.Contains(KeyOf(c)));
            Save(queue);
        }

        public static void Clear()
        {
            SessionState.EraseString(k_Key);
        }

        static readonly FigmaScanCache<FigmaPropertyChange> s_ScanCache = new FigmaScanCache<FigmaPropertyChange>();

        // Per-folder "can't sync" notes from the last diff, shown in the connector window so an edit outside
        // the write-back mapping is never a silent no-op.
        static readonly Dictionary<string, List<string>> s_Warnings = new Dictionary<string, List<string>>();

        /// <summary>
        /// Diffs every import folder against recorded baselines WITHOUT enqueuing (window preview).
        /// Cached per folder by manifest/.uss/.uxml mtimes, so an unchanged folder costs only stat calls.
        /// </summary>
        public static List<FigmaPropertyChange> Scan()
        {
            var changes = new List<FigmaPropertyChange>();
            var root = Path.Combine(Application.dataPath, "Figma");
            if (!Directory.Exists(root))
                return changes;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var manifestPath = Path.Combine(dir, ".figma-manifest.json");
                if (!File.Exists(manifestPath))
                    continue;
                changes.AddRange(s_ScanCache.GetOrCompute(dir, FolderSig(dir, manifestPath), () => ComputeFolder(dir, manifestPath)));
            }
            return changes;
        }

        /// <summary>
        /// "Can't sync" notes across all folders, one line per reason. Runs the cached Scan, so idle polls stay cheap.
        /// </summary>
        public static List<FigmaSyncWarning> ScanWarnings()
        {
            Scan();
            var entries = new List<string>();
            foreach (var kv in s_Warnings)
                foreach (var w in kv.Value)
                    if (!entries.Contains(w))
                        entries.Add(w);
            return FigmaPropertyDiff.AggregateWarnings(entries);
        }

        // Manifest + sorted .uss/.uxml mtimes: any screen edit or re-import invalidates the cached diff.
        // Size rides along with the timestamp: a branch switch or checkout can restore a file whose mtime
        // does not move the way an edit would, and coarse filesystems round ticks.
        static string FolderSig(string dir, string manifestPath)
        {
            var sb = new StringBuilder();
            AppendStamp(sb, manifestPath);
            AppendStamps(sb, dir, "*.uss");
            AppendStamps(sb, dir, "*.uxml");
            return sb.ToString();
        }

        static void AppendStamps(StringBuilder sb, string dir, string pattern)
        {
            var files = Directory.GetFiles(dir, pattern);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
                AppendStamp(sb, f);
        }

        static void AppendStamp(StringBuilder sb, string path)
        {
            var info = new FileInfo(path);
            sb.Append('|').Append(info.Name).Append(':');
            if (info.Exists)
                sb.Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length);
        }

        // Records are partitioned by their doc (screen or template .uxml) and each group diffs against its
        // own .uxml; records with no doc ride with the screen document.
        static List<FigmaPropertyChange> ComputeFolder(string dir, string manifestPath)
        {
            var changes = new List<FigmaPropertyChange>();
            var warnings = new List<string>();
            s_Warnings[dir] = warnings;
            var manifest = FigmaManifestIO.ReadFile(manifestPath);
            if (manifest?.screens == null)
                return changes;

            foreach (var screen in manifest.screens)
            {
                var hasNodes = screen?.nodes != null && screen.nodes.Count > 0;
                var hasStates = screen?.states != null && screen.states.Count > 0;
                if (screen == null || (!hasNodes && !hasStates) || string.IsNullOrEmpty(screen.assetName))
                    continue;
                var screenUss = Path.Combine(dir, screen.assetName + ".uss");
                if (!File.Exists(screenUss))
                    continue;
                var ussText = File.ReadAllText(screenUss);

                var byDoc = new Dictionary<string, List<FigmaNodeRecord>>();
                foreach (var node in hasNodes ? screen.nodes : new List<FigmaNodeRecord>())
                {
                    var doc = string.IsNullOrEmpty(node.doc) ? screen.assetName : node.doc;
                    if (!byDoc.TryGetValue(doc, out var group))
                        byDoc[doc] = group = new List<FigmaNodeRecord>();
                    group.Add(node);
                }

                foreach (var kv in byDoc)
                {
                    var uxmlPath = Path.Combine(dir, kv.Key + ".uxml");
                    changes.AddRange(FigmaPropertyDiff.Compute(kv.Value, ussText,
                        File.Exists(uxmlPath) ? File.ReadAllText(uxmlPath) : null, screen.components, warnings));
                }

                // Interaction-state rules live in the screen stylesheet and target their Figma variant.
                changes.AddRange(FigmaPropertyDiff.ComputeStates(screen.states, ussText, warnings));
            }

            // All changes in a folder belong to one Figma file; stamp it for scoping and the ack key.
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
        /// snapshots the value, so re-editing a pushed prop needs another Push.
        /// </summary>
        public static List<FigmaPropertyChange> Reconcile()
        {
            var scan = Scan();
            var live = new HashSet<string>();
            foreach (var c in scan)
                live.Add(KeyOf(c));
            var queue = Load();
            queue.props.RemoveAll(c => !live.Contains(KeyOf(c)));
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
