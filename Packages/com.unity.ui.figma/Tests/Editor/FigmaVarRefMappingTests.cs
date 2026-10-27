using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaVarRefMappingTests
    {
        static FigmaNode Derived(string type, bool raster, FigmaCssProp[] css = null)
        {
            var nodes = new[]
            {
                FigmaTestBuilders.Node(-1, type, "Node", raster: raster, css: css,
                    varRefs: FigmaTestBuilders.Css("fills", "Foreground/Secondary"))
            };
            FigmaNodeDerive.Apply(nodes);
            return nodes[0];
        }

        [Test]
        public void VectorFillBinding_WithAnExportedRaster_IsDropped()
        {
            Assert.IsEmpty(Derived("VECTOR", true).varRefs);
        }

        [TestCase("BOOLEAN_OPERATION")]
        [TestCase("STAR")]
        [TestCase("LINE")]
        [TestCase("POLYGON")]
        public void EveryVectorShape_WithAnExportedRaster_DropsItsFillBinding(string type)
        {
            Assert.IsEmpty(Derived(type, true).varRefs);
        }

        [Test]
        public void VectorFillBinding_WithoutARaster_StaysABackgroundColor()
        {
            var refs = Derived("VECTOR", false).varRefs;
            Assert.AreEqual(1, refs.Length);
            Assert.AreEqual("background-color", refs[0].k);
        }

        [Test]
        public void VectorFillBinding_WithAnImageFill_StaysABackgroundColor()
        {
            var refs = Derived("VECTOR", true,
                FigmaTestBuilders.Css("background", "url(<path-to-image>) lightgray 50% / cover no-repeat")).varRefs;
            Assert.AreEqual(1, refs.Length);
            Assert.AreEqual("background-color", refs[0].k);
        }

        [Test]
        public void ImageFillBinding_OnARasterisedRectangle_StaysABackgroundColor()
        {
            var refs = Derived("RECTANGLE", true).varRefs;
            Assert.AreEqual(1, refs.Length);
            Assert.AreEqual("background-color", refs[0].k);
        }

        [Test]
        public void FrameFillBinding_StaysABackgroundColor()
        {
            var refs = Derived("FRAME", false).varRefs;
            Assert.AreEqual(1, refs.Length);
            Assert.AreEqual("background-color", refs[0].k);
        }

        [Test]
        public void TextFillBinding_StaysAColor()
        {
            var refs = Derived("TEXT", false).varRefs;
            Assert.AreEqual(1, refs.Length);
            Assert.AreEqual("color", refs[0].k);
        }
    }
}
