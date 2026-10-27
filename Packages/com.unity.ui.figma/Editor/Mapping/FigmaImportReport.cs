using System;
using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Post-import notes surfaced to the user (e.g. why something rendered blank). Derived from the parsed
    /// context, plus what the emitter reports it discarded, which only the emitter knows. Never changes the
    /// emitted UXML/USS.
    /// </summary>
    static class FigmaImportReport
    {
        /// <summary>
        /// Collects the import notes. Everything after settings is optional so the pure harness and unit
        /// tests can run without the editor-only font map, a component library, or a run of the emitter.
        /// </summary>
        public static List<FigmaImportNote> Collect(FigmaImportContext context, FigmaImportSettings settings,
            Func<string, string> resolveFont = null, FigmaComponentLibrary library = null,
            List<int> discarded = null)
        {
            var notes = new List<FigmaImportNote>();
            CollectNearMissNames(context, settings, notes);
            CollectDiscardedLayers(context, discarded, notes);
            CollectCompositeMismatches(context, settings, notes);
            CollectImagePending(context, notes);
            CollectFontFallbacks(context, resolveFont, notes);
            CollectUnmappedStates(context, notes);
            CollectSkippedStateVariants(context, notes);
            CollectUnlinkedTextProps(context, notes);
            CollectMissingMasters(context, library, notes);
#if !UITOOLKIT_HAS_USS_GAP
            CollectDroppedCss(context, notes, FigmaImportNoteKind.DroppedGap, "gap", "row-gap", "column-gap");
#endif
            CollectDroppedCss(context, notes, FigmaImportNoteKind.DroppedShadows, "box-shadow");
#if !UITOOLKIT_HAS_USS_FILTER
            CollectDroppedCss(context, notes, FigmaImportNoteKind.DroppedBackgroundBlur, "backdrop-filter");
#endif
            CollectDroppedGradients(context, notes);
            CollectDroppedCss(context, notes, FigmaImportNoteKind.DroppedTextStrokes,
                "-webkit-text-stroke", "-webkit-text-stroke-width", "-webkit-text-stroke-color");
            return notes;
        }

        // A rasterized layer carries its gradient in the PNG, so only unrasterized fills are lost.
        static void CollectDroppedGradients(FigmaImportContext context, List<FigmaImportNote> notes)
        {
            var count = 0;
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var node = context.Nodes[i];
                if (node.raster || node.css == null)
                    continue;
                foreach (var prop in node.css)
                {
                    if (prop.k != "background" || prop.v == null
                        || prop.v.IndexOf("gradient(", StringComparison.Ordinal) < 0)
                        continue;
                    count++;
                    break;
                }
            }
            if (count > 0)
                notes.Add(Note(FigmaImportNoteKind.DroppedGradients, count));
        }

        static FigmaImportNote Note(FigmaImportNoteKind kind, int count, List<string> items = null)
        {
            return new FigmaImportNote { kind = kind, count = count, items = items?.ToArray() };
        }

        // FigmaToUss drops these properties on this Editor version, so say so rather than let the
        // effect silently vanish. One note per kind, counting the layers that carried any of the keys.
        static void CollectDroppedCss(FigmaImportContext context, List<FigmaImportNote> notes,
            FigmaImportNoteKind kind, params string[] keys)
        {
            var count = 0;
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var css = context.Nodes[i].css;
                if (css == null)
                    continue;
                foreach (var prop in css)
                {
                    if (Array.IndexOf(keys, prop.k) < 0)
                        continue;
                    count++;
                    break;
                }
            }
            if (count > 0)
                notes.Add(Note(kind, count));
        }

        /// <summary>
        /// Note naming the theme modes exported to per-mode stylesheets, or null when none.
        /// </summary>
        public static FigmaImportNote? MultiModeNote(IReadOnlyList<string> modes)
        {
            if (modes == null || modes.Count == 0)
                return null;
            var files = new string[modes.Count];
            var names = new string[modes.Count];
            for (var i = 0; i < modes.Count; ++i)
            {
                names[i] = modes[i];
                files[i] = FigmaTokens.ModeFileName(modes[i]);
            }
            return new FigmaImportNote
            {
                kind = FigmaImportNoteKind.MultiMode,
                count = modes.Count,
                items = names,
                extra = files
            };
        }

        /// <summary>
        /// Warns when distinct Figma variable names collapse onto one USS custom property, since only the
        /// first is emitted and every reference then resolves to its value.
        /// </summary>
        public static FigmaImportNote? TokenCollisionNote(IReadOnlyDictionary<string, List<string>> collisions)
        {
            if (collisions == null || collisions.Count == 0)
                return null;

            var tokens = new List<string>(collisions.Count);
            var names = new List<string>(collisions.Count);
            foreach (var kv in collisions)
            {
                tokens.Add(kv.Key);
                names.Add(string.Join(", ", kv.Value));
            }
            return new FigmaImportNote
            {
                kind = FigmaImportNoteKind.TokenCollision,
                count = tokens.Count,
                items = tokens.ToArray(),
                extra = names.ToArray()
            };
        }

        // A component whose name contains a control word without being one. Matching it would be a guess,
        // so it imports as a plain element; this points at the two ways to say what it should be.
        static void CollectNearMissNames(FigmaImportContext context, FigmaImportSettings settings, List<FigmaImportNote> notes)
        {
            var flagged = new List<string>();
            var seen = new HashSet<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var node = context.Nodes[i];
                if (node.type != "COMPONENT" && node.type != "INSTANCE")
                    continue;
                if (ControlMap.Resolve(context, i, settings).isControl)
                    continue;
                if (!ControlMap.NameSuggestsControl(node.name) || !seen.Add(node.name))
                    continue;
                flagged.Add(node.name);
            }

            if (flagged.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.NearMissNames, flagged.Count, flagged));
        }

        /// <summary>
        /// The layers the emitter discarded, reported by the element that could not hold them. Fed by the
        /// emitter rather than re-derived here, because only it knows what it actually dropped.
        /// </summary>
        static void CollectDiscardedLayers(FigmaImportContext context, List<int> discarded, List<FigmaImportNote> notes)
        {
            if (discarded == null || discarded.Count == 0)
                return;

            var owners = new List<int>();
            var countByParent = new Dictionary<int, int>();
            foreach (var index in discarded)
            {
                var parent = context.Nodes[index].parentIndex;
                if (!countByParent.TryGetValue(parent, out var count))
                    owners.Add(parent);
                countByParent[parent] = count + 1;
            }

            var names = new string[owners.Count];
            var counts = new int[owners.Count];
            for (var i = 0; i < owners.Count; ++i)
            {
                names[i] = context.Nodes[owners[i]].name;
                counts[i] = countByParent[owners[i]];
            }
            notes.Add(new FigmaImportNote
            {
                kind = FigmaImportNoteKind.DiscardedLayers,
                count = owners.Count,
                items = names,
                counts = counts
            });
        }

        // A Tab works only inside a TabView, a RadioButton only inside a RadioButtonGroup. Never
        // restructure; just flag the orphan so the designer tags the parent.
        static void CollectCompositeMismatches(FigmaImportContext context, FigmaImportSettings settings, List<FigmaImportNote> notes)
        {
            var orphanTabs = new List<string>();
            var orphanRadios = new List<string>();
            var emptyTabs = new List<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                if (context.Nodes[i].master)
                    continue; // a component master is a template source, not a placed control

                var resolution = ControlMap.Resolve(context, i, settings);
                if (!resolution.isControl)
                    continue;

                if (resolution.tag == "Tab")
                {
                    if (ParentTag(context, i, settings) != "TabView")
                        orphanTabs.Add(context.Nodes[i].name);
                    else if (context.Children[i].Count == 0 && context.Nodes[i].type != "INSTANCE")
                        emptyTabs.Add(context.Nodes[i].name); // empty tab (an INSTANCE's content lives in its master, not the tree)
                }
                else if (resolution.tag == "RadioButton" && ParentTag(context, i, settings) != "RadioButtonGroup")
                    orphanRadios.Add(context.Nodes[i].name);
            }

            if (orphanTabs.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.TabsOutsideTabView, orphanTabs.Count, orphanTabs));
            if (orphanRadios.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.RadioButtonsOutsideGroup, orphanRadios.Count, orphanRadios));
            if (emptyTabs.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.EmptyTabs, emptyTabs.Count, emptyTabs));
        }

        static string ParentTag(FigmaImportContext context, int index, FigmaImportSettings settings)
        {
            var parent = context.Nodes[index].parentIndex;
            if (parent < 0 || parent >= context.Nodes.Length)
                return null;
            return ControlMap.Resolve(context, parent, settings).tag;
        }

        // Image/vector fills aren't imported yet; call them out so a blank region reads as pending, not broken.
        static void CollectImagePending(FigmaImportContext context, List<FigmaImportNote> notes)
        {
            var count = 0;
            for (var i = 0; i < context.Nodes.Length; ++i)
                if (IsImageLike(context, i))
                    count++;

            if (count > 0)
                notes.Add(Note(FigmaImportNoteKind.ImagePending, count));
        }

        static bool IsImageLike(FigmaImportContext context, int index)
        {
            if (context.Nodes[index].raster)
                return false; // exported to PNG and emitted as background-image, not pending

            if (FigmaNodeDerive.IsVectorShape(context.Nodes[index].type))
                return true;

            if (FigmaNodeDerive.HasImageFill(context.Nodes[index].css))
                return true;

            return context.Css(index, "background-image") != null;
        }

        // Families with no matching project asset fall back to the default font; flag them to fix.
        static void CollectFontFallbacks(FigmaImportContext context, Func<string, string> resolveFont, List<FigmaImportNote> notes)
        {
            if (resolveFont == null)
                return;

            var missing = new List<string>();
            var seen = new HashSet<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var family = FirstFamily(context.Css(i, "font-family"));
                if (string.IsNullOrEmpty(family) || !seen.Add(family))
                    continue;
                if (resolveFont(family) == null)
                    missing.Add(family);
            }

            if (missing.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.MissingFonts, missing.Count, missing));
        }

        // Unrecognized "State" variant values are skipped (no pseudo-state rule); surface them for renaming.
        static void CollectUnmappedStates(FigmaImportContext context, List<FigmaImportNote> notes)
        {
            var unmapped = new List<string>();
            var seen = new HashSet<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var variants = context.Nodes[i].variants;
                if (variants == null)
                    continue;
                foreach (var v in variants)
                {
                    // Map returns "" for a recognized resting state and ":x" for a real one; only null is unknown.
                    if (string.IsNullOrEmpty(v.state) || FigmaPseudoState.Map(v.state) != null)
                        continue;
                    // A prototype trigger that is not an interaction state is not a naming mistake.
                    if (v.source == "trigger")
                        continue;
                    if (seen.Add(v.state))
                        unmapped.Add(v.state);
                }
            }

            if (unmapped.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.UnmappedStates, unmapped.Count, unmapped));
        }

        /// <summary>
        /// Records that matched this file before but match nothing now, still existing in the settings:
        /// the rename-decay signal, scoped per file so other files' records never nag. `matchedNow` is
        /// the normalized names this push matched, for the caller to persist.
        /// </summary>
        public static FigmaImportNote? MappingDecayNote(FigmaImportContext context, FigmaImportSettings settings,
            List<string> priorMatched, out List<string> matchedNow)
        {
            matchedNow = new List<string>();
            if (settings == null || settings.controlMap == null || settings.controlMap.Count == 0)
                return null;

            var nodeNames = new HashSet<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var key = ControlMap.NormalizeName(context.Nodes[i].name);
                if (key != null)
                    nodeNames.Add(key);
            }

            List<string> decayed = null;
            var prior = priorMatched == null ? null : new HashSet<string>(priorMatched);
            foreach (var entry in settings.controlMap)
            {
                var key = ControlMap.NormalizeName(entry.figmaName);
                if (key == null)
                    continue;
                if (nodeNames.Contains(key))
                {
                    if (!matchedNow.Contains(key))
                        matchedNow.Add(key);
                }
                else if (prior != null && prior.Contains(key))
                {
                    if (decayed == null)
                        decayed = new List<string>();
                    if (!decayed.Contains(entry.figmaName))
                        decayed.Add(entry.figmaName);
                }
            }

            if (decayed == null)
                return null;
            return Note(FigmaImportNoteKind.MappingDecay, decayed.Count, decayed);
        }

        // A TEXT property that drives no layer in the realized variant is ignored (the layer's own text
        // renders), which is safe but silent; name it so the designer knows to link the variant's layer.
        static void CollectUnlinkedTextProps(FigmaImportContext context, List<FigmaImportNote> notes)
        {
            List<string> props = null;
            List<string> components = null;
            HashSet<string> seen = null;
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var node = context.Nodes[i];
                if (node.type != "INSTANCE" || node.props == null || node.props.Length == 0)
                    continue;
                foreach (var p in node.props)
                {
                    if (p.t != "TEXT" || SubtreeHasCharProp(context, i, p.k))
                        continue;
                    // Left at the master's default: nothing was typed into the dead property, so nothing was lost.
                    if (!string.IsNullOrEmpty(p.d) && p.d == p.v)
                        continue;
                    var component = string.IsNullOrEmpty(node.masterName) ? node.name : node.masterName;
                    var prop = BasePropName(p.k);
                    if (seen == null)
                    {
                        seen = new HashSet<string>();
                        props = new List<string>();
                        components = new List<string>();
                    }
                    if (!seen.Add(prop + "\n" + component))
                        continue;
                    props.Add(prop);
                    components.Add(component);
                }
            }

            if (props == null)
                return;
            notes.Add(new FigmaImportNote
            {
                kind = FigmaImportNoteKind.UnlinkedTextProps,
                count = props.Count,
                items = props.ToArray(),
                extra = components.ToArray()
            });
        }

        static bool SubtreeHasCharProp(FigmaImportContext context, int index, string fullName)
        {
            foreach (var child in context.Children[index])
            {
                var node = context.Nodes[child];
                if (node.type == "TEXT" && node.charProp == fullName)
                    return true;
                if (SubtreeHasCharProp(context, child, fullName))
                    return true;
            }
            return false;
        }

        static string BasePropName(string full)
        {
            var hash = full.LastIndexOf('#');
            return hash > 0 ? full.Substring(0, hash) : full;
        }

        // The wire format carries one style diff per variant, not per descendant, so the plugin skips
        // state collection for a component holding two or more texts. Say so rather than lose the states
        // silently.
        static void CollectSkippedStateVariants(FigmaImportContext context, List<FigmaImportNote> notes)
        {
            var skipped = new List<string>();
            var seen = new HashSet<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var node = context.Nodes[i];
                if (node.type != "INSTANCE" || node.textOverrides == null || node.textOverrides.Length < 2)
                    continue;
                if (node.variants != null && node.variants.Length > 0)
                    continue;
                if (node.masterAxes == null)
                    continue;
                var hasStateAxis = false;
                foreach (var axis in node.masterAxes)
                    if (string.Equals(axis.k, "state", StringComparison.OrdinalIgnoreCase))
                    {
                        hasStateAxis = true;
                        break;
                    }
                if (!hasStateAxis)
                    continue;
                var shownName = string.IsNullOrEmpty(node.masterName) ? node.name : node.masterName;
                if (seen.Add(shownName))
                    skipped.Add(shownName);
            }

            if (skipped.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.SkippedStateVariants, skipped.Count, skipped));
        }

        // An instance whose master style is unavailable (old payload, detached, unresolved library master)
        // imports fully inline: correct, just unshared. Surface it so the verbose one-off is diagnosable.
        static void CollectMissingMasters(FigmaImportContext context, FigmaComponentLibrary library, List<FigmaImportNote> notes)
        {
            if (context.SharedClassByIndex == null)
                return; // styling model not run (pure unit-test contexts)

            var names = new List<string>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var node = context.Nodes[i];
                if (node.type != "INSTANCE" || node.master)
                    continue;
                if (library != null && library.TryGetTemplate(node.componentKey, out _))
                    continue; // templated: the template root owns the class, placement-only here by design
                if (context.SharedClass(i) == null)
                    names.Add(node.name);
            }

            if (names.Count > 0)
                notes.Add(Note(FigmaImportNoteKind.MissingMasters, names.Count, names));
        }

        // First family in a CSS font-family list, unquoted. Mirrors the editor-only FigmaFontMap.CleanFamily.
        static string FirstFamily(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily))
                return null;

            var family = fontFamily.Trim();
            var comma = family.IndexOf(',');
            if (comma >= 0)
                family = family.Substring(0, comma);

            return family.Trim().Trim('\'', '"').Trim();
        }
    }
}
