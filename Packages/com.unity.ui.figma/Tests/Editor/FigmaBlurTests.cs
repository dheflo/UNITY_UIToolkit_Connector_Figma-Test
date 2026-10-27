using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaBlurTests
    {
        [Test]
        public void Filter_Blur_RebuildsCanonicalValue()
        {
            Assert.AreEqual("blur(40px)", FigmaToUss.FilterValue("blur(40px)"));
            Assert.AreEqual("blur(12px)", FigmaToUss.FilterValue("blur( 12 )"));
            Assert.AreEqual("blur(2.5px)", FigmaToUss.FilterValue("blur(2.5px)"));
        }

        [Test]
        public void Filter_UnsupportedFunctions_Skipped()
        {
            Assert.IsNull(FigmaToUss.FilterValue("grayscale(1)"));
            Assert.IsNull(FigmaToUss.FilterValue("blur(url(evil))"));
            Assert.IsNull(FigmaToUss.FilterValue("blur(4px"));
            Assert.AreEqual("blur(4px)", FigmaToUss.FilterValue("grayscale(1) blur(4px)"));
        }

        [Test]
        public void BackdropFilter_AcceptsSingleBlurOnly()
        {
            Assert.AreEqual("blur(50px)", FigmaToUss.BlurFilterValue("blur(50px)"));
            Assert.IsNull(FigmaToUss.BlurFilterValue("blur(4px) blur(2px)"));
            Assert.IsNull(FigmaToUss.BlurFilterValue("grayscale(1)"));
        }

        [Test]
        public void LayerBlur_EmitsFilterFromNodeCss()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "FRAME", "Panel",
                    css: FigmaTestBuilders.Css("filter", "blur(4px)")));
            var decls = FigmaToUss.BuildDeclarationBodyDecls(context, 0, null, null);
            Assert.AreEqual(1, decls.Count);
            Assert.AreEqual("filter", decls[0].prop);
            Assert.AreEqual("blur(4px)", decls[0].value);
        }

        // A raster's PNG is rendered with its effects baked in; the CSS filter must not apply twice.
        [Test]
        public void RasterNode_FilterNotEmitted()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "VECTOR", "Icon", raster: true,
                    css: FigmaTestBuilders.Css("filter", "blur(4px)")));
            var decls = FigmaToUss.BuildDeclarationBodyDecls(context, 0, null, null);
            foreach (var decl in decls)
                Assert.AreNotEqual("filter", decl.prop);
        }

#if UITOOLKIT_HAS_USS_FILTER
        [Test]
        public void BackgroundBlur_EmitsBackdropFilter()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "FRAME", "Glass",
                    css: FigmaTestBuilders.Css("backdrop-filter", "blur(50px)")));
            var decls = FigmaToUss.BuildDeclarationBodyDecls(context, 0, null, null);
            Assert.AreEqual(1, decls.Count);
            Assert.AreEqual("backdrop-filter", decls[0].prop);
            Assert.AreEqual("blur(50px)", decls[0].value);
        }
#else
        [Test]
        public void Report_NamesDroppedBackgroundBlur()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "FRAME", "Glass",
                    css: FigmaTestBuilders.Css("backdrop-filter", "blur(50px)")));
            var notes = FigmaTestBuilders.Text(FigmaImportReport.Collect(context, null));
            var sawDropped = false;
            foreach (var note in notes)
                if (note.Contains("background blur"))
                    sawDropped = true;
            Assert.IsTrue(sawDropped, "dropped background-blur note missing");
        }
#endif
    }
}
