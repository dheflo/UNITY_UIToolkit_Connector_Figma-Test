namespace Unity.UIToolkit.Figma
{
    // A UXML control the mapping drawer can target.
    struct DiscoveredControl
    {
        public string ns;        // namespace, e.g. "UnityEngine.UIElements" or "MyCompany.UI"
        public string tag;       // local type name, e.g. "Button"
        public string fullName;  // ns + "." + tag
        public bool engine;      // ns is UnityEngine.UIElements -> stored/emitted as a bare name
    }

    // One authorable attribute of a control, for the drawer's property hints (e.g. SliderInt -> low-value).
    struct ControlAttrInfo
    {
        public string name;         // UXML attribute name, e.g. "low-value"
        public System.Type type;    // CLR type of the attribute value (for a type-matched Value editor); may be null
        public string typeName;     // CLR type (short), e.g. "Int32"
        public string defaultValue; // default as a string, or "" when none
        public bool inherited;      // declared on a base type (VisualElement etc.), not the control itself
    }
}
