using System;

namespace Unity.UIToolkit.Figma
{
    enum FigmaImportNoteKind
    {
        NearMissNames,
        DiscardedLayers,
        TabsOutsideTabView,
        RadioButtonsOutsideGroup,
        EmptyTabs,
        ImagePending,
        MissingFonts,
        UnmappedStates,
        UnlinkedTextProps,
        SkippedStateVariants,
        MissingMasters,
        DroppedGap,
        DroppedShadows,
        DroppedBackgroundBlur,
        DroppedGradients,
        DroppedTextStrokes,
        MultiMode,
        TokenCollision,
        MappingDecay,
    }

    /// <summary>
    /// One import finding as facts: the kind, a headline count, and the items it points at. Pure so the
    /// report stays Unity-free; FigmaStrings turns a note into text in the Editor's language.
    /// </summary>
    [Serializable]
    struct FigmaImportNote
    {
        public FigmaImportNoteKind kind;
        public int count;
        public string[] items;  // untruncated; the formatter decides how many to show
        public int[] counts;    // DiscardedLayers: layers dropped per item
        public string[] extra;  // per item: MultiMode stylesheet file, TokenCollision colliding names, UnlinkedTextProps component
    }
}
