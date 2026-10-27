using System;
using NUnit.Framework;
using static Unity.UIToolkit.Figma.Tests.FigmaTestBuilders;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaComponentClassTests
    {
        static readonly Func<string, string> k_NoFont = f => null;

        static void BuildModel(FigmaImportContext ctx)
        {
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            FigmaComponentStyleModel.Build(ctx, library, k_NoFont, null);
        }

        static string Inline(FigmaImportContext ctx, int index) => FigmaToUss.RenderInline(ctx.InlineDecls(index));

        [TestCase("red } * { display: none")]
        [TestCase("0px; position: absolute; left: -9999px")]
        [TestCase("red /* trailing")]
        public void ValueWithUssStructuralCharacters_IsDropped(string value)
        {
            var ctx = Context(Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Box", css: Css("background-color", value, "width", "10px")));
            BuildModel(ctx);

            var uss = FigmaToUss.Build(ctx, null) + Inline(ctx, 1);
            Assert.IsFalse(uss.Contains("display: none"));
            Assert.IsFalse(uss.Contains("-9999px"));
            Assert.IsFalse(uss.Contains("/*"));
            StringAssert.Contains("width: 10px", uss);
        }

        [Test]
        public void TwoInstances_OneRule_PositionInline_SizeInClass()
        {
            var master = Css("background-color", "#0d99ff", "border-radius", "6px", "width", "120px");
            var a = Node(0, "INSTANCE", "Button", css: Css("background-color", "#0d99ff", "border-radius", "6px",
                "width", "120px", "position", "absolute", "left", "10px", "top", "20px"),
                masterKey: "k1", masterName: "Button", masterCss: master);
            var b = Node(0, "INSTANCE", "Button", css: Css("background-color", "#0d99ff", "border-radius", "6px",
                "width", "140px", "position", "absolute", "left", "300px", "top", "20px"),
                masterKey: "k1", masterName: "Button", masterCss: master);
            var ctx = Context(Node(-1, "FRAME", "Screen"), a, b);
            BuildModel(ctx);

            Assert.AreEqual("button", ctx.SharedClass(1));
            Assert.AreEqual(ctx.SharedClass(1), ctx.SharedClass(2));
            Assert.AreEqual(1, ctx.ComponentRules.Count);
            StringAssert.Contains("background-color: #0d99ff;", ctx.ComponentRules[0].body);
            StringAssert.Contains("width: 120px;", ctx.ComponentRules[0].body);
            StringAssert.DoesNotContain("left", ctx.ComponentRules[0].body);
            StringAssert.Contains("left: 10px;", Inline(ctx, 1));
            StringAssert.DoesNotContain("width", Inline(ctx, 1));
            StringAssert.Contains("left: 300px;", Inline(ctx, 2));
            StringAssert.Contains("width: 140px;", Inline(ctx, 2));
        }

        [Test]
        public void InstanceOverride_OnlyDeltaInline()
        {
            var master = Css("background-color", "#222222", "border-radius", "6px");
            var stock = Node(0, "INSTANCE", "Button", css: Css("background-color", "#222222", "border-radius", "6px"),
                masterKey: "k1", masterName: "Button", masterCss: master);
            var tinted = Node(0, "INSTANCE", "Button", css: Css("background-color", "#111111", "border-radius", "6px"),
                masterKey: "k1", masterName: "Button", masterCss: master);
            var ctx = Context(Node(-1, "FRAME", "Screen"), stock, tinted);
            BuildModel(ctx);

            StringAssert.DoesNotContain("background-color", Inline(ctx, 1));
            StringAssert.Contains("background-color: #111111;", Inline(ctx, 2));
            StringAssert.DoesNotContain("border-radius", Inline(ctx, 2));
        }

        [Test]
        public void NonComponent_FullyInline_NoClass()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Button", css: Css("background-color", "#0d99ff", "width", "120px")));
            BuildModel(ctx);

            Assert.IsNull(ctx.SharedClass(1));
            Assert.AreEqual(0, ctx.ComponentRules.Count);
            StringAssert.Contains("background-color: #0d99ff;", Inline(ctx, 1));
            StringAssert.Contains("width: 120px;", Inline(ctx, 1));
        }

        [Test]
        public void SameName_DistinctKeys_Disambiguated()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Submit - SM", masterKey: "sm", masterName: "Button",
                    masterCss: Css("background-color", "#0d99ff")),
                Node(0, "INSTANCE", "Submit - MD", masterKey: "md", masterName: "Button",
                    masterCss: Css("background-color", "#0b87e0")));
            BuildModel(ctx);

            Assert.AreEqual("button", ctx.SharedClass(1));
            Assert.AreEqual("button-2", ctx.SharedClass(2));
            Assert.AreEqual(2, ctx.ComponentRules.Count);
        }

        [Test]
        public void Naming_PrefixAndIdentifierHardening()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Login Button", masterKey: "k1", masterName: "Login Button",
                    masterCss: Css("background-color", "#0d99ff")));
            ctx.ClassPrefix = "figma";
            BuildModel(ctx);
            Assert.AreEqual("figma-login-button", ctx.SharedClass(1));

            var ctx2 = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "3D Card", masterKey: "k2", masterName: "3D Card",
                    masterCss: Css("background-color", "#101010")));
            BuildModel(ctx2);
            Assert.AreEqual("_3d-card", ctx2.SharedClass(1));
        }

        [Test]
        public void MasterRoot_OwnCssIsBody_NoInlineResidue()
        {
            var ctx = Context(
                Node(-1, "COMPONENT", "Button", master: true, componentKey: "k1",
                    css: Css("background-color", "#0d99ff", "width", "120px", "height", "40px"),
                    masterKey: "k1", masterName: "Button"));
            BuildModel(ctx);

            Assert.AreEqual("button", ctx.SharedClass(0));
            StringAssert.Contains("background-color: #0d99ff;", ctx.ComponentRules[0].body);
            StringAssert.Contains("width: 120px;", ctx.ComponentRules[0].body);
            StringAssert.Contains("height: 40px;", ctx.ComponentRules[0].body);
            Assert.AreEqual(string.Empty, Inline(ctx, 0));
        }

        [Test]
        public void MissingMasterChannel_FallsBackToInline()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Button", css: Css("background-color", "#0d99ff")));
            BuildModel(ctx);

            Assert.IsNull(ctx.SharedClass(1));
            Assert.AreEqual(0, ctx.ComponentRules.Count);
            StringAssert.Contains("background-color: #0d99ff;", Inline(ctx, 1));
        }
    }
}
