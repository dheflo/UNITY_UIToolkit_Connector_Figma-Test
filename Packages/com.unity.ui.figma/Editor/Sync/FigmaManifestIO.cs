using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Reads/writes the .figma-manifest.json sidecar for the multi-screen
    /// shape on read. Editor-only; the pure schema/diff helpers live in FigmaSyncManifest.
    /// </summary>
    static class FigmaManifestIO
    {
        const string k_BackupSuffix = ".bak";

        /// <summary>
        /// Reads the sidecar, falling back to the .bak left by the previous write when the main file is
        /// unreadable. Returns null only when there is genuinely no baseline, and warns when one existed
        /// but could not be read (silently treating that as "no baseline" turns every property into a
        /// phantom edit).
        /// </summary>
        public static FigmaSyncManifest ReadFile(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;

            var manifest = TryRead(fullPath, out var existed);
            if (manifest != null)
                return manifest;

            var backup = fullPath + k_BackupSuffix;
            var fromBackup = TryRead(backup, out var hadBackup);
            if (fromBackup != null)
            {
                Debug.LogWarning("[UI Toolkit Connector] " + fullPath + " was unreadable; recovered the previous"
                    + " baseline from " + Path.GetFileName(backup) + ".");
                return fromBackup;
            }

            if (existed || hadBackup)
                Debug.LogWarning("[UI Toolkit Connector] " + fullPath + " is corrupt and no usable backup remains;"
                    + " re-import the screen to rebuild its write-back baseline.");
            return null;
        }

        static FigmaSyncManifest TryRead(string fullPath, out bool existed)
        {
            existed = File.Exists(fullPath);
            if (!existed)
                return null;
            try { return Parse(File.ReadAllText(fullPath)); }
            catch { return null; }
        }

        /// <summary>
        /// Parses the multi-screen shape; if a sidecar predates it (no screens), wraps its flat
        /// generated/tokens/nodes into a single screen entry.
        /// </summary>
        public static FigmaSyncManifest Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var manifest = JsonUtility.FromJson<FigmaSyncManifest>(json);
            if (manifest == null)
                return null;
            if (manifest.screens == null)
                manifest.screens = new List<FigmaScreenManifest>();

            return manifest;
        }

        /// <summary>
        /// Writes the sidecar atomically: full write to a temp file, then a replace that keeps the previous
        /// copy as .bak. An interrupted write can no longer truncate the baseline in place.
        /// </summary>
        public static void Write(string fullPath, FigmaSyncManifest manifest)
        {
            if (string.IsNullOrEmpty(fullPath))
                return;

            var temp = fullPath + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(manifest, true));

                if (!File.Exists(fullPath))
                {
                    File.Move(temp, fullPath);
                    return;
                }

                try
                {
                    File.Replace(temp, fullPath, fullPath + k_BackupSuffix, true);
                }
                catch (Exception)
                {
                    // Replace is unsupported or blocked (some network and virtual filesystems); losing the
                    // baseline entirely is worse than a brief window with no manifest.
                    File.Delete(fullPath);
                    File.Move(temp, fullPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI Toolkit Connector] could not write manifest: " + e.Message);
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception) { /* the temp file is already gone or locked */ }
            }
        }

        /// <summary>
        /// Every imported screen root id (tagged by Figma file) across all Assets/Figma/ manifests, for live
        /// sync's ancestor intersect. Touches Application.dataPath; main thread only.
        /// </summary>
        public static List<FigmaRootRef> TrackedRoots()
        {
            var roots = new List<FigmaRootRef>();
            var figmaRoot = Path.Combine(Application.dataPath, "Figma"); // FigmaImportPipeline's root folder
            if (!Directory.Exists(figmaRoot))
                return roots;

            foreach (var dir in Directory.GetDirectories(figmaRoot))
            {
                var manifest = ReadFile(Path.Combine(dir, ".figma-manifest.json"));
                if (manifest?.screens == null || string.IsNullOrEmpty(manifest.fileName))
                    continue;
                foreach (var screen in manifest.screens)
                    if (screen != null && !string.IsNullOrEmpty(screen.rootNodeId))
                        roots.Add(new FigmaRootRef { file = manifest.fileName, id = screen.rootNodeId });
            }
            return roots;
        }

        /// <summary>
        /// Every imported Figma file under Assets/Figma/ (folder name + its manifest), for the connector
        /// window's read-only ledger view. Reads the filesystem, so call on the main thread.
        /// </summary>
        public static List<FigmaImportFolder> AllImports()
        {
            var imports = new List<FigmaImportFolder>();
            var figmaRoot = Path.Combine(Application.dataPath, "Figma");
            if (!Directory.Exists(figmaRoot))
                return imports;

            foreach (var dir in Directory.GetDirectories(figmaRoot))
            {
                var manifest = ReadFile(Path.Combine(dir, ".figma-manifest.json"));
                if (manifest?.screens == null || string.IsNullOrEmpty(manifest.fileName))
                    continue;
                imports.Add(new FigmaImportFolder { folder = Path.GetFileName(dir), manifest = manifest });
            }
            return imports;
        }

        /// <summary>
        /// Untracks a deleted screen: drops its entry from the manifest at fullPath so auto-push won't
        /// recreate it. No-op when the manifest or a matching entry is absent.
        /// </summary>
        public static void PruneScreen(string fullPath, string assetName)
        {
            var manifest = ReadFile(fullPath);
            if (manifest != null && manifest.RemoveScreensByAsset(assetName) > 0)
                Write(fullPath, manifest);
        }

        /// <summary>
        /// Records applied tokens' new values as the baseline across all manifests, so they stop counting
        /// as a write-back diff once Figma has them. Main thread (filesystem).
        /// </summary>
        public static void AdvanceTokenBaselines(IEnumerable<FigmaTokenChange> applied)
        {
            ForEachManifest((dir, m) =>
            {
                var changed = false;
                foreach (var c in applied)
                    changed |= m.AdvanceTokenBaseline(c.id, c.value);
                return changed;
            });
        }

        /// <summary>
        /// Records applied node properties as baselines across all manifests. A master-targeted change
        /// advances every sharer of its class except inline overriders (checked against the current .uxml).
        /// </summary>
        public static void AdvanceNodeBaselines(IEnumerable<FigmaPropertyChange> applied)
        {
            ForEachManifest((dir, m) =>
            {
                // Lazy per-document path map: doc -> (structural path -> element attrs), for the inline check.
                var docPaths = new Dictionary<string, Dictionary<string, (string tag, string classTokens, string inlineStyle)>>();
                Dictionary<string, (string tag, string classTokens, string inlineStyle)> PathsFor(string doc)
                {
                    if (string.IsNullOrEmpty(doc))
                        return null;
                    if (!docPaths.TryGetValue(doc, out var paths))
                    {
                        // Guarded like the writes: doc comes from the manifest file, not from us.
                        paths = FigmaPathGuard.TryResolve(dir, doc + ".uxml", out var uxmlPath) && File.Exists(uxmlPath)
                            ? FigmaPropertyDiff.ParsePaths(File.ReadAllText(uxmlPath))
                            : null;
                        docPaths[doc] = paths;
                    }
                    return paths;
                }

                var changed = false;
                foreach (var c in applied)
                {
                    var prop = c.prop;
                    changed |= m.AdvanceNodeBaseline(c.figmaNodeId, prop, c.value, rec =>
                    {
                        var paths = PathsFor(rec.doc);
                        return paths != null && paths.TryGetValue(rec.path ?? string.Empty, out var el)
                            && FigmaPropertyDiff.InlineDeclares(el.inlineStyle, prop);
                    });
                }
                return changed;
            });
        }

        // Applies mutate to every manifest under Assets/Figma/ and writes back the ones that changed.
        static void ForEachManifest(Func<string, FigmaSyncManifest, bool> mutate)
        {
            var figmaRoot = Path.Combine(Application.dataPath, "Figma");
            if (!Directory.Exists(figmaRoot))
                return;

            foreach (var dir in Directory.GetDirectories(figmaRoot))
            {
                var path = Path.Combine(dir, ".figma-manifest.json");
                var manifest = ReadFile(path);
                if (manifest != null && mutate(dir, manifest))
                    Write(path, manifest);
            }
        }

    }

    /// <summary>
    /// One imported screen root, tagged by Figma file, sent to the plugin for the auto-push "refresh what
    /// already exists" intersect.
    /// </summary>
    [Serializable]
    struct FigmaRootRef
    {
        public string file;
        public string id;
    }

    /// <summary>
    /// One imported Figma file's output folder + its manifest, returned to the connector window's ledger view.
    /// </summary>
    struct FigmaImportFolder
    {
        public string folder;              // folder name under Assets/Figma/ (e.g. "Level 3")
        public FigmaSyncManifest manifest; // carries fileName + screens[]
    }
}
