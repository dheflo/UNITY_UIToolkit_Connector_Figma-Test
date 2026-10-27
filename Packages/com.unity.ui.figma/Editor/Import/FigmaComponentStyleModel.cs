using System;
using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Component-keyed styling: one USS class per distinct component; nodes carry class + inline delta.
    /// Pure (no Unity APIs); runs after FigmaComponentLibrary, before FigmaToUss/FigmaToUxml.
    /// </summary>
    static class FigmaComponentStyleModel
    {
        /// <summary>
        /// One render-ready USS rule per distinct component, in first-occurrence node order.
        /// </summary>
        public struct ComponentRule
        {
            public string className;
            public string body;                  // declaration body ("" -> emit no base rule)
            public List<FigmaStateRule> states;  // interaction-state delta rules (may be null)
            public string masterNodeId;          // in-file master's node id for class-edit->master write-back; "" -> instance fan-out
        }

        /// <summary>
        /// The class-to-master routing table recorded in the manifest (classes whose master is in the file).
        /// </summary>
        public static List<FigmaComponentRecord> ComponentRecords(List<ComponentRule> rules, string ussText)
        {
            var records = new List<FigmaComponentRecord>();
            if (rules == null)
                return records;

            // Read the body back out of the generated USS, as StateRecords does, so the baseline
            // normalizes exactly the way the diff will parse it.
            var emitted = FigmaPropertyDiff.ParseRules(ussText);
            foreach (var rule in rules)
            {
                if (string.IsNullOrEmpty(rule.masterNodeId))
                    continue;

                var props = new List<FigmaCssProp>();
                if (emitted.TryGetValue(rule.className, out var body))
                    foreach (var kv in body)
                        props.Add(new FigmaCssProp { k = kv.Key, v = kv.Value });

                records.Add(new FigmaComponentRecord
                {
                    className = rule.className,
                    masterNodeId = rule.masterNodeId,
                    props = props.ToArray()
                });
            }
            return records;
        }

        /// <summary>
        /// Write-back baselines for the emitted pseudo-class rules, read back out of the generated USS so
        /// they normalize exactly as the diff will. An unknown variant node is recorded as "" (read-only).
        /// </summary>
        public static List<FigmaStateRecord> StateRecords(List<ComponentRule> rules, string ussText)
        {
            var records = new List<FigmaStateRecord>();
            if (rules == null)
                return records;

            var emitted = FigmaPropertyDiff.ParseStateRules(ussText);
            foreach (var rule in rules)
            {
                if (rule.states == null)
                    continue;
                foreach (var state in rule.states)
                {
                    if (!emitted.TryGetValue(rule.className + state.pseudo, out var props))
                        continue;

                    var list = new List<FigmaCssProp>(props.Count);
                    foreach (var kv in props)
                        list.Add(new FigmaCssProp { k = kv.Key, v = kv.Value });
                    records.Add(new FigmaStateRecord
                    {
                        className = rule.className,
                        pseudo = state.pseudo,
                        figmaNodeId = state.nodeId ?? string.Empty,
                        props = list.ToArray()
                    });
                }
            }
            return records;
        }

        public static void Build(FigmaImportContext context, FigmaComponentLibrary library,
            Func<string, string> resolveFont, Func<int, string> resolveBackgroundImage)
        {
            var count = context.Nodes.Length;
            var sharedClass = new string[count];
            var inlineDecls = new List<(string prop, string value)>[count];
            var rules = new List<ComponentRule>();

            // Pass 1: collect each masterKey's class-body source in first-occurrence order. Prefer the first
            // instance's masterCss (matches the caption text-style merge); a master's own css is the fallback.
            var order = new List<string>();
            var bodyByKey = new Dictionary<string, List<(string prop, string value)>>();
            var nameByKey = new Dictionary<string, string>();
            var statesByKey = new Dictionary<string, List<FigmaStateRule>>();
            var masterIdByKey = new Dictionary<string, string>();
            foreach (var index in context.Order)
            {
                var node = context.Nodes[index];
                var key = node.masterKey;
                if (string.IsNullOrEmpty(key))
                    continue;
                // A remote (library) master's id can't be resolved in the open file; unrecorded -> fan-out.
                if (node.type == "COMPONENT" && !node.remote && !masterIdByKey.ContainsKey(key))
                    masterIdByKey[key] = node.id;
                // First carrier wins; sharers are instances of one component, so their variant sets agree.
                if (!statesByKey.ContainsKey(key))
                {
                    var states = FigmaToUss.BuildStateRules(context, index, resolveFont);
                    if (states != null)
                        statesByKey[key] = states;
                }
                if (!bodyByKey.ContainsKey(key))
                {
                    var body = node.type == "COMPONENT" && (node.masterCss == null || node.masterCss.Length == 0)
                        ? FigmaToUss.ClassBodyDecls(node.css, node.varRefs, resolveFont)
                        : FigmaToUss.ClassBodyDecls(node.masterCss, node.masterVarRefs, resolveFont);
                    if (body.Count == 0 && !statesByKey.ContainsKey(key))
                        continue; // no body and no states -> no class; a later node may still establish one
                    order.Add(key);
                    bodyByKey[key] = body;
                    nameByKey[key] = node.masterName;
                }
            }

            // Pass 2: class names from component (set) names, disambiguated "-2"/"-3" (set names can collide).
            var classByKey = new Dictionary<string, string>();
            var used = new HashSet<string>();
            foreach (var key in order)
            {
                var label = context.ClassNameFrom(string.IsNullOrEmpty(nameByKey[key]) ? "component" : nameByKey[key]);
                var cls = label;
                for (var n = 2; used.Contains(cls); ++n)
                    cls = label + "-" + n;
                used.Add(cls);
                classByKey[key] = cls;

                statesByKey.TryGetValue(key, out var states);
                masterIdByKey.TryGetValue(key, out var masterNodeId);
                rules.Add(new ComponentRule
                {
                    className = cls,
                    body = FigmaToUss.RenderBody(bodyByKey[key]),
                    states = states,
                    masterNodeId = masterNodeId ?? string.Empty
                });
            }

            // Pass 3: per node, shared class + inline delta (components), placement only (templated
            // instances), or the full body inline (non-components).
            foreach (var index in context.Order)
            {
                var node = context.Nodes[index];
                var templated = node.type == "INSTANCE" && library != null
                    && library.TryGetTemplate(node.componentKey, out _);
                if (templated)
                {
                    // The template root owns the class; class styling here would double-paint.
                    sharedClass[index] = null;
                    inlineDecls[index] = FigmaToUss.PlacementDecls(context, index, resolveFont);
                }
                else if (!string.IsNullOrEmpty(node.masterKey) && classByKey.TryGetValue(node.masterKey, out var cls))
                {
                    sharedClass[index] = cls;
                    inlineDecls[index] = FigmaToUss.InstanceInlineDecls(context, index, bodyByKey[node.masterKey],
                        resolveFont, resolveBackgroundImage);
                }
                else
                {
                    sharedClass[index] = null;
                    inlineDecls[index] = FigmaToUss.BuildDeclarationBodyDecls(context, index, resolveFont,
                        resolveBackgroundImage);
                }
            }

            context.SharedClassByIndex = sharedClass;
            context.InlineDeclsByIndex = inlineDecls;
            context.ComponentRules = rules;
        }
    }
}
