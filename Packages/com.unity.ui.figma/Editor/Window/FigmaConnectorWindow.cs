using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Status surface for the connector: whether Pipeline is reachable, the active mapping, what has been
    /// imported, and what is queued to go back to Figma.
    /// </summary>
    internal class FigmaConnectorWindow : EditorWindow
    {
        string m_ImportsSig;
        string m_ReportSig;
        string m_SyncSig;
        Label m_MappingLabel;
        Label m_ImportsMeta;
        Label m_StatusLabel;
        Label m_PortLabel;
        Label m_ReportMeta;
        VisualElement m_ReportBody;
        VisualElement m_ImportsBody;
        VisualElement m_StatusDot;
        VisualElement m_OfflineBody;
        VisualElement m_SyncBody;
        Foldout m_SyncFold;

        internal static void Open()
        {
            GetWindow<FigmaConnectorWindow>(FigmaStrings.Name);
        }

        void CreateGUI()
        {
            minSize = new Vector2(300, 240);
            BuildConnectorUI();
            rootVisualElement.schedule.Execute(Refresh).Every(1000);
        }

        void BuildConnectorUI()
        {
            var root = rootVisualElement;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.viewDataKey = "figma-connector-scroll";
            root.Add(scroll);

            var content = scroll.contentContainer;
            content.style.paddingTop = 8;
            content.style.paddingBottom = 8;

            var sheet = FigmaResources.LoadStyleSheet("FigmaConnectorWindow.uss");
            if (sheet != null)
                root.styleSheets.Add(sheet);

            Foldout Section(string heading, string key, bool expanded)
            {
                var f = new Foldout { text = heading, value = expanded, viewDataKey = key };
                f.AddToClassList("figma-section");
                scroll.Add(f);
                return f;
            }

            var title = new Label(FigmaStrings.WindowTitle);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginLeft = 6;
            title.style.marginBottom = 4;
            scroll.Add(title);

            var conn = new VisualElement();
            conn.AddToClassList("figma-conn");

            var statusRow = new VisualElement();
            statusRow.AddToClassList("figma-status-row");
            m_StatusDot = new VisualElement();
            m_StatusDot.AddToClassList("figma-dot");
            statusRow.Add(m_StatusDot);
            m_StatusLabel = new Label();
            statusRow.Add(m_StatusLabel);
            m_PortLabel = new Label();
            m_PortLabel.AddToClassList("figma-port");
            statusRow.Add(m_PortLabel);
            var statusSpacer = new VisualElement();
            statusSpacer.AddToClassList("figma-spacer");
            statusRow.Add(statusSpacer);
            // Copied by hand because Pipeline has no consent handshake; if one lands, this becomes an
            // approval banner instead.
            var copy = new Button(CopyToken) { text = FigmaStrings.CopyToken, tooltip = FigmaStrings.CopyTokenTooltip };
            statusRow.Add(copy);
            conn.Add(statusRow);

            m_OfflineBody = new VisualElement();
            conn.Add(m_OfflineBody);

            scroll.Add(conn);

            // --- Mapping ---
            var mapWrap = new VisualElement();
            mapWrap.AddToClassList("figma-map-row");

            m_MappingLabel = new Label();
            m_MappingLabel.tooltip = FigmaStrings.ActiveMappingTooltip;
            mapWrap.Add(m_MappingLabel);

            var mapSpacer = new VisualElement();
            mapSpacer.AddToClassList("figma-spacer");
            mapWrap.Add(mapSpacer);

            var openSettings = new Button(() => SettingsService.OpenProjectSettings(FigmaImportSettingsProvider.k_Path))
                { text = FigmaStrings.OpenImportSettings };
            mapWrap.Add(openSettings);

            scroll.Add(mapWrap);

            // --- Imported screens ---
            var importsFold = Section(FigmaStrings.SectionImportedScreens, "figma-fold-imports", true);
            m_ImportsMeta = AddHeaderMeta(importsFold);
            m_ImportsBody = new VisualElement();
            importsFold.Add(m_ImportsBody);

            // --- Sync to Figma ---
            m_SyncFold = Section(FigmaStrings.SectionSyncToFigma, "figma-fold-sync", true);
            m_SyncBody = new VisualElement();
            m_SyncFold.Add(m_SyncBody);

            // --- Last import ---
            var reportFold = Section(FigmaStrings.SectionLastImport, "figma-fold-report", false);
            m_ReportMeta = AddHeaderMeta(reportFold);

            m_ReportBody = new VisualElement();
            reportFold.Add(m_ReportBody);

            Refresh();
        }

        void Refresh()
        {
            var port = PipelinePort();
            var running = port > 0;
            m_StatusDot.EnableInClassList("figma-dot--ok", running);
            m_StatusDot.EnableInClassList("figma-dot--warn", !running);
            m_StatusLabel.text = running ? FigmaStrings.StatusRunning : FigmaStrings.StatusOffline;
            m_PortLabel.text = running ? FigmaStrings.Port(port) : string.Empty;
            RefreshOffline(running);

            RefreshImports();
            RefreshReport();
            RefreshSync();
        }

        // Pipeline's port descriptor file, its documented discovery channel. A pid other than ours
        // is a leftover from a crashed session.
        [Serializable]
        class PipelineDescriptor
        {
            public int pid;
            public int port;
            public string evalToken;
        }

        static PipelineDescriptor ReadPipelineDescriptor()
        {
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Library", "Pipeline", ".unity-pipeline-port");
            if (!File.Exists(path))
                return null;
            try
            {
                var descriptor = JsonUtility.FromJson<PipelineDescriptor>(File.ReadAllText(path));
                if (descriptor == null || descriptor.port <= 0)
                    return null;
                if (descriptor.pid != System.Diagnostics.Process.GetCurrentProcess().Id)
                    return null;
                return descriptor;
            }
            catch (Exception)
            {
                // A read can race the heartbeat rewrite; the next Refresh sees the settled file.
                return null;
            }
        }

        // Unity does not own the listener, so this reports reachability rather than implying we run it.
        static int PipelinePort()
        {
            var descriptor = ReadPipelineDescriptor();
            return descriptor != null ? descriptor.port : 0;
        }

        // The clipboard is the one place the token can leave the machine, via sync tooling, so it expires.
        const double k_ClipboardHoldSeconds = 60;
        static string s_Copied;
        static double s_ClearClipboardAt;

        // No "Disconnect all" counterpart: the Pipeline token is shared with every Pipeline client, so
        // rotating it here would drop unrelated tooling, not just Figma.
        static void CopyToken()
        {
            var descriptor = ReadPipelineDescriptor();
            if (descriptor == null || string.IsNullOrEmpty(descriptor.evalToken))
            {
                Debug.LogWarning("[UI Toolkit Connector] Pipeline is not running, so there is no token to copy.");
                return;
            }
            // The port travels with the token so the plugin talks to the Editor named here, rather than
            // offering the credential to every port in the range to find out which one answers.
            s_Copied = descriptor.port + ":" + descriptor.evalToken;
            EditorGUIUtility.systemCopyBuffer = s_Copied;
            s_ClearClipboardAt = EditorApplication.timeSinceStartup + k_ClipboardHoldSeconds;
            EditorApplication.update -= ClearCopiedToken;
            EditorApplication.update += ClearCopiedToken;
        }

        /// <summary>
        /// Wipes the copy once its hold expires, and only if the clipboard still holds it, so anything
        /// copied since is left alone.
        /// </summary>
        static void ClearCopiedToken()
        {
            if (EditorApplication.timeSinceStartup < s_ClearClipboardAt)
                return;

            EditorApplication.update -= ClearCopiedToken;
            if (s_Copied != null && EditorGUIUtility.systemCopyBuffer == s_Copied)
                EditorGUIUtility.systemCopyBuffer = string.Empty;
            s_Copied = null;
        }

        void RefreshOffline(bool running)
        {
            if (running)
            {
                if (m_OfflineBody.childCount > 0)
                    m_OfflineBody.Clear();
                return;
            }
            if (m_OfflineBody.childCount > 0)
                return;
            m_OfflineBody.Add(new HelpBox(FigmaStrings.PipelineOffline, HelpBoxMessageType.Warning));
        }

        // Local edits grouped by Figma file, each with an opt-in Push; pushed groups show "waiting".
        // Rebuilt only when the diff or the pushed set changes.
        void RefreshSync()
        {
            var tokens = FigmaTokenWriteBack.Scan();
            var props = FigmaPropertyWriteBack.Scan();
            var syncWarnings = FigmaPropertyWriteBack.ScanWarnings();
            var n = tokens.Count + props.Count;

            var pushedTokens = new HashSet<string>();
            foreach (var c in FigmaTokenWriteBack.Pending())
                pushedTokens.Add(FigmaTokenWriteBack.KeyOf(c));
            var pushedProps = new HashSet<string>();
            foreach (var c in FigmaPropertyWriteBack.Pending())
                pushedProps.Add(FigmaPropertyWriteBack.KeyOf(c));

            var sig = n.ToString();
            foreach (var t in tokens)
                sig += "|t:" + FigmaTokenWriteBack.KeyOf(t) + "=" + t.value + (pushedTokens.Contains(FigmaTokenWriteBack.KeyOf(t)) ? "*" : "");
            foreach (var p in props)
                sig += "|p:" + FigmaPropertyWriteBack.KeyOf(p) + "=" + p.value + (pushedProps.Contains(FigmaPropertyWriteBack.KeyOf(p)) ? "*" : "");
            foreach (var w in syncWarnings)
                sig += "|w:" + w.reason + ":" + string.Join(",", w.props);
            if (sig == m_SyncSig)
                return;
            m_SyncSig = sig;

            m_SyncBody.Clear();
            if (n == 0 && syncWarnings.Count == 0)
            {
                var ok = new Label("✓ " + FigmaStrings.InSync);
                ok.AddToClassList("figma-insync");
                m_SyncBody.Add(ok);
                return;
            }
            if (n == 0)
            {
                var ok = new Label("✓ " + FigmaStrings.InSyncWithNotes);
                ok.AddToClassList("figma-insync");
                m_SyncBody.Add(ok);
            }
            // Property edits that can't sync are explained here. Structure is out of scope: Figma owns it.
            if (syncWarnings.Count > 0)
            {
                var warnBlock = new VisualElement();
                warnBlock.style.marginBottom = 8;
                foreach (var w in syncWarnings)
                {
                    var warn = new Label("⚠ " + FigmaStrings.Format(w));
                    warn.style.whiteSpace = WhiteSpace.Normal;
                    warn.style.color = new Color(0.9f, 0.75f, 0.3f);
                    warn.style.marginTop = 2;
                    warnBlock.Add(warn);
                }
                m_SyncBody.Add(warnBlock);
            }
            if (n == 0)
                return;

            // Group changes by Figma file, headed by the file to open; order = first appearance.
            var files = new List<string>();
            var fileTokens = new Dictionary<string, List<FigmaTokenChange>>();
            var fileProps = new Dictionary<string, List<FigmaPropertyChange>>();
            void Ensure(string f)
            {
                if (fileTokens.ContainsKey(f))
                    return;
                files.Add(f);
                fileTokens[f] = new List<FigmaTokenChange>();
                fileProps[f] = new List<FigmaPropertyChange>();
            }
            foreach (var t in tokens) { var f = string.IsNullOrEmpty(t.file) ? "" : t.file; Ensure(f); fileTokens[f].Add(t); }
            foreach (var p in props) { var f = string.IsNullOrEmpty(p.file) ? "" : p.file; Ensure(f); fileProps[f].Add(p); }

            // Unpushed count drives the header wording and whether "Push all" shows.
            var unpushed = 0;
            foreach (var t in tokens) if (!pushedTokens.Contains(FigmaTokenWriteBack.KeyOf(t))) unpushed++;
            foreach (var p in props) if (!pushedProps.Contains(FigmaPropertyWriteBack.KeyOf(p))) unpushed++;

            var headRow = new VisualElement();
            headRow.style.flexDirection = FlexDirection.Row;
            headRow.style.alignItems = Align.Center;
            headRow.style.marginBottom = 4;
            var headDot = new VisualElement();
            headDot.AddToClassList("figma-dot");
            headDot.AddToClassList("figma-dot--warn");
            headRow.Add(headDot);
            var head = new Label(FigmaStrings.SyncHead(n, files.Count, unpushed > 0));
            head.style.whiteSpace = WhiteSpace.Normal;
            headRow.Add(head);
            m_SyncBody.Add(headRow);

            foreach (var f in files)
            {
                var ts = fileTokens[f];
                var ps = fileProps[f];
                var c = ts.Count + ps.Count;
                var group = new Foldout
                {
                    text = FigmaStrings.Pair(string.IsNullOrEmpty(f) ? FigmaStrings.UnknownFile : f, FigmaStrings.ChangeCount(c)),
                    value = true
                };
                group.AddToClassList("figma-file-group");

                var tKeys = new List<string>();
                foreach (var t in ts) tKeys.Add(FigmaTokenWriteBack.KeyOf(t));
                var pKeys = new List<string>();
                foreach (var p in ps) pKeys.Add(FigmaPropertyWriteBack.KeyOf(p));
                var groupPushed = tKeys.TrueForAll(pushedTokens.Contains) && pKeys.TrueForAll(pushedProps.Contains);
                AddGroupPush(group, tKeys, pKeys, groupPushed);

                foreach (var t in ts)
                {
                    var r = new Label(FigmaStrings.ChangeLine(t.name, t.value));
                    r.AddToClassList("figma-change-line");
                    group.Add(r);
                }
                foreach (var p in ps)
                {
                    var r = new Label(FigmaStrings.ChangeLine(p.prop, p.value));
                    r.AddToClassList("figma-change-line");
                    group.Add(r);
                }
                m_SyncBody.Add(group);
            }

            // Push all: send every unpushed edit. Hidden once everything is pushed.
            if (unpushed > 0)
            {
                var allTokenKeys = new List<string>();
                foreach (var t in tokens) allTokenKeys.Add(FigmaTokenWriteBack.KeyOf(t));
                var allPropKeys = new List<string>();
                foreach (var p in props) allPropKeys.Add(FigmaPropertyWriteBack.KeyOf(p));
                var pushAll = new Button(() =>
                {
                    FigmaTokenWriteBack.Push(allTokenKeys);
                    FigmaPropertyWriteBack.Push(allPropKeys);
                    RefreshSync();
                }) { text = FigmaStrings.PushAll, tooltip = FigmaStrings.PushAllTooltip };
                pushAll.style.alignSelf = Align.FlexEnd;
                pushAll.style.marginTop = 8;
                m_SyncBody.Add(pushAll);
            }
        }

        // Per-file Push button (or a muted "waiting" note) right-aligned in the foldout header; stops its
        // pointer event so clicking doesn't collapse the group.
        void AddGroupPush(Foldout fold, List<string> tokenKeys, List<string> propKeys, bool pushed)
        {
            var toggle = fold.Q<Toggle>();
            if (toggle == null)
                return;
            var label = toggle.Q<Label>();
            if (label != null)
                label.style.flexGrow = 1;

            if (pushed)
            {
                var waiting = new Label(FigmaStrings.WaitingForFigma);
                waiting.AddToClassList("figma-group-waiting");
                toggle.Add(waiting);
                return;
            }

            var push = new Button(() =>
            {
                FigmaTokenWriteBack.Push(tokenKeys);
                FigmaPropertyWriteBack.Push(propKeys);
                RefreshSync();
            }) { text = FigmaStrings.Push, tooltip = FigmaStrings.PushTooltip };
            push.AddToClassList("figma-group-push");
            push.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            toggle.Add(push);
        }

        // Right-aligned muted metadata label in a foldout header; returned so refresh can update it.
        static Label AddHeaderMeta(Foldout fold)
        {
            var meta = new Label();
            meta.AddToClassList("figma-header-meta");
            var toggle = fold.Q<Toggle>();
            if (toggle != null)
            {
                var label = toggle.Q<Label>();
                if (label != null)
                    label.style.flexGrow = 1;
                toggle.Add(meta);
            }
            return meta;
        }

        // Ledger view: one entry per imported Figma file (read-only screens). Rebuilt only when the set of
        // files/screens changes (the poll runs every second).
        void RefreshImports()
        {
            var imports = FigmaManifestIO.AllImports();
            var active = FigmaImportSettings.Find();
            m_MappingLabel.text = FigmaStrings.ActiveMapping(active != null ? active.name : null);

            var screenCount = 0;
            foreach (var imp in imports)
                screenCount += imp.manifest.screens.Count;
            m_ImportsMeta.text = imports.Count == 0 ? FigmaStrings.NoneYet
                : FigmaStrings.Pair(FigmaStrings.ScreenCount(screenCount), FigmaStrings.FileCount(imports.Count));

            var sig = "";
            foreach (var imp in imports)
                sig += imp.manifest.fileName + ":" + imp.manifest.screens.Count + "|";
            if (sig == m_ImportsSig)
                return;
            m_ImportsSig = sig;

            m_ImportsBody.Clear();
            if (imports.Count == 0)
            {
                m_ImportsBody.Add(new HelpBox(FigmaStrings.NoImportsYet, HelpBoxMessageType.Info));
                return;
            }

            foreach (var imp in imports)
            {
                var manifest = imp.manifest;
                var folder = imp.folder;

                var group = new Label(manifest.fileName);
                group.AddToClassList("figma-import-group");
                m_ImportsBody.Add(group);

                foreach (var screen in manifest.screens)
                {
                    if (screen == null || string.IsNullOrEmpty(screen.assetName))
                        continue;
                    var path = "Assets/Figma/" + folder + "/" + screen.assetName + ".uxml";
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    var field = new ObjectField(screen.assetName)
                        { objectType = typeof(VisualTreeAsset), allowSceneObjects = false };
                    field.SetValueWithoutNotify(asset);
                    field.SetEnabled(false);
                    field.tooltip = FigmaStrings.GeneratedScreenTooltip;
                    m_ImportsBody.Add(field);
                }
            }
        }

        // Rebuilds the "Last import" notes only when the stored report changed (1s poll).
        void RefreshReport()
        {
            var report = FigmaImportReportStore.Load();
            m_ReportMeta.text = report == null ? FigmaStrings.NoneYet : FigmaStrings.Pair(report.screen, report.when);
            var sig = report == null
                ? string.Empty
                : report.when + "|" + (report.notes?.Length ?? 0) + "|" + report.ussErrors;
            if (sig == m_ReportSig)
                return;
            m_ReportSig = sig;

            m_ReportBody.Clear();
            if (report == null)
            {
                var none = new Label(FigmaStrings.NoImportYetNotes);
                none.style.whiteSpace = WhiteSpace.Normal;
                none.style.opacity = 0.7f;
                m_ReportBody.Add(none);
                return;
            }

            // A line per note (warnings first), then a footer with severity counts + Re-import last.
            var list = new VisualElement();
            list.AddToClassList("figma-note-list");

            int warn = 0, info = 0;
            if (report.ussErrors)
            {
                list.Add(NoteRow(true, FigmaStrings.UssImportedWithErrors));
                warn++;
            }
            if (report.notes != null)
            {
                // Warnings first: they are the actionable ones.
                foreach (var note in report.notes)
                    if (FigmaStrings.IsWarning(note))
                    {
                        list.Add(NoteRow(true, FigmaStrings.Format(note)));
                        warn++;
                    }
                foreach (var note in report.notes)
                    if (!FigmaStrings.IsWarning(note))
                    {
                        list.Add(NoteRow(false, FigmaStrings.Format(note)));
                        info++;
                    }
            }

            if (warn + info == 0)
            {
                var ok = new Label(FigmaStrings.NothingFlagged);
                ok.style.opacity = 0.7f;
                list.Add(ok);
            }

            var footer = new VisualElement();
            footer.AddToClassList("figma-note-footer");
            if (warn > 0)
                footer.Add(CountChip(true, warn));
            if (info > 0)
                footer.Add(CountChip(false, info));
            var spacer = new VisualElement();
            spacer.AddToClassList("figma-spacer");
            footer.Add(spacer);
            footer.Add(new Button(() => { FigmaConnectorReplay.ReplayLast(); RefreshReport(); })
            {
                text = FigmaStrings.ReimportLast,
                tooltip = FigmaStrings.ReimportLastTooltip
            });
            list.Add(footer);

            m_ReportBody.Add(list);
        }

        // One review note as a single line: console icon + ellipsised title; full text on hover.
        static VisualElement NoteRow(bool warn, string note)
        {
            var row = new VisualElement();
            row.AddToClassList("figma-note-row");

            var icon = HelpIcon(warn);
            icon.AddToClassList("figma-note-ic");
            row.Add(icon);

            var title = new Label(ShortTitle(note)) { tooltip = note };
            title.AddToClassList("figma-note-title");
            row.Add(title);
            return row;
        }

        // Unity's built-in console icons so notes read as native warning/info on both themes.
        static Image HelpIcon(bool warn)
        {
            return new Image
            {
                image = EditorGUIUtility.IconContent(warn ? "console.warnicon.sml" : "console.infoicon.sml").image,
                scaleMode = ScaleMode.ScaleToFit
            };
        }

        // Footer severity tally: a small console icon + the count.
        static VisualElement CountChip(bool warn, int n)
        {
            var chip = new VisualElement();
            chip.AddToClassList("figma-note-chip");
            var icon = HelpIcon(warn);
            icon.AddToClassList("figma-note-cic");
            chip.Add(icon);
            chip.Add(new Label(n.ToString()));
            return chip;
        }

        // Headline before the first " - " or " (" detail; the full note is the row tooltip.
        static string ShortTitle(string note)
        {
            var cut = note.IndexOf(" - ", System.StringComparison.Ordinal);
            var paren = note.IndexOf(" (", System.StringComparison.Ordinal);
            if (paren >= 0 && (cut < 0 || paren < cut))
                cut = paren;
            var title = cut > 0 ? note.Substring(0, cut) : note;
            return title.Length > 64 ? title.Substring(0, 63).TrimEnd() + "…" : title;
        }
    }
}
