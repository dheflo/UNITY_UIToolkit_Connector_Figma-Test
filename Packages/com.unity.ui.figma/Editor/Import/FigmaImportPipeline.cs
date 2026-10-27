using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Parses the wire payload, generates USS/UXML under Assets/Figma/&lt;file&gt;/, imports them, and
    /// returns a JSON summary. Must run on the main thread (touches AssetDatabase).
    /// </summary>
    static class FigmaImportPipeline
    {
        const string k_RootFolder = "Figma";
        /// <summary>
        /// Wire schemas this package can read, newest first, advertised by figma_status. The plugin updates
        /// on its own, so it is the side that adapts by emitting the newest shape both know.
        /// </summary>
        internal static readonly int[] ReadableSchemas = { 1 };

        // One file write and one asset import each, so this is what bounds a push. Real pushes emit tens.
        const int k_MaxDocuments = 2000;

        public static string Import(string json)
        {
            var message = JsonUtility.FromJson<FigmaImportMessage>(json);
            if (message.nodes == null || message.nodes.Length == 0)
                return "{\"ok\":false,\"error\":\"no nodes\"}";

            // Refused rather than warned: a half-correct screen is harder to diagnose than a blocked push.
            if (Array.IndexOf(ReadableSchemas, message.schema) < 0)
                return "{\"ok\":false,\"schemaMismatch\":true,\"pluginSchema\":" + message.schema
                    + ",\"editorSchemas\":[" + string.Join(",", ReadableSchemas)
                    + "],\"error\":\"plugin wire schema " + message.schema
                    + " is not one this package reads (" + string.Join(", ", ReadableSchemas) + ")\"}";

            FigmaNodeDerive.Apply(message.nodes);

            var context = BuildContext(message);
            var settings = FigmaImportSettings.Find();
            context.ClassPrefix = settings != null ? settings.classPrefix : string.Empty;
            var assetName = AssetName(message, context);
            var library = FigmaComponentLibrary.Build(context, settings, assetName);

            // Modes count too: each becomes a stylesheet, so omitting them let an unbounded list through.
            var documents = library.Templates.Count + 1 + (message.modes != null ? message.modes.Length : 0);
            if (documents > k_MaxDocuments)
                return "{\"ok\":false,\"error\":\"this push would generate " + documents
                    + " documents, over the " + k_MaxDocuments
                    + " allowed in one import - push fewer screens at a time\"}";

            // Output folder is always the sanitized Figma file name, so a later settings asset never moves it.
            var subFolder = FigmaPathGuard.SanitizeName(string.IsNullOrEmpty(message.fileName) ? "Untitled" : message.fileName);

            var importRoot = Path.Combine(Application.dataPath, k_RootFolder);
            if (!FigmaPathGuard.TryResolve(importRoot, subFolder + "/" + assetName + ".uxml", out var uxmlFull)
                || !FigmaPathGuard.TryResolve(importRoot, subFolder + "/" + assetName + ".uss", out var ussFull))
                return "{\"ok\":false,\"error\":\"path rejected\"}";

            var projectFolder = "Assets/" + k_RootFolder + "/" + subFolder + "/";
            var prior = ReadManifest(importRoot, subFolder);
            var generated = new List<string>(); // paths (relative to the import folder) produced this run

            // Promote staged PNGs BEFORE USS generation so the emitted url() resolves on first import.
            FigmaImageStore.EnsureInitialized(Application.dataPath);
            // One map across both passes: an icon whose artwork matches a layer we already imported is the
            // same file, not a second copy of it.
            var importedByKey = new Dictionary<string, string>();
            var imageUrls = ResolveImages(context, importRoot, subFolder, projectFolder, generated, importedByKey);
            context.ImageUriByIndex = new string[context.Nodes.Length];
            foreach (var pair in imageUrls)
                context.ImageUriByIndex[pair.Key] = pair.Value;
            ResolveIconImages(context, importRoot, subFolder, projectFolder, generated, importedByKey);

            // Templates link tokens.uss only: a mode file on a template makes the template root define that
            // mode over the screen's, since UI Toolkit treats the element a sheet is attached to as :root.
            var tokenStyleSrcs = new List<string>();    // <Style src> file names shared by the screen and its templates
            var tokenProjectPaths = new List<string>(); // token USS project paths to import, base first

            var tokensUss = FigmaTokens.BuildTokensUss(message.variables);
            if (!string.IsNullOrEmpty(tokensUss)
                && FigmaPathGuard.TryResolve(importRoot, subFolder + "/tokens.uss", out var tokensFull))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tokensFull));
                File.WriteAllText(tokensFull, tokensUss);
                tokenStyleSrcs.Add("tokens.uss");
                tokenProjectPaths.Add(projectFolder + "tokens.uss");
                generated.Add("tokens.uss");
            }

            // All modes are written so a runtime swap has them; the screen links the ones its frame uses.
            var exportedModes = new List<string>();
            if (message.modes != null)
            {
                foreach (var mode in message.modes)
                {
                    var modeUss = FigmaTokens.BuildModeUss(message.modeVariables, mode);
                    var modeFile = FigmaTokens.ModeFileName(mode);
                    if (string.IsNullOrEmpty(modeUss)
                        || !FigmaPathGuard.TryResolve(importRoot, subFolder + "/" + modeFile, out var modeFull))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(modeFull));
                    File.WriteAllText(modeFull, modeUss);
                    tokenProjectPaths.Add(projectFolder + modeFile);
                    generated.Add(modeFile);
                    exportedModes.Add(mode);
                }
            }
            var screenStyleSrcs = new List<string>(tokenStyleSrcs);
            screenStyleSrcs.AddRange(LinkedModeFiles(message, generated));

            System.Func<string, string> resolveFont = family => ResolveFont(settings, family);
            System.Func<int, string> resolveBg = index =>
                imageUrls.TryGetValue(index, out var uri) ? "url(\"" + uri + "\")" : null;
            FigmaComponentStyleModel.Build(context, library, resolveFont, resolveBg);

            // Build UXML first, then emit USS only for referenced classes (drops rules for unrendered nodes).
            // Each document's emission map feeds the write-back baselines below.
            var emissionByDoc = new List<(string doc, List<FigmaToUxml.EmissionEntry> entries)>();
            var discarded = new List<int>();   // layers no element could hold; the report names them
            var screenEmission = new List<FigmaToUxml.EmissionEntry>();
            var screenUxml = FigmaToUxml.BuildScreen(context, assetName + ".uss", screenStyleSrcs, settings, library, screenEmission, discarded);
            emissionByDoc.Add((assetName, screenEmission));
            var allUxml = new List<string> { screenUxml };

            var templateFulls = new List<string>();
            var templateUxml = new List<string>();
            var templatePaths = new List<string>();
            foreach (var template in library.Templates)
            {
                if (!FigmaPathGuard.TryResolve(importRoot, subFolder + "/" + template.name + ".uxml", out var templateFull))
                    continue;
                var templateEmission = new List<FigmaToUxml.EmissionEntry>();
                var tu = FigmaToUxml.BuildTemplate(context, assetName + ".uss", tokenStyleSrcs, settings, library, template, templateEmission, discarded);
                emissionByDoc.Add((template.name, templateEmission));
                templateFulls.Add(templateFull);
                templateUxml.Add(tu);
                allUxml.Add(tu);
                templatePaths.Add(projectFolder + template.name + ".uxml");
                generated.Add(template.name + ".uxml");
            }

            var ussText = FigmaToUss.Build(context, settings, FigmaToUss.UsedClasses(allUxml));

            Directory.CreateDirectory(Path.GetDirectoryName(uxmlFull));
            File.WriteAllText(ussFull, ussText);
            File.WriteAllText(uxmlFull, screenUxml);
            for (var t = 0; t < templateFulls.Count; ++t)
                File.WriteAllText(templateFulls[t], templateUxml[t]);

            var ussProjectPath = projectFolder + assetName + ".uss";
            var uxmlProjectPath = projectFolder + assetName + ".uxml";
            generated.Add(assetName + ".uss");
            generated.Add(assetName + ".uxml");

            // One refresh instead of a forced import per file; Unity now picks the order. The finally is
            // load-bearing: leaking StopAssetEditing pauses the AssetDatabase for the session.
            var showProgress = templatePaths.Count + tokenProjectPaths.Count > 8;
            AssetDatabase.StartAssetEditing();
            try
            {
                if (showProgress)
                    EditorUtility.DisplayProgressBar("Import from Figma design",
                        "Importing " + (templatePaths.Count + 2) + " documents\u2026", 0.5f);

                foreach (var tokenPath in tokenProjectPaths)
                    AssetDatabase.ImportAsset(tokenPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(ussProjectPath, ImportAssetOptions.ForceSynchronousImport);
                foreach (var path in templatePaths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(uxmlProjectPath, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }

            // Re-import hygiene scoped to THIS screen (keyed by stable root id): delete only files it no
            // longer produces, protecting anything a sibling screen in the same folder still owns.
            var currentSet = new HashSet<string>(generated);
            var manifestRoot = ScreenRoot(context);
            var currentRootId = manifestRoot >= 0 ? message.nodes[manifestRoot].id : string.Empty;

            var manifest = prior ?? new FigmaSyncManifest();
            manifest.fileName = message.fileName ?? string.Empty;
            var priorScreen = string.IsNullOrEmpty(currentRootId)
                ? null
                : manifest.screens.Find(s => s != null && s.rootNodeId == currentRootId);

            var protectedByOthers = new HashSet<string>();
            foreach (var screen in manifest.screens)
                if (screen != null && screen != priorScreen && screen.generated != null)
                    foreach (var path in screen.generated)
                        protectedByOthers.Add(path);

            var orphans = FigmaSyncManifest.Orphans(priorScreen?.generated, currentSet, protectedByOthers);
            // Dependents before dependencies (.uxml -> .uss -> images) to avoid invalid-asset import errors.
            orphans.Sort((a, b) => DeleteRank(a).CompareTo(DeleteRank(b)));
            var orphanCount = 0;
            foreach (var orphan in orphans)
                if (AssetDatabase.DeleteAsset(projectFolder + orphan))
                    orphanCount++;
            if (orphanCount > 0)
                Debug.Log("[UI Toolkit Connector] cleaned " + orphanCount + " orphaned file(s) from this screen's prior import.");

            // Upsert this screen's entry (a masters-only push has no screen root and is skipped).
            if (!string.IsNullOrEmpty(currentRootId))
            {
                if (priorScreen != null)
                    manifest.screens.Remove(priorScreen);
                manifest.screens.Add(new FigmaScreenManifest
                {
                    assetName = assetName,
                    rootNodeId = currentRootId,
                    contentHash = FigmaSyncManifest.ContentHash(json),
                    generated = generated,
                    tokens = FigmaTokenDiff.RecordsFrom(message.variables), // originals for token write-back diffs
                    nodes = BuildNodeBaselines(ussText, emissionByDoc),     // per-node property baselines for write-back
                    components = FigmaComponentStyleModel.ComponentRecords(context.ComponentRules, ussText), // class->master routing + body baseline
                    states = FigmaComponentStyleModel.StateRecords(context.ComponentRules, ussText)  // .cls:hover -> variant
                });
            }
            var decayNote = FigmaImportReport.MappingDecayNote(context, settings, manifest.matchedMappings,
                out var matchedMappings);
            manifest.matchedMappings = matchedMappings;
            WriteManifest(importRoot, subFolder, manifest);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussProjectPath);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlProjectPath);

            var ussErrors = styleSheet != null && styleSheet.importedWithErrors;
            var ok = styleSheet != null && tree != null && !ussErrors;

            Debug.Log("[UI Toolkit Connector] Imported " + message.nodes.Length + " node(s), " + library.ControlCount
                + " inlined control(s), " + library.TemplateCount + " template(s) -> " + uxmlProjectPath
                + (ok ? " (ok)" : " (issues)") + (ussErrors ? "; USS imported with errors" : ""));

            if (tree != null)
                EditorApplication.delayCall += () => EditorGUIUtility.PingObject(tree);

            var notes = FigmaImportReport.Collect(context, settings, family => ResolveFont(settings, family), library, discarded);
            var multiModeNote = FigmaImportReport.MultiModeNote(exportedModes);
            if (multiModeNote != null)
                notes.Add(multiModeNote.Value);
            var collisionNote = FigmaImportReport.TokenCollisionNote(FigmaTokens.Collisions(message.variables));
            if (collisionNote != null)
                notes.Add(collisionNote.Value);
            if (decayNote != null)
                notes.Add(decayNote.Value);
            var noteTexts = new List<string>(notes.Count);
            foreach (var note in notes)
            {
                var text = FigmaStrings.Format(note);
                noteTexts.Add(text);
                Debug.Log("[UI Toolkit Connector] note: " + text);
            }

            // Surface the notes in the connector window (they otherwise only reach the console / HTTP response).
            FigmaImportReportStore.Save(assetName, message.nodes.Length, ok, ussErrors, notes);
            FigmaPropertyCatalog.Record(context);

            return "{\"ok\":" + Bool(ok)
                + ",\"nodes\":" + message.nodes.Length
                + ",\"templates\":" + library.TemplateCount
                + ",\"uxml\":\"" + JsonEscape(uxmlProjectPath) + "\""
                + ",\"ussErrors\":" + Bool(ussErrors)
                + ",\"notes\":" + NotesArray(noteTexts) + "}";
        }

        static string NotesArray(List<string> notes)
        {
            var sb = new StringBuilder("[");
            for (var i = 0; i < notes.Count; ++i)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append('"').Append(JsonEscape(notes[i])).Append('"');
            }
            return sb.Append(']').ToString();
        }

        // internal so the golden tests build the context the importer really builds, rather than a copy
        // that can drift from it.
        internal static FigmaImportContext BuildContext(FigmaImportMessage message)
        {
            var count = message.nodes.Length;
            var children = new List<int>[count];
            for (var i = 0; i < count; ++i)
                children[i] = new List<int>();

            var roots = new List<int>();
            var order = new int[count];
            for (var i = 0; i < count; ++i)
            {
                order[i] = i;
                var parent = message.nodes[i].parentIndex;
                // A parent always precedes its children on the wire; anything else is malformed and
                // roots the node, which also makes parent cycles structurally impossible.
                if (parent >= 0 && parent < i)
                    children[parent].Add(i);
                else
                    roots.Add(i);
            }

            return new FigmaImportContext
            {
                Nodes = message.nodes,
                Children = children,
                Roots = roots,
                Order = order
            };
        }

        // First non-master (screen) root, or -1 when the payload is only component masters.
        static int ScreenRoot(FigmaImportContext context)
        {
            foreach (var root in context.Roots)
                if (!context.Nodes[root].master)
                    return root;
            return -1;
        }

        /// <summary>
        /// The mode stylesheets the screen links after tokens.uss: the modes its content resolves to in Figma,
        /// else the collection default an older plugin sends. Only files this import produced are linked.
        /// </summary>
        internal static List<string> LinkedModeFiles(FigmaImportMessage message, ICollection<string> generated)
        {
            var files = new List<string>();
            if (message.screenModes != null)
                foreach (var mode in message.screenModes)
                    AddModeFile(files, mode, generated);
            if (files.Count == 0)
                AddModeFile(files, message.defaultMode, generated);
            return files;
        }

        static void AddModeFile(List<string> files, string mode, ICollection<string> generated)
        {
            if (string.IsNullOrEmpty(mode))
                return;
            var file = FigmaTokens.ModeFileName(mode);
            if (generated.Contains(file) && !files.Contains(file))
                files.Add(file);
        }

        static string AssetName(FigmaImportMessage message, FigmaImportContext context)
        {
            var screenRoot = ScreenRoot(context);
            if (screenRoot >= 0)
            {
                var rootName = FigmaPathGuard.SanitizeName(message.nodes[screenRoot].name);
                if (rootName != "node")
                    return rootName;
            }

            return FigmaPathGuard.SanitizeName(string.IsNullOrEmpty(message.fileName) ? "FigmaImport" : message.fileName);
        }

        /// <summary>
        /// Promotes flattened icons. The plugin decides which layers are icon candidates and names the key
        /// it staged them under, so this promotes what it was told rather than re-deciding: what the emitter
        /// then absorbs stays its own, structural, call. One key serves every layer that shares it, so an
        /// icon used across fifty buttons is imported once.
        /// </summary>
        static void ResolveIconImages(FigmaImportContext context, string importRoot, string subFolder,
            string projectFolder, List<string> generated, Dictionary<string, string> byKey)
        {
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var key = context.Nodes[i].iconKey;
                if (string.IsNullOrEmpty(key) || context.ImageUri(i) != null)
                    continue;

                if (byKey.TryGetValue(key, out var known))
                {
                    context.ImageUriByIndex[i] = known;
                    continue;
                }

                var staged = FigmaImageStore.StagedPath(key);
                if (staged == null)
                    continue;

                var fileName = FigmaPathGuard.SanitizeName(key) + ".png";
                if (!FigmaPathGuard.TryResolve(importRoot, subFolder + "/images/" + fileName, out var full))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.Copy(staged, full, true);
                generated.Add("images/" + fileName);

                var projectPath = projectFolder + "images/" + fileName;
                AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(projectPath);
                if (texture == null)
                    continue;

                byKey[key] = FigmaFontMap.MakeAssetUri(texture);
                context.ImageUriByIndex[i] = byKey[key];
            }
        }

        // Copies staged PNGs into images/, imports them, and maps node index -> background-image URI.
        static Dictionary<int, string> ResolveImages(FigmaImportContext context, string importRoot,
            string subFolder, string projectFolder, List<string> generated, Dictionary<string, string> byKey)
        {
            var urls = new Dictionary<int, string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                if (!context.Nodes[i].raster)
                    continue;

                // The plugin names the key; identical artwork shares one, so a vector instanced fifty times
                // is imported once. An older plugin sends none and the node's own id is the key, as before.
                var key = string.IsNullOrEmpty(context.Nodes[i].imageKey)
                    ? context.Nodes[i].id
                    : context.Nodes[i].imageKey;

                if (byKey.TryGetValue(key, out var known))
                {
                    urls[i] = known;
                    continue;
                }

                var staged = FigmaImageStore.StagedPath(key);
                if (staged == null)
                    continue;

                var fileName = FigmaPathGuard.SanitizeName(key) + ".png";
                if (!FigmaPathGuard.TryResolve(importRoot, subFolder + "/images/" + fileName, out var full))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.Copy(staged, full, true);
                generated.Add("images/" + fileName);

                var projectPath = projectFolder + "images/" + fileName;
                AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(projectPath);
                if (texture == null)
                    continue;

                byKey[key] = FigmaFontMap.MakeAssetUri(texture);
                urls[i] = byKey[key];
            }

            return urls;
        }

        // The manifest is a hidden dot-file sidecar: committed by git, ignored by Unity (no .meta, never imports).
        static FigmaSyncManifest ReadManifest(string importRoot, string subFolder)
        {
            return FigmaPathGuard.TryResolve(importRoot, subFolder + "/.figma-manifest.json", out var full)
                ? FigmaManifestIO.ReadFile(full)
                : null;
        }

        static void WriteManifest(string importRoot, string subFolder, FigmaSyncManifest manifest)
        {
            if (FigmaPathGuard.TryResolve(importRoot, subFolder + "/.figma-manifest.json", out var full))
                FigmaManifestIO.Write(full, manifest);
        }

        // One record per rendered element, even without props: the diff counts children per parent to
        // detect inserted or removed elements. Props come from the same EffectiveProps the diff uses.
        static List<FigmaNodeRecord> BuildNodeBaselines(string ussText,
            List<(string doc, List<FigmaToUxml.EmissionEntry> entries)> emissionByDoc)
        {
            var rules = FigmaPropertyDiff.ParseRules(ussText);
            var records = new List<FigmaNodeRecord>();
            foreach (var (doc, entries) in emissionByDoc)
            {
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.figmaNodeId))
                        continue;
                    var effective = FigmaPropertyDiff.EffectiveProps(rules, entry.classTokens, entry.inlineStyle);
                    records.Add(new FigmaNodeRecord
                    {
                        figmaNodeId = entry.figmaNodeId,
                        styleClass = entry.sharedClass, // component class: master-ack advances all sharers
                        doc = doc,
                        path = entry.path,
                        tag = entry.tag,
                        classes = entry.classTokens,
                        props = FigmaPropertyDiff.ToProps(effective)
                    });
                }
            }
            return records;
        }

        // Settings font map first, then the project-wide font search.
        static string ResolveFont(FigmaImportSettings settings, string family)
        {
            if (settings != null)
            {
                var mapped = settings.ResolveFont(family);
                if (mapped != null)
                    return FigmaFontMap.MakeFontDefinition(mapped);
            }

            return FigmaFontMap.ResolveFontDefinition(family);
        }

        // Orphan deletion order: dependents before dependencies (.uxml references .uss references images).
        static int DeleteRank(string path)
        {
            if (path.EndsWith(".uxml"))
                return 0;
            if (path.EndsWith(".uss"))
                return 1;
            return 2;
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        // Notes carry Figma layer names, which can hold newlines and tabs; raw control characters would
        // make the hand-built response invalid JSON.
        static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
