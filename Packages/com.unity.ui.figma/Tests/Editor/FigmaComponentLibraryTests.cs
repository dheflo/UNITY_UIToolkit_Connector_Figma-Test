using NUnit.Framework;
using static Unity.UIToolkit.Figma.Tests.FigmaTestBuilders;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaComponentLibraryTests
    {
        [Test]
        public void ButtonMaster_ClassifiedAsControl()
        {
            var ctx = Context(
                Node(-1, "COMPONENT", "Button", componentKey: "k1", master: true, css: Css("background-color", "#0d99ff")),
                Node(0, "TEXT", "l", text: "PRESS", master: true));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");

            Assert.AreEqual(1, library.ControlCount);
            Assert.AreEqual(0, library.TemplateCount);
            Assert.IsTrue(library.TryGetControl("k1", out var control));
            Assert.AreEqual("Button", control.tag);
        }

        static FigmaImportContext Composite(FigmaCssProp[] instanceCss, string headingText)
        {
            return Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Card", componentKey: "k2", masterKey: "k2", masterName: "Card",
                    masterCss: k_Master, css: instanceCss),
                Node(1, "TEXT", "Heading", text: headingText, css: k_Caption),
                Node(1, "TEXT", "Detail", text: "Detail A", css: k_Caption),
                Node(-1, "COMPONENT", "Card", componentKey: "k2", master: true, css: k_Master),
                Node(4, "TEXT", "Heading", text: "Heading", css: k_Caption),
                Node(4, "TEXT", "Detail", text: "Detail", css: k_Caption));
        }

        [Test]
        public void CompositeWhoseInstancesMatch_IsTemplated()
        {
            var library = FigmaComponentLibrary.Build(Composite(k_Master, "Card A"), null, "Screen");

            Assert.AreEqual(1, library.TemplateCount);
            Assert.IsTrue(library.TryGetTemplate("k2", out var template));
            Assert.AreEqual(2, template.textCount, "both captions are override slots");
        }

        [Test]
        public void CompositeWhoseInstancesDiffer_IsNotTemplated()
        {
            var wider = Css("display", "flex", "background-color", "#0d99ff", "width", "420px");
            Assert.AreEqual(0, FigmaComponentLibrary.Build(Composite(wider, "Card A"), null, "Screen").TemplateCount);
        }

        [Test]
        public void MasterContainingANestedInstance_IsNotTemplated()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Panel", componentKey: "k3", masterKey: "k3", masterName: "Panel",
                    masterCss: k_Master, css: k_Master),
                Node(1, "TEXT", "Heading", text: "Hi", css: k_Caption),
                Node(1, "INSTANCE", "Badge", componentKey: "k4"),
                Node(-1, "COMPONENT", "Panel", componentKey: "k3", master: true, css: k_Master),
                Node(4, "TEXT", "Heading", text: "Heading", css: k_Caption),
                Node(4, "INSTANCE", "Badge", componentKey: "k4"));

            Assert.IsFalse(FigmaComponentLibrary.Build(ctx, null, "Screen").TryGetTemplate("k3", out _));
        }

        [Test]
        public void MasterContainingATaggedLeafControl_IsNotTemplated()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Panel", componentKey: "k3", masterKey: "k3", masterName: "Panel",
                    masterCss: k_Master, css: k_Master),
                Node(1, "FRAME", "Mute", control: "Toggle"),
                Node(2, "TEXT", "Label", text: "Mute", css: k_Caption),
                Node(-1, "COMPONENT", "Panel", componentKey: "k3", master: true, css: k_Master),
                Node(4, "FRAME", "Mute", control: "Toggle"),
                Node(5, "TEXT", "Label", text: "Mute", css: k_Caption));

            Assert.IsFalse(FigmaComponentLibrary.Build(ctx, null, "Screen").TryGetTemplate("k3", out _));
        }

        static readonly FigmaCssProp[] k_Master = Css("display", "flex", "background-color", "#0d99ff", "width", "160px");
        static readonly FigmaCssProp[] k_Caption = Css("color", "#ffffff", "font-size", "14px");

        static FigmaImportContext OneInstance(FigmaCssProp[] instanceCss, FigmaCssProp[] captionCss, string text)
        {
            return Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Volume", componentKey: "k1", masterKey: "k1", masterName: "Volume",
                    masterCss: k_Master, css: instanceCss),
                Node(1, "TEXT", "Label", text: text, css: captionCss),
                Node(-1, "COMPONENT", "Volume", componentKey: "k1", master: true, css: k_Master),
                Node(3, "TEXT", "Label", text: "Volume", css: k_Caption));
        }

        static int TemplateCountOf(FigmaImportContext ctx) =>
            FigmaComponentLibrary.Build(ctx, null, "Screen").TemplateCount;

        [Test]
        public void InstanceMatchingTheMaster_IsTemplated()
        {
            Assert.AreEqual(1, TemplateCountOf(OneInstance(k_Master, k_Caption, "Volume")));
        }

        [Test]
        public void InstanceDifferingOnlyInText_IsTemplated()
        {
            Assert.AreEqual(1, TemplateCountOf(OneInstance(k_Master, k_Caption, "Loudness")));
        }

        [Test]
        public void InstanceDifferingOnlyInPlacement_IsTemplated()
        {
            var placed = Css("display", "flex", "background-color", "#0d99ff", "width", "160px",
                "position", "absolute", "left", "40px", "top", "10px");
            Assert.AreEqual(1, TemplateCountOf(OneInstance(placed, k_Caption, "Volume")));
        }

        [Test]
        public void InstanceDifferingInStyle_IsNotTemplated()
        {
            var wider = Css("display", "flex", "background-color", "#0d99ff", "width", "180px");
            Assert.AreEqual(0, TemplateCountOf(OneInstance(wider, k_Caption, "Volume")));
        }

        [Test]
        public void InstanceWhoseCaptionIsRestyled_IsNotTemplated()
        {
            var restyled = Css("color", "#ff0000", "font-size", "14px");
            Assert.AreEqual(0, TemplateCountOf(OneInstance(k_Master, restyled, "Volume")));
        }

        [Test]
        public void MasterWhoseInstancesAreAllTagged_IsNotTemplated()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Volume", componentKey: "k1", control: "SliderInt"),
                Node(1, "TEXT", "Label", text: "Volume"),
                Node(-1, "COMPONENT", "Volume", componentKey: "k1", master: true),
                Node(3, "TEXT", "Label", text: "Volume"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");

            Assert.AreEqual(0, library.TemplateCount);
            Assert.IsFalse(library.TryGetTemplate("k1", out _));
        }

        [Test]
        public void MasterWithOneUntaggedInstance_IsStillTemplated()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Volume", componentKey: "k1", control: "SliderInt"),
                Node(1, "TEXT", "Label", text: "Volume"),
                Node(0, "INSTANCE", "Volume", componentKey: "k1"),
                Node(3, "TEXT", "Label", text: "Volume"),
                Node(-1, "COMPONENT", "Volume", componentKey: "k1", master: true),
                Node(5, "TEXT", "Label", text: "Volume"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");

            Assert.AreEqual(1, library.TemplateCount);
        }

        [Test]
        public void MasterWithNoInstances_IsStillTemplated()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(-1, "COMPONENT", "Volume", componentKey: "k1", master: true),
                Node(1, "TEXT", "Label", text: "Volume"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");

            Assert.AreEqual(1, library.TemplateCount);
        }

        [Test]
        public void SingleTextMaster_ClassifiedAsTemplate_WithTextCount()
        {
            var ctx = Context(
                Node(-1, "COMPONENT", "Card", componentKey: "k3", master: true),
                Node(0, "TEXT", "title", text: "Title", master: true));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");

            Assert.AreEqual(1, library.TemplateCount);
            Assert.IsTrue(library.TryGetTemplate("k3", out var template));
            Assert.AreEqual(1, template.textCount, "textCount must count TEXT descendants for overrides");
        }

        [Test]
        public void NonMasterComponent_Ignored()
        {
            var ctx = Context(Node(-1, "COMPONENT", "Button", componentKey: "k3", master: false));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");

            Assert.AreEqual(0, library.ControlCount);
            Assert.AreEqual(0, library.TemplateCount);
        }
    }
}
