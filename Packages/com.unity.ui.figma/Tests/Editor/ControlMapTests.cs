using NUnit.Framework;
using UnityEngine;
using static Unity.UIToolkit.Figma.Tests.FigmaTestBuilders;

namespace Unity.UIToolkit.Figma.Tests
{
    class ControlMapTests
    {
        [Test]
        public void Text_ResolvesToLabel()
        {
            var ctx = Context(Node(-1, "TEXT", "Hi", text: "Hi"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("Label", r.tag);
            Assert.IsTrue(r.isControl);
        }

        [Test]
        public void PlainFrame_ResolvesToVisualElement()
        {
            var ctx = Context(Node(-1, "FRAME", "Panel", css: Css("display", "flex")));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
            Assert.IsFalse(r.isControl);
        }

        [Test]
        public void FramedSingleText_WithFill_StaysVisualElement()
        {
            var ctx = Context(
                Node(-1, "FRAME", "CTA", css: Css("background-color", "#0d99ff", "border-radius", "6px")),
                Node(0, "TEXT", "t", text: "GO"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
            Assert.IsFalse(r.isControl);
        }

        [Test]
        public void FramedSingleText_NoStyling_StaysVisualElement()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Row"),
                Node(0, "TEXT", "t", text: "Player name"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
        }

        [Test]
        public void NameMatchedLeaf_WithTwoMeaningfulChildren_IsRefused()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "Toggle"),
                Node(0, "FRAME", "Box"),
                Node(0, "FRAME", "Tick"));
            Assert.AreEqual("VisualElement", ControlMap.Resolve(ctx, 0, null).tag);
        }

        [Test]
        public void NameMatchedLeaf_WithOneMeaningfulChild_Resolves()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "Toggle"),
                Node(0, "FRAME", "Box"));
            Assert.AreEqual("Toggle", ControlMap.Resolve(ctx, 0, null).tag);
        }

        [Test]
        public void NameMatchedLeaf_WithTwoRasterChildren_Resolves()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "Toggle"),
                Node(0, "RECTANGLE", "Box", raster: true),
                Node(0, "VECTOR", "Tick", raster: true));
            Assert.AreEqual("Toggle", ControlMap.Resolve(ctx, 0, null).tag);
        }

        [Test]
        public void NameMatchedButton_WithAnIconInstanceAndALabel_Resolves()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "Button"),
                Node(0, "INSTANCE", "Icon"),
                Node(0, "TEXT", "lbl", text: "Save"));
            Assert.AreEqual("Button", ControlMap.Resolve(ctx, 0, null).tag);
        }

        [Test]
        public void NameMatchedLeafWithoutAnIconSlot_StillRefusesTwoChildren()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "Toggle"),
                Node(0, "INSTANCE", "Icon"),
                Node(0, "TEXT", "lbl", text: "On"));
            Assert.AreEqual("VisualElement", ControlMap.Resolve(ctx, 0, null).tag);
        }

        [Test]
        public void NameMatchedLeaf_RefusalDoesNotApplyToTaggedNodes()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "whatever", control: "Toggle"),
                Node(0, "FRAME", "Box"),
                Node(0, "FRAME", "Tick"));
            Assert.AreEqual("Toggle", ControlMap.Resolve(ctx, 0, null).tag);
        }

        [Test]
        public void MultiChildInstance_NamedInput_StaysContainer()
        {
            var ctx = Context(
                Node(-1, "INSTANCE", "Input - Button"),
                Node(0, "FRAME", "Input Field", css: Css("border", "1px solid #424242")),
                Node(1, "TEXT", "ph", text: "Email"),
                Node(0, "FRAME", "Btn", css: Css("background-color", "#0d99ff")),
                Node(3, "TEXT", "l", text: "Submit"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag, "multi-child node must not become a leaf control");
        }

        [Test]
        public void ComponentNamedButton_ResolvesToButton()
        {
            var ctx = Context(Node(-1, "COMPONENT", "Button", componentKey: "k1", master: true));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("Button", r.tag);
            Assert.AreEqual("unity-button", r.ussClass);
        }

        [Test]
        public void PlainFrameNamedLikeControl_DoesNotMisfire()
        {
            var ctx = Context(Node(-1, "FRAME", "Slider Row"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
        }

        [Test]
        public void PluginDataTag_Wins()
        {
            var ctx = Context(Node(-1, "FRAME", "whatever", control: "Toggle"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("Toggle", r.tag);
        }

        [TestCase("My.Evil\" style=\"color:red\" x=\"")]
        [TestCase("Ns\">< script")]
        [TestCase("My.<Evil>")]
        [TestCase("1Bad.Control")]
        public void ControlNameThatIsNotAnXmlName_FallsBackToVisualElement(string control)
        {
            var ctx = Context(Node(-1, "FRAME", "whatever", control: control));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
        }

        [Test]
        public void QualifiedForeignControl_StillResolves()
        {
            var ctx = Context(Node(-1, "FRAME", "whatever", control: "Some.Team.UI.FancyButton"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("FancyButton", r.tag);
            Assert.AreEqual("Some.Team.UI", r.ns);
        }

        [Test]
        public void ClickableFrame_NoLongerButton_StaysVisualElement()
        {
            var ctx = Context(
                Node(-1, "FRAME", "wired", css: Css("border", "2px solid #424242"), clickable: true),
                Node(0, "TEXT", "t", text: "Enter your name here"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
        }

        [Test]
        public void ClickableMultiChild_StaysContainer()
        {
            var ctx = Context(
                Node(-1, "FRAME", "card", css: Css("background-color", "#2f2f2f"), clickable: true),
                Node(0, "TEXT", "a", text: "Title"),
                Node(0, "TEXT", "b", text: "Body"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
        }

        [Test]
        public void Tag_ForcesButton()
        {
            var ctx = Context(
                Node(-1, "FRAME", "A", control: "Button", css: Css("background-color", "#0d99ff")),
                Node(0, "TEXT", "t", text: "Enter your name here"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("Button", r.tag);
        }

        [Test]
        public void Tag_ForcesTextField()
        {
            var ctx = Context(
                Node(-1, "FRAME", "B", control: "TextField", css: Css("border", "2px solid #424242")),
                Node(0, "TEXT", "t", text: "SUBMIT"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("TextField", r.tag);
        }

        [Test]
        public void SettingsControlMap_NameToEngineControl_Resolves()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "Fancy Thing", uitkControl = "Toggle" });
            try
            {
                var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Fancy Thing")), 0, settings);
                Assert.AreEqual("Toggle", r.tag);
                Assert.IsTrue(r.isControl);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_FullyQualifiedTarget_ResolvesForeign()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "Rating", uitkControl = "MyCompany.UI.StarBar" });
            try
            {
                var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Rating")), 0, settings);
                Assert.AreEqual("StarBar", r.tag);
                Assert.AreEqual("MyCompany.UI", r.ns);
                Assert.IsTrue(r.isControl);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void QualifiedNonCatalogEngineControl_Resolves()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "Qty", uitkControl = "UnityEngine.UIElements.IntegerField" });
            try
            {
                var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Qty")), 0, settings);
                Assert.AreEqual("IntegerField", r.tag);
                Assert.IsTrue(r.isControl);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void BareNonCatalogMappingName_ResolvesAsGlobalNamespaceControl()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "Qty", uitkControl = "IntegerField" });
            try
            {
                var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Qty")), 0, settings);
                Assert.IsTrue(r.isControl);
                Assert.AreEqual("IntegerField", r.tag);
                Assert.AreEqual(ControlMap.GlobalNamespace, r.ns);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void Tag_BareNonCatalogName_IsIgnored()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "whatever", control: "IntegerField")), 0, null);
            Assert.IsFalse(r.isControl);
        }

        [Test]
        public void Tag_Tab_ResolvesToTabControl()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "whatever", control: "Tab")), 0, null);
            Assert.AreEqual("Tab", r.tag);
            Assert.AreEqual("unity-tab", r.ussClass);
            Assert.AreEqual("label", r.captionAttr);
        }

        [Test]
        public void Tab_IsAContainer()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Overview", control: "Tab")), 0, null);
            Assert.IsTrue(r.container);
            Assert.IsFalse(ControlMap.IsLeafControlTag("Tab"));
        }

        [Test]
        public void Tag_TabView_ResolvesToContainer()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Tabs", control: "TabView")), 0, null);
            Assert.AreEqual("TabView", r.tag);
            Assert.AreEqual("unity-tab-view", r.ussClass);
            Assert.IsTrue(r.container);
        }

        [Test]
        public void TabTray_AllTabChildren_ResolvesToTabView()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Tab tray"),
                Node(0, "INSTANCE", "Tab"),
                Node(0, "INSTANCE", "Tab"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("TabView", r.tag);
            Assert.IsTrue(r.container);
        }

        [Test]
        public void MixedChildren_DoNotBecomeTabView()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Row"),
                Node(0, "INSTANCE", "Button"),
                Node(0, "INSTANCE", "Tab"));
            var r = ControlMap.Resolve(ctx, 0, null);
            Assert.AreEqual("VisualElement", r.tag);
        }

        [Test]
        public void Tag_RadioButtonGroup_ResolvesToContainer()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Choose", control: "RadioButtonGroup")), 0, null);
            Assert.AreEqual("RadioButtonGroup", r.tag);
            Assert.AreEqual("unity-radio-button-group", r.ussClass);
            Assert.IsTrue(r.container);
        }

        [Test]
        public void RadioButton_CaptionsIntoLabel()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "opt", control: "RadioButton")), 0, null);
            Assert.AreEqual("RadioButton", r.tag);
            Assert.AreEqual("label", r.captionAttr);
            Assert.IsFalse(r.container);
        }

        [Test]
        public void Tag_ToggleButtonGroup_ResolvesToContainer()
        {
            var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Format", control: "ToggleButtonGroup")), 0, null);
            Assert.AreEqual("ToggleButtonGroup", r.tag);
            Assert.AreEqual("unity-toggle-button-group", r.ussClass);
            Assert.IsTrue(r.container);
        }

        [Test]
        public void SettingsControlMap_Bindings_CarriedOntoResolution()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Volume",
                uitkControl = "SliderInt",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "low-value", source = FigmaBindingSource.FixedValue, value = "0" }
                }
            });
            try
            {
                var r = ControlMap.Resolve(Context(Node(-1, "FRAME", "Volume")), 0, settings);
                Assert.AreEqual("SliderInt", r.tag);
                Assert.IsNotNull(r.bindings);
                Assert.AreEqual(1, r.bindings.Length);
                Assert.AreEqual("low-value", r.bindings[0].uxmlAttribute);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_Bindings_EmitAsUxmlAttributes()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Volume",
                uitkControl = "SliderInt",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "low-value", source = FigmaBindingSource.FixedValue, value = "0" },
                    new PropertyBinding { uxmlAttribute = "high-value", source = FigmaBindingSource.FixedValue, value = "100" }
                }
            });
            try
            {
                var ctx = Context(Node(-1, "FRAME", "Volume", css: Css("display", "flex")));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("<ui:SliderInt", uxml);
                StringAssert.Contains("low-value=\"0\"", uxml);
                StringAssert.Contains("high-value=\"100\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void BareNonCatalogName_EmitsAGlobalNamespaceElement()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Widget", uitkControl = "GlobalChip",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "label", source = FigmaBindingSource.DesignText }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Widget"),
                    Node(1, "TEXT", "Lbl", text: "Go"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("<GlobalChip", uxml);
                StringAssert.Contains("label=\"Go\"", uxml);
                StringAssert.DoesNotContain("ui:GlobalChip", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void PropertyRows_AbsorbTheirDrivenLayers()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Hero",
                uitkControl = "Some.Team.UI.Hero",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "title-text", source = FigmaBindingSource.ComponentProperty, value = "Title" },
                    new PropertyBinding { uxmlAttribute = "body-text", source = FigmaBindingSource.ComponentProperty, value = "Body" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "Hero", componentKey: "kh",
                        props: Props("Title#1", "TEXT", "Big", "Body#2", "TEXT", "Lorem")),
                    Node(1, "TEXT", "TitleL", text: "Big", charProp: "Title#1"),
                    Node(1, "TEXT", "BodyL", text: "Lorem", charProp: "Body#2"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("title-text=\"Big\"", uxml);
                StringAssert.Contains("body-text=\"Lorem\"", uxml);
                StringAssert.DoesNotContain("<ui:Label", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void AbsorbedPropertyLayers_ConsumeTemplateTextSlots()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Widget",
                uitkControl = "Some.Team.UI.Hero",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "title-text", source = FigmaBindingSource.ComponentProperty, value = "Title" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Card"),
                    Node(0, "INSTANCE", "Widget", componentKey: "kw",
                        props: Props("Title#1", "TEXT", "Go")),
                    Node(1, "TEXT", "Lbl", text: "Go", charProp: "Title#1"),
                    Node(0, "TEXT", "Tail", text: "After"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var template = new FigmaComponentLibrary.Template { name = "card", rootIndex = 0, textCount = 2 };
                var uxml = FigmaToUxml.BuildTemplate(ctx, "screen.uss", null, settings, library, template);

                StringAssert.Contains("title-text=\"Go\"", uxml);
                // The absorbed text consumed slot 0, so the trailing text stays aligned at slot 1.
                StringAssert.Contains("name=\"text-1\"", uxml);
                StringAssert.DoesNotContain("name=\"text-0\"", uxml);
                StringAssert.DoesNotContain(" text=\"Go\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void LegacyCaptionAndIconFields_MigrateToDesignRows()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            try
            {
                var json = "{\"controlMap\":[{\"figmaName\":\"Chip\",\"uitkControl\":\"Some.Team.UI.Chip\","
                    + "\"captionAttribute\":\"label\",\"iconAttribute\":\"glyph\",\"bindings\":[]}]}";
                JsonUtility.FromJsonOverwrite(json, settings);

                var entry = settings.controlMap[0];
                Assert.IsEmpty(entry.captionAttribute);
                Assert.IsEmpty(entry.iconAttribute);
                Assert.AreEqual(2, entry.bindings.Length);
                Assert.AreEqual("label", entry.bindings[0].uxmlAttribute);
                Assert.AreEqual(FigmaBindingSource.DesignText, entry.bindings[0].source);
                Assert.AreEqual("glyph", entry.bindings[1].uxmlAttribute);
                Assert.AreEqual(FigmaBindingSource.DesignIcon, entry.bindings[1].source);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void CanonicalizeTypedControl_ReachesDiscoveredEngineControls()
        {
            Assert.AreEqual("Button", ControlMappingDrawer.CanonicalizeTypedControl("button"));
            Assert.AreEqual("UnityEngine.UIElements.IntegerField",
                ControlMappingDrawer.CanonicalizeTypedControl("integerfield"));
            Assert.IsNull(ControlMappingDrawer.CanonicalizeTypedControl("NoSuchControlAnywhere"));
        }

        [Test]
        public void CanonicalCatalogTag_ResolvesBareNamesAndRejectsForeign()
        {
            Assert.AreEqual("Image", ControlMap.CanonicalCatalogTag("image"));
            Assert.AreEqual("TabView", ControlMap.CanonicalCatalogTag("tab-view"));
            Assert.IsNull(ControlMap.CanonicalCatalogTag("Test"));
            Assert.IsNull(ControlMap.CanonicalCatalogTag(""));
        }

        [Test]
        public void CaptionAttrFor_Catalog_And_Foreign()
        {
            Assert.AreEqual("text", ControlMap.CaptionAttrFor("Button"));
            Assert.AreEqual(string.Empty, ControlMap.CaptionAttrFor("Slider"));
            Assert.AreEqual(string.Empty, ControlMap.CaptionAttrFor("MyCompany.UI.Widget"));
        }

        [Test]
        public void SettingsControlMap_Bindings_SkipCaptionCollision()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Cta",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "text", source = FigmaBindingSource.FixedValue, value = "X" },
                    new PropertyBinding { uxmlAttribute = "enable-rich-text", source = FigmaBindingSource.FixedValue, value = "true" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Cta", css: Css("display", "flex")),
                    Node(0, "TEXT", "label", text: "Go"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("<ui:Button", uxml);
                StringAssert.Contains("text=\"Go\"", uxml);
                StringAssert.DoesNotContain("text=\"X\"", uxml);
                StringAssert.Contains("enable-rich-text=\"true\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_Bindings_CaptionConstantIsTheFallbackWhenNoTextExists()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Cta",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "text", source = FigmaBindingSource.FixedValue, value = "X" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Cta"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("<ui:Button", uxml);
                StringAssert.Contains("text=\"X\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_Bindings_IconLayerWinsOverIconConstant()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Save",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "icon-image", source = FigmaBindingSource.FixedValue, value = "Assets/fallback.png" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Save"),
                    Node(1, "TEXT", "Label", text: "Save"),
                    Node(1, "RECTANGLE", "Glyph", raster: true));
                ctx.ImageUriByIndex = new string[ctx.Nodes.Length];
                ctx.ImageUriByIndex[3] = "project://database/Assets/glyph.png?fileID=1&guid=abc&type=3#glyph";

                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);

                StringAssert.Contains("icon-image=\"project://database/Assets/glyph.png", uxml);
                StringAssert.DoesNotContain("Assets/fallback.png", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_Bindings_IconConstantIsTheFallbackWhenNoIconLayer()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Save",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "icon-image", source = FigmaBindingSource.FixedValue, value = "Assets/fallback.png" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Save"),
                    Node(1, "TEXT", "Label", text: "Save"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("icon-image=\"Assets/fallback.png\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_Bindings_GeneratorAttributesNeverEmit()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Cta",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "class", source = FigmaBindingSource.FixedValue, value = "hijack" },
                    new PropertyBinding { uxmlAttribute = "style", source = FigmaBindingSource.FixedValue, value = "color: red" },
                    new PropertyBinding { uxmlAttribute = "name", source = FigmaBindingSource.FixedValue, value = "hijack" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Cta"),
                    Node(1, "TEXT", "Label", text: "Go"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.DoesNotContain("hijack", uxml);
                StringAssert.DoesNotContain("color: red", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SettingsControlMap_MatchIgnoresPunctuationSpacingAndCase()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "my-button", uitkControl = "Toggle" });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "My Button"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("<ui:Toggle", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SingleTextProperty_NamesTheCaption()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "My Button", uitkControl = "Button" });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "My Button", componentKey: "k1",
                        props: Props("Label#1:2", "TEXT", "Go"),
                        textOverrides: new[] { "First", "Go" }),
                    Node(1, "TEXT", "First", text: "First"),
                    Node(1, "TEXT", "Lbl", text: "Go", charProp: "Label#1:2"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("text=\"Go\"", uxml);
                StringAssert.DoesNotContain("text=\"First\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void CaptionRow_PicksTheNamedProperty()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "My Button",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "text", source = FigmaBindingSource.ComponentProperty, value = "Badge" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "My Button", componentKey: "k1",
                        props: Props("Label#1", "TEXT", "Go", "Badge#2", "TEXT", "3"),
                        textOverrides: new[] { "Go", "3" }),
                    Node(1, "TEXT", "Lbl", text: "Go", charProp: "Label#1"),
                    Node(1, "TEXT", "Count", text: "3", charProp: "Badge#2"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("text=\"3\"", uxml);
                StringAssert.DoesNotContain("text=\"Go\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void TwoTextProperties_WithoutARow_FallBackToPositional()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "My Button", uitkControl = "Button" });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "My Button", componentKey: "k1",
                        props: Props("Label#1", "TEXT", "Go", "Badge#2", "TEXT", "3"),
                        textOverrides: new[] { "Go", "3" }),
                    Node(1, "TEXT", "Lbl", text: "Go", charProp: "Label#1"),
                    Node(1, "TEXT", "Count", text: "3", charProp: "Badge#2"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("text=\"Go\"", uxml);
                StringAssert.DoesNotContain("text=\"3\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void NestedTextComponentProperty_NamesTheCaption()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "My Button", uitkControl = "Button" });
            try
            {
                // The label is a nested typography component; its TEXT property lives on the nested
                // instance, and a badge text sits ahead of it in document order.
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "My Button", componentKey: "k1",
                        textOverrides: new[] { "3", "Go" }),
                    Node(1, "TEXT", "Badge", text: "3"),
                    Node(1, "INSTANCE", "Text", componentKey: "ktext",
                        props: Props("Content#5", "TEXT", "Go")),
                    Node(3, "TEXT", "Label", text: "Go", charProp: "Content#5"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("text=\"Go\"", uxml);
                StringAssert.DoesNotContain("text=\"3\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void HiddenIconLayer_IsNotAbsorbedIntoTheIconSlot()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Save", control: "Button"),
                Node(1, "TEXT", "Label", text: "Save"),
                Node(1, "RECTANGLE", "Glyph", raster: true, css: Css("display", "none")));
            ctx.ImageUriByIndex = new string[ctx.Nodes.Length];
            ctx.ImageUriByIndex[3] = "project://database/Assets/glyph.png?fileID=1&guid=abc&type=3#glyph";

            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("text=\"Save\"", uxml);
            StringAssert.DoesNotContain("icon-image", uxml);
        }

        [Test]
        public void PropertyDrivingNoLayer_DoesNotOverrideRealText()
        {
            // A typography instance whose realized variant is not linked to the TEXT property: the
            // property still rides the instance at its declared default, but the characters are a plain
            // override. The default must not replace them.
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "INSTANCE", "Text", componentKey: "ktext",
                    props: Props("Text#1", "TEXT", "Label"),
                    textOverrides: new[] { "HEADER" }),
                Node(1, "TEXT", "Label", text: "HEADER"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);
            StringAssert.Contains("text=\"HEADER\"", uxml);
            StringAssert.DoesNotContain("text=\"Label\"", uxml);
        }

        [Test]
        public void BooleanProperty_EmitsIntoTheBoundAttribute()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Cta",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "enable-rich-text", source = FigmaBindingSource.ComponentProperty, value = "Rich" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "Cta", componentKey: "k1",
                        props: Props("Rich#9", "BOOLEAN", "true"),
                        textOverrides: new[] { "Go" }),
                    Node(1, "TEXT", "Lbl", text: "Go"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.Contains("enable-rich-text=\"true\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void MissingProperty_EmitsNothing()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Cta",
                uitkControl = "Button",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "enable-rich-text", source = FigmaBindingSource.ComponentProperty, value = "Nope" }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "INSTANCE", "Cta", componentKey: "k1", textOverrides: new[] { "Go" }),
                    Node(1, "TEXT", "Lbl", text: "Go"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);
                StringAssert.DoesNotContain("enable-rich-text", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void MappedCustomControl_AbsorbsAnIconIntoTheAttributeItNames()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Chip", uitkControl = "Some.Team.UI.Chip",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "label", source = FigmaBindingSource.DesignText },
                    new PropertyBinding { uxmlAttribute = "glyph", source = FigmaBindingSource.DesignIcon }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Chip"),
                    Node(1, "TEXT", "lbl", text: "Draft"),
                    Node(1, "RECTANGLE", "Glyph", raster: true));
                ctx.ImageUriByIndex = new string[ctx.Nodes.Length];
                ctx.ImageUriByIndex[3] = "project://database/Assets/g.png?fileID=1&guid=abc&type=3#g";

                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);

                StringAssert.Contains("label=\"Draft\"", uxml);
                StringAssert.Contains("glyph=\"project://database/Assets/g.png?fileID=1&amp;guid=abc&amp;type=3#g\"", uxml);
                StringAssert.DoesNotContain("Glyph", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void Button_AbsorbsARasterChildAsItsIcon()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Save", control: "Button"),
                Node(1, "TEXT", "Label", text: "Save"),
                Node(1, "RECTANGLE", "Glyph", raster: true));
            ctx.ImageUriByIndex = new string[ctx.Nodes.Length];
            ctx.ImageUriByIndex[3] = "project://database/Assets/glyph.png?fileID=1&guid=abc&type=3#glyph";

            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("text=\"Save\"", uxml);
            StringAssert.Contains("icon-image=\"project://database/Assets/glyph.png?fileID=1&amp;guid=abc&amp;type=3#glyph\"", uxml);
            StringAssert.DoesNotContain("</ui:Button>", uxml);
            StringAssert.DoesNotContain("Glyph", uxml);
        }

        [Test]
        public void Button_WithoutAnExportedIcon_DropsTheChildAsBefore()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Save", control: "Button"),
                Node(1, "TEXT", "Label", text: "Save"),
                Node(1, "RECTANGLE", "Glyph"));

            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("text=\"Save\"", uxml);
            StringAssert.DoesNotContain("icon-image", uxml);
            StringAssert.DoesNotContain("</ui:Button>", uxml);
        }

        [Test]
        public void ForeignControlWithACaptionAttribute_KeepsItsChildren()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping
            {
                figmaName = "Rating", uitkControl = "Some.Team.UI.StarBar",
                bindings = new[]
                {
                    new PropertyBinding { uxmlAttribute = "label", source = FigmaBindingSource.DesignText }
                }
            });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Rating"),
                    Node(1, "TEXT", "Value", text: "4.5"),
                    Node(1, "FRAME", "Stars"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);

                StringAssert.Contains("StarBar", uxml);
                StringAssert.Contains("</team:StarBar>", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void LeafControl_WithoutCaption_DropsChildren()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Volume", control: "SliderInt", css: Css("width", "200px")),
                Node(1, "TEXT", "Label", text: "Volume"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("<ui:SliderInt", uxml);
            StringAssert.DoesNotContain("</ui:SliderInt>", uxml);
            StringAssert.DoesNotContain("Volume\"", uxml);
        }

        [Test]
        public void ContainerControl_KeepsChildren()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Panel", control: "ScrollView", css: Css("width", "200px")),
                Node(1, "TEXT", "Label", text: "Row"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("</ui:ScrollView>", uxml);
            StringAssert.Contains("text=\"Row\"", uxml);
        }

        static string TagFor(string layerName)
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", layerName, componentKey: "k1"));
            return ControlMap.Resolve(ctx, 1, null).tag;
        }

        [Test]
        public void NameConvention_MatchesTheWholeName()
        {
            Assert.AreEqual("TabView", TagFor("TabView"));
            Assert.AreEqual("TabView", TagFor("Tab View"));
            Assert.AreEqual("TabView", TagFor("tab-view"));
            Assert.AreEqual("TextField", TagFor("TextField"));
            Assert.AreEqual("ToggleButtonGroup", TagFor("ToggleButtonGroup"));
            Assert.AreEqual("SliderInt", TagFor("SliderInt"));
            Assert.AreEqual("RadioButton", TagFor("RadioButton"));
            Assert.AreEqual("GroupBox", TagFor("GroupBox"));
        }

        [Test]
        public void NameConvention_MatchesAFolderSegment()
        {
            Assert.AreEqual("Button", TagFor("Button/Primary"));
            Assert.AreEqual("Toggle", TagFor("Controls/Toggle"));
        }

        [Test]
        public void NameConvention_DoesNotGuessFromAPhrase()
        {
            Assert.AreEqual("VisualElement", TagFor("Tab Bar"));
            Assert.AreEqual("VisualElement", TagFor("Slider Track"));
            Assert.AreEqual("VisualElement", TagFor("Play Button"));
            Assert.AreEqual("VisualElement", TagFor("Icon button"));
        }

        [Test]
        public void EveryCatalogControl_IsReachableByItsOwnName()
        {
            foreach (var name in ControlMap.CatalogControlNames)
                Assert.AreEqual(name, TagFor(name), name + " is not reachable by its own name");
        }

        [Test]
        public void ContainerControlFromTheLibrary_KeepsChildren()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "INSTANCE", "TabView", componentKey: "k1", masterKey: "k1", masterName: "TabView"),
                Node(1, "FRAME", "Overview", control: "Tab"),
                Node(2, "TEXT", "Label", text: "Hi"),
                Node(-1, "COMPONENT", "TabView", componentKey: "k1", master: true),
                Node(4, "FRAME", "Overview", control: "Tab"),
                Node(5, "TEXT", "Label", text: "Hi"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            Assert.IsTrue(library.TryGetControl("k1", out var control), "the master resolves to a control");
            Assert.IsTrue(control.container);
            StringAssert.Contains("</ui:TabView>", uxml);
            StringAssert.Contains("<ui:Tab", uxml);
        }

        [Test]
        public void ContainerWhoseWholeContentIsOneText_TakesItAsTheHeader()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Tab", control: "Tab"),
                Node(1, "INSTANCE", "Text"),
                Node(2, "TEXT", "Label", text: "ACTIVE TAB"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("label=\"ACTIVE TAB\"", uxml);
            StringAssert.DoesNotContain("label=\"Tab\"", uxml, "the layer name is not the caption");
            StringAssert.DoesNotContain("</ui:Tab>", uxml, "the text was the whole content, so no page is left");
        }

        [Test]
        public void ContainerWithRealContent_KeepsItAndNamesItselfFromTheLayer()
        {
            var ctx = Context(
                Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                Node(0, "FRAME", "Overview", control: "Tab"),
                Node(1, "TEXT", "Heading", text: "Stats"),
                Node(1, "TEXT", "Body", text: "All time"));
            var library = FigmaComponentLibrary.Build(ctx, null, "Screen");
            var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, null, library);

            StringAssert.Contains("label=\"Overview\"", uxml);
            StringAssert.Contains("</ui:Tab>", uxml);
            StringAssert.Contains("text=\"Stats\"", uxml);
        }

        [Test]
        public void ForeignControl_KeepsChildren()
        {
            var settings = ScriptableObject.CreateInstance<FigmaImportSettings>();
            settings.controlMap.Add(new ControlMapping { figmaName = "Panel", uitkControl = "MyCompany.UI.Card" });
            try
            {
                var ctx = Context(
                    Node(-1, "FRAME", "Screen", css: Css("display", "flex")),
                    Node(0, "FRAME", "Panel", css: Css("width", "200px")),
                    Node(1, "TEXT", "Label", text: "Row"));
                var library = FigmaComponentLibrary.Build(ctx, settings, "Screen");
                var uxml = FigmaToUxml.BuildScreen(ctx, "screen.uss", null, settings, library);

                StringAssert.Contains("text=\"Row\"", uxml);
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
