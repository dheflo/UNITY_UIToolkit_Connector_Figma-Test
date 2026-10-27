using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaTokenDiffTests
    {
        [Test]
        public void ParseRoot_ReadsCustomProperties()
        {
            var uss = ":root {\n    --color-primary: rgba(1, 2, 3, 1);\n    --radius-lg: 20px;\n}\n";
            var map = FigmaTokenDiff.ParseRoot(uss);
            Assert.AreEqual("rgba(1, 2, 3, 1)", map["--color-primary"]);
            Assert.AreEqual("20px", map["--radius-lg"]);
        }

        [Test]
        public void Compute_DetectsOnlyChangedTokens()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--color-primary", id = "V:1", type = "COLOR", value = "rgba(1, 2, 3, 1)" },
                new FigmaTokenRecord { name = "--radius-lg", id = "V:2", type = "FLOAT", value = "20px" }
            };
            var edited = ":root {\n    --color-primary: rgba(9, 9, 9, 1);\n    --radius-lg: 20px;\n}\n";
            var changes = FigmaTokenDiff.Compute(records, edited);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("V:1", changes[0].id);
            Assert.AreEqual("rgba(9, 9, 9, 1)", changes[0].value);
        }

        [Test]
        public void Compute_IgnoresWhitespaceOnlyDifferences()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--c", id = "V:1", type = "COLOR", value = "rgba(1, 2, 3, 1)" }
            };
            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --c: rgba(1,2,3,1); }").Count);
        }

        [Test]
        public void Compute_TreatsRgbAndRgbaAsEqual()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--fill-primary", id = "V:1", type = "COLOR", value = "rgb(255, 100, 60)" }
            };
            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --fill-primary: rgba(255, 100, 60, 1); }").Count);
        }

        [Test]
        public void Compute_IgnoresAlphaFormatting()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--c", id = "V:1", type = "COLOR", value = "rgba(255, 255, 255, 1.0)" }
            };
            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --c: rgba(255, 255, 255, 1); }").Count);
        }

        [Test]
        public void Compute_DetectsRealColorChangeAcrossFormats()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--c", id = "V:1", type = "COLOR", value = "rgb(255, 100, 60)" }
            };
            var changes = FigmaTokenDiff.Compute(records, ":root { --c: rgba(255, 100, 61, 1); }");
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("rgba(255, 100, 61, 1)", changes[0].value);
        }

        [Test]
        public void RecordsFrom_SanitizesNameAndKeepsId()
        {
            var vars = new[]
            {
                new FigmaVariable { id = "V:1", name = "color/primary", type = "COLOR", value = "rgba(1, 2, 3, 1)" }
            };
            var records = FigmaTokenDiff.RecordsFrom(vars);
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("--color-primary", records[0].name);
            Assert.AreEqual("V:1", records[0].id);
        }

        [Test]
        public void Compute_ResolvesAliasRepointToTargetId()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--black", id = "V:black", type = "COLOR", value = "rgba(0, 0, 0, 1)" },
                new FigmaTokenRecord { name = "--white", id = "V:white", type = "COLOR", value = "rgba(255, 255, 255, 1)" },
                new FigmaTokenRecord { name = "--primary", id = "V:primary", type = "COLOR", value = "var(--black)" }
            };
            var edited = ":root {\n  --black: rgba(0, 0, 0, 1);\n  --white: rgba(255, 255, 255, 1);\n  --primary: var(--white);\n}";
            var changes = FigmaTokenDiff.Compute(records, edited);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("V:primary", changes[0].id);
            Assert.AreEqual("var(--white)", changes[0].value);
            Assert.AreEqual("V:white", changes[0].aliasId, "re-point resolves the target token's Figma id");
        }

        [Test]
        public void Compute_LiteralEdit_HasNoAliasId()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--c", id = "V:1", type = "COLOR", value = "rgba(1, 2, 3, 1)" }
            };
            var changes = FigmaTokenDiff.Compute(records, ":root { --c: rgba(9, 9, 9, 1); }");

            Assert.AreEqual(1, changes.Count);
            Assert.IsTrue(string.IsNullOrEmpty(changes[0].aliasId), "a literal edit carries no aliasId");
        }

        [Test]
        public void Compute_NamedColorMatchesTheImportedHex()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--surface", id = "V:s", type = "COLOR", value = "#FFFFFF" },
                new FigmaTokenRecord { name = "--overlay", id = "V:o", type = "COLOR", value = "rgba(0, 0, 0, 0)" }
            };

            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --surface: white; --overlay: transparent; }").Count,
                "a re-serialized name must not push a phantom color into the designer's file");
            Assert.AreEqual(1, FigmaTokenDiff.Compute(records, ":root { --surface: black; }").Count,
                "a real change still registers");
        }

        [Test]
        public void Compute_HexAndRgbSpellingsAreTheSameValue()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--c", id = "V:c", type = "COLOR", value = "rgba(255, 255, 255, 1)" }
            };

            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --c: #FFFFFF; }").Count);
            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --c: #FFF; }").Count);
            Assert.AreEqual(1, FigmaTokenDiff.Compute(records, ":root { --c: #FEFEFE; }").Count);
        }

        [Test]
        public void Compute_NumericTokenSpellingsAreTheSameValue()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--radius", id = "V:radius", type = "FLOAT", value = "16px" }
            };

            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --radius: 16; }").Count);
            Assert.AreEqual(0, FigmaTokenDiff.Compute(records, ":root { --radius: 16.00px; }").Count);
            Assert.AreEqual(1, FigmaTokenDiff.Compute(records, ":root { --radius: 18px; }").Count);
        }

        [Test]
        public void Compute_RepointToUnknownToken_HasNoAliasId()
        {
            var records = new List<FigmaTokenRecord>
            {
                new FigmaTokenRecord { name = "--primary", id = "V:primary", type = "COLOR", value = "var(--black)" }
            };
            var changes = FigmaTokenDiff.Compute(records, ":root { --primary: var(--ghost); }");

            Assert.AreEqual(1, changes.Count);
            Assert.IsTrue(string.IsNullOrEmpty(changes[0].aliasId));
        }
    }
}
