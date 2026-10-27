using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaTokensTests
    {
        [Test]
        public void SanitizeTokenName_CollapsesSeparators()
        {
            Assert.AreEqual("--color-primary", FigmaTokens.SanitizeTokenName("color/primary"));
            Assert.AreEqual("--spacing-large", FigmaTokens.SanitizeTokenName("Spacing / Large"));
        }

        [Test]
        public void BuildTokensUss_EmitsRootBlock()
        {
            var vars = new[]
            {
                new FigmaVariable { name = "color/primary", type = "COLOR", value = "rgba(13, 153, 255, 1)" },
                new FigmaVariable { name = "radius/lg", type = "FLOAT", value = "20px" }
            };
            var uss = FigmaTokens.BuildTokensUss(vars);
            StringAssert.Contains(":root {", uss);
            StringAssert.Contains("--color-primary: rgba(13, 153, 255, 1);", uss);
            StringAssert.Contains("--radius-lg: 20px;", uss);
        }

        [Test]
        public void BuildTokensUss_EmptyWhenNoVariables()
        {
            Assert.AreEqual(string.Empty, FigmaTokens.BuildTokensUss(new FigmaVariable[0]));
        }

        [Test]
        public void ModeFileName_SlugsMode()
        {
            Assert.AreEqual("dark.uss", FigmaTokens.ModeFileName("Dark"));
            Assert.AreEqual("high-contrast.uss", FigmaTokens.ModeFileName("High Contrast"));
        }

        [Test]
        public void BuildModeUss_EmitsThatModesValues()
        {
            var modeVars = new[]
            {
                new FigmaModeVariable
                {
                    id = "v1", name = "Background/Primary", type = "COLOR",
                    values = new[]
                    {
                        new FigmaVariableMode { mode = "Dark", value = "rgba(0, 0, 0, 1)", aliasOf = "neutral-000" },
                        new FigmaVariableMode { mode = "Light", value = "rgba(255, 255, 255, 1)", aliasOf = "neutral-250" }
                    }
                }
            };

            var dark = FigmaTokens.BuildModeUss(modeVars, "Dark");
            StringAssert.Contains("--background-primary: var(--neutral-000);", dark);

            var light = FigmaTokens.BuildModeUss(modeVars, "Light");
            StringAssert.Contains("--background-primary: var(--neutral-250);", light);
        }

        [Test]
        public void BuildModeUss_EmptyWhenNoModeVariables()
        {
            Assert.AreEqual(string.Empty, FigmaTokens.BuildModeUss(new FigmaModeVariable[0], "Dark"));
        }

        static FigmaVariable Color(string name, string value)
            => new FigmaVariable { id = "V:" + name, name = name, type = "COLOR", value = value };

        [Test]
        public void Collisions_ReportsNamesThatCollapseOntoOneToken()
        {
            var variables = new[]
            {
                Color("color/primary", "rgba(1, 1, 1, 1)"),
                Color("color primary", "rgba(2, 2, 2, 1)"),
                Color("spacing/small", "4px")
            };

            var collisions = FigmaTokens.Collisions(variables);

            Assert.AreEqual(1, collisions.Count, "only the colliding pair is reported");
            CollectionAssert.AreEquivalent(new[] { "color/primary", "color primary" }, collisions["--color-primary"]);

            var note = FigmaStrings.Format(FigmaImportReport.TokenCollisionNote(collisions).Value);
            StringAssert.Contains("--color-primary", note);
            StringAssert.Contains("color primary", note);
        }

        [Test]
        public void Collisions_SilentWhenNamesAreDistinct()
        {
            var variables = new[] { Color("color/primary", "rgba(1, 1, 1, 1)"), Color("color/secondary", "rgba(2, 2, 2, 1)") };

            Assert.AreEqual(0, FigmaTokens.Collisions(variables).Count);
            Assert.IsNull(FigmaImportReport.TokenCollisionNote(FigmaTokens.Collisions(variables)));
        }
    }
}
