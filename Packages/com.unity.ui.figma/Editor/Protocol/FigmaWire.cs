using System;

namespace Unity.UIToolkit.Figma
{
    // Inbound wire fields are populated by JsonUtility, never assigned in code.
#pragma warning disable 649

    /// <summary>
    /// Flat, versioned wire schema parsed with JsonUtility. CSS rides as key/value pairs because
    /// JsonUtility can't deserialize arbitrary-key maps.
    /// </summary>
    [Serializable]
    struct FigmaImportMessage
    {
        public int schema;
        public string fileName;
        public int rootIndex;
        public FigmaNode[] nodes;
        public FigmaVariable[] variables; // single-mode collections -> tokens.uss :root custom properties
        public FigmaModeVariable[] modeVariables; // multi-mode collections -> one <mode>.uss per mode
        public string[] modes; // distinct mode names across multi-mode collections
        public string defaultMode; // primary multi-mode collection's default mode; linked when screenModes is absent
        public string[] screenModes; // the mode the screen's content resolves to, per multi-mode collection -> linked in the screen UXML
        public string[] multiModeCollections; // names of multi-mode collections (informational)
    }

    /// <summary>
    /// A Figma colour as Figma holds it: channels in 0..1. FigmaTokens formats it as USS.
    /// </summary>
    [Serializable]
    struct FigmaColor
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [Serializable]
    struct FigmaVariable
    {
        public string id;    // Figma VariableID: the durable handle for write-back
        public string name;  // raw Figma variable name (C# sanitizes to --token)
        public string type;  // "COLOR" | "FLOAT" (informational)
        public string value; // pre-formatted USS value; only older payloads carry one
        public bool hasColor;
        public FigmaColor color;
        public bool hasNumber;
        public float number;
        public string aliasOf; // raw name of the local Variable this aliases (empty if none) -> emitted as var(--target)
    }

    /// <summary>
    /// A variable from a multi-mode collection: its value in every mode; fans out to per-mode .uss files.
    /// Values are an array, not a map (JsonUtility).
    /// </summary>
    [Serializable]
    struct FigmaModeVariable
    {
        public string id;   // Figma VariableID
        public string name; // raw Figma variable name (C# sanitizes to --token)
        public string type; // "COLOR" | "FLOAT"
        public FigmaVariableMode[] values; // one entry per mode of the variable's collection
    }

    [Serializable]
    struct FigmaVariableMode
    {
        public string mode;    // mode name (e.g. "Dark") -> <mode>.uss
        public string value;   // pre-formatted USS value; only older payloads carry one
        public bool hasColor;
        public FigmaColor color;
        public bool hasNumber;
        public float number;
        public string aliasOf; // raw name of the local Variable aliased in this mode (emitted as var(--target))
    }

    [Serializable]
    struct FigmaNode
    {
        public string id;
        public string unityId;
        public int parentIndex;
        public string type;
        public string name;
        public string text;
        public string componentKey; // COMPONENT key, or an INSTANCE's main-component key
        public string control;      // explicit tag from getSharedPluginData("unity","control")
        public bool master;         // fetched COMPONENT master -> a template source, not screen content
        public bool raster;         // an image/vector fill was exported to PNG -> emit as background-image
        public string imageKey;     // staged key of this layer's own exported image; identical artwork shares one
        public string iconKey;      // staged key of a flattened stand-in for this layer, for a control's icon slot
        public bool clickable;      // prototype click/press interaction; reserved, not yet consumed
        public string[] textOverrides; // INSTANCE text values in document order, for template overrides
        public FigmaCssProp[] varRefs; // USS property -> raw Figma variable name (from boundVariables)
        public FigmaCssProp[] css;
        public FigmaVariantStyle[] variants; // per-interaction-state styling from a "State" variant axis
        public FigmaComponentProp[] props;   // INSTANCE component properties (TEXT/BOOLEAN), per-instance values
        public string charProp;              // TEXT sublayer: the component property driving its characters

        // Raw Figma facts. FigmaNodeDerive turns these into USS, so the plugin never needs to know a
        // UI Toolkit default or a USS property name.
        public string layoutMode;        // "NONE" | "HORIZONTAL" | "VERTICAL"
        public string layoutPositioning; // "ABSOLUTE" when pulled out of the parent's auto layout
        public string textAutoResize;    // "WIDTH_AND_HEIGHT" hugs on one line; anything else wraps
        public string textCase;          // "UPPER" | "LOWER" | "TITLE" | "ORIGINAL"
        public bool hasBox;              // absoluteBoundingBox was available
        public float boxX;
        public float boxY;
        public bool hasXY;               // node-local x/y was available (the fallback when hasBox is false)
        public float x;
        public float y;
        public string masterLayoutMode;     // the main component's, so masterCss derives the same way
        public string masterTextAutoResize;

        // A caption control absorbs its label's text, so it needs the label's styling: getCSSAsync puts
        // that on the nested TEXT, not on the control's own box. Sent unfiltered; the importer keeps the
        // subset a control can carry. Present only when the node actually collapsed a caption.
        public FigmaCssProp[] captionCss;
        public FigmaCssProp[] captionVarRefs;
        public FigmaCssProp[] masterCaptionCss;
        public FigmaCssProp[] masterCaptionVarRefs;

        // Master-diff channel: set for INSTANCEs with a resolvable main component and for
        // COMPONENT nodes; the styling model keys shared classes by masterKey and diffs css vs masterCss.
        public string masterKey;             // resolved main-component key ("" -> non-component: full inline)
        public string masterName;            // component set name; FigmaNodeDerive appends the variant values
        public FigmaCssProp[] masterAxes;    // variant axis -> value, as Figma reports them (k=axis, v=value)
        public FigmaCssProp[] masterCss;     // master's intrinsic style = class body (empty on COMPONENT nodes: own css is the body)
        public FigmaCssProp[] masterVarRefs; // master's var bindings -> class-body var() refs
        public bool remote;                  // library COMPONENT: id unresolvable in the open file -> class edits fan out per instance
    }

    [Serializable]
    struct FigmaCssProp
    {
        public string k;
        public string v;
    }

    // One Figma component property on an instance. Names arrive as Figma stores them, with a
    // "#id" suffix on TEXT/BOOLEAN properties; match on the base name.
    [Serializable]
    struct FigmaComponentProp
    {
        public string k; // property name, e.g. "Label#12:3"
        public string t; // "TEXT" | "BOOLEAN"
        public string v; // instance value, stringified
        public string d; // the master's default, stringified; absent from plugins before v5
    }

    /// <summary>
    /// One interaction state of a component: state name, full effective styling, and its OWN variable
    /// bindings (a themed state's delta must emit its own var(--token)). Becomes a pseudo-class rule.
    /// </summary>
    [Serializable]
    struct FigmaVariantStyle
    {
        public string state;   // a "State" axis value, or Figma's prototype trigger name
        public string source;  // "axis" (a value a designer typed) or "trigger" (a prototype reaction)
        public string nodeId;          // the variant COMPONENT's node id, so state edits can write back
        public FigmaCssProp[] css;
        public FigmaCssProp[] varRefs; // the VARIANT's bound-variable refs (null when the variant has none)
        public FigmaCssProp[] captionCss;     // the variant's caption label, unfiltered
        public FigmaCssProp[] captionVarRefs;
        public string textAutoResize;         // the caption's, for the same wrapping reason as a node's
    }

    /// <summary>
    /// A node's write-back baseline: its Figma node, its (doc, structural path) location, what was emitted
    /// there, and the property values at import (later USS/inline edits diff against them).
    /// </summary>
    [Serializable]
    struct FigmaNodeRecord
    {
        public string figmaNodeId;
        public string cssClass;   // the node's unique identity class ("" once a structural path is recorded)
        public string styleClass; // shared COMPONENT class ("" when none): a master-targeted ack advances every sharer
        public string doc;        // document the element renders in: screen assetName or template name
        public string path;       // structural path within doc, e.g. "1/2/1" ("" falls back to the class branch)
        public string tag;        // element name emitted at path (empty on manifests written before it was recorded)
        public string classes;    // class attribute emitted at path; the diff checks these still sit there
        public FigmaCssProp[] props; // supported props only (k=USS property, v=value at import)
    }

    /// <summary>
    /// One emitted pseudo-class rule (.cls:hover) plus the Figma variant it came from, so edits to the
    /// rule can be written back.
    /// </summary>
    [Serializable]
    struct FigmaStateRecord
    {
        public string className;
        public string pseudo;        // ":hover" | ":active" | ":disabled" ...
        public string figmaNodeId;   // the variant COMPONENT node ("" -> unresolvable, so read-only)
        public FigmaCssProp[] props; // supported props emitted in the rule, at import
    }

    /// <summary>
    /// Class -> in-file master routing for class-body edits: target the master once and let Figma
    /// propagate. Masters not in the file have no record -> per-instance fan-out.
    /// </summary>
    [Serializable]
    struct FigmaComponentRecord
    {
        public string className;
        public string masterNodeId;
        public FigmaCssProp[] props; // the rule body at import, so a class edit is diffed against the class
    }

    /// <summary>
    /// A node property whose value changed in Unity, to push back to its Figma node (Design mode).
    /// </summary>
    [Serializable]
    struct FigmaPropertyChange
    {
        public string figmaNodeId;
        public string prop;  // USS property: background-color | color | border-radius | width | height | font-size
        public string value; // the new USS value; effect props in FigmaEffectValue's canonical form
        public string file;  // raw Figma file name (write-back scoping + ack key); stamped at scan, not by the pure diff
    }

    /// <summary>
    /// POST /figma/ack body: applied write-back keys to drop from Unity's queues. Both are file-qualified
    /// so acking one file's change can't pop a same-id change in another.
    /// </summary>
    [Serializable]
    struct FigmaAck
    {
        public string[] ids;      // token keys, "file|variableId"
        public string[] propKeys; // property keys, "file|figmaNodeId|prop"
    }

#pragma warning restore 649
}
