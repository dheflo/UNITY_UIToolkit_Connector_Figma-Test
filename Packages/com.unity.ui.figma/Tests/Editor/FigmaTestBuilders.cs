using System.Collections.Generic;

namespace Unity.UIToolkit.Figma.Tests
{
    static class FigmaTestBuilders
    {
        public static FigmaCssProp[] Css(params string[] keyValues)
        {
            var css = new FigmaCssProp[keyValues.Length / 2];
            for (var i = 0; i < css.Length; ++i)
                css[i] = new FigmaCssProp { k = keyValues[i * 2], v = keyValues[i * 2 + 1] };
            return css;
        }

        public static FigmaNode Node(int parentIndex, string type, string name, string text = null,
            string componentKey = "", string control = "", bool master = false, FigmaCssProp[] css = null,
            bool clickable = false, string masterKey = "", string masterName = "", FigmaCssProp[] masterCss = null,
            FigmaCssProp[] masterVarRefs = null, string[] textOverrides = null, bool raster = false,
            FigmaCssProp[] varRefs = null, FigmaCssProp[] masterAxes = null,
            FigmaComponentProp[] props = null, string charProp = "")
        {
            return new FigmaNode
            {
                id = name + parentIndex,
                parentIndex = parentIndex,
                type = type,
                name = name,
                text = text,
                componentKey = componentKey ?? "",
                control = control ?? "",
                master = master,
                clickable = clickable,
                raster = raster,
                textOverrides = textOverrides ?? new string[0],
                css = css ?? new FigmaCssProp[0],
                varRefs = varRefs ?? new FigmaCssProp[0],
                masterKey = masterKey ?? "",
                masterName = masterName ?? "",
                masterCss = masterCss ?? new FigmaCssProp[0],
                masterVarRefs = masterVarRefs ?? new FigmaCssProp[0],
                masterAxes = masterAxes ?? new FigmaCssProp[0],
                props = props ?? new FigmaComponentProp[0],
                charProp = charProp ?? ""
            };
        }

        public static FigmaComponentProp[] Props(params string[] nameTypeValues)
        {
            var props = new FigmaComponentProp[nameTypeValues.Length / 3];
            for (var i = 0; i < props.Length; ++i)
                props[i] = new FigmaComponentProp
                {
                    k = nameTypeValues[i * 3],
                    t = nameTypeValues[i * 3 + 1],
                    v = nameTypeValues[i * 3 + 2]
                };
            return props;
        }

        public static FigmaComponentProp[] PropWithDefault(string name, string type, string value, string defaultValue)
        {
            return new[] { new FigmaComponentProp { k = name, t = type, v = value, d = defaultValue } };
        }

        // Notes rendered the way the window shows them, so tests can assert on wording.
        public static List<string> Text(List<FigmaImportNote> notes)
        {
            var texts = new List<string>(notes.Count);
            foreach (var note in notes)
                texts.Add(FigmaStrings.Format(note));
            return texts;
        }

        public static FigmaImportContext Context(params FigmaNode[] nodes)
        {
            var count = nodes.Length;
            var children = new List<int>[count];
            for (var i = 0; i < count; ++i)
                children[i] = new List<int>();

            var roots = new List<int>();
            var order = new int[count];
            for (var i = 0; i < count; ++i)
            {
                order[i] = i;
                var parent = nodes[i].parentIndex;
                if (parent >= 0 && parent < count)
                    children[parent].Add(i);
                else
                    roots.Add(i);
            }

            return new FigmaImportContext { Nodes = nodes, Children = children, Roots = roots, Order = order };
        }
    }
}
