using System;

namespace Unity.UIToolkit.Figma
{
    // Where a mapped attribute's value comes from.
    enum FigmaBindingSource
    {
        FixedValue = 0,        // the authored 'value' literal, emitted as a fallback
        // 1 was LayerName, removed before release; do not reuse.
        ComponentProperty = 2, // a Figma component property; 'value' names it, the instance supplies it
        DesignText = 3,        // the text the import extracts from the drawing; at most one per mapping
        DesignIcon = 4,        // the icon layer the import exports; at most one per mapping
    }

    // One UXML attribute a ControlMapping emits, e.g. a SliderInt's low-value="0". A Design row
    // declares a destination rather than a value. Data-only so ControlMap + FigmaToUxml stay
    // harness-compilable.
    [Serializable]
    struct PropertyBinding
    {
        public string uxmlAttribute;      // e.g. "low-value"
        public FigmaBindingSource source;
        public string value;              // the literal, or the component property name; unused for Design rows
    }
}
