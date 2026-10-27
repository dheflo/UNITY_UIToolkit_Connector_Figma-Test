using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    /// <summary>
    /// What each converter case is meant to demonstrate, asserted rather than recorded. A case folder
    /// holds only its input.json; the claims live here, so a failure names the behaviour that moved.
    /// Window > UI Toolkit > Connector Tests dumps what a case emits, for writing these.
    /// </summary>
    class FigmaConverterCaseTests
    {
        static FigmaEmitted Emit(string name) => FigmaConverterFixtures.Emit(name);

        [Test]
        public void EveryCase_IsStructurallySound([ValueSource(typeof(FigmaConverterFixtures), nameof(FigmaConverterFixtures.Cases))] string name)
        {
            FigmaConverterInvariants.Check(Emit(name));
        }

        [Test]
        public void AbsolutePosition_ChildrenOfAPlainFrameArePlacedFromTheirBoxes()
        {
            var styles = Emit("absolute-position").Styles("screen.uxml");

            StringAssert.Contains("position: absolute; left: 40px; top: 60px", styles[1]);
            StringAssert.Contains("position: absolute; left: 12px; top: 34px", styles[2]);
            StringAssert.Contains("position: relative; left: 7px", styles[3]);
            StringAssert.DoesNotContain("absolute", styles[3]);
        }

        [Test]
        public void AutolayoutAbsoluteChild_OnlyThePulledOutChildIsPlaced()
        {
            var styles = Emit("autolayout-absolute-child").Styles("screen.uxml");

            StringAssert.DoesNotContain("position", styles[1]);
            StringAssert.Contains("position: absolute; left: 260px; top: 4px", styles[2]);
        }

        [Test]
        public void GroupOrigin_NestedGroupDoesNotDoubleTheOffset()
        {
            var styles = Emit("group-origin").Styles("screen.uxml");

            StringAssert.Contains("left: 120px; top: 90px", styles[1]);
            StringAssert.Contains("left: 30px; top: 40px", styles[2]);
        }

        [Test]
        public void TextCase_IsBakedIntoTheCharacters()
        {
            CollectionAssert.AreEqual(
                new[] { "QUIET WORDS", "loud words", "The Quick Brown Fox", "As Authored", "No textCase field" },
                Emit("text-case").Captions("screen.uxml"));
        }

        [Test]
        public void TextWrap_FollowsTheFigmaResizeMode()
        {
            var styles = Emit("text-wrap").Styles("screen.uxml");

            StringAssert.Contains("white-space: normal", styles[1]);
            StringAssert.Contains("white-space: nowrap", styles[2]);
        }

        [Test]
        public void CaptionButton_APlainFrameIsNotGuessedToBeAControl()
        {
            var emitted = Emit("caption-button");

            CollectionAssert.DoesNotContain(emitted.Tags("screen.uxml"), "Button");
            CollectionAssert.Contains(emitted.Tags("screen.uxml"), "Label");
            CollectionAssert.AreEqual(new[] { "Play" }, emitted.Captions("screen.uxml"));
        }

        [Test]
        public void InstanceOverride_CaptionAndDeltaAreInline_TheRestIsTheClass()
        {
            var emitted = Emit("instance-override");

            CollectionAssert.Contains(emitted.Tags("screen.uxml"), "Button");
            CollectionAssert.AreEqual(new[] { "Cancel" }, emitted.Captions("screen.uxml"));

            var style = emitted.Styles("screen.uxml")[1];
            StringAssert.Contains("background-color: #ff5533", style);
            StringAssert.DoesNotContain("border-radius", style);
            CollectionAssert.Contains(emitted.Rule("screen.uss", ".button"), "border-radius: 6px");
        }

        [Test]
        public void NestedCaptionInstance_ACaptionInsideANestedComponentStillReachesTheControl()
        {
            var emitted = Emit("nested-caption-instance");

            CollectionAssert.Contains(emitted.Tags("screen.uxml"), "Button");
            CollectionAssert.AreEqual(new[] { "PRIMARY" }, emitted.Captions("screen.uxml"));
        }

        [Test]
        public void TaggedInstance_TheTagBeatsTheLibraryAndEmitsNoTemplate()
        {
            var emitted = Emit("tagged-instance");

            CollectionAssert.Contains(emitted.Tags("screen.uxml"), "SliderInt");
            CollectionAssert.IsEmpty(emitted.TemplateDeclarations("screen.uxml"));
            CollectionAssert.DoesNotContain(emitted.FileNames, "Volume.uxml");
        }

        [Test]
        public void TextFieldReset_FlattensTheThemedInnerInput()
        {
            var emitted = Emit("textfield-reset");

            CollectionAssert.Contains(emitted.Tags("screen.uxml"), "TextField");
            CollectionAssert.Contains(emitted.Selectors("screen.uss"),
                ".unity-text-field .unity-base-text-field__input");
        }

        [Test]
        public void CompositePanel_ANonComponentIsFullyInline()
        {
            var emitted = Emit("composite-panel");

            CollectionAssert.IsEmpty(emitted.Classes("screen.uxml").Where(c => !c.StartsWith("unity-")));
            Assert.IsEmpty(emitted.Text("screen.uss").Trim());
        }

        [Test]
        public void CompositeMasterDiff_InstancesInlineAndOnlyOneCarriesADelta()
        {
            var emitted = Emit("composite-master-diff");

            CollectionAssert.IsEmpty(emitted.TemplateDeclarations("screen.uxml"));
            CollectionAssert.AreEqual(
                new[] { "Card A", "Detail A", "Card B", "Detail B" }, emitted.Captions("screen.uxml"));

            Assert.AreEqual(2, emitted.Classes("screen.uxml").Count(c => c == "card"));
            CollectionAssert.Contains(emitted.Rule("screen.uss", ".card"), "width: 300px");
            Assert.AreEqual(1, emitted.Styles("screen.uxml").Count(s => s.Contains("width: 420px")));
        }

        [Test]
        public void VariantDedup_TwoComponentsSharingANameGetDistinctClasses()
        {
            var emitted = Emit("variant-dedup");

            CollectionAssert.IsSubsetOf(new[] { "button", "button-2" }, emitted.Classes("screen.uxml"));
            CollectionAssert.Contains(emitted.Rule("screen.uss", ".button"), "background-color: #0d99ff");
            CollectionAssert.Contains(emitted.Rule("screen.uss", ".button-2"), "background-color: #333333");
        }

        [Test]
        public void CompositeTemplate_TwoInstancesShareOneTemplateAndOverrideBothCaptions()
        {
            var emitted = Emit("composite-template");

            CollectionAssert.AreEqual(new[] { "Card" }, emitted.TemplateDeclarations("screen.uxml"));
            CollectionAssert.AreEqual(new[] { "text-0", "text-1" }, emitted.TextSlots("Card.uxml"));

            var instances = emitted.Instances("screen.uxml");
            Assert.AreEqual(2, instances.Count);
            CollectionAssert.AreEqual(new[] { "Card A", "Detail A" }, instances[0].overrides);
            CollectionAssert.AreEqual(new[] { "Card B", "Detail B" }, instances[1].overrides);

            Assert.IsEmpty(instances[0].style);
            Assert.IsEmpty(instances[1].style);

            CollectionAssert.Contains(emitted.Classes("Card.uxml"), "card");
            CollectionAssert.Contains(emitted.Rule("screen.uss", ".card"), "width: 300px");
        }

        [Test]
        public void VariantMasterDiff_InstancesDifferingInWidthAreInlined()
        {
            var emitted = Emit("variant-master-diff");

            CollectionAssert.IsEmpty(emitted.TemplateDeclarations("screen.uxml"));
            CollectionAssert.IsEmpty(emitted.Instances("screen.uxml"));
            CollectionAssert.DoesNotContain(emitted.FileNames, "Play-Button.uxml");

            var styles = emitted.Styles("screen.uxml");
            Assert.IsTrue(styles.Any(s => s.Contains("width: 180px")));
            Assert.IsTrue(styles.Any(s => s.Contains("width: 120px")));
            CollectionAssert.AreEqual(new[] { "Go", "Stop" }, emitted.Captions("screen.uxml"));

            CollectionAssert.Contains(emitted.Rule("screen.uss", ".play-button"), "background-color: #0d99ff");
        }

        [Test]
        public void VariantStatesButton_StateVariantsBecomePseudoClassRules()
        {
            var emitted = Emit("variant-states-button");

            CollectionAssert.AreEqual(new[] { "Play-Button" }, emitted.TemplateDeclarations("screen.uxml"));
            CollectionAssert.AreEqual(
                new[] { ".play-button", ".play-button:active", ".play-button:disabled", ".play-button:hover" },
                emitted.Selectors("screen.uss"));

            CollectionAssert.AreEqual(new[] { "background-color: #0b87e0" },
                emitted.Rule("screen.uss", ".play-button:hover"));
        }

        [Test]
        public void VariantStatesMultiaxis_TheNonStateAxisNamesTheClass()
        {
            var emitted = Emit("variant-states-multiaxis");

            CollectionAssert.Contains(emitted.Classes("Chip.uxml"), "chip-large");
            CollectionAssert.AreEqual(new[] { ".chip-large", ".chip-large:hover" },
                emitted.Selectors("screen.uss"));
        }

        [Test]
        public void VariantReactions_APrototypeHoverBecomesAStateRule()
        {
            var emitted = Emit("variant-reactions");

            CollectionAssert.Contains(emitted.Selectors("screen.uss"), ".tab:hover");
            CollectionAssert.AreEqual(new[] { "background-color: #333333" },
                emitted.Rule("screen.uss", ".tab:hover"));
        }

        [Test]
        public void IconButtonStates_AControlWithNoTextKeepsItsStates()
        {
            var emitted = Emit("icon-button-states");

            CollectionAssert.AreEqual(new[] { ".button", ".button:disabled", ".button:hover" },
                emitted.Selectors("screen.uss"));
            CollectionAssert.IsEmpty(emitted.Captions("screen.uxml"));
        }

        [Test]
        public void VariantCaptionBoxCss_TheCaptionFoldsInButTheStateStaysMinimal()
        {
            var emitted = Emit("variant-caption-box-css");

            var rule = emitted.Rule("screen.uss", ".button");
            CollectionAssert.Contains(rule, "color: #ffffff");
            CollectionAssert.Contains(rule, "font-size: 18px");

            CollectionAssert.AreEqual(new[] { "background-color: #ff8a6b" },
                emitted.Rule("screen.uss", ".button:hover"));
        }

        [Test]
        public void TokensAlias_AnAliasResolvesToAVarChain()
        {
            var emitted = Emit("tokens-alias");

            var tokens = emitted.Text("tokens.uss");
            StringAssert.Contains("--neutral-000: rgba(255, 255, 255, 1)", tokens);
            StringAssert.Contains("--background-primary: var(--neutral-000)", tokens);

            StringAssert.Contains("background-color: var(--background-primary)", emitted.Styles("screen.uxml")[0]);
        }

        [Test]
        public void TokensMultimode_EmitsEveryModeButLinksOnlyTheFramesMode()
        {
            var emitted = Emit("tokens-multimode");

            CollectionAssert.DoesNotContain(emitted.FileNames, "tokens.uss");
            CollectionAssert.Contains(emitted.FileNames, "dark.uss");
            CollectionAssert.Contains(emitted.FileNames, "light.uss");

            CollectionAssert.AreEqual(new[] { "dark.uss", "screen.uss" }, StyleLinks(emitted, "screen.uxml"),
                "a frame left on the collection default links that mode");

            StringAssert.Contains("var(--background-primary)", emitted.Text("screen.uxml"));
        }

        [Test]
        public void TokensMultimodeLight_LinksTheModeTheFrameResolvesTo()
        {
            var emitted = Emit("tokens-multimode-light");

            CollectionAssert.Contains(emitted.FileNames, "dark.uss");
            CollectionAssert.Contains(emitted.FileNames, "light.uss");
            CollectionAssert.AreEqual(new[] { "light.uss", "screen.uss" }, StyleLinks(emitted, "screen.uxml"),
                "the frame's own mode wins over the collection default");
        }

        [Test]
        public void LinkedModeFiles_PreferTheScreenModes_AndFallBackToTheDefault()
        {
            var produced = new[] { "dark.uss", "light.uss" };
            var message = new FigmaImportMessage { defaultMode = "Dark", screenModes = new[] { "Light", "Light" } };
            CollectionAssert.AreEqual(new[] { "light.uss" }, FigmaImportPipeline.LinkedModeFiles(message, produced),
                "the frame's mode wins and a repeated mode links once");

            message.screenModes = null;
            CollectionAssert.AreEqual(new[] { "dark.uss" }, FigmaImportPipeline.LinkedModeFiles(message, produced),
                "an older plugin sends no screen modes, so the default is linked");

            message.screenModes = new[] { "Compact" };
            CollectionAssert.AreEqual(new[] { "dark.uss" }, FigmaImportPipeline.LinkedModeFiles(message, produced),
                "a mode with no stylesheet falls back to the default rather than linking nothing");
        }

        [Test]
        public void Templates_LinkNoModeStylesheet_SoTheScreensModeAppliesToThem()
        {
            var json = File.ReadAllText(Path.Combine(FigmaConverterFixtures.CasesDir(), "composite-template", "input.json"))
                .Replace("\"modeVariables\": []", "\"modeVariables\": [{\"id\":\"V:s\",\"name\":\"surface\",\"type\":\"COLOR\",\"values\":["
                    + "{\"mode\":\"Dark\",\"hasColor\":true,\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":1}},"
                    + "{\"mode\":\"Light\",\"hasColor\":true,\"color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}]}]")
                .Replace("\"modes\": []", "\"modes\": [\"Dark\", \"Light\"]")
                .Replace("\"defaultMode\": \"\"", "\"defaultMode\": \"Dark\"")
                .Replace("\"screenModes\": []", "\"screenModes\": [\"Light\"]");
            StringAssert.Contains("\"screenModes\": [\"Light\"]", json, "the case's input.json changed shape; update the replacements");
            StringAssert.Contains("\"mode\":\"Light\"", json, "the case's input.json changed shape; update the replacements");
            var emitted = new FigmaEmitted("composite-template-light", FigmaConverterFixtures.EmitDocuments(json));

            CollectionAssert.AreEqual(new[] { "light.uss", "screen.uss" }, StyleLinks(emitted, "screen.uxml"));

            var templates = 0;
            foreach (var file in emitted.FileNames)
            {
                if (!file.EndsWith(".uxml") || file == "screen.uxml")
                    continue;
                templates++;
                CollectionAssert.AreEqual(new[] { "screen.uss" }, StyleLinks(emitted, file),
                    file + " would define its own mode on the template root and override the screen's");
            }
            Assert.Greater(templates, 0, "the case is expected to emit component templates");
        }

        static List<string> StyleLinks(FigmaEmitted emitted, string file)
        {
            var links = new List<string>();
            foreach (var element in emitted.Elements(file, "Style"))
                links.Add((string)element.Attribute("src"));
            return links;
        }
    }
}
