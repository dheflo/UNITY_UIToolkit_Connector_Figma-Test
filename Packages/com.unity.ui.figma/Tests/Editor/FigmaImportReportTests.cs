using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static Unity.UIToolkit.Figma.Tests.FigmaTestBuilders;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaImportReportTests
    {
        [Test]
        public void NameThatLooksLikeAControl_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Tab Bar", componentKey: "k1"),
                Node(0, "INSTANCE", "Play Button", componentKey: "k2"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));

            Assert.IsTrue(notes.Exists(n => n.Contains("look like controls")
                && n.Contains("'Tab Bar'") && n.Contains("'Play Button'")));
        }

        [Test]
        public void HiddenDroppedLayer_IsNotReported()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Save", control: "Button"),
                Node(1, "TEXT", "Label", text: "Save"),
                Node(1, "RECTANGLE", "Glyph", css: Css("display", "none")));
            var notes = Text(FigmaImportReport.Collect(ctx, null, null, null, Emit(ctx, null)));
            Assert.IsFalse(notes.Exists(n => n.Contains("were dropped")));
        }

        [Test]
        public void TextPropertyDrivingNoLayer_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Text", componentKey: "kt", masterName: "Text Sub Header",
                    props: Props("Label#1", "TEXT", "Label")),
                Node(1, "TEXT", "sub header", text: "SUB HEADER"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("drives no layer")
                && n.Contains("'Label' in Text Sub Header")));
        }

        [Test]
        public void TextPropertyDrivingALayer_IsNotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Text", componentKey: "kt", props: Props("Label#1", "TEXT", "Go")),
                Node(1, "TEXT", "Label", text: "Go", charProp: "Label#1"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsFalse(notes.Exists(n => n.Contains("drives no layer")));
        }

        [Test]
        public void UnlinkedTextPropertyLeftAtDefault_IsNotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Card", componentKey: "kc", masterName: "Card",
                    props: PropWithDefault("Body#1", "TEXT", "Body", "Body")),
                Node(1, "TEXT", "title", text: "Title"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsFalse(notes.Exists(n => n.Contains("drives no layer")));
        }

        [Test]
        public void UnlinkedTextPropertySetOffDefault_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Card", componentKey: "kc", masterName: "Card",
                    props: PropWithDefault("Body#1", "TEXT", "Hello", "Body")),
                Node(1, "TEXT", "title", text: "Title"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("drives no layer") && n.Contains("'Body' in Card")));
        }

        [Test]
        public void GradientFillOnFrame_IsReported()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Panel", css: Css("background", "linear-gradient(180deg, #FFF 0%, #000 100%)")));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("gradient fill")));
        }

        [Test]
        public void GradientFillOnRasterizedLayer_IsNotReported()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "VECTOR", "Sheen", raster: true,
                    css: Css("background", "linear-gradient(180deg, #FFF 0%, #000 100%)")));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsFalse(notes.Exists(n => n.Contains("gradient fill")), "the PNG carries the gradient");
        }

        [Test]
        public void TextStroke_IsReported()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "TEXT", "Title", text: "Hi",
                    css: Css("-webkit-text-stroke-width", "2px", "-webkit-text-stroke-color", "#000")));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("use a stroke")));
        }

        [Test]
        public void MappingDecay_FlagsRecordsThatMatchedBefore()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "My Button", uitkControl = "Button" });
            try
            {
                var ctx = Context(Node(-1, "FRAME", "Screen"));
                var note = FigmaImportReport.MappingDecayNote(ctx, settings,
                    new List<string> { "mybutton" }, out var now);
                Assert.IsNotNull(note);
                StringAssert.Contains("My Button", FigmaStrings.Format(note.Value));
                Assert.IsEmpty(now);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void MappingDecay_FirstImportAndDeletedRecords_AreQuiet()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "My Button", uitkControl = "Button" });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen"),
                    Node(0, "INSTANCE", "My Button", componentKey: "k1"));
                Assert.IsNull(FigmaImportReport.MappingDecayNote(ctx, settings, null, out var now));
                Assert.Contains("mybutton", now);

                settings.controlMap.Clear();
                var empty = Context(Node(-1, "FRAME", "Screen"));
                Assert.IsNull(FigmaImportReport.MappingDecayNote(empty, settings,
                    new List<string> { "mybutton" }, out _));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void MultiTextInstanceWithStateAxis_SkippedStatesAreFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Badge Button", componentKey: "k1", masterName: "Badge Button",
                    textOverrides: new[] { "Play", "3" },
                    masterAxes: Css("Type", "Primary", "State", "Default")));
            var notes = Text(FigmaImportReport.Collect(ctx, null));

            Assert.IsTrue(notes.Exists(n => n.Contains("State axis") && n.Contains("Badge Button")));
        }

        [Test]
        public void SingleTextInstanceWithStateAxis_IsNotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Cta", componentKey: "k1", masterName: "Cta",
                    textOverrides: new[] { "Play" },
                    masterAxes: Css("State", "Default")));
            var notes = Text(FigmaImportReport.Collect(ctx, null));

            Assert.IsFalse(notes.Exists(n => n.Contains("State axis")),
                "one text means the plugin collected variants; an empty result is not a skip");
        }

        [Test]
        public void NameThatIsAControl_IsNotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Tab View", componentKey: "k1"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));

            Assert.IsFalse(notes.Exists(n => n.Contains("look like controls")),
                "it matched exactly, so there is nothing to nudge about");
        }

        [Test]
        public void NameWithNoControlWord_IsNotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Top tray", componentKey: "k1"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));

            Assert.IsFalse(notes.Exists(n => n.Contains("look like controls")));
        }

        static List<int> Emit(FigmaImportContext ctx, FigmaImportSettings settings)
        {
            var discarded = new List<int>();
            var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
            FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library, null, discarded);
            return discarded;
        }

        [Test]
        public void LeafControlWithChildLayers_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "MyToggle", control: "Toggle", css: Css("background-color", "#0d99ff")),
                Node(1, "RECTANGLE", "bg"),
                Node(1, "TEXT", "lbl", text: "On"));
            var notes = Text(FigmaImportReport.Collect(ctx, null, null, null, Emit(ctx, null)));
            Assert.IsTrue(notes.Exists(n => n.Contains("were dropped") && n.Contains("MyToggle")));
        }

        [Test]
        public void CaptionFromAnOverride_IsNotCountedAsADroppedLayer()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "Save", control: "Button", textOverrides: new[] { "Save" }),
                Node(1, "TEXT", "lbl", text: "Save"),
                Node(1, "TEXT", "hint", text: "Ctrl+S"));
            var notes = Text(FigmaImportReport.Collect(ctx, null, null, null, Emit(ctx, null)));

            var note = notes.Find(n => n.Contains("were dropped"));
            Assert.IsNotNull(note);
            StringAssert.Contains("1 layer)", note);
        }

        [Test]
        public void ChipContainerAbsorbingItsText_ReportsTheLayersItDropped()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Home", control: "Tab"),
                Node(1, "RECTANGLE", "icon"),
                Node(1, "TEXT", "lbl", text: "Home"));
            var notes = Text(FigmaImportReport.Collect(ctx, null, null, null, Emit(ctx, null)));

            var note = notes.Find(n => n.Contains("were dropped"));
            Assert.IsNotNull(note);
            StringAssert.Contains("Home (1 layer)", note);
        }

        [Test]
        public void ForeignControlWithChildLayers_NotFlagged()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "Panel", uitkControl = "MyCompany.UI.Card" });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen"),
                    Node(0, "FRAME", "Panel", css: Css("background-color", "#0d99ff")),
                    Node(1, "TEXT", "lbl", text: "On"));
                var notes = Text(FigmaImportReport.Collect(ctx, settings, null, null, Emit(ctx, settings)));
                Assert.IsFalse(notes.Exists(n => n.Contains("were dropped")));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void CaptionedLeafWithSingleText_NotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "CleanBtn", control: "Button", css: Css("background-color", "#0d99ff")),
                Node(1, "TEXT", "cap", text: "Save"));
            var notes = Text(FigmaImportReport.Collect(ctx, null, null, null, Emit(ctx, null)));
            Assert.IsFalse(notes.Exists(n => n.Contains("were dropped")));
        }

        [Test]
        public void MissingFont_IsReported()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "TEXT", "label", text: "Hi", css: Css("font-family", "\"Inter\", sans-serif")));
            var notes = Text(FigmaImportReport.Collect(ctx, null, family => null));
            Assert.IsTrue(notes.Exists(n => n.Contains("falls back to the default") && n.Contains("Inter")));
        }

        [Test]
        public void ResolvedFont_NotReported()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "TEXT", "label", text: "Hi", css: Css("font-family", "Inter")));
            var notes = Text(FigmaImportReport.Collect(ctx, null, family => "url(inter)"));
            Assert.IsFalse(notes.Exists(n => n.Contains("falls back to the default")));
        }

        [Test]
        public void NoResolver_SkipsFontFallbackNote()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "TEXT", "label", text: "Hi", css: Css("font-family", "Inter")));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsFalse(notes.Exists(n => n.Contains("falls back to the default")));
        }

        [Test]
        public void TabOutsideTabView_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Overview", control: "Tab"),
                Node(1, "TEXT", "t", text: "hi"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("not inside a TabView") && n.Contains("Overview")));
        }

        [Test]
        public void TabInsideTabView_NotFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Tabs", control: "TabView"),
                Node(1, "FRAME", "Overview", control: "Tab"),
                Node(2, "TEXT", "t", text: "hi"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsFalse(notes.Exists(n => n.Contains("not inside a TabView")));
        }

        [Test]
        public void EmptyTabInTabView_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Tabs", control: "TabView"),
                Node(1, "FRAME", "CREATE LOBBY", control: "Tab"),
                Node(1, "FRAME", "JOIN LOBBY", control: "Tab"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("empty tab") && n.Contains("CREATE LOBBY")));
        }

        [Test]
        public void TabWithContent_NotFlaggedEmpty()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "Tabs", control: "TabView"),
                Node(1, "FRAME", "Overview", control: "Tab"),
                Node(2, "TEXT", "t", text: "hi"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsFalse(notes.Exists(n => n.Contains("empty tab")));
        }

        [Test]
        public void RadioButtonOutsideGroup_IsFlagged()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "FRAME", "opt", control: "RadioButton"));
            var notes = Text(FigmaImportReport.Collect(ctx, null));
            Assert.IsTrue(notes.Exists(n => n.Contains("not inside a RadioButtonGroup") && n.Contains("opt")));
        }

        [Test]
        public void MultiModeNote_ListsExportedModes()
        {
            var raw = FigmaImportReport.MultiModeNote(new[] { "Dark", "Light" });
            Assert.IsNotNull(raw);
            var note = FigmaStrings.Format(raw.Value);
            Assert.IsTrue(note.Contains("Dark") && note.Contains("Light"));
            Assert.IsTrue(note.Contains("dark.uss") && note.Contains("light.uss"));
        }

        [Test]
        public void MultiModeNote_NullOrEmpty_IsNull()
        {
            Assert.IsNull(FigmaImportReport.MultiModeNote(null));
            Assert.IsNull(FigmaImportReport.MultiModeNote(new string[0]));
        }
    }
}
