using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace Unity.UIToolkit.Figma
{
    // Import report notes and sync warnings: whole sentences per case, with the items spliced in last so
    // word order stays the translator's call.
    static partial class FigmaStrings
    {
        const int k_MaxNamed = 6;

        static readonly string k_More = L10n.Tr(", +{0} more", k_Group);
        static readonly string k_Quoted = L10n.Tr("'{0}'", k_Group);

        static readonly string k_NearMissNames = L10n.Tr(
            "{0} component(s) look like controls but are not named exactly like one, so they imported as plain "
            + "elements. Tag them in the plugin or add a mapping: {1}", k_Group);
        static readonly string k_DiscardedLayers = L10n.Tr(
            "{0} control(s) had layers the control cannot hold, so they were dropped; map them to a control that "
            + "takes children, or componentize: {1}", k_Group);
        static readonly string k_OneLayerDropped = L10n.Tr("{0} ({1} layer)", k_Group);
        static readonly string k_LayersDropped = L10n.Tr("{0} ({1} layers)", k_Group);
        static readonly string k_TabsOutsideTabView = L10n.Tr(
            "{0} Tab(s) not inside a TabView (they won't render as tabs; tag the parent TabView): {1}", k_Group);
        static readonly string k_RadioButtonsOutsideGroup = L10n.Tr(
            "{0} RadioButton(s) not inside a RadioButtonGroup (grouping/selection won't work; tag the parent "
            + "RadioButtonGroup): {1}", k_Group);
        static readonly string k_EmptyTabs = L10n.Tr(
            "{0} empty tab(s) - group each tab's content inside its Tab in Figma so it imports into the right tab "
            + "(content left outside renders below the tab bar): {1}", k_Group);
        static readonly string k_ImagePending = L10n.Tr(
            "{0} node(s) have image or vector fills that are not imported yet and render blank (image support is "
            + "a follow-up).", k_Group);
        static readonly string k_MissingFont = L10n.Tr(
            "Missing font: {0} - not in the project, so text falls back to the default font. Add the font to your "
            + "project, or map it in the import settings font map.", k_Group);
        static readonly string k_MissingFonts = L10n.Tr(
            "Missing fonts: {0} - not in the project, so text falls back to the default font. Add the font to your "
            + "project, or map it in the import settings font map.", k_Group);
        static readonly string k_UnmappedStates = L10n.Tr(
            "{0} unrecognized variant state(s) skipped (no interaction style emitted); rename to "
            + "Hover/Pressed/Disabled/Selected/Focused to import: {1}", k_Group);
        static readonly string k_UnlinkedTextProp = L10n.Tr(
            "{0} text property drives no layer in the placed variant, so the layer's own text was used; link the "
            + "variant's text layer to the property in Figma: {1}", k_Group);
        static readonly string k_UnlinkedTextProps = L10n.Tr(
            "{0} text properties drive no layer in the placed variant, so the layer's own text was used; link the "
            + "variant's text layer to the property in Figma: {1}", k_Group);
        static readonly string k_PropInComponent = L10n.Tr("'{0}' in {1}", k_Group);
        static readonly string k_SkippedStateVariant = L10n.Tr(
            "{0} component with a State axis imported without interaction styles; state variants are not imported "
            + "for a component holding more than one text layer: {1}", k_Group);
        static readonly string k_SkippedStateVariants = L10n.Tr(
            "{0} components with a State axis imported without interaction styles; state variants are not "
            + "imported for a component holding more than one text layer: {1}", k_Group);
        static readonly string k_MissingMasters = L10n.Tr(
            "{0} component instance(s) imported fully inline (no shared class; master style unavailable): re-push "
            + "with the current plugin, or check for a detached instance / a library component whose style didn't "
            + "resolve: {1}", k_Group);
        static readonly string k_DroppedGap = L10n.Tr(
            "{0} frame(s) use auto-layout spacing (gap), which USS supports from Unity 6000.7.0a4. The spacing is "
            + "dropped on this version; set it with margins on the child elements, or upgrade the Editor.", k_Group);
        static readonly string k_DroppedShadows = L10n.Tr(
            "{0} layer(s) use a drop shadow on a frame or shape, which USS cannot express (no box-shadow); the "
            + "shadow is not imported. Text shadows do import.", k_Group);
        static readonly string k_DroppedBackgroundBlur = L10n.Tr(
            "{0} layer(s) use background blur, which USS supports (backdrop-filter) from Unity 6000.6.0a6. The "
            + "blur is dropped on this version; upgrade the Editor.", k_Group);
        static readonly string k_DroppedGradients = L10n.Tr(
            "{0} layer(s) use a gradient fill, which the connector does not import yet; the layer has no fill in "
            + "Unity. Flatten the layer to an image in Figma, or use a solid fill.", k_Group);
        static readonly string k_DroppedTextStrokes = L10n.Tr(
            "{0} text layer(s) use a stroke, which the connector does not import yet; the text has no outline in "
            + "Unity.", k_Group);
        static readonly string k_MultiMode = L10n.Tr(
            "Exported {0} theme mode(s) ({1}): {2}. Each screen links the mode its content uses in Figma, or the "
            + "collection default when the plugin doesn't report one; swap the stylesheet to re-theme.", k_Group);
        static readonly string k_TokenCollision = L10n.Tr(
            "Variable names collapsed onto the same token: {0}. Only the first is emitted; rename them in Figma so "
            + "they stay distinct.", k_Group);
        static readonly string k_TokenCollisionItem = L10n.Tr("{0} ({1})", k_Group);
        static readonly string k_MappingDecayOne = L10n.Tr(
            "{0} mapping matched no component in this file but did before (renamed in Figma?): {1}", k_Group);
        static readonly string k_MappingDecay = L10n.Tr(
            "{0} mappings matched no component in this file but did before (renamed in Figma?): {1}", k_Group);

        static readonly string k_NotSynced = L10n.Tr("Not synced: {0} ({1})", k_Group);
        static readonly string k_NoFigmaEquivalent = L10n.Tr("no Figma equivalent", k_Group);
        static readonly string k_UseContainerGap = L10n.Tr(
            "no Figma equivalent; use the container's gap for flow spacing", k_Group);
        static readonly string k_InstanceLockedLayout = L10n.Tr(
            "Figma locks layout inside instances; edit the component", k_Group);
        static readonly string k_StateVariantUnresolved = L10n.Tr(
            "this state's Figma variant could not be resolved", k_Group);
        static readonly string k_StatesShareVariant = L10n.Tr(
            "two interaction states share one Figma variant", k_Group);
        static readonly string k_StructureChanged = L10n.Tr(
            "elements were added, removed or moved in this document, so its edits can't be matched to Figma "
            + "layers; undo the change, or push the screen from Figma again", k_Group);

        /// <summary>
        /// Missing fonts are the most actionable note, so the window surfaces them as warnings.
        /// </summary>
        public static bool IsWarning(FigmaImportNote note)
        {
            return note.kind == FigmaImportNoteKind.MissingFonts;
        }

        public static string Format(FigmaImportNote note)
        {
            var one = note.count == 1;
            switch (note.kind)
            {
                case FigmaImportNoteKind.NearMissNames:
                    return string.Format(k_NearMissNames, note.count, Named(note.items, k_Quoted));
                case FigmaImportNoteKind.DiscardedLayers:
                    return string.Format(k_DiscardedLayers, note.count, Named(DroppedLayerItems(note)));
                case FigmaImportNoteKind.TabsOutsideTabView:
                    return string.Format(k_TabsOutsideTabView, note.count, Named(note.items));
                case FigmaImportNoteKind.RadioButtonsOutsideGroup:
                    return string.Format(k_RadioButtonsOutsideGroup, note.count, Named(note.items));
                case FigmaImportNoteKind.EmptyTabs:
                    return string.Format(k_EmptyTabs, note.count, Named(note.items));
                case FigmaImportNoteKind.ImagePending:
                    return string.Format(k_ImagePending, note.count);
                case FigmaImportNoteKind.MissingFonts:
                    return string.Format(one ? k_MissingFont : k_MissingFonts, Named(note.items));
                case FigmaImportNoteKind.UnmappedStates:
                    return string.Format(k_UnmappedStates, note.count, Named(note.items));
                case FigmaImportNoteKind.UnlinkedTextProps:
                    return string.Format(one ? k_UnlinkedTextProp : k_UnlinkedTextProps, note.count,
                        Named(Paired(note.items, note.extra, k_PropInComponent)));
                case FigmaImportNoteKind.SkippedStateVariants:
                    return string.Format(one ? k_SkippedStateVariant : k_SkippedStateVariants, note.count,
                        Named(note.items));
                case FigmaImportNoteKind.MissingMasters:
                    return string.Format(k_MissingMasters, note.count, Named(note.items));
                case FigmaImportNoteKind.DroppedGap:
                    return string.Format(k_DroppedGap, note.count);
                case FigmaImportNoteKind.DroppedShadows:
                    return string.Format(k_DroppedShadows, note.count);
                case FigmaImportNoteKind.DroppedBackgroundBlur:
                    return string.Format(k_DroppedBackgroundBlur, note.count);
                case FigmaImportNoteKind.DroppedGradients:
                    return string.Format(k_DroppedGradients, note.count);
                case FigmaImportNoteKind.DroppedTextStrokes:
                    return string.Format(k_DroppedTextStrokes, note.count);
                case FigmaImportNoteKind.MultiMode:
                    return string.Format(k_MultiMode, note.count, string.Join(", ", note.items),
                        string.Join(", ", note.extra));
                case FigmaImportNoteKind.TokenCollision:
                    return string.Format(k_TokenCollision,
                        string.Join("; ", Paired(note.items, note.extra, k_TokenCollisionItem)));
                case FigmaImportNoteKind.MappingDecay:
                    return string.Format(one ? k_MappingDecayOne : k_MappingDecay, note.count, Named(note.items));
            }
            return note.kind.ToString();
        }

        public static string Format(FigmaSyncWarning warning)
        {
            string reason;
            switch (warning.reason)
            {
                case FigmaSyncWarningReason.UseContainerGap:
                    reason = k_UseContainerGap;
                    break;
                case FigmaSyncWarningReason.InstanceLockedLayout:
                    reason = k_InstanceLockedLayout;
                    break;
                case FigmaSyncWarningReason.StateVariantUnresolved:
                    reason = k_StateVariantUnresolved;
                    break;
                case FigmaSyncWarningReason.StatesShareVariant:
                    reason = k_StatesShareVariant;
                    break;
                case FigmaSyncWarningReason.StructureChanged:
                    reason = k_StructureChanged;
                    break;
                default:
                    reason = k_NoFigmaEquivalent;
                    break;
            }
            return string.Format(k_NotSynced, string.Join(", ", warning.props), reason);
        }

        // The first few items, comma-joined, then how many were left out.
        static string Named(IList<string> items, string itemFormat = null)
        {
            if (items == null || items.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            var shown = items.Count > k_MaxNamed ? k_MaxNamed : items.Count;
            for (var i = 0; i < shown; ++i)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(itemFormat == null ? items[i] : string.Format(itemFormat, items[i]));
            }
            if (items.Count > shown)
                sb.Append(string.Format(k_More, items.Count - shown));
            return sb.ToString();
        }

        static string[] Paired(string[] first, string[] second, string format)
        {
            var result = new string[first.Length];
            for (var i = 0; i < first.Length; ++i)
                result[i] = string.Format(format, first[i], i < second.Length ? second[i] : string.Empty);
            return result;
        }

        static string[] DroppedLayerItems(FigmaImportNote note)
        {
            var result = new string[note.items.Length];
            for (var i = 0; i < result.Length; ++i)
            {
                var layers = note.counts != null && i < note.counts.Length ? note.counts[i] : 0;
                result[i] = string.Format(layers == 1 ? k_OneLayerDropped : k_LayersDropped, note.items[i], layers);
            }
            return result;
        }
    }
}
