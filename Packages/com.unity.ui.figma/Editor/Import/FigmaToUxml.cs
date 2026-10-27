using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Builds UXML from the node tree: instances become inline controls or ui:Instance templates,
    /// everything else resolves via ControlMap. Component nodes carry class + inline delta.
    /// </summary>
    static class FigmaToUxml
    {
        const string k_EngineNamespace = "UnityEngine.UIElements";
        const string k_EditorNamespace = "UnityEditor.UIElements";

        /// <summary>
        /// One entry per rendered content element. Write-back keys on (document, structural path): /-joined
        /// 1-based indices among CONTENT children only, matching FigmaPropertyDiff's XDocument recovery.
        /// </summary>
        public struct EmissionEntry
        {
            public string figmaNodeId;
            public string path;        // e.g. "1/2/1"
            public string tag;         // the emitted element name, unprefixed
            public string classTokens; // the emitted class attribute value ("" when none)
            public string sharedClass; // component class alone ("" when none); lets a master-targeted ack advance every sharer
            public string inlineStyle; // the emitted style attribute value, unescaped ("" when none)
        }

        // Tracks xmlns prefixes for the header; engine stays "ui", foreign namespaces get derived deduped prefixes.
        class NamespaceScope
        {
            readonly Dictionary<string, string> m_PrefixByNs = new Dictionary<string, string>();
            readonly HashSet<string> m_UsedPrefixes = new HashSet<string>();
            readonly List<KeyValuePair<string, string>> m_Order = new List<KeyValuePair<string, string>>();

            public NamespaceScope()
            {
                Reserve("ui", k_EngineNamespace);
                Reserve("uie", k_EditorNamespace);
            }

            void Reserve(string prefix, string ns)
            {
                m_PrefixByNs[ns] = prefix;
                m_UsedPrefixes.Add(prefix);
                m_Order.Add(new KeyValuePair<string, string>(prefix, ns));
            }

            public string PrefixFor(string ns)
            {
                if (string.IsNullOrEmpty(ns) || ns == k_EngineNamespace)
                    return "ui";
                if (m_PrefixByNs.TryGetValue(ns, out var prefix))
                    return prefix;

                prefix = DerivePrefix(ns);
                Reserve(prefix, ns);
                return prefix;
            }

            // Last meaningful segment (skips UI/Editor/Runtime), letters-only lowercase, deduped; "Some.Team.UI" -> "team".
            string DerivePrefix(string ns)
            {
                var segments = ns.Split('.');
                string baseName = null;
                for (var i = segments.Length - 1; i >= 0; --i)
                {
                    var segment = segments[i];
                    if (segment.Length == 0 || segment == "UI" || segment == "UIElements"
                        || segment == "Editor" || segment == "Runtime")
                        continue;
                    baseName = segment;
                    break;
                }
                if (string.IsNullOrEmpty(baseName))
                    baseName = segments[segments.Length - 1];

                var prefix = Letters(baseName).ToLowerInvariant();
                if (prefix.Length == 0)
                    prefix = "ns";

                var unique = prefix;
                var n = 2;
                while (m_UsedPrefixes.Contains(unique))
                    unique = prefix + n++;
                return unique;
            }

            static string Letters(string value)
            {
                var sb = new StringBuilder(value.Length);
                foreach (var c in value)
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                        sb.Append(c);
                return sb.ToString();
            }

            public string Header()
            {
                var sb = new StringBuilder("<ui:UXML");
                foreach (var entry in m_Order)
                    sb.Append(" xmlns:").Append(entry.Key).Append("=\"").Append(entry.Value).Append("\"");
                sb.Append(">\n");
                return sb.ToString();
            }
        }

        /// <summary>
        /// The screen document: every root except component masters; optional `emission` collects
        /// write-back entries.
        /// </summary>
        public static string BuildScreen(FigmaImportContext context, string ussFileName, IReadOnlyList<string> tokenStyleSrcs,
            FigmaImportSettings settings, FigmaComponentLibrary library, List<EmissionEntry> emission = null,
            List<int> discarded = null)
        {
            var scope = new NamespaceScope();
            var body = new StringBuilder();
            AppendStyleLinks(body, tokenStyleSrcs);
            body.Append("    <Style src=\"").Append(ussFileName).Append("\" />\n");

            AppendTemplateDeclarations(body, context, library, ScreenRoots(context, library), null);

            var textIndex = 0;
            var sibling = 0;
            foreach (var root in context.Roots)
                if (!library.IsMasterRoot(root))
                    AppendNode(body, context, root, 1, settings, library, scope, false, ref textIndex,
                        emission, "", ref sibling, discarded);

            return scope.Header() + body + "</ui:UXML>\n";
        }

        /// <summary>
        /// One component master rendered as a reusable template file. Text elements get stable names so
        /// instance AttributeOverrides can target them.
        /// </summary>
        public static string BuildTemplate(FigmaImportContext context, string ussFileName, IReadOnlyList<string> tokenStyleSrcs,
            FigmaImportSettings settings, FigmaComponentLibrary library, FigmaComponentLibrary.Template template,
            List<EmissionEntry> emission = null, List<int> discarded = null)
        {
            var scope = new NamespaceScope();
            var body = new StringBuilder();
            AppendStyleLinks(body, tokenStyleSrcs);
            body.Append("    <Style src=\"").Append(ussFileName).Append("\" />\n");

            AppendTemplateDeclarations(body, context, library, new[] { template.rootIndex }, template.name);

            var textIndex = 0;
            var sibling = 0;
            AppendNode(body, context, template.rootIndex, 1, settings, library, scope, true, ref textIndex,
                emission, "", ref sibling, discarded);

            return scope.Header() + body + "</ui:UXML>\n";
        }

        // Token stylesheets link ahead of the screen USS so var(--token) resolves; entries layer in order.
        static void AppendStyleLinks(StringBuilder body, IReadOnlyList<string> tokenStyleSrcs)
        {
            if (tokenStyleSrcs == null)
                return;
            foreach (var src in tokenStyleSrcs)
                if (!string.IsNullOrEmpty(src))
                    body.Append("    <Style src=\"").Append(src).Append("\" />\n");
        }

        static IEnumerable<int> ScreenRoots(FigmaImportContext context, FigmaComponentLibrary library)
        {
            foreach (var root in context.Roots)
                if (!library.IsMasterRoot(root))
                    yield return root;
        }

        // `self`: a template never declares itself.
        static void AppendTemplateDeclarations(StringBuilder sb, FigmaImportContext context,
            FigmaComponentLibrary library, IEnumerable<int> roots, string self)
        {
            // First-use order, not Dictionary order, so the emitted lines are stable across runs.
            var used = new List<FigmaComponentLibrary.Template>();
            var seen = new HashSet<string>();
            foreach (var root in roots)
                CollectUsedTemplates(context, library, root, used, seen);

            foreach (var template in used)
            {
                if (template.name == self)
                    continue;
                sb.Append("    <Template name=\"").Append(template.name)
                    .Append("\" src=\"").Append(template.name).Append(".uxml\" />\n");
            }
        }

        static void CollectUsedTemplates(FigmaImportContext context, FigmaComponentLibrary library, int index,
            List<FigmaComponentLibrary.Template> used, HashSet<string> seen)
        {
            var node = context.Nodes[index];
            // A tagged instance renders as its control, so it does not declare the template (see AppendNode).
            if (node.type == "INSTANCE" && !ControlMap.HasExplicitControl(node)
                && library.TryGetTemplate(node.componentKey, out var template))
            {
                if (seen.Add(template.name))
                    used.Add(template);
                return; // the instance's content lives in its own template
            }

            foreach (var child in context.Children[index])
                CollectUsedTemplates(context, library, child, used, seen);
        }

        // Depth is bounded and repeats per node; hand out shared strings instead of allocating per call.
        static readonly string[] k_Indents = BuildIndents();

        static string[] BuildIndents()
        {
            var indents = new string[24];
            for (var i = 0; i < indents.Length; ++i)
                indents[i] = new string(' ', i * 4);
            return indents;
        }

        static string Indent(int depth)
        {
            return depth >= 0 && depth < k_Indents.Length ? k_Indents[depth] : new string(' ', depth * 4);
        }

        static void AppendNode(StringBuilder sb, FigmaImportContext context, int index, int depth,
            FigmaImportSettings settings, FigmaComponentLibrary library, NamespaceScope scope,
            bool nameText, ref int textIndex, List<EmissionEntry> emission, string parentPath, ref int sibling,
            List<int> discarded)
        {
            var node = context.Nodes[index];
            var indent = Indent(depth);

            // Every branch below renders exactly one content element at this level.
            sibling++;
            var path = parentPath.Length == 0 ? sibling.ToString() : parentPath + "/" + sibling;

            // An explicit per-instance tag overrides the component library (tag = precedence #1).
            var fromLibrary = false;
            var collapse = false;   // a library control renders as one element, whatever its own flags say
            var libraryControl = default(ControlMap.ControlResolution);
            if (node.type == "INSTANCE" && library != null && !ControlMap.HasExplicitControl(node))
            {
                if (library.TryGetTemplate(node.componentKey, out var template))
                {
                    AppendInstance(sb, context, index, indent, template, emission, path);
                    return;
                }
                // A library control collapses to one element. A container must not: its children are its
                // content, and a TabView emitted as a leaf loses every Tab inside it.
                if (library.TryGetControl(node.componentKey, out libraryControl))
                {
                    collapse = !libraryControl.container;
                    fromLibrary = true;
                }
                else
                {
                    fromLibrary = libraryControl.isControl;
                }
            }

            var resolution = fromLibrary ? libraryControl : ControlMap.Resolve(context, index, settings);
            // A global-namespace control emits unprefixed; UXML resolves it by bare name.
            var prefix = resolution.ns == ControlMap.GlobalNamespace
                ? string.Empty : scope.PrefixFor(resolution.ns);
            var qualified = prefix.Length == 0 ? resolution.tag : prefix + ":" + resolution.tag;
            var classAttr = ClassAndStyle(context, index, resolution.ussClass, emission, path, resolution.tag);

            // Containers keep their children and carry a header of their own (Tab.label, Foldout.text). A
            // chip holds its caption, so a lone text inside one is that header and no page is left.
            if (resolution.container)
            {
                var header = string.Empty;
                var absorbed = false;
                string title = null;
                if (!string.IsNullOrEmpty(resolution.captionAttr))
                {
                    title = resolution.headerFromContent ? SoleTextDescendant(context, index) : null;
                    absorbed = title != null;
                    if (!absorbed)
                        title = node.name?.Trim();
                    if (!string.IsNullOrEmpty(title))
                        header = " " + resolution.captionAttr + "=\"" + EscapeText(title) + "\"";
                }

                // A chip takes its whole content as the header, so everything drawn inside it goes, bar a
                // text child that became the header in its entirety.
                if (absorbed && discarded != null)
                    RecordDiscarded(context, discarded, context.Children[index], title, -1);

                var kids = absorbed ? k_NoChildren : context.Children[index];
                if (kids.Count == 0)
                {
                    sb.Append(indent).Append('<').Append(qualified)
                        .Append(header).Append(BindingsAttr(resolution, context, index, header.Length != 0, false))
                        .Append(classAttr).Append(" />\n");
                    return;
                }

                sb.Append(indent).Append('<').Append(qualified)
                    .Append(header).Append(BindingsAttr(resolution, context, index, header.Length != 0, false))
                    .Append(classAttr).Append(">\n");
                var kidSibling = 0;
                foreach (var child in kids)
                    AppendNode(sb, context, child, depth + 1, settings, library, scope, nameText, ref textIndex,
                        emission, path, ref kidSibling, discarded);
                sb.Append(indent).Append("</").Append(qualified).Append(">\n");
                return;
            }

            // A control with an icon slot takes one text-free child as its own icon. The candidate is
            // structural so the icon is never mistaken for the caption, but only an icon we can point at
            // leaves the children: with no exported image, a control that can render the layer still should.
            var iconCandidate = ControlMap.IconChild(context, index, resolution);
            var iconUri = iconCandidate >= 0 ? context.ImageUri(iconCandidate) : null;
            var iconChild = iconUri == null ? -1 : iconCandidate;

            // Leaf caption: a TEXT node's own text, or a framed single text child absorbed so a tagged
            // control is a clean leaf instead of a control wrapping a Label.
            string captionText = null;
            var captionChild = -1;
            if (!string.IsNullOrEmpty(resolution.captionAttr))
            {
                if (node.type == "TEXT")
                    captionText = node.text;
                else
                {
                    captionText = PropertyCaption(context, index, resolution, out captionChild);
                    if (captionText == null)
                        captionText = SingleTextChild(context, index, iconCandidate, out captionChild);
                    if (captionText == null)
                        captionText = FirstTextOverride(node);
                }
            }

            var absorbedChildren = AbsorbedPropertyChildren(context, index, resolution, iconChild, captionChild);
            var remaining = Remaining(context, index, iconChild, captionChild, absorbedChildren);
            var rendersChildren = remaining.Count > 0 && !resolution.leaf && !collapse;

            // An override-sourced caption is the text of a descendant rather than a child we consumed, so
            // where that descendant is about to be rendered the attribute would draw it a second time.
            if (rendersChildren && captionChild < 0 && node.type != "TEXT")
                captionText = null;

            // In templates, TEXT gets a stable text-<i> name for AttributeOverrides. A collapsed caption
            // control still consumes its TEXT slot, and an absorbed child consumes one per text it holds
            // (keeps template/instance text ordering in lockstep).
            var nameAttr = string.Empty;
            if (nameText && (node.type == "TEXT" || captionText != null))
            {
                if (node.type == "TEXT")
                    nameAttr = " name=\"text-" + textIndex + "\"";
                textIndex++;
            }
            if (nameText && absorbedChildren != null)
                foreach (var a in absorbedChildren)
                    textIndex += TextDescendantCount(context, a);

            var caption = captionText == null ? string.Empty
                : " " + resolution.captionAttr + "=\"" + EscapeText(captionText) + "\"";
            var icon = iconChild < 0 ? string.Empty
                : " " + resolution.iconAttr + "=\"" + Escape(iconUri) + "\"";

            // A catalog leaf draws itself, so whatever it could not absorb is dropped. Foreign controls keep
            // theirs: nesting is visible where deleting is not.
            if (!rendersChildren)
            {
                if (discarded != null)
                    RecordDiscarded(context, discarded, remaining, captionText, captionChild);

                sb.Append(indent).Append('<').Append(qualified).Append(nameAttr)
                    .Append(caption).Append(icon)
                    .Append(BindingsAttr(resolution, context, index, caption.Length != 0, icon.Length != 0))
                    .Append(classAttr).Append(" />\n");
                return;
            }

            sb.Append(indent).Append('<').Append(qualified).Append(nameAttr)
                .Append(caption).Append(icon)
                .Append(BindingsAttr(resolution, context, index, caption.Length != 0, icon.Length != 0))
                .Append(classAttr).Append(">\n");
            var childSibling = 0;
            foreach (var child in remaining)
                AppendNode(sb, context, child, depth + 1, settings, library, scope, nameText, ref textIndex,
                    emission, path, ref childSibling, discarded);
            sb.Append(indent).Append("</").Append(qualified).Append(">\n");
        }

        // <ui:Instance> must be prefixed (the importer requires the namespace, unlike <Template>/<Style>).
        // Its AttributeOverrides children are NOT content elements (excluded from structural paths).
        static void AppendInstance(StringBuilder sb, FigmaImportContext context, int index, string indent,
            FigmaComponentLibrary.Template template, List<EmissionEntry> emission, string path)
        {
            var node = context.Nodes[index];
            var classAttr = ClassAndStyle(context, index, string.Empty, emission, path, "Instance");
            var overrides = node.textOverrides;
            var count = overrides == null ? 0 : System.Math.Min(overrides.Length, template.textCount);

            if (count <= 0)
            {
                sb.Append(indent).Append("<ui:Instance template=\"").Append(template.name).Append('"')
                    .Append(classAttr).Append(" />\n");
                return;
            }

            sb.Append(indent).Append("<ui:Instance template=\"").Append(template.name).Append('"')
                .Append(classAttr).Append(">\n");
            for (var i = 0; i < count; ++i)
                sb.Append(indent).Append("    <AttributeOverrides element-name=\"text-").Append(i)
                    .Append("\" text=\"").Append(EscapeText(overrides[i])).Append("\" />\n");
            sb.Append(indent).Append("</ui:Instance>\n");
        }

        static readonly List<int> k_NoChildren = new List<int>();

        // The one text under a container, wherever it sits: a design system wraps a caption in a Text
        // component. Null for none or several, which is real content rather than a chip that is its label.
        static string SoleTextDescendant(FigmaImportContext context, int index)
        {
            string found = null;
            var count = 0;
            void Recurse(int i)
            {
                var node = context.Nodes[i];
                if (node.type == "TEXT")
                {
                    count++;
                    found = node.text;
                    return;
                }
                foreach (var child in context.Children[i])
                    Recurse(child);
            }
            foreach (var child in context.Children[index])
                Recurse(child);

            return count == 1 && !string.IsNullOrEmpty(found) ? found : null;
        }

        // Text of a node's single TEXT child, for absorbing into a caption; null otherwise.
        /// <summary>
        /// Records what the element could not hold. A text child whose whole content became the caption is
        /// not a loss, so it is spared. A layer that merely contained that text is: the string survives as
        /// an attribute, but the layer and its styling are gone, and the report should say so.
        /// </summary>
        static void RecordDiscarded(FigmaImportContext context, List<int> discarded, IReadOnlyList<int> remaining,
            string captionText, int captionChild)
        {
            var spared = -1;
            if (captionText != null && captionChild < 0)
                foreach (var child in remaining)
                    if (context.Nodes[child].type == "TEXT" && context.Nodes[child].text == captionText)
                    {
                        spared = child;
                        break;
                    }

            // A hidden layer would not have rendered, so dropping it is not a loss worth a note.
            foreach (var child in remaining)
                if (child != spared && !ControlMap.IsHidden(context, child))
                    discarded.Add(child);
        }

        // The caption ladder's top rungs. An authored row names the property; else a lone TEXT property
        // in the subtree (a label is often a nested text component) counts as declared. Only a property
        // that drives a text layer here is trusted, and that layer's characters are the caption: they
        // are what renders. Otherwise null, and the positional rules decide.
        static string PropertyCaption(FigmaImportContext context, int index,
            ControlMap.ControlResolution resolution, out int captionChild)
        {
            captionChild = -1;

            string authored = null;
            if (resolution.bindings != null)
                foreach (var b in resolution.bindings)
                    if (b.source == FigmaBindingSource.ComponentProperty
                        && b.uxmlAttribute == resolution.captionAttr && !string.IsNullOrEmpty(b.value))
                        authored = b.value;

            var texts = new List<FigmaComponentProp>();
            CollectTextProps(context, index, texts);
            if (texts.Count == 0)
                return null;

            string fullName = null;
            if (authored != null)
            {
                foreach (var p in texts)
                    if (PropNameMatches(p.k, authored))
                    {
                        fullName = p.k;
                        break;
                    }
            }
            // Two or more TEXT properties without a row would be a guess.
            else if (texts.Count == 1)
            {
                fullName = texts[0].k;
            }
            if (fullName == null)
                return null;

            // The instance's property value is the declared default until someone sets it, while the
            // driven layer's characters are always what renders. A property that drives no layer here
            // (an unlinked variant) proves nothing, so fall through.
            var driven = FindCharProp(context, index, fullName);
            if (driven < 0)
                return null;

            // Absorb the direct child holding the driven layer, so it is not rendered again.
            var walk = driven;
            while (walk >= 0)
            {
                var parent = context.Nodes[walk].parentIndex;
                if (parent == index)
                {
                    captionChild = walk;
                    break;
                }
                walk = parent;
            }
            return context.Nodes[driven].text;
        }

        // Depth-first, the node's own properties ahead of nested instances'.
        static void CollectTextProps(FigmaImportContext context, int index, List<FigmaComponentProp> into)
        {
            var node = context.Nodes[index];
            if (node.props != null)
                foreach (var p in node.props)
                    if (p.t == "TEXT")
                        into.Add(p);
            foreach (var child in context.Children[index])
                CollectTextProps(context, child, into);
        }

        // The TEXT descendant the property drives, or -1.
        static int FindCharProp(FigmaImportContext context, int index, string fullName)
        {
            foreach (var child in context.Children[index])
            {
                var node = context.Nodes[child];
                if (node.type == "TEXT" && node.charProp == fullName)
                    return child;
                var found = FindCharProp(context, child, fullName);
                if (found >= 0)
                    return found;
            }
            return -1;
        }

        // Property names carry a "#id" suffix on TEXT/BOOLEAN properties; authors type the base name.
        static bool PropNameMatches(string full, string authored)
        {
            if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(authored))
                return false;
            var hash = full.LastIndexOf('#');
            var baseName = hash > 0 ? full.Substring(0, hash) : full;
            return string.Equals(baseName.Trim(), authored.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // The stored full name of the property an authored name reaches, or null; subtree, like PropValue.
        static string PropFullName(FigmaImportContext context, int index, string authored)
        {
            var node = context.Nodes[index];
            if (node.props != null)
                foreach (var p in node.props)
                    if (PropNameMatches(p.k, authored))
                        return p.k;
            foreach (var child in context.Children[index])
            {
                var found = PropFullName(context, child, authored);
                if (found != null)
                    return found;
            }
            return null;
        }

        // Subtree lookup, the node's own properties first, so nested-instance properties resolve too.
        static string PropValue(FigmaImportContext context, int index, string authored)
        {
            var node = context.Nodes[index];
            if (node.props != null)
                foreach (var p in node.props)
                    if (PropNameMatches(p.k, authored))
                        return p.v;
            foreach (var child in context.Children[index])
            {
                var v = PropValue(context, child, authored);
                if (v != null)
                    return v;
            }
            return null;
        }

        // The lone TEXT among the children an icon did not take. Anything else left over is content the
        // control cannot hold, so there is no caption to absorb and the caller decides what to do with it.
        static string SingleTextChild(FigmaImportContext context, int index, int excluded, out int captionChild)
        {
            captionChild = -1;
            string text = null;
            foreach (var child in context.Children[index])
            {
                if (child == excluded || ControlMap.IsHidden(context, child))
                    continue;
                if (text != null || context.Nodes[child].type != "TEXT")
                {
                    captionChild = -1;
                    return null;
                }
                text = context.Nodes[child].text;
                captionChild = child;
            }
            return text;
        }

        // Children left for the element to render once its caption and icon have been taken. Read-only:
        // with nothing absorbed this is the context's own list rather than a copy.
        static IReadOnlyList<int> Remaining(FigmaImportContext context, int index, int iconChild,
            int captionChild, List<int> absorbed)
        {
            var children = context.Children[index];
            if (iconChild < 0 && captionChild < 0 && (absorbed == null || absorbed.Count == 0))
                return children;

            var rest = new List<int>(children.Count);
            foreach (var child in children)
                if (child != iconChild && child != captionChild
                    && (absorbed == null || !absorbed.Contains(child)))
                    rest.Add(child);
            return rest;
        }

        // Children whose driven layer a ComponentProperty row consumed: the row emits the value, so
        // rendering the layer again would draw it twice. Caption and icon attributes stay with their
        // own paths.
        static List<int> AbsorbedPropertyChildren(FigmaImportContext context, int index,
            ControlMap.ControlResolution resolution, int iconChild, int captionChild)
        {
            var bindings = resolution.bindings;
            if (bindings == null || bindings.Length == 0)
                return null;

            List<int> absorbed = null;
            foreach (var b in bindings)
            {
                if (b.source != FigmaBindingSource.ComponentProperty
                    || string.IsNullOrEmpty(b.uxmlAttribute) || string.IsNullOrEmpty(b.value))
                    continue;
                if (b.uxmlAttribute == resolution.captionAttr || b.uxmlAttribute == resolution.iconAttr)
                    continue;
                var fullName = PropFullName(context, index, b.value);
                if (fullName == null)
                    continue;
                var driven = FindCharProp(context, index, fullName);
                if (driven < 0)
                    continue;
                var walk = driven;
                while (walk >= 0)
                {
                    var parent = context.Nodes[walk].parentIndex;
                    if (parent == index)
                    {
                        if (walk != iconChild && walk != captionChild
                            && (absorbed == null || !absorbed.Contains(walk)))
                        {
                            if (absorbed == null)
                                absorbed = new List<int>();
                            absorbed.Add(walk);
                        }
                        break;
                    }
                    walk = parent;
                }
            }
            return absorbed;
        }

        static int TextDescendantCount(FigmaImportContext context, int index)
        {
            var count = context.Nodes[index].type == "TEXT" ? 1 : 0;
            foreach (var child in context.Children[index])
                count += TextDescendantCount(context, child);
            return count;
        }

        // Caption for a tagged instance; a normal instance's text rides in its template via AttributeOverrides.
        static string FirstTextOverride(FigmaNode node)
        {
            return node.textOverrides != null && node.textOverrides.Length > 0 ? node.textOverrides[0] : null;
        }

        // Emits class="[component][ unity-*]" and style="..." (each omitted when empty) and records the
        // element's emission entry for write-back.
        static string ClassAndStyle(FigmaImportContext context, int index, string ussClass,
            List<EmissionEntry> emission, string path, string tag)
        {
            var sharedClass = context.SharedClass(index);
            var inline = FigmaToUss.RenderInline(context.InlineDecls(index));

            var classTokens = string.IsNullOrEmpty(sharedClass)
                ? (ussClass ?? string.Empty)
                : (string.IsNullOrEmpty(ussClass) ? sharedClass : sharedClass + " " + ussClass);

            emission?.Add(new EmissionEntry
            {
                figmaNodeId = context.Nodes[index].id,
                path = path,
                tag = tag,
                classTokens = classTokens,
                sharedClass = sharedClass ?? string.Empty,
                inlineStyle = inline
            });

            var sb = new StringBuilder();
            if (classTokens.Length > 0)
                sb.Append(" class=\"").Append(classTokens).Append('"');
            if (inline.Length > 0)
                sb.Append(" style=\"").Append(Escape(inline)).Append('"');
            return sb.ToString();
        }

        // The mapping's extra attribute rows. A design-sourced value (caption, icon) wins and the
        // constant is the fallback; a ComponentProperty row with no matching property emits nothing.
        // name/class/style belong to the generator.
        static string BindingsAttr(ControlMap.ControlResolution resolution, FigmaImportContext context,
            int index, bool captionEmitted, bool iconEmitted)
        {
            var bindings = resolution.bindings;
            if (bindings == null || bindings.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var b in bindings)
            {
                if (string.IsNullOrEmpty(b.uxmlAttribute))
                    continue;
                // Design rows declare destinations; the caption/icon paths emit them.
                if (b.source == FigmaBindingSource.DesignText || b.source == FigmaBindingSource.DesignIcon)
                    continue;
                if (b.uxmlAttribute == "name" || b.uxmlAttribute == "class" || b.uxmlAttribute == "style")
                    continue;
                if (captionEmitted && b.uxmlAttribute == resolution.captionAttr)
                    continue;
                if (iconEmitted && b.uxmlAttribute == resolution.iconAttr)
                    continue;
                var v = b.source == FigmaBindingSource.ComponentProperty
                    ? PropValue(context, index, b.value) : b.value;
                if (string.IsNullOrEmpty(v))
                    continue;
                sb.Append(' ').Append(b.uxmlAttribute).Append("=\"").Append(EscapeText(v)).Append('"');
            }
            return sb.ToString();
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        // Trim, XML-escape, and encode interior line breaks as &#10; (a raw newline in an attribute
        // collapses to a space under XML attribute-value normalization).
        static string EscapeText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return Escape(value.Trim())
                .Replace("\r\n", "&#10;")
                .Replace("\r", "&#10;")
                .Replace("\n", "&#10;")
                .Replace("\t", " ");
        }
    }
}
