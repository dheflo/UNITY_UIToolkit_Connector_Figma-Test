using System.Collections.Generic;
using System.Globalization;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Turns the raw Figma facts on a node into the USS the importer works with: flex-direction from
    /// the auto-layout mode, white-space from the text resize mode, parent-relative placement from the
    /// bounding boxes, and text with Figma's textCase applied.
    /// A derivation never overwrites a value the payload already carries, since getCSSAsync reports
    /// some of these itself.
    /// </summary>
    static class FigmaNodeDerive
    {
        // What a control inherits from the caption Label it absorbed. white-space is here because the
        // collapsed leaf has no inner TEXT left to wrap on its own.
        static readonly HashSet<string> k_TextStyle = new HashSet<string>(new[]
        {
            "color", "font-size", "font-family", "font-weight", "letter-spacing", "text-align", "white-space"
        });

        public static void Apply(FigmaNode[] nodes)
        {
            if (nodes == null)
                return;

            for (var i = 0; i < nodes.Length; ++i)
            {
                var node = nodes[i];
                var isText = node.type == "TEXT";
                var fillBaked = node.raster && IsVectorShape(node.type) && !HasImageFill(node.css);
                node.text = ApplyTextCase(node.text, node.textCase);
                node.css = DeriveCss(node, nodes);

                // Caption bindings always come off a TEXT node, even when folded onto a control that
                // is not one, so they map with text semantics.
                node.varRefs = FoldCaption(MapVarRefs(node.varRefs, isText, fillBaked),
                    MapVarRefs(node.captionVarRefs, true));
                node.masterCss = DeriveMasterCss(node);
                node.masterVarRefs = FoldCaption(MapVarRefs(node.masterVarRefs, false, fillBaked),
                    MapVarRefs(node.masterCaptionVarRefs, true));

                node.masterName = MasterName(node.masterName, node.masterAxes);

                if (node.variants != null)
                    for (var v = 0; v < node.variants.Length; ++v)
                    {
                        var variant = node.variants[v];
                        var variantCss = new List<FigmaCssProp>(variant.css ?? new FigmaCssProp[0]);
                        AddCaption(variantCss, variant.captionCss, variant.textAutoResize);
                        variant.css = variantCss.ToArray();
                        variant.varRefs = FoldCaption(MapVarRefs(variant.varRefs, false, fillBaked),
                            MapVarRefs(variant.captionVarRefs, true));
                        node.variants[v] = variant;
                    }

                nodes[i] = node;
            }
        }

        /// <summary>
        /// The component class name: the set name plus its variant values. The State axis is left out
        /// because state variants become pseudo-class rules on the one class, not classes of their own.
        /// </summary>
        static string MasterName(string setName, FigmaCssProp[] axes)
        {
            if (axes == null || axes.Length == 0)
                return setName;

            var name = setName;
            for (var i = 0; i < axes.Length; ++i)
            {
                if (string.Equals(axes[i].k, "state", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(axes[i].v))
                    name += " " + axes[i].v;
            }
            return name;
        }

        /// <summary>
        /// Whether Figma reported an image fill on the node. Such a fill exports as the image itself rather
        /// than a flattened render, so the node's own color fills are not in those pixels.
        /// </summary>
        public static bool HasImageFill(FigmaCssProp[] css)
        {
            if (css == null)
                return false;

            for (var i = 0; i < css.Length; ++i)
                if (css[i].k == "background" && css[i].v != null && css[i].v.Contains("url("))
                    return true;
            return false;
        }

        /// <summary>
        /// Shapes with no USS equivalent, which the plugin exports to PNG instead of emitting as a color.
        /// </summary>
        public static bool IsVectorShape(string type)
        {
            switch (type)
            {
                case "VECTOR":
                case "BOOLEAN_OPERATION":
                case "STAR":
                case "LINE":
                case "POLYGON":
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Figma binding name to the USS property it drives. Anything unrecognised passes through, so a
        /// payload that already carries USS names still works. fillBaked marks a node whose fill is already
        /// in the pixels of its exported PNG.
        /// </summary>
        static FigmaCssProp[] MapVarRefs(FigmaCssProp[] refs, bool isText, bool fillBaked = false)
        {
            if (refs == null || refs.Length == 0)
                return refs;

            var mapped = new List<FigmaCssProp>(refs.Length);
            for (var i = 0; i < refs.Length; ++i)
            {
                var r = refs[i];
                switch (r.k)
                {
                    // A baked fill is already in the exported pixels; binding it again paints a solid box
                    // the size of the element behind a glyph that is mostly transparent.
                    case "fills":
                        if (fillBaked) continue;
                        r.k = isText ? "color" : "background-color";
                        break;
                    case "strokes": r.k = "border-color"; break;
                    case "cornerRadius": r.k = "border-radius"; break;
                    case "strokeWeight": r.k = "border-width"; break;
                    case "paddingLeft": r.k = "padding-left"; break;
                    case "paddingRight": r.k = "padding-right"; break;
                    case "paddingTop": r.k = "padding-top"; break;
                    case "paddingBottom": r.k = "padding-bottom"; break;
                    // Text metrics only mean something on text; elsewhere Figma has no USS counterpart.
                    case "fontSize": if (!isText) continue; r.k = "font-size"; break;
                    case "letterSpacing": if (!isText) continue; r.k = "letter-spacing"; break;
                }
                mapped.Add(r);
            }
            return mapped.ToArray();
        }

        /// <summary>
        /// USS has no text-transform, so Figma's textCase is baked into the characters.
        /// </summary>
        public static string ApplyTextCase(string text, string textCase)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(textCase))
                return text;

            switch (textCase)
            {
                case "UPPER": return text.ToUpperInvariant();
                case "LOWER": return text.ToLowerInvariant();
                case "TITLE": return ToTitleCase(text);
                default: return text; // ORIGINAL, SMALL_CAPS and anything new: leave the characters alone
            }
        }

        static string ToTitleCase(string text)
        {
            var chars = text.ToCharArray();
            var startOfWord = true;
            for (var i = 0; i < chars.Length; ++i)
            {
                if (char.IsWhiteSpace(chars[i]))
                {
                    startOfWord = true;
                    continue;
                }
                chars[i] = startOfWord ? char.ToUpperInvariant(chars[i]) : char.ToLowerInvariant(chars[i]);
                startOfWord = false;
            }
            return new string(chars);
        }

        static FigmaCssProp[] DeriveCss(FigmaNode node, FigmaNode[] nodes)
        {
            var css = new List<FigmaCssProp>(node.css ?? new FigmaCssProp[0]);

            // getCSSAsync omits flex-direction for horizontal auto layout (web flex defaults to row);
            // UI Toolkit defaults to column, so it has to be explicit.
            var direction = FlexDirection(node.layoutMode);
            if (direction != null && !Has(css, "flex-direction"))
                css.Add(new FigmaCssProp { k = "flex-direction", v = direction });

            // getCSSAsync omits white-space and UI Toolkit defaults to nowrap, so text would never wrap.
            // Only WIDTH_AND_HEIGHT hugs on one line; every other mode, including none reported, wraps.
            if (node.type == "TEXT" && !Has(css, "white-space"))
                css.Add(WhiteSpace(node.textAutoResize));

            AddPlacement(css, node, nodes);
            AddCaption(css, node.captionCss, node.textAutoResize);
            return css.ToArray();
        }

        // masterCss describes the main component's intrinsic style, so it needs the same treatment.
        // Placement is deliberately absent: it is contextual, not intrinsic.
        static FigmaCssProp[] DeriveMasterCss(FigmaNode node)
        {
            if (node.masterCss == null || node.masterCss.Length == 0)
                return node.masterCss;

            var css = new List<FigmaCssProp>(node.masterCss);

            var direction = FlexDirection(node.masterLayoutMode);
            if (direction != null && !Has(css, "flex-direction"))
                css.Add(new FigmaCssProp { k = "flex-direction", v = direction });

            AddCaption(css, node.masterCaptionCss, node.masterTextAutoResize);
            return css.ToArray();
        }

        // Folded last, matching the order the declarations are emitted in. The collapsed leaf has no
        // inner TEXT left to wrap on its own, so wrapping rides along with the rest of the label styling.
        static void AddCaption(List<FigmaCssProp> css, FigmaCssProp[] caption, string textAutoResize)
        {
            if (caption == null || caption.Length == 0)
                return;
            for (var i = 0; i < caption.Length; ++i)
                if (k_TextStyle.Contains(caption[i].k) && !Has(css, caption[i].k))
                    css.Add(caption[i]);
            if (!Has(css, "white-space"))
                css.Add(WhiteSpace(textAutoResize));
        }

        static FigmaCssProp WhiteSpace(string textAutoResize)
            => new FigmaCssProp { k = "white-space", v = textAutoResize == "WIDTH_AND_HEIGHT" ? "nowrap" : "normal" };

        static FigmaCssProp[] FoldCaption(FigmaCssProp[] refs, FigmaCssProp[] caption)
        {
            if (caption == null || caption.Length == 0)
                return refs;

            var merged = new List<FigmaCssProp>(refs ?? new FigmaCssProp[0]);
            for (var i = 0; i < caption.Length; ++i)
                if (k_TextStyle.Contains(caption[i].k) && !Has(merged, caption[i].k))
                    merged.Add(caption[i]);
            return merged.ToArray();
        }

        static string FlexDirection(string layoutMode)
        {
            if (layoutMode == "HORIZONTAL")
                return "row";
            return layoutMode == "VERTICAL" ? "column" : null;
        }

        // getCSSAsync omits position within the parent. A node is absolutely placed when the parent is
        // not an auto-layout frame, or when it is explicitly pulled out of one.
        static void AddPlacement(List<FigmaCssProp> css, FigmaNode node, FigmaNode[] nodes)
        {
            if (node.parentIndex < 0 || node.parentIndex >= nodes.Length)
                return;
            if (Has(css, "position") || Has(css, "left") || Has(css, "top"))
                return;

            var parent = nodes[node.parentIndex];
            var parentAutoLayout = parent.layoutMode == "HORIZONTAL" || parent.layoutMode == "VERTICAL";
            if (parentAutoLayout && node.layoutPositioning != "ABSOLUTE")
                return;

            float left;
            float top;
            if (node.hasBox && parent.hasBox)
            {
                // Figma GROUPs do not reset the coordinate origin, so node.x/y double-offsets a nested
                // group. The box difference is the only reliable parent-relative offset.
                left = node.boxX - parent.boxX;
                top = node.boxY - parent.boxY;
            }
            else if (node.hasXY)
            {
                left = node.x;
                top = node.y;
            }
            else
            {
                return; // no placement facts: an older payload already carried them, or there are none
            }

            css.Add(new FigmaCssProp { k = "position", v = "absolute" });
            css.Add(new FigmaCssProp { k = "left", v = Px(left) });
            css.Add(new FigmaCssProp { k = "top", v = Px(top) });
        }

        // Floor(v + 0.5) rather than Math.Round: Round is banker's by default, so 2.5 would land on 2
        // where every previous import produced 3.
        static string Px(float value)
            => ((int)System.Math.Floor(value + 0.5f)).ToString(CultureInfo.InvariantCulture) + "px";

        static bool Has(List<FigmaCssProp> css, string key)
        {
            for (var i = 0; i < css.Count; ++i)
                if (css[i].k == key)
                    return true;
            return false;
        }
    }
}
