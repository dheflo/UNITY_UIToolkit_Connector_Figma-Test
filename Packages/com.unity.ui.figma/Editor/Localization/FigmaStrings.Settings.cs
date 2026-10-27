using UnityEditor;

namespace Unity.UIToolkit.Figma
{
    // The Project Settings page, the mapping asset inspector and the Control Map entry drawer.
    static partial class FigmaStrings
    {
        public static readonly string SettingsSubtitle = L10n.Tr(
            "Global mapping from Figma components to Unity UI Toolkit controls.", k_Group);
        public static readonly string SettingsHelp = L10n.Tr(
            "These mappings apply to every Figma file imported into this project. Add a mapping for each Figma "
            + "component or layer name you want bound to a specific control.", k_Group);
        public static readonly string MappingFoldout = L10n.Tr("Mapping", k_Group);
        public static readonly string GlobalSettingsHint = L10n.Tr("Global settings", k_Group);

        public static readonly string FontMap = L10n.Tr("Font Map", k_Group);
        public static readonly string FontMapTooltip = L10n.Tr("Figma font family → project Font/FontAsset.", k_Group);
        public static readonly string AddFont = L10n.Tr("+ Add font", k_Group);
        public static readonly string ListIsEmpty = L10n.Tr("List is empty", k_Group);
        public static readonly string FontFamilyTooltip = L10n.Tr("Figma font family, e.g. \"Inter\".", k_Group);
        public static readonly string FontAssetTooltip = L10n.Tr(
            "TextCore FontAsset to use for this Figma font family.", k_Group);
        public static readonly string RemoveFont = L10n.Tr("Remove font", k_Group);
        static readonly string k_OneFont = L10n.Tr("{0} font", k_Group);
        static readonly string k_Fonts = L10n.Tr("{0} fonts", k_Group);
        public static string FontCount(int n) => string.Format(n == 1 ? k_OneFont : k_Fonts, n);

        public static readonly string ClassPrefix = L10n.Tr("USS Class Prefix", k_Group);
        public static readonly string ClassPrefixTooltip = L10n.Tr(
            "USS class prefix for generated classes (e.g. \"app\" → .app-login-button). Empty = no prefix. "
            + "Namespaces generated classes away from your own USS.", k_Group);

        public static readonly string ControlMap = L10n.Tr("Control Map", k_Group);
        public static readonly string ExpandAll = L10n.Tr("Expand all", k_Group);
        public static readonly string CollapseAll = L10n.Tr("Collapse all", k_Group);
        public static readonly string AddMapping = L10n.Tr("+ Add mapping", k_Group);
        public static readonly string SearchMappingsTooltip = L10n.Tr(
            "Filter mappings by Figma name, component key or control.", k_Group);
        public static readonly string FilterMappingsTooltip = L10n.Tr(
            "Show all mappings, only those bound to a control, or only forced templates.", k_Group);
        public static readonly string FilterAll = L10n.Tr("All", k_Group);
        public static readonly string FilterControls = L10n.Tr("Controls", k_Group);
        public static readonly string FilterTemplates = L10n.Tr("Templates", k_Group);
        public static readonly string Template = L10n.Tr("Template", k_Group);
        public static readonly string Delete = L10n.Tr("Delete", k_Group);
        public static readonly string UntitledMapping = L10n.Tr("Untitled mapping", k_Group);
        static readonly string k_MatchesNothing = L10n.Tr("{0} (matches nothing)", k_Group);
        public static string MatchesNothing(string name) => string.Format(k_MatchesNothing, name);
        public static readonly string MatchesNothingTooltip = L10n.Tr(
            "This name has no letters or digits, so it never matches a Figma name.", k_Group);
        static readonly string k_OneMapping = L10n.Tr("{0} mapping", k_Group);
        static readonly string k_Mappings = L10n.Tr("{0} mappings", k_Group);
        public static string MappingCount(int n) => string.Format(n == 1 ? k_OneMapping : k_Mappings, n);
        static readonly string k_OneAttr = L10n.Tr("{0} attr", k_Group);
        static readonly string k_Attrs = L10n.Tr("{0} attrs", k_Group);
        public static string AttrCount(int n) => string.Format(n == 1 ? k_OneAttr : k_Attrs, n);
        public static readonly string NoMappingsYet = L10n.Tr(
            "No mappings yet; add one to bind a Figma component or layer name to a control.", k_Group);
        public static readonly string NoMappingsMatch = L10n.Tr(
            "No mappings match the current search or filter.", k_Group);

        public static readonly string ActiveMappingField = L10n.Tr("Active mapping", k_Group);
        public static readonly string ActiveMappingFieldTooltip = L10n.Tr(
            "One project-wide mapping (control map, fonts, class prefix) applied to every import. Drag one here "
            + "or pick it, or create one when empty. Saved with the project.", k_Group);
        public static readonly string NoMappingSelected = L10n.Tr(
            "No mapping selected; imports use built-in defaults.", k_Group);
        public static readonly string CreateMapping = L10n.Tr("Create mapping", k_Group);
        public static readonly string[] SettingsKeywords =
        {
            L10n.Tr("Figma", k_Group), L10n.Tr("connector", k_Group), L10n.Tr("import", k_Group),
            L10n.Tr("control map", k_Group), L10n.Tr("font", k_Group), L10n.Tr("USS class prefix", k_Group),
            L10n.Tr("mapping", k_Group)
        };

        public static readonly string FigmaName = L10n.Tr("Figma name", k_Group);
        public static readonly string Control = L10n.Tr("Control", k_Group);
        public static readonly string ControlTooltip = L10n.Tr(
            "The UI Toolkit control this Figma component/name maps to.", k_Group);
        public static readonly string ControlLockedTooltip = L10n.Tr(
            "Remove all attributes before changing the control.", k_Group);
        public static readonly string None = L10n.Tr("None", k_Group);
        public static readonly string CustomType = L10n.Tr("Custom type…", k_Group);
        public static readonly string CustomTypeField = L10n.Tr("Custom type", k_Group);
        public static readonly string CustomTypeTooltip = L10n.Tr(
            "Fully-qualified type, e.g. MyCompany.UI.Button.", k_Group);
        public static readonly string GlobalNamespaceWarning = L10n.Tr(
            "Not a built-in control, so this imports as a global-namespace element resolved by this name alone. "
            + "A namespaced control needs its fully-qualified type, e.g. MyCompany.UI.Button.", k_Group);
        public static readonly string GlobalNamespace = L10n.Tr("(global)", k_Group);

        public static readonly string Attributes = L10n.Tr("Attributes", k_Group);
        static readonly string k_AttributesCount = L10n.Tr("Attributes ({0})", k_Group);
        public static string AttributesCount(int n) => string.Format(k_AttributesCount, n);
        public static readonly string AddAttribute = L10n.Tr("+ Add attribute", k_Group);
        public static readonly string TemplateEmitsNoAttributes = L10n.Tr(
            "A template emits no attributes; remove these or pick a control.", k_Group);
        public static readonly string NoAttributesYet = L10n.Tr("No attributes yet.", k_Group);
        public static readonly string Attribute = L10n.Tr("Attribute", k_Group);
        public static readonly string Source = L10n.Tr("Source", k_Group);
        public static readonly string Value = L10n.Tr("Value", k_Group);
        public static readonly string AttributeNameTooltip = L10n.Tr("UXML attribute name.", k_Group);
        public static readonly string RemoveAttribute = L10n.Tr("Remove attribute", k_Group);

        public static readonly string SourceFixedValue = L10n.Tr("Fixed Value", k_Group);
        public static readonly string SourceComponentProperty = L10n.Tr("Component Property", k_Group);
        public static readonly string SourceDesignText = L10n.Tr("Design Text", k_Group);
        public static readonly string SourceDesignIcon = L10n.Tr("Design Icon", k_Group);
        public static readonly string SourceLockedTooltip = L10n.Tr(
            "No Figma property type carries an image; this attribute is locked to Fixed Value.", k_Group);
        public static readonly string AbsorbedTextLayer = L10n.Tr("the absorbed text layer", k_Group);
        public static readonly string ExportedIconLayer = L10n.Tr("the exported icon layer", k_Group);

        public static readonly string ImageAssetTooltip = L10n.Tr(
            "Texture, Sprite or VectorImage; stored as a project URL.", k_Group);
        static readonly string k_AssetTooltip = L10n.Tr("{0}; stored as a project URL.", k_Group);
        public static string AssetTooltip(string typeName) => string.Format(k_AssetTooltip, typeName);

        public static readonly string PropertyNameTooltip = L10n.Tr(
            "Figma component property name, without the #id suffix.", k_Group);
        public static readonly string BooleanPropertyNameUnrecordedTooltip = L10n.Tr(
            "Figma Boolean property name. None were recorded for this component in the last import; a name "
            + "typed here is matched against the design at import.", k_Group);
        public static readonly string PropertyNameUnrecordedTooltip = L10n.Tr(
            "Figma component property name, without the #id suffix. None were recorded for this component in "
            + "the last import; a name typed here is matched against the design at import.", k_Group);
        public static readonly string OtherName = L10n.Tr("Other…", k_Group);
        static readonly string k_BooleanProperty = L10n.Tr("{0} (Boolean)", k_Group);
        static readonly string k_TextProperty = L10n.Tr("{0} (Text)", k_Group);
        public static string PropertyChoice(string name, bool boolean)
            => string.Format(boolean ? k_BooleanProperty : k_TextProperty, name);
    }
}
