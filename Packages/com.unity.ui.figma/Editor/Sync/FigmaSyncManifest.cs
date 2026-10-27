using System;
using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// One screen's slice of an import: what it generated plus token/node write-back baselines. Keyed by
    /// the stable Figma rootNodeId so screens sharing a folder never clobber each other.
    /// </summary>
    [Serializable]
    class FigmaScreenManifest
    {
        public string assetName;               // screen uxml/uss base name (also the .uss diffed for write-back)
        public string rootNodeId;              // stable Figma node id; matches a screen across renames
        public string contentHash;
        public List<string> generated = new List<string>();               // paths relative to the import folder
        public List<FigmaTokenRecord> tokens = new List<FigmaTokenRecord>();
        public List<FigmaNodeRecord> nodes = new List<FigmaNodeRecord>();
        public List<FigmaComponentRecord> components = new List<FigmaComponentRecord>(); // class->master routing
        public List<FigmaStateRecord> states = new List<FigmaStateRecord>();             // .cls:hover -> variant node
    }

    /// <summary>
    /// Per-file import ledger, one entry per screen, so a re-import updates in place and deletes only its
    /// own now-unused files. Persisted as the hidden .figma-manifest.json sidecar; pure for the harness.
    /// </summary>
    [Serializable]
    class FigmaSyncManifest
    {
        public string fileName;
        public List<FigmaScreenManifest> screens = new List<FigmaScreenManifest>();
        public List<string> matchedMappings = new List<string>();

        /// <summary>
        /// Drops every screen with this assetName (its .uxml was deleted; auto-push must not resurrect it).
        /// </summary>
        public int RemoveScreensByAsset(string assetName)
        {
            return string.IsNullOrEmpty(assetName)
                ? 0
                : screens.RemoveAll(s => s != null && s.assetName == assetName);
        }

        /// <summary>
        /// Records an applied token's new value as the baseline; otherwise the same edit keeps showing
        /// as a diff forever. Returns true if a record changed.
        /// </summary>
        public bool AdvanceTokenBaseline(string id, string value)
        {
            var changed = false;
            foreach (var screen in screens)
            {
                if (screen?.tokens == null)
                    continue;
                for (var i = 0; i < screen.tokens.Count; ++i)
                {
                    if (screen.tokens[i].id != id || screen.tokens[i].value == value)
                        continue;
                    var rec = screen.tokens[i];      // struct: mutate the copy, then write it back
                    rec.value = value;
                    screen.tokens[i] = rec;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Records an applied node property as the baseline. A master-targeted change advances every sharer
        /// of the component class EXCEPT ones overriding the prop inline (sharerPropInline; null = all).
        /// </summary>
        public bool AdvanceNodeBaseline(string nodeId, string prop, string value,
            Func<FigmaNodeRecord, bool> sharerPropInline = null)
        {
            var changed = false;
            foreach (var screen in screens)
            {
                if (screen == null)
                    continue;
                changed |= AdvanceStateBaseline(screen, nodeId, prop, value);
                if (screen.nodes == null)
                    continue;

                string className = null; // set when nodeId is a component master in this screen
                if (screen.components != null)
                    for (var c = 0; c < screen.components.Count; ++c)
                    {
                        if (screen.components[c].masterNodeId != nodeId)
                            continue;
                        className = screen.components[c].className;
                        // The class body is diffed against itself, so its baseline has to move too or
                        // the same edit reports again on every scan.
                        var comp = screen.components[c];
                        if (SetProp(ref comp.props, prop, value))
                        {
                            screen.components[c] = comp;
                            changed = true;
                        }
                        break;
                    }

                for (var i = 0; i < screen.nodes.Count; ++i)
                {
                    var rec = screen.nodes[i];
                    var sharer = className != null && !string.IsNullOrEmpty(rec.styleClass)
                        && rec.styleClass == className
                        && (sharerPropInline == null || !sharerPropInline(rec));
                    if (rec.figmaNodeId != nodeId && !sharer)
                        continue;

                    var props = rec.props ?? Array.Empty<FigmaCssProp>();
                    var idx = -1;
                    for (var j = 0; j < props.Length; ++j)
                        if (props[j].k == prop) { idx = j; break; }

                    if (idx >= 0)
                    {
                        if (props[idx].v == value)
                            continue;
                        props[idx].v = value;        // mutate the shared array in place (rec.props is the same ref)
                    }
                    else
                    {
                        var grown = new FigmaCssProp[props.Length + 1];
                        Array.Copy(props, grown, props.Length);
                        grown[props.Length] = new FigmaCssProp { k = prop, v = value };
                        rec.props = grown;           // struct: reassigned the array, so write the record back
                        screen.nodes[i] = rec;
                    }
                    changed = true;
                }
            }
            return changed;
        }

        // Sets prop on a baseline array, growing it when the property is new. True when anything moved.
        static bool SetProp(ref FigmaCssProp[] props, string prop, string value)
        {
            props = props ?? Array.Empty<FigmaCssProp>();
            for (var i = 0; i < props.Length; ++i)
            {
                if (props[i].k != prop)
                    continue;
                if (props[i].v == value)
                    return false;
                props[i].v = value;
                return true;
            }

            var grown = new FigmaCssProp[props.Length + 1];
            Array.Copy(props, grown, props.Length);
            grown[props.Length] = new FigmaCssProp { k = prop, v = value };
            props = grown;
            return true;
        }

        // A pseudo-state change targets the variant node, which no nodes[] record owns.
        static bool AdvanceStateBaseline(FigmaScreenManifest screen, string nodeId, string prop, string value)
        {
            if (screen.states == null)
                return false;

            var changed = false;
            for (var i = 0; i < screen.states.Count; ++i)
            {
                var rec = screen.states[i];
                if (rec.figmaNodeId != nodeId)
                    continue;

                var props = rec.props ?? Array.Empty<FigmaCssProp>();
                var idx = -1;
                for (var j = 0; j < props.Length; ++j)
                    if (props[j].k == prop) { idx = j; break; }

                if (idx >= 0)
                {
                    if (props[idx].v == value)
                        continue;
                    props[idx].v = value;    // the array is shared with the record, so no write-back needed
                }
                else
                {
                    // Added by the user: grow the baseline or the change re-reports forever.
                    var grown = new FigmaCssProp[props.Length + 1];
                    Array.Copy(props, grown, props.Length);
                    grown[props.Length] = new FigmaCssProp { k = prop, v = value };
                    rec.props = grown;       // struct: reassigned the array, so write the record back
                    screen.states[i] = rec;
                }
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Prior-generated paths the current push no longer produces and no other screen still owns.
        /// </summary>
        public static List<string> Orphans(ICollection<string> priorGenerated, ICollection<string> current,
            ICollection<string> protectedByOthers)
        {
            var orphans = new List<string>();
            if (priorGenerated == null)
                return orphans;
            foreach (var path in priorGenerated)
                if (!string.IsNullOrEmpty(path)
                    && (current == null || !current.Contains(path))
                    && (protectedByOthers == null || !protectedByOthers.Contains(path)))
                    orphans.Add(path);
            return orphans;
        }

        /// <summary>
        /// Stable FNV-1a hash of the raw payload. Deliberately NOT used to skip regeneration: the dev
        /// replay loop re-runs the same payload after code changes and a hash-skip would keep stale output.
        /// </summary>
        public static string ContentHash(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return "0";

            ulong hash = 14695981039346656037UL;
            foreach (var c in payload)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x");
        }
    }
}
