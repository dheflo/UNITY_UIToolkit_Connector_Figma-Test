using UnityEditor;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Every user-facing string of the Editor assembly, translated once per domain reload. Console logs,
    /// wire text and identifiers stay English and never enter this catalog. Each literal sits directly in
    /// an L10n.Tr call so string extraction finds it.
    /// </summary>
    static partial class FigmaStrings
    {
        // Must match the assembly name; .po files register under it.
        const string k_Group = "Unity.UIToolkit.Figma.Editor";

        public const string Name = "Connector for Figma design";

        public static readonly string WindowTitle = L10n.Tr("UI Toolkit ↔ Figma design", k_Group);

        public static readonly string StatusRunning = L10n.Tr("Running", k_Group);
        public static readonly string StatusOffline = L10n.Tr("Offline", k_Group);
        static readonly string k_Port = L10n.Tr("port:{0}", k_Group);
        public static string Port(int port) => string.Format(k_Port, port);

        public static readonly string CopyToken = L10n.Tr("Copy token", k_Group);
        public static readonly string CopyTokenTooltip = L10n.Tr(
            "Copies this Editor's Pipeline port and token. Paste it into the Figma plugin's Settings tab to "
            + "connect. Copy it again after an Editor restart, or a Pipeline port change.\n\nThe token is a "
            + "credential for this Editor, so the clipboard is wiped a minute after copying.", k_Group);
        public static readonly string PipelineOffline = L10n.Tr(
            "Unity Pipeline is not running, so the Figma plugin cannot reach this Editor.", k_Group);

        public static readonly string ActiveMappingTooltip = L10n.Tr(
            "The project-wide mapping (control map, fonts, class prefix) applied to every import. Edit it in "
            + "Project Settings.", k_Group);
        public static readonly string OpenImportSettings = L10n.Tr("Open import settings", k_Group);
        static readonly string k_ActiveMapping = L10n.Tr("Active mapping: {0}", k_Group);
        public static readonly string NoMapping = L10n.Tr("(none)", k_Group);
        public static string ActiveMapping(string name) => string.Format(k_ActiveMapping, name ?? NoMapping);

        public static readonly string SectionImportedScreens = L10n.Tr("Imported screens", k_Group);
        public static readonly string SectionSyncToFigma = L10n.Tr("Sync to Figma", k_Group);
        public static readonly string SectionLastImport = L10n.Tr("Last import", k_Group);
        public static readonly string NoneYet = L10n.Tr("none yet", k_Group);

        public static readonly string NoImportsYet = L10n.Tr(
            "No imports yet; push a frame from the Figma plugin.", k_Group);
        public static readonly string GeneratedScreenTooltip = L10n.Tr("Generated screen (read-only).", k_Group);

        public static readonly string InSync = L10n.Tr("In sync", k_Group);
        public static readonly string InSyncWithNotes = L10n.Tr("In sync (with notes)", k_Group);
        public static readonly string WaitingForFigma = L10n.Tr("waiting for Figma to accept", k_Group);
        public static readonly string UnknownFile = L10n.Tr("Unknown file", k_Group);
        public static readonly string Push = L10n.Tr("Push", k_Group);
        public static readonly string PushTooltip = L10n.Tr("Send this file's edits to Figma.", k_Group);
        public static readonly string PushAll = L10n.Tr("Push all", k_Group);
        public static readonly string PushAllTooltip = L10n.Tr("Send all these edits to Figma.", k_Group);

        static readonly string k_OneChange = L10n.Tr("{0} change", k_Group);
        static readonly string k_Changes = L10n.Tr("{0} changes", k_Group);
        static readonly string k_OneFile = L10n.Tr("{0} file", k_Group);
        static readonly string k_Files = L10n.Tr("{0} files", k_Group);
        static readonly string k_OneScreen = L10n.Tr("{0} screen", k_Group);
        static readonly string k_Screens = L10n.Tr("{0} screens", k_Group);
        static readonly string k_SyncHeadPush = L10n.Tr("{0} in {1} - Push to send to Figma.", k_Group);
        static readonly string k_SyncHeadWaiting = L10n.Tr("{0} in {1} - waiting for Figma to accept.", k_Group);
        static readonly string k_Pair = L10n.Tr("{0} · {1}", k_Group);
        static readonly string k_ChangeLine = L10n.Tr("· {0} → {1}", k_Group);

        public static string ChangeCount(int n) => string.Format(n == 1 ? k_OneChange : k_Changes, n);
        public static string FileCount(int n) => string.Format(n == 1 ? k_OneFile : k_Files, n);
        public static string ScreenCount(int n) => string.Format(n == 1 ? k_OneScreen : k_Screens, n);
        public static string SyncHead(int changes, int files, bool unpushed)
            => string.Format(unpushed ? k_SyncHeadPush : k_SyncHeadWaiting, ChangeCount(changes), FileCount(files));
        public static string Pair(string left, string right) => string.Format(k_Pair, left, right);
        public static string ChangeLine(string name, string value) => string.Format(k_ChangeLine, name, value);

        public static readonly string NoImportYetNotes = L10n.Tr(
            "No import yet; push a frame from the Figma plugin to see review notes.", k_Group);
        public static readonly string UssImportedWithErrors = L10n.Tr(
            "USS imported with errors; the screen may not open in UI Builder.", k_Group);
        public static readonly string NothingFlagged = L10n.Tr("Nothing flagged for review.", k_Group);
        public static readonly string ReimportLast = L10n.Tr("Re-import last", k_Group);
        public static readonly string ReimportLastTooltip = L10n.Tr(
            "Re-run the last Figma push through the import pipeline (no Figma round-trip).", k_Group);
    }
}
