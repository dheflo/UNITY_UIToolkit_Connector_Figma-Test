using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaShadowTests
    {
        [Test]
        public void TextShadow_RebuildsFirstShadow()
        {
            Assert.AreEqual("0px 4px 4px rgba(0, 0, 0, 0.25)",
                FigmaToUss.TextShadowValue("0px 4px 4px rgba(0, 0, 0, 0.25)"));
        }

        [Test]
        public void TextShadow_TwoLengths_DefaultsBlur()
        {
            Assert.AreEqual("1px 2px 0px #000", FigmaToUss.TextShadowValue("1px 2px #000"));
        }

        [Test]
        public void TextShadow_TakesFirstOfList()
        {
            Assert.AreEqual("1px 1px 2px #000",
                FigmaToUss.TextShadowValue("1px 1px 2px #000, 0px 0px 9px #fff"));
        }

        [Test]
        public void TextShadow_Garbage_ReturnsNull()
        {
            Assert.IsNull(FigmaToUss.TextShadowValue("oops"));
            Assert.IsNull(FigmaToUss.TextShadowValue("1px 2px 3px url(evil.png)"));
            Assert.IsNull(FigmaToUss.TextShadowValue(""));
        }

        [Test]
        public void TextShadow_EmitsFromNodeCss()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "TEXT", "Label",
                    css: FigmaTestBuilders.Css("text-shadow", "0px 2px 4px rgba(0, 0, 0, 0.5)")));
            var decls = FigmaToUss.BuildDeclarationBodyDecls(context, 0, null, null);
            Assert.AreEqual(1, decls.Count);
            Assert.AreEqual("text-shadow", decls[0].prop);
            Assert.AreEqual("0px 2px 4px rgba(0, 0, 0, 0.5)", decls[0].value);
        }

        [Test]
        public void BoxShadow_IsNotImported()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "FRAME", "Panel",
                    css: FigmaTestBuilders.Css("box-shadow", "0px 0px 50px 0px #1500FF")));
            var decls = FigmaToUss.BuildDeclarationBodyDecls(context, 0, null, null);
            Assert.AreEqual(0, decls.Count);
        }

        [Test]
        public void Report_NamesDroppedShadows()
        {
            var context = FigmaTestBuilders.Context(
                FigmaTestBuilders.Node(-1, "FRAME", "Panel",
                    css: FigmaTestBuilders.Css("box-shadow", "0px 0px 50px 0px #1500FF")));
            var notes = FigmaTestBuilders.Text(FigmaImportReport.Collect(context, null));
            var sawDropped = false;
            foreach (var note in notes)
                if (note.Contains("drop shadow"))
                    sawDropped = true;
            Assert.IsTrue(sawDropped, "dropped-shadow note missing");
        }

#if UITOOLKIT_HAS_USS_FILTER
        // A vector-ish node's shadow arrives as CSS filter: drop-shadow(), which maps same-to-same.
        [Test]
        public void FilterDropShadow_RebuiltWithSigmaBlur()
        {
            Assert.AreEqual("drop-shadow(0px 4px 2px rgba(0, 0, 0, 0.25))",
                FigmaToUss.FilterValue("drop-shadow(0px 4px 4px rgba(0, 0, 0, 0.25))"));
        }

        [Test]
        public void FilterDropShadow_RefusesSpreadAndGarbage()
        {
            Assert.IsNull(FigmaToUss.FilterValue("drop-shadow(0px 0px 4px 2px #fff)"));
            Assert.IsNull(FigmaToUss.FilterValue("drop-shadow(0px 0px 4px url(evil))"));
        }

        [Test]
        public void Filter_ChainsSupportedFunctions()
        {
            Assert.AreEqual("blur(4px) drop-shadow(1px 2px 1.5px #000)",
                FigmaToUss.FilterValue("blur(4px) drop-shadow(1px 2px 3px #000)"));
        }
#endif
    }
}
