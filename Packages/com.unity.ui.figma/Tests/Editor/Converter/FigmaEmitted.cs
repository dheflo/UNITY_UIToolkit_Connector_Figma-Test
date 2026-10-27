using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    /// <summary>
    /// The documents one case emits, with enough of a query surface to assert what they mean rather than
    /// what they read like. Parsed rather than string-matched, so whitespace and attribute order are free
    /// to change and only the claims a test makes can fail it.
    /// </summary>
    class FigmaEmitted
    {
        public readonly string caseName;
        readonly Dictionary<string, string> m_Docs;

        public FigmaEmitted(string caseName, Dictionary<string, string> docs)
        {
            this.caseName = caseName;
            m_Docs = docs;
        }

        public IEnumerable<string> FileNames => m_Docs.Keys;

        public string Text(string file)
        {
            Assert.IsTrue(m_Docs.ContainsKey(file), caseName + " emitted no " + file
                + ". It emitted: " + string.Join(", ", m_Docs.Keys.OrderBy(k => k)));
            return m_Docs[file];
        }

        public XDocument Xml(string file) => XDocument.Parse(Text(file));

        /// <summary>Elements by local name, ignoring the ui:/uie: prefix.</summary>
        public List<XElement> Elements(string file, string localName)
        {
            return Xml(file).Descendants().Where(e => e.Name.LocalName == localName).ToList();
        }

        /// <summary>Template names the document declares, in document order.</summary>
        public List<string> TemplateDeclarations(string file)
        {
            return Elements(file, "Template").Select(e => (string)e.Attribute("name")).ToList();
        }

        /// <summary>One &lt;ui:Instance&gt;: which template, its inline delta, and its text overrides.</summary>
        public struct Instance
        {
            public string template;
            public string style;
            public List<string> overrides;
        }

        public List<Instance> Instances(string file)
        {
            return Elements(file, "Instance").Select(e => new Instance
            {
                template = (string)e.Attribute("template"),
                style = (string)e.Attribute("style") ?? string.Empty,
                overrides = e.Elements().Where(c => c.Name.LocalName == "AttributeOverrides")
                    .Select(c => c.Attributes().First(a => a.Name.LocalName != "element-name").Value)
                    .ToList()
            }).ToList();
        }

        /// <summary>The name= of every element a template exposes for AttributeOverrides, in order.</summary>
        public List<string> TextSlots(string file)
        {
            return Xml(file).Descendants()
                .Select(e => (string)e.Attribute("name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }

        /// <summary>Element names in document order, without the ui:/uie: prefix, root excluded.</summary>
        public List<string> Tags(string file)
        {
            return Xml(file).Root.Descendants().Select(e => e.Name.LocalName).ToList();
        }

        /// <summary>The style= of every element that carries one, in document order.</summary>
        public List<string> Styles(string file)
        {
            return Xml(file).Descendants()
                .Select(e => (string)e.Attribute("style"))
                .Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        /// <summary>Every text=/label=/placeholder-text= value, in document order.</summary>
        public List<string> Captions(string file)
        {
            return Xml(file).Descendants()
                .Select(e => (string)(e.Attribute("text") ?? e.Attribute("label") ?? e.Attribute("placeholder-text")))
                .Where(t => t != null).ToList();
        }

        /// <summary>
        /// Selectors the USS defines, in source order. Whole selector, so a descendant one like
        /// ".unity-text-field .unity-base-text-field__input" comes back intact rather than truncated.
        /// </summary>
        public List<string> Selectors(string file)
        {
            return Regex.Matches(Text(file), @"^([.:][^{]*?)\s*\{", RegexOptions.Multiline)
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        }

        /// <summary>
        /// Class tokens across the document, one entry per element that carries each, so a count says how
        /// many elements wear a class.
        /// </summary>
        public List<string> Classes(string file)
        {
            return Xml(file).Descendants()
                .Select(e => (string)e.Attribute("class"))
                .Where(c => !string.IsNullOrEmpty(c))
                .SelectMany(c => c.Split(' '))
                .Where(c => c.Length > 0)
                .ToList();
        }

        /// <summary>Selectors the USS defines, without the leading dot or any pseudo-class.</summary>
        public List<string> UssClasses(string file)
        {
            return Regex.Matches(Text(file), @"^\.([A-Za-z0-9_-]+)", RegexOptions.Multiline)
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        }

        /// <summary>Declarations of one rule, in source order, as "prop: value".</summary>
        public List<string> Rule(string file, string selector)
        {
            var m = Regex.Match(Text(file), Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            Assert.IsTrue(m.Success, caseName + "/" + file + " has no rule for " + selector
                + ". It defines: " + string.Join(", ", UssClasses(file)));
            return m.Groups[1].Value.Split(';')
                .Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
        }

        /// <summary>The whole emission, for a failure message or the dump menu item.</summary>
        public string Dump()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var file in m_Docs.Keys.OrderBy(k => k))
                sb.Append("--- ").Append(file).Append('\n').Append(m_Docs[file]).Append('\n');
            return sb.ToString();
        }
    }
}
