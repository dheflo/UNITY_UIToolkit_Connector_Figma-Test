using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaPropertyWriteBackTests
    {
        static FigmaCssProp[] Props(params string[] kv)
        {
            var props = new FigmaCssProp[kv.Length / 2];
            for (var i = 0; i < props.Length; ++i)
                props[i] = new FigmaCssProp { k = kv[i * 2], v = kv[i * 2 + 1] };
            return props;
        }

        const string k_Ns = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">";

        static FigmaNodeRecord Emitted(string id, string path, string tag, string classes, params string[] props)
        {
            return new FigmaNodeRecord { figmaNodeId = id, doc = "screen", path = path, tag = tag, classes = classes, props = Props(props) };
        }

        [Test]
        public void Compute_ReserializedTextShadow_IsNotAnEdit()
        {
            var baseline = new List<FigmaNodeRecord> { Emitted("1:0", "1", "Label", "", "text-shadow", "0px 2px 4px #00000040") };
            var uxml = k_Ns + "<ui:Label style=\"text-shadow: 0 2 4 rgba(0, 0, 0, 0.25);\" /></ui:UXML>";
            var warnings = new List<string>();

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", uxml, null, warnings).Count,
                "UI Builder re-spells colours and lengths on save; that is not an edit");
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Compute_FilterEdit_IsQueuedInCanonicalForm()
        {
            var baseline = new List<FigmaNodeRecord> { Emitted("1:0", "1", "VisualElement", "", "filter", "blur(4px)") };
            var uxml = k_Ns + "<ui:VisualElement style=\"filter: blur(2px) drop-shadow(0px 2px 1.5px rgb(0, 0, 255));\" /></ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, "", uxml, null);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("filter", changes[0].prop);
            Assert.AreEqual("blur(2) drop-shadow(0 2 1.5 rgba(0,0,255,1))", changes[0].value);
        }

        [Test]
        public void Compute_FilterWithNoFigmaFunction_Warns()
        {
            var baseline = new List<FigmaNodeRecord> { Emitted("1:0", "1", "VisualElement", "", "filter", "blur(4px)") };
            var uxml = k_Ns + "<ui:VisualElement style=\"filter: blur(4px) grayscale(1);\" /></ui:UXML>";
            var warnings = new List<string>();

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", uxml, null, warnings).Count,
                "half a chain must not be pushed");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("filter|" + FigmaSyncWarningReason.NoFigmaEquivalent, warnings[0]);
        }

        [Test]
        public void Compute_FilterNone_IsQueued()
        {
            var baseline = new List<FigmaNodeRecord> { Emitted("1:0", "1", "VisualElement", "", "filter", "blur(4px)") };
            var uxml = k_Ns + "<ui:VisualElement style=\"filter: none;\" /></ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, "", uxml, null);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("none", changes[0].value);
        }

        [Test]
        public void ComputeStates_TextShadowEdit_TargetsTheVariant()
        {
            var baseline = new List<FigmaStateRecord>
            {
                new FigmaStateRecord { className = "button", pseudo = ":hover", figmaNodeId = "5:1",
                    props = Props("text-shadow", "0px 1px 2px #000000") }
            };

            var changes = FigmaPropertyDiff.ComputeStates(baseline, ".button:hover { text-shadow: 0px 3px 2px #000000; }\n");

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("5:1", changes[0].figmaNodeId);
            Assert.AreEqual("0 3 2 rgba(0,0,0,1)", changes[0].value);
        }

        [Test]
        public void EffectCanonical_RefusesWhatFigmaCannotHold()
        {
            Assert.IsNull(FigmaEffectValue.Canonical("text-shadow", "1px 1px 2px #000, 0 0 9px #fff"), "one shadow only");
            Assert.IsNull(FigmaEffectValue.Canonical("text-shadow", "inset 1px 1px 2px #000"));
            Assert.IsNull(FigmaEffectValue.Canonical("filter", "drop-shadow(0 0 2px 4px #000)"), "spread has no filter equivalent");
            Assert.IsNull(FigmaEffectValue.Canonical("backdrop-filter", "blur(4px) blur(2px)"), "one background blur, as on import");
            Assert.IsNull(FigmaEffectValue.Canonical("backdrop-filter", "drop-shadow(0 1px 1px #000)"));
            Assert.AreEqual("none", FigmaEffectValue.Canonical("backdrop-filter", "none"));
            Assert.AreEqual("blur(4)", FigmaEffectValue.Canonical("backdrop-filter", "blur(4px)"));
        }

        [Test]
        public void Compute_InsertedElement_StopsSyncUnderThatParent_AndWarns()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                Emitted("1:0", "1", "VisualElement", "", "background-color", "#000000"),
                Emitted("1:1", "1/1", "Label", "", "font-size", "60px")
            };
            var uxml = k_Ns + "<ui:VisualElement style=\"background-color: #000000;\">"
                + "<ui:Label style=\"font-size: 12px;\" /><ui:Label style=\"font-size: 60px;\" />"
                + "</ui:VisualElement></ui:UXML>";
            var warnings = new List<string>();

            var changes = FigmaPropertyDiff.Compute(baseline, "", uxml, null, warnings);

            Assert.AreEqual(0, changes.Count, "the shifted Label must not be diffed against the inserted one");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(FigmaSyncWarningReason.StructureChanged.ToString(), warnings[0]);
        }

        [Test]
        public void Compute_SwappedElements_SameCount_AreCaughtByTheirTags()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                Emitted("1:0", "1", "VisualElement", ""),
                Emitted("1:1", "1/1", "Label", "", "font-size", "60px"),
                Emitted("1:2", "1/2", "Button", "", "background-color", "#111111")
            };
            var uxml = k_Ns + "<ui:VisualElement>"
                + "<ui:Button style=\"background-color: #111111;\" /><ui:Label style=\"font-size: 60px;\" />"
                + "</ui:VisualElement></ui:UXML>";
            var warnings = new List<string>();

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", uxml, null, warnings).Count,
                "a Button's props read through a Label's record would look like edits");
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void Compute_AddedClass_StillSyncsTheElement()
        {
            var baseline = new List<FigmaNodeRecord> { Emitted("1:0", "1", "VisualElement", "card", "background-color", "#111111") };
            var uxml = k_Ns + "<ui:VisualElement class=\"card mine\" style=\"background-color: #222222;\" /></ui:UXML>";
            var warnings = new List<string>();

            var changes = FigmaPropertyDiff.Compute(baseline, "", uxml, null, warnings);

            Assert.AreEqual(1, changes.Count, "a class the user added on top is not a structural change");
            Assert.AreEqual("#222222", changes[0].value);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Compute_StructureBreak_OnlyStopsTheAffectedSubtree()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                Emitted("1:0", "1", "VisualElement", ""),
                Emitted("1:1", "1/1", "VisualElement", ""),
                Emitted("1:2", "1/1/1", "Label", "", "font-size", "14px"),
                Emitted("1:3", "1/2", "Label", "", "font-size", "60px")
            };
            var uxml = k_Ns + "<ui:VisualElement>"
                + "<ui:VisualElement><ui:Label style=\"font-size: 12px;\" /><ui:Label style=\"font-size: 14px;\" /></ui:VisualElement>"
                + "<ui:Label style=\"font-size: 70px;\" />"
                + "</ui:VisualElement></ui:UXML>";
            var warnings = new List<string>();

            var changes = FigmaPropertyDiff.Compute(baseline, "", uxml, null, warnings);

            Assert.AreEqual(1, changes.Count, "the sibling outside the changed container still syncs");
            Assert.AreEqual("1:3", changes[0].figmaNodeId);
            Assert.AreEqual("70px", changes[0].value);
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void ClassEdit_SyncsEvenWhenAnInstanceOverridesItInline()
        {
            var uss = ".button {\n    background-color: rgb(0, 0, 255);\n}\n";
            var uxml = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">"
                + "<ui:Button class=\"button\" style=\"background-color: rgb(255, 0, 0);\" /></ui:UXML>";

            var nodes = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", styleClass = "button", doc = "screen", path = "1",
                    props = Props("background-color", "rgb(255, 0, 0)") }
            };
            var components = new List<FigmaComponentRecord>
            {
                new FigmaComponentRecord { className = "button", masterNodeId = "9:9",
                    props = Props("background-color", "rgb(255, 0, 0)") }
            };

            var changes = FigmaPropertyDiff.Compute(nodes, uss, uxml, components);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("9:9", changes[0].figmaNodeId, "routed to the master, not the instance");
            Assert.AreEqual("background-color", changes[0].prop);
            Assert.AreEqual("rgb(0, 0, 255)", changes[0].value);
        }

        [Test]
        public void ClassEditVisibleBothWays_IsSentOnce()
        {
            var uss = ".button {\n    background-color: rgb(0, 0, 255);\n}\n";
            var uxml = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">"
                + "<ui:Button class=\"button\" /></ui:UXML>";

            var nodes = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", styleClass = "button", doc = "screen", path = "1",
                    props = Props("background-color", "rgb(255, 0, 0)") }
            };
            var components = new List<FigmaComponentRecord>
            {
                new FigmaComponentRecord { className = "button", masterNodeId = "9:9",
                    props = Props("background-color", "rgb(255, 0, 0)") }
            };

            var changes = FigmaPropertyDiff.Compute(nodes, uss, uxml, components);
            Assert.AreEqual(1, changes.Count, "the node pass and the class pass must not both queue it");
            Assert.AreEqual("9:9", changes[0].figmaNodeId);
        }

        [Test]
        public void AckingAClassEdit_AdvancesTheClassBaseline()
        {
            var manifest = new FigmaSyncManifest();
            manifest.screens.Add(new FigmaScreenManifest
            {
                components = new List<FigmaComponentRecord>
                {
                    new FigmaComponentRecord { className = "button", masterNodeId = "9:9",
                        props = Props("background-color", "rgb(255, 0, 0)") }
                }
            });

            Assert.IsTrue(manifest.AdvanceNodeBaseline("9:9", "background-color", "rgb(0, 0, 255)"));
            Assert.AreEqual("rgb(0, 0, 255)", manifest.screens[0].components[0].props[0].v,
                "or the same edit reports again on every scan");
        }

        [Test]
        public void ParseRules_KeepsAllLiteralProps_DropsVar_ExpandsShorthand()
        {
            var uss =
                ".fg-a { background-color: #123456; display: flex; border-radius: 8px; width: var(--w); padding: 8px 12px; }\n" +
                ".fg-b { color: rgba(1, 2, 3, 1); font-size: 14px; }\n";
            var rules = FigmaPropertyDiff.ParseRules(uss);

            Assert.AreEqual("#123456", rules["fg-a"]["background-color"]);
            Assert.AreEqual("8px", rules["fg-a"]["border-radius"]);
            Assert.AreEqual("flex", rules["fg-a"]["display"], "everything literal is recorded (warnable, never silent)");
            Assert.IsFalse(rules["fg-a"].ContainsKey("width"), "var(--token) value dropped (token write-back owns it)");
            Assert.AreEqual("8px", rules["fg-a"]["padding-top"], "box shorthand expands to longhands");
            Assert.AreEqual("12px", rules["fg-a"]["padding-left"]);
            Assert.AreEqual("14px", rules["fg-b"]["font-size"]);
        }

        [Test]
        public void Compute_PropsAbsentFromBaseline_CountAsEdits()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"background-color: #111111; display: flex; left: 0; padding-left: 8px;\" />" +
                "</ui:UXML>";

            Assert.AreEqual(3, FigmaPropertyDiff.Compute(baseline, "", currentUxml).Count,
                "props the baseline never recorded are additions, not noise");
        }

        [Test]
        public void Compute_ReformattedColor_IsNotAnEdit()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1",
                    props = new[]
                    {
                        new FigmaCssProp { k = "background-color", v = "#FFFFFF" },
                        new FigmaCssProp { k = "color", v = "rgba(0, 0, 0, 0)" }
                    } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"background-color: white; color: transparent;\" />" +
                "</ui:UXML>";

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", currentUxml).Count,
                "named colors mean the same thing as the hex Figma sent");
        }

        [Test]
        public void Compute_AlphaSpellingsAreTheSameColor()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#00000080" } } }
            };
            var half =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"background-color: rgba(0, 0, 0, 0.5);\" />" +
                "</ui:UXML>";

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", half).Count,
                "hex alpha 80 and 0.5 are the same colour, one grid apart at best");

            var opaque =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"background-color: rgba(0, 0, 0, 1);\" />" +
                "</ui:UXML>";
            Assert.AreEqual(1, FigmaPropertyDiff.Compute(baseline, "", opaque).Count,
                "a real alpha change still registers");
        }

        [Test]
        public void Compute_PercentageColorChannels_AreUnderstood()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "rgb(255, 0, 0)" } } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"background-color: rgb(100%, 0%, 0%);\" />" +
                "</ui:UXML>";

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", currentUxml).Count);
        }

        [Test]
        public void Compute_PseudoStateRule_IsIgnoredNotMisread()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1", styleClass = "button",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#101010" } } }
            };
            var uss = ".button { background-color: #101010; }\n.button:hover { background-color: #202020; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement class=\"button\" />" +
                "</ui:UXML>";

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, uss, currentUxml).Count,
                "the hover rule must not leak into the base element's effective props");
        }

        [Test]
        public void Compute_LayoutEdit_ProducesChange()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1",
                    props = new[]
                    {
                        new FigmaCssProp { k = "padding-left", v = "8px" },
                        new FigmaCssProp { k = "left", v = "20px" }
                    } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"padding-left: 16px; left: 40px;\" />" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, "", currentUxml);
            CollectionAssert.AreEquivalent(new[] { "padding-left", "left" }, changes.ConvertAll(c => c.prop));
        }

        [Test]
        public void Compute_UnsupportedEdit_Warns_ChangedAndAdded()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1",
                    props = new[] { new FigmaCssProp { k = "flex-basis", v = "auto" } } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"flex-basis: 50%; margin-top: 27px;\" />" +
                "</ui:UXML>";

            var warnings = new List<string>();
            var changes = FigmaPropertyDiff.Compute(baseline, "", currentUxml, null, warnings);
            Assert.AreEqual(0, changes.Count);
            Assert.AreEqual(2, warnings.Count, "changed flex-basis AND freshly-added margin-top both warn");
            Assert.IsTrue(warnings.Exists(w => w.Contains("flex-basis")));
            Assert.IsTrue(warnings.Exists(w => w.Contains("margin-top")));
        }

        [Test]
        public void Compute_GapEdit_IsAContainerChange_MarginsWarn()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "p:0", doc = "screen", path = "1",
                    props = new[] { new FigmaCssProp { k = "gap", v = "8px" } } },
                new FigmaNodeRecord { figmaNodeId = "c:1", doc = "screen", path = "1/1",
                    props = new[] { new FigmaCssProp { k = "margin-bottom", v = "0" } } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"gap: 12px;\">" +
                "<ui:VisualElement style=\"margin-bottom: 6px;\" />" +
                "</ui:VisualElement>" +
                "</ui:UXML>";

            var warnings = new List<string>();
            var changes = FigmaPropertyDiff.Compute(baseline, "", currentUxml, null, warnings);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("p:0", changes[0].figmaNodeId);
            Assert.AreEqual("gap", changes[0].prop);
            Assert.AreEqual("12px", changes[0].value);
            Assert.AreEqual(1, warnings.Count, "the margin edit warns (no Figma equivalent)");

            var lines = FigmaPropertyDiff.AggregateWarnings(warnings);
            Assert.AreEqual(FigmaSyncWarningReason.UseContainerGap, lines[0].reason);
            StringAssert.StartsWith("Not synced: margin-bottom (", FigmaStrings.Format(lines[0]));
        }

        [Test]
        public void Compute_GapEdit_InsideInstance_Warns()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "I1:2;3:4", doc = "screen", path = "1",
                    props = new[] { new FigmaCssProp { k = "gap", v = "8px" } } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement style=\"gap: 12px;\" />" +
                "</ui:UXML>";

            var warnings = new List<string>();
            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", currentUxml, null, warnings).Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("gap", warnings[0]);
        }

        [Test]
        public void Compute_DetectsChangedAndAdded_IgnoresUnchanged()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord
                {
                    figmaNodeId = "1:10", cssClass = "fg-a",
                    props = new[]
                    {
                        new FigmaCssProp { k = "background-color", v = "#111111" },
                        new FigmaCssProp { k = "border-radius", v = "8px" }
                    }
                }
            };
            var currentUss = ".fg-a { background-color: #222222; border-radius: 8px; font-size: 16px; }\n";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, null);
            Assert.AreEqual(2, changes.Count);
            CollectionAssert.AreEquivalent(new[] { "background-color", "font-size" }, changes.ConvertAll(c => c.prop));
            foreach (var c in changes)
                Assert.AreEqual("1:10", c.figmaNodeId);
        }

        [Test]
        public void Compute_NoChanges_WhenUssMatchesBaseline()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:10", cssClass = "fg-a",
                    props = new[] { new FigmaCssProp { k = "border-radius", v = "8px" } } }
            };
            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, ".fg-a { border-radius: 8px; }", null).Count);
        }

        [Test]
        public void Compute_DetectsInlineOverride_FromUxml()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "36:38", cssClass = "button-36-38",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#848484" } } }
            };
            var currentUss = ".button-36-38 { background-color: #848484; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:Button text=\"Submit\" class=\"button-36-38 unity-button\" style=\"background-color: rgb(255, 0, 0);\" />" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("36:38", changes[0].figmaNodeId);
            Assert.AreEqual("background-color", changes[0].prop);
            Assert.AreEqual("rgb(255, 0, 0)", changes[0].value);
        }

        [Test]
        public void Compute_InlineMatchesBaseline_NoChange()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "36:38", cssClass = "button-36-38",
                    props = new[] { new FigmaCssProp { k = "background-color", v = "#ff0000" } } }
            };
            var currentUss = ".button-36-38 { background-color: #ff0000; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:Button class=\"button-36-38\" style=\"background-color: rgb(255, 0, 0);\" />" +
                "</ui:UXML>";

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml).Count);
        }

        [Test]
        public void Compute_Path_SharedClassEditFansOutToInstances()
        {
            var props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } };
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1/1", props = props },
                new FigmaNodeRecord { figmaNodeId = "1:2", doc = "screen", path = "1/2", props = props }
            };
            var currentUss = ".button { background-color: #222222; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<Style src=\"screen.uss\" />" +
                "<ui:VisualElement>" +
                "<ui:Button class=\"button unity-button\" />" +
                "<ui:Button class=\"button unity-button\" />" +
                "</ui:VisualElement>" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml);
            Assert.AreEqual(2, changes.Count);
            CollectionAssert.AreEquivalent(new[] { "1:1", "1:2" }, changes.ConvertAll(c => c.figmaNodeId));
            foreach (var c in changes)
                Assert.AreEqual("#222222", c.value);
        }

        [Test]
        public void Compute_Path_InlineEditHitsOneNode()
        {
            var props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } };
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", doc = "screen", path = "1/1", props = props },
                new FigmaNodeRecord { figmaNodeId = "1:2", doc = "screen", path = "1/2", props = props }
            };
            var currentUss = ".button { background-color: #111111; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement>" +
                "<ui:Button class=\"button\" />" +
                "<ui:Button class=\"button\" style=\"background-color: rgb(0, 255, 0);\" />" +
                "</ui:VisualElement>" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("1:2", changes[0].figmaNodeId);
            Assert.AreEqual("rgb(0, 255, 0)", changes[0].value);
        }

        [Test]
        public void Compute_Path_MissingPathSkips_ReimportHeals()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:9", doc = "screen", path = "1/3",
                    props = new[] { new FigmaCssProp { k = "width", v = "100px" } } }
            };
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement><ui:Button style=\"width: 200px;\" /></ui:VisualElement>" +
                "</ui:UXML>";

            Assert.AreEqual(0, FigmaPropertyDiff.Compute(baseline, "", currentUxml).Count);
        }

        [Test]
        public void ParsePaths_SkipsNonContent_MatchesEmitterScheme()
        {
            var uxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<Style src=\"tokens.uss\" /><Style src=\"screen.uss\" />" +
                "<Template name=\"Card\" src=\"Card.uxml\" />" +
                "<ui:VisualElement class=\"a\">" +
                "<ui:Button class=\"b\" style=\"width: 10px;\" />" +
                "<ui:Instance template=\"Card\" style=\"left: 5px;\">" +
                "<AttributeOverrides element-name=\"text-0\" text=\"Hi\" />" +
                "</ui:Instance>" +
                "</ui:VisualElement>" +
                "</ui:UXML>";
            var paths = FigmaPropertyDiff.ParsePaths(uxml);

            Assert.AreEqual(3, paths.Count, "Style/Template/AttributeOverrides are not content elements");
            Assert.AreEqual("a", paths["1"].classTokens);
            Assert.AreEqual("b", paths["1/1"].classTokens);
            Assert.AreEqual("width: 10px;", paths["1/1"].inlineStyle);
            Assert.AreEqual("left: 5px;", paths["1/2"].inlineStyle);
        }

        [Test]
        public void Compute_ClassEdit_TargetsMasterOnce()
        {
            var props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } };
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", styleClass = "button", doc = "screen", path = "1/1", props = props },
                new FigmaNodeRecord { figmaNodeId = "1:2", styleClass = "button", doc = "screen", path = "1/2", props = props }
            };
            var components = new List<FigmaComponentRecord>
            {
                new FigmaComponentRecord { className = "button", masterNodeId = "2:0" }
            };
            var currentUss = ".button { background-color: #222222; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement>" +
                "<ui:Button class=\"button unity-button\" />" +
                "<ui:Button class=\"button unity-button\" />" +
                "</ui:VisualElement>" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml, components);
            Assert.AreEqual(1, changes.Count, "one master-targeted change, not per-instance fan-out");
            Assert.AreEqual("2:0", changes[0].figmaNodeId);
            Assert.AreEqual("#222222", changes[0].value);
        }

        [Test]
        public void Compute_ClassEdit_SkipsInlineOverriddenInstance_InlineEditTargetsInstance()
        {
            var props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } };
            var overridden = new[] { new FigmaCssProp { k = "background-color", v = "#e01b1b" } };
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", styleClass = "button", doc = "screen", path = "1/1", props = props },
                new FigmaNodeRecord { figmaNodeId = "1:2", styleClass = "button", doc = "screen", path = "1/2", props = overridden }
            };
            var components = new List<FigmaComponentRecord>
            {
                new FigmaComponentRecord { className = "button", masterNodeId = "2:0" }
            };
            var currentUss = ".button { background-color: #222222; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement>" +
                "<ui:Button class=\"button\" />" +
                "<ui:Button class=\"button\" style=\"background-color: #00ff00;\" />" +
                "</ui:VisualElement>" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml, components);
            Assert.AreEqual(2, changes.Count);
            var master = changes.Find(c => c.figmaNodeId == "2:0");
            var instance = changes.Find(c => c.figmaNodeId == "1:2");
            Assert.AreEqual("#222222", master.value, "class edit -> master");
            Assert.AreEqual("#00ff00", instance.value, "inline edit -> that instance only");
        }

        [Test]
        public void Compute_ClassEdit_NoMasterMapping_FansOut()
        {
            var props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } };
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", styleClass = "button", doc = "screen", path = "1/1", props = props },
                new FigmaNodeRecord { figmaNodeId = "1:2", styleClass = "button", doc = "screen", path = "1/2", props = props }
            };
            var currentUss = ".button { background-color: #222222; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement><ui:Button class=\"button\" /><ui:Button class=\"button\" /></ui:VisualElement>" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml, null);
            Assert.AreEqual(2, changes.Count);
            CollectionAssert.AreEquivalent(new[] { "1:1", "1:2" }, changes.ConvertAll(c => c.figmaNodeId));
        }

        [Test]
        public void AdvanceNodeBaseline_MasterAck_AdvancesSharers_SkipsInlineOverride()
        {
            var manifest = new FigmaSyncManifest
            {
                screens = new List<FigmaScreenManifest>
                {
                    new FigmaScreenManifest
                    {
                        assetName = "screen",
                        components = new List<FigmaComponentRecord>
                        {
                            new FigmaComponentRecord { className = "button", masterNodeId = "2:0" }
                        },
                        nodes = new List<FigmaNodeRecord>
                        {
                            new FigmaNodeRecord { figmaNodeId = "1:1", styleClass = "button", doc = "screen", path = "1/1",
                                props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } } },
                            new FigmaNodeRecord { figmaNodeId = "1:2", styleClass = "button", doc = "screen", path = "1/2",
                                props = new[] { new FigmaCssProp { k = "background-color", v = "#e01b1b" } } }
                        }
                    }
                }
            };

            var changed = manifest.AdvanceNodeBaseline("2:0", "background-color", "#222222",
                rec => rec.path == "1/2");

            Assert.IsTrue(changed);
            Assert.AreEqual("#222222", manifest.screens[0].nodes[0].props[0].v, "sharer advanced to the class value");
            Assert.AreEqual("#e01b1b", manifest.screens[0].nodes[1].props[0].v, "inline-overriding sharer untouched");
        }

        [Test]
        public void Compute_PathlessRecord_SharedStyleClassStillWorks()
        {
            var props = new[] { new FigmaCssProp { k = "background-color", v = "#111111" } };
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", cssClass = "row-1", styleClass = "row", props = props },
                new FigmaNodeRecord { figmaNodeId = "1:2", cssClass = "row-2", styleClass = "row", props = props }
            };
            var currentUss = ".row { background-color: #222222; }\n";
            var currentUxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:VisualElement class=\"row-1 row\" />" +
                "<ui:VisualElement class=\"row-2 row\" />" +
                "</ui:UXML>";

            var changes = FigmaPropertyDiff.Compute(baseline, currentUss, currentUxml);
            Assert.AreEqual(2, changes.Count);
            CollectionAssert.AreEquivalent(new[] { "1:1", "1:2" }, changes.ConvertAll(c => c.figmaNodeId));
        }

        [Test]
        public void Compute_PathlessRecord_NoStyleClass_FallsBackToIdentityClass()
        {
            var baseline = new List<FigmaNodeRecord>
            {
                new FigmaNodeRecord { figmaNodeId = "1:1", cssClass = "fg-a",
                    props = new[] { new FigmaCssProp { k = "border-radius", v = "8px" } } }
            };
            var changes = FigmaPropertyDiff.Compute(baseline, ".fg-a { border-radius: 4px; }", null);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("1:1", changes[0].figmaNodeId);
        }

        [Test]
        public void ParseInlineStyles_KeepsLiteralProps_DropsVar_IgnoresNoClass()
        {
            var uxml =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" +
                "<ui:Button class=\"fg-a\" style=\"background-color: rgb(1, 2, 3); display: flex; width: var(--w); font-size: 14px;\" />" +
                "<ui:Label style=\"color: rgb(4, 5, 6);\" />" +
                "</ui:UXML>";
            var inline = FigmaPropertyDiff.ParseInlineStyles(uxml);

            Assert.AreEqual(1, inline.Count, "element without a class is ignored");
            Assert.AreEqual("rgb(1, 2, 3)", inline["fg-a"]["background-color"]);
            Assert.AreEqual("14px", inline["fg-a"]["font-size"]);
            Assert.AreEqual("flex", inline["fg-a"]["display"], "all literal props recorded (supported filter is at emission)");
            Assert.IsFalse(inline["fg-a"].ContainsKey("width"), "var(--token) value dropped");
        }

        [Test]
        public void Queue_LatestWinsPerNodeProp_AndAckRemoves()
        {
            FigmaPropertyWriteBack.Clear();
            try
            {
                FigmaPropertyWriteBack.Enqueue(new[]
                {
                    new FigmaPropertyChange { figmaNodeId = "1:10", prop = "border-radius", value = "4px", file = "F" },
                    new FigmaPropertyChange { figmaNodeId = "1:10", prop = "border-radius", value = "8px", file = "F" },
                    new FigmaPropertyChange { figmaNodeId = "1:11", prop = "color", value = "#ffffff", file = "F" }
                });

                var pending = FigmaPropertyWriteBack.Pending();
                Assert.AreEqual(2, pending.Count);
                Assert.AreEqual("8px", pending.Find(c => c.figmaNodeId == "1:10").value);

                FigmaPropertyWriteBack.Ack(new[] { "F|1:10|border-radius" });
                Assert.AreEqual(1, FigmaPropertyWriteBack.Count);
                Assert.AreEqual("1:11", FigmaPropertyWriteBack.Pending()[0].figmaNodeId);
            }
            finally { FigmaPropertyWriteBack.Clear(); }
        }

        [Test]
        public void Ack_IsFileScoped_LeavesOtherFileQueued()
        {
            FigmaPropertyWriteBack.Clear();
            try
            {
                FigmaPropertyWriteBack.Enqueue(new[]
                {
                    new FigmaPropertyChange { figmaNodeId = "1:10", prop = "color", value = "#aaaaaa", file = "FileA" },
                    new FigmaPropertyChange { figmaNodeId = "1:10", prop = "color", value = "#bbbbbb", file = "FileB" }
                });
                Assert.AreEqual(2, FigmaPropertyWriteBack.Count, "same node id in two files are distinct changes");

                FigmaPropertyWriteBack.Ack(new[] { "FileA|1:10|color" });

                var pending = FigmaPropertyWriteBack.Pending();
                Assert.AreEqual(1, pending.Count);
                Assert.AreEqual("FileB", pending[0].file, "acking FileA leaves FileB's same-node change queued");
            }
            finally { FigmaPropertyWriteBack.Clear(); }
        }

        [Test]
        public void Push_ExposesPropertyEdit_DetectionAloneDoesNot()
        {
            const string name = "FigmaPropWriteBackUnitTest";
            const string folder = "Assets/Figma/" + name;
            try
            {
                var json =
                    "{\"schema\":1,\"fileName\":\"" + name + "\",\"rootIndex\":0,\"variables\":[]," +
                    "\"nodes\":[{\"id\":\"n:1\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + name + "\"," +
                        "\"css\":[{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"background-color\",\"v\":\"#111111\"}]}]}";
                FigmaImportPipeline.Import(json);
                FigmaPropertyWriteBack.Clear();

                var uxmlPath = Path.Combine(Application.dataPath, "Figma", name, name + ".uxml");
                Assert.IsTrue(File.Exists(uxmlPath), "import should have written the screen .uxml");
                var uxmlText = File.ReadAllText(uxmlPath);
                StringAssert.Contains("#111111", uxmlText, "non-component styling should be inline in the UXML");
                File.WriteAllText(uxmlPath, uxmlText.Replace("#111111", "#222222"));

                Assert.IsNotEmpty(FigmaPropertyWriteBack.Scan(), "edit should be detected by Scan");
                Assert.IsEmpty(FigmaPropertyWriteBack.Reconcile(), "an unpushed edit must not be exposed to Figma");

                var keys = new List<string>();
                foreach (var c in FigmaPropertyWriteBack.Scan())
                    keys.Add(FigmaPropertyWriteBack.KeyOf(c));
                FigmaPropertyWriteBack.Push(keys);
                Assert.IsNotEmpty(FigmaPropertyWriteBack.Reconcile(), "a pushed edit is exposed on reconcile");
            }
            finally
            {
                FigmaPropertyWriteBack.Clear();
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
