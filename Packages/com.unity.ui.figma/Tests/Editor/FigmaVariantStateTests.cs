using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Unity.UIToolkit.Figma.Tests.FigmaTestBuilders;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaVariantStateTests
    {
        static readonly Func<string, string> k_NoFont = f => null;

        static FigmaVariantStyle Variant(string state, params string[] css)
            => new FigmaVariantStyle { state = state, nodeId = "v:" + state.ToLowerInvariant(), css = Css(css) };

        static string BuildUss(FigmaImportContext ctx)
        {
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            FigmaComponentStyleModel.Build(ctx, library, k_NoFont, null);
            return FigmaToUss.Build(ctx, null);
        }

        static List<FigmaStateRule> States(FigmaImportContext ctx, string className)
            => ctx.ComponentRules.Find(r => r.className == className).states;

        static int Count(string haystack, string needle)
        {
            int count = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
            return count;
        }

        [Test]
        public void Variants_EmitPseudoStateDeltas()
        {
            var baseCss = Css("background-color", "#0d99ff", "border-radius", "6px", "color", "#ffffff");
            var btn = Node(0, "INSTANCE", "Play Button", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Play Button", masterCss: baseCss);
            btn.variants = new[]
            {
                Variant("Hover", "background-color", "#0b87e0", "border-radius", "6px", "color", "#ffffff"),
                Variant("Pressed", "background-color", "#0a6ec0", "border-radius", "6px", "color", "#ffffff", "border-width", "2px"),
                Variant("Disabled", "background-color", "#e0e0e0", "border-radius", "6px", "color", "#8a8a8a"),
            };
            var uss = BuildUss(Context(Node(-1, "FRAME", "Screen"), btn));

            StringAssert.Contains(".play-button:hover {", uss);
            StringAssert.Contains(".play-button:active {", uss);
            StringAssert.Contains(".play-button:disabled {", uss);
            StringAssert.Contains("background-color: #0b87e0;", uss);
            StringAssert.Contains("border-width: 2px;", uss);
            StringAssert.Contains("color: #8a8a8a;", uss);

            Assert.Less(uss.IndexOf(":active", StringComparison.Ordinal), uss.IndexOf(":disabled", StringComparison.Ordinal));
            Assert.Less(uss.IndexOf(":disabled", StringComparison.Ordinal), uss.IndexOf(":hover", StringComparison.Ordinal));
        }

        [Test]
        public void SameComponent_SharesOneClassAndOneHoverRule()
        {
            var baseCss = Css("background-color", "#0d99ff");
            var save = Node(0, "INSTANCE", "Save", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Button", masterCss: baseCss);
            save.variants = new[] { Variant("Hover", "background-color", "#0b87e0") };
            var cancel = Node(0, "INSTANCE", "Cancel", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Button", masterCss: baseCss);
            cancel.variants = new[] { Variant("Hover", "background-color", "#0b87e0") };
            var ctx = Context(Node(-1, "FRAME", "Screen"), save, cancel);
            var uss = BuildUss(ctx);

            Assert.AreEqual(ctx.SharedClass(1), ctx.SharedClass(2));
            Assert.AreEqual(1, Count(uss, ":hover {"));
        }

        [Test]
        public void DifferentComponents_IdenticalLook_DistinctClasses()
        {
            var baseCss = Css("background-color", "#0d99ff");
            var save = Node(0, "INSTANCE", "Save", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Save", masterCss: baseCss);
            save.variants = new[] { Variant("Hover", "background-color", "#0b87e0") };
            var del = Node(0, "INSTANCE", "Delete", control: "Button", css: baseCss,
                masterKey: "k2", masterName: "Delete", masterCss: baseCss);
            del.variants = new[] { Variant("Hover", "background-color", "#0b87e0") };
            var ctx = Context(Node(-1, "FRAME", "Screen"), save, del);
            var uss = BuildUss(ctx);

            Assert.AreNotEqual(ctx.SharedClass(1), ctx.SharedClass(2));
            Assert.AreEqual(2, Count(uss, ":hover {"));
        }

        [Test]
        public void TokenBoundHover_EmitsVarDelta()
        {
            var baseCss = Css("background-color", "#f2994a");
            var btn = Node(0, "INSTANCE", "Btn", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Btn", masterCss: baseCss);
            btn.varRefs = new[] { new FigmaCssProp { k = "background-color", v = "buttons/default" } };
            btn.variants = new[]
            {
                new FigmaVariantStyle
                {
                    state = "Hover",
                    css = Css("background-color", "#f7b733"),
                    varRefs = new[] { new FigmaCssProp { k = "background-color", v = "buttons/hover" } }
                }
            };
            var ctx = Context(Node(-1, "FRAME", "Screen"), btn);
            var uss = BuildUss(ctx);

            StringAssert.Contains(".btn:hover {", uss);
            StringAssert.Contains("background-color: var(--buttons-hover);", uss);
        }

        [Test]
        public void RestingVariantOnly_EmitsNoStateRule()
        {
            var baseCss = Css("background-color", "#0d99ff");
            var btn = Node(0, "INSTANCE", "Btn", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Btn", masterCss: baseCss);
            btn.variants = new[] { Variant("Default", "background-color", "#0d99ff") };
            var ctx = Context(Node(-1, "FRAME", "Screen"), btn);
            var uss = BuildUss(ctx);

            Assert.IsNull(States(ctx, "btn"));
            StringAssert.DoesNotContain(".btn:", uss);
        }

        [Test]
        public void HoverIdenticalToBase_EmitsNoHoverRule()
        {
            var baseCss = Css("background-color", "#0d99ff");
            var btn = Node(0, "INSTANCE", "Btn", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Btn", masterCss: baseCss);
            btn.variants = new[] { Variant("Hover", "background-color", "#0d99ff") };
            var ctx = Context(Node(-1, "FRAME", "Screen"), btn);
            var uss = BuildUss(ctx);

            Assert.IsNull(States(ctx, "btn"));
            StringAssert.DoesNotContain(":hover", uss);
        }

        [Test]
        public void StateRule_DoesNotLeakIntoTheBaseElementsDiff()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord
                {
                    figmaNodeId = "n1", cssClass = "play-button", styleClass = "play-button",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#0d99ff" } },
                },
            };
            var uss = ".play-button {\n    background-color: #0d99ff;\n}\n\n"
                    + ".play-button:hover {\n    background-color: #0b87e0;\n}\n\n";
            var changes = FigmaPropertyDiff.Compute(baseline, uss, "<ui:UXML></ui:UXML>");
            Assert.AreEqual(0, changes.Count);
        }

        static List<FigmaStateRecord> StateBaseline(string nodeId = "v:hover")
            => new List<FigmaStateRecord>
            {
                new FigmaStateRecord
                {
                    className = "play-button", pseudo = ":hover", figmaNodeId = nodeId,
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#0b87e0" } }
                }
            };

        [Test]
        public void EditedStateRule_PushesToTheVariantNode()
        {
            var uss = ".play-button {\n    background-color: #0d99ff;\n}\n\n"
                    + ".play-button:hover {\n    background-color: #123456;\n}\n\n";

            var changes = FigmaPropertyDiff.ComputeStates(StateBaseline(), uss);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("v:hover", changes[0].figmaNodeId, "the edit targets the Hover variant, not the base");
            Assert.AreEqual("background-color", changes[0].prop);
            Assert.AreEqual("#123456", changes[0].value);
        }

        [Test]
        public void UnchangedStateRule_ProducesNoChange()
        {
            var uss = ".play-button:hover {\n    background-color: #0b87e0;\n}\n";
            Assert.AreEqual(0, FigmaPropertyDiff.ComputeStates(StateBaseline(), uss).Count);
        }

        [Test]
        public void StateOnLibraryComponent_WarnsInsteadOfPushing()
        {
            var uss = ".play-button:hover {\n    background-color: #123456;\n}\n";
            var warnings = new List<string>();

            var changes = FigmaPropertyDiff.ComputeStates(StateBaseline(string.Empty), uss, warnings);

            Assert.AreEqual(0, changes.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("background-color", warnings[0]);
        }

        [Test]
        public void RemovedStateRule_IsSkipped()
        {
            var uss = ".play-button {\n    background-color: #0d99ff;\n}\n";
            Assert.AreEqual(0, FigmaPropertyDiff.ComputeStates(StateBaseline(), uss).Count,
                "a deleted rule is restored by the next import, not pushed as an edit");
        }

        [Test]
        public void TokenBoundStateRule_IsBaselinedAndLiteralEditIsPushed()
        {
            var themed = ".play-button:hover {\n    background-color: var(--accent-hover);\n}\n";
            Assert.IsTrue(FigmaPropertyDiff.ParseStateRules(themed).ContainsKey("play-button:hover"),
                "a rule whose declarations are all var() still exists and needs a baseline");

            var baseline = new List<FigmaStateRecord>
            {
                new FigmaStateRecord
                {
                    className = "play-button", pseudo = ":hover", figmaNodeId = "v:hover",
                    props = new FigmaCssProp[0] // var() declarations leave nothing literal to record
                }
            };
            var edited = ".play-button:hover {\n    background-color: #ff0000;\n}\n";

            var changes = FigmaPropertyDiff.ComputeStates(baseline, edited);

            Assert.AreEqual(1, changes.Count, "replacing the token with a literal is an edit");
            Assert.AreEqual("#ff0000", changes[0].value);
            Assert.AreEqual("v:hover", changes[0].figmaNodeId);
        }

        [Test]
        public void PropAddedToStateRule_IsPushed()
        {
            var uss = ".play-button:hover {\n    background-color: #0b87e0;\n    opacity: 0.5;\n}\n";

            var changes = FigmaPropertyDiff.ComputeStates(StateBaseline(), uss);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("opacity", changes[0].prop);
            Assert.AreEqual("v:hover", changes[0].figmaNodeId);
        }

        [Test]
        public void UnsupportedPropInStateRule_Warns()
        {
            var uss = ".play-button:hover {\n    background-color: #0b87e0;\n    flex-basis: 40%;\n}\n";
            var warnings = new List<string>();

            var changes = FigmaPropertyDiff.ComputeStates(StateBaseline(), uss, warnings);

            Assert.AreEqual(0, changes.Count);
            Assert.AreEqual(1, warnings.Count, "an edit that cannot travel is explained, not dropped");
        }

        [Test]
        public void TwoStatesSharingOneVariant_PushOnceAndWarn()
        {
            var baseline = new List<FigmaStateRecord>
            {
                new FigmaStateRecord
                {
                    className = "play-button", pseudo = ":hover", figmaNodeId = "v:same",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#0b87e0" } }
                },
                new FigmaStateRecord
                {
                    className = "play-button", pseudo = ":active", figmaNodeId = "v:same",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#0b87e0" } }
                }
            };
            var uss = ".play-button:hover {\n    background-color: #111111;\n}\n"
                    + ".play-button:active {\n    background-color: #222222;\n}\n";
            var warnings = new List<string>();

            var changes = FigmaPropertyDiff.ComputeStates(baseline, uss, warnings);

            Assert.AreEqual(1, changes.Count, "one variant cannot hold two different values for a property");
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void CommentedStylesheet_StillParses()
        {
            var uss = "/* hand-written note with a brace { */\n"
                    + ".play-button:hover {\n    background-color: #123456;\n}\n";

            var changes = FigmaPropertyDiff.ComputeStates(StateBaseline(), uss);

            Assert.AreEqual(1, changes.Count, "a comment must not hide the rule behind it");
            Assert.AreEqual("#123456", changes[0].value);
        }

        [Test]
        public void StateDelta_ExcludesVariantPlacement()
        {
            var baseCss = Css("background-color", "#0d99ff", "left", "10px", "top", "20px");
            var btn = Node(0, "INSTANCE", "Play Button", control: "Button", css: baseCss,
                masterKey: "k1", masterName: "Play Button", masterCss: baseCss);
            btn.variants = new[]
            {
                Variant("Hover", "background-color", "#0b87e0", "left", "400px", "top", "900px")
            };
            var uss = BuildUss(Context(Node(-1, "FRAME", "Screen"), btn));

            StringAssert.Contains("background-color: #0b87e0", uss);
            StringAssert.DoesNotContain("left: 400px", uss);
            StringAssert.DoesNotContain("top: 900px", uss);
        }

        [Test]
        public void UnmappedState_IsReported()
        {
            var btn = Node(0, "FRAME", "Btn", control: "Button", css: Css("background-color", "#0d99ff"));
            btn.variants = new[] { Variant("Warning", "background-color", "#ffaa00") };
            var notes = FigmaTestBuilders.Text(FigmaImportReport.Collect(Context(Node(-1, "FRAME", "Screen"), btn), null));
            Assert.IsTrue(notes.Exists(n => n.Contains("unrecognized variant state") && n.Contains("Warning")));
        }

        [Test]
        public void MappedState_NotReported()
        {
            var btn = Node(0, "FRAME", "Btn", control: "Button", css: Css("background-color", "#0d99ff"));
            btn.variants = new[] { Variant("Hover", "background-color", "#0b87e0") };
            var notes = FigmaTestBuilders.Text(FigmaImportReport.Collect(Context(Node(-1, "FRAME", "Screen"), btn), null));
            Assert.IsFalse(notes.Exists(n => n.Contains("unrecognized variant state")));
        }
    }
}
