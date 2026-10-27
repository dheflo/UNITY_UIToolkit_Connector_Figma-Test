using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Classifies COMPONENT masters: one that resolves to a control is inlined at each instance site;
    /// one that resolves to nothing becomes a reusable .uxml template.
    /// </summary>
    class FigmaComponentLibrary
    {
        public struct Template
        {
            public string name;         // unique template + file name
            public int rootIndex;       // master root node index in the context
            public string componentKey; // the Figma componentKey this template serves
            public int textCount;       // TEXT descendants (caps how many instance overrides we emit)
        }

        readonly Dictionary<string, Template> m_TemplateByKey = new Dictionary<string, Template>();
        readonly Dictionary<string, ControlMap.ControlResolution> m_ControlByKey =
            new Dictionary<string, ControlMap.ControlResolution>();
        readonly HashSet<int> m_MasterRoots = new HashSet<int>();
        readonly List<Template> m_Templates = new List<Template>();

        public IReadOnlyList<Template> Templates => m_Templates;
        public int TemplateCount => m_Templates.Count;
        public int ControlCount => m_ControlByKey.Count;

        public bool IsMasterRoot(int index) => m_MasterRoots.Contains(index);

        public bool TryGetTemplate(string componentKey, out Template template)
        {
            template = default;
            return !string.IsNullOrEmpty(componentKey) && m_TemplateByKey.TryGetValue(componentKey, out template);
        }

        public bool TryGetControl(string componentKey, out ControlMap.ControlResolution control)
        {
            control = default;
            return !string.IsNullOrEmpty(componentKey) && m_ControlByKey.TryGetValue(componentKey, out control);
        }

        /// <summary>
        /// Classifies each master root as an inline control or a template. reservedName keeps template file
        /// names from colliding with the screen's own asset name.
        /// </summary>
        public static FigmaComponentLibrary Build(FigmaImportContext context, FigmaImportSettings settings, string reservedName)
        {
            var library = new FigmaComponentLibrary();
            var used = new HashSet<string>();
            if (!string.IsNullOrEmpty(reservedName))
                used.Add(reservedName);

            var instancesByKey = InstancesByKey(context);

            foreach (var root in context.Roots)
            {
                var node = context.Nodes[root];
                if (!node.master || node.type != "COMPONENT" || string.IsNullOrEmpty(node.componentKey))
                    continue;
                if (library.m_ControlByKey.ContainsKey(node.componentKey)
                    || library.m_TemplateByKey.ContainsKey(node.componentKey))
                    continue; // one classification per componentKey

                library.m_MasterRoots.Add(root);

                var resolution = ControlMap.Resolve(context, root, settings);
                if (!resolution.isControl && !Templatable(context, root, settings))
                    continue;

                instancesByKey.TryGetValue(node.componentKey, out var instances);

                if (resolution.isControl)
                {
                    library.m_ControlByKey[node.componentKey] = resolution;
                }
                else if (Instantiated(context, instances) && Interchangeable(context, root, instances))
                {
                    var template = new Template
                    {
                        name = UniqueName(node.name, used),
                        rootIndex = root,
                        componentKey = node.componentKey,
                        textCount = CountText(context, root)
                    };
                    library.m_TemplateByKey[node.componentKey] = template;
                    library.m_Templates.Add(template);
                }
            }

            return library;
        }

        static string UniqueName(string rawName, HashSet<string> used)
        {
            var baseName = FigmaPathGuard.SanitizeName(rawName);
            var name = baseName;
            var n = 2;
            while (used.Contains(name))
                name = baseName + "-" + n++;
            used.Add(name);
            return name;
        }

        // One pass, so classifying N masters does not scan the document N times.
        static Dictionary<string, List<int>> InstancesByKey(FigmaImportContext context)
        {
            var map = new Dictionary<string, List<int>>();
            for (var i = 0; i < context.Nodes.Length; ++i)
            {
                var node = context.Nodes[i];
                if (node.type != "INSTANCE" || string.IsNullOrEmpty(node.componentKey))
                    continue;
                if (!map.TryGetValue(node.componentKey, out var list))
                    map[node.componentKey] = list = new List<int>();
                list.Add(i);
            }
            return map;
        }

        // An explicit tag beats the library, so a component whose every instance is tagged would get a
        // .uxml and a rule nothing references. A master pushed on its own has nothing to ask, and earns one.
        static bool Instantiated(FigmaImportContext context, List<int> instances)
        {
            if (instances == null)
                return true;
            for (var i = 0; i < instances.Count; ++i)
                if (!ControlMap.HasExplicitControl(context.Nodes[instances[i]]))
                    return true;
            return false;
        }

        /// <summary>
        /// True when every instance renders exactly as the master does, allowing only per-instance text and
        /// placement. A template has no way to carry anything else: AttributeOverrides takes attributes but
        /// not style, and style on the instance lands on the TemplateContainer, one level above the root.
        /// An instance that differs by so much as a colour is inlined instead, where the delta survives.
        /// </summary>
        static bool Interchangeable(FigmaImportContext context, int master, List<int> instances)
        {
            if (instances == null)
                return true; // a master pushed on its own has nothing to disagree with it

            var masterNode = context.Nodes[master];
            for (var i = 0; i < instances.Count; ++i)
            {
                var instance = context.Nodes[instances[i]];
                // A tagged instance renders as its control and never reaches the template, so it has no
                // say in whether one is worth emitting for its siblings.
                if (ControlMap.HasExplicitControl(instance))
                    continue;
                // masterCss and masterVarRefs are what the shared class is built from, so the root
                // compares against its own copy of the master rather than the master node.
                if (!SameCss(instance.css, instance.masterCss, true)
                    || !SameCss(instance.varRefs, instance.masterVarRefs, false)
                    || instance.raster != masterNode.raster)
                    return false;
                if (!SameSubtree(context, instances[i], master))
                    return false;
            }
            return true;
        }

        // Instances mirror their main component, so children line up positionally. Everything the emitter
        // reads off a node is compared except text, which is the one difference a template can carry.
        static bool SameSubtree(FigmaImportContext context, int instance, int master)
        {
            var a = context.Children[instance];
            var b = context.Children[master];
            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; ++i)
            {
                var left = context.Nodes[a[i]];
                var right = context.Nodes[b[i]];
                if (left.type != right.type
                    || left.control != right.control   // an override tagged as a control on one side only
                    || left.raster != right.raster     // a fill overridden to an image on one side only
                    || !SameCss(left.css, right.css, false)
                    || !SameCss(left.varRefs, right.varRefs, false))
                    return false;
                if (!SameSubtree(context, a[i], b[i]))
                    return false;
            }
            return true;
        }

        static bool SameCss(FigmaCssProp[] a, FigmaCssProp[] b, bool allowPlacement)
        {
            var left = ToMap(a, allowPlacement);
            var right = ToMap(b, allowPlacement);
            if (left.Count != right.Count)
                return false;
            foreach (var kv in left)
                if (!right.TryGetValue(kv.Key, out var v) || v != kv.Value)
                    return false;
            return true;
        }

        // Keyed rather than positional: declaration order is the plugin's business, not a contract.
        static Dictionary<string, string> ToMap(FigmaCssProp[] css, bool dropPlacement)
        {
            var map = new Dictionary<string, string>();
            if (css == null)
                return map;
            for (var i = 0; i < css.Length; ++i)
                // The one set the <ui:Instance> can carry, so the one set a difference is allowed in.
                if (!dropPlacement || !FigmaToUss.k_Placement.Contains(css[i].k))
                    map[css[i].k] = css[i].v;
            return map;
        }

        /// <summary>
        /// Whether a master's shape lets AttributeOverrides line up. Three things enumerate a template's
        /// text and they must agree: the text-i names FigmaToUxml emits, <see cref="CountText"/>, and the
        /// plugin's collectInstanceText. They do when every TEXT is emitted as its own named element, so
        /// this refuses the two shapes where one of them counts something the others do not.
        /// Nothing asserts that alignment elsewhere: these refusals are what holds it. Widening either of
        /// them means making the three agree by some other means first.
        /// </summary>
        static bool Templatable(FigmaImportContext context, int root, FigmaImportSettings settings)
        {
            // The caller only asks about a master COMPONENT that did not resolve to a control, so the two
            // refusals below are about its descendants; the root satisfies both by construction.
            var ok = true;
            void Recurse(int index)
            {
                foreach (var child in context.Children[index])
                {
                    if (!ok)
                        return;
                    var node = context.Nodes[child];

                    // A TEXT is the slot itself, and resolves to Label, so it passes before the check below.
                    if (node.type == "TEXT")
                        continue;

                    // The emitter descends into an inlined nested instance where the counter and the plugin
                    // stop, and whether it inlines is unknown until every master is classified.
                    if (node.type == "INSTANCE") { ok = false; return; }

                    // A leaf absorbs or drops its TEXT and a chip absorbs one as its header, leaving no
                    // text-i name for text the others counted. A container that keeps its children is safe.
                    var resolution = ControlMap.Resolve(context, child, settings);
                    if (resolution.isControl && (!resolution.container || resolution.headerFromContent))
                    { ok = false; return; }

                    Recurse(child);
                }
            }
            Recurse(root);
            return ok;
        }

        // TEXT descendants. Caps how many overrides FigmaToUxml emits; Templatable is what keeps that
        // equal to the text-i names emitted, by refusing the shapes where the two could differ.
        static int CountText(FigmaImportContext context, int root)
        {
            var count = 0;
            void Recurse(int index)
            {
                if (context.Nodes[index].type == "TEXT") { count++; return; }
                foreach (var child in context.Children[index])
                    Recurse(child);
            }
            Recurse(root);
            return count;
        }
    }
}
