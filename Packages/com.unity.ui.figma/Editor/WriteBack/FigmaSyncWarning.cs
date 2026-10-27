namespace Unity.UIToolkit.Figma
{
    enum FigmaSyncWarningReason
    {
        NoFigmaEquivalent,
        UseContainerGap,
        InstanceLockedLayout,
        StateVariantUnresolved,
        StatesShareVariant,
        StructureChanged,
    }

    /// <summary>
    /// Edits that cannot sync back, one per reason with the properties it covers. Pure; FigmaStrings
    /// renders it.
    /// </summary>
    struct FigmaSyncWarning
    {
        public FigmaSyncWarningReason reason;
        public string[] props;
    }
}
