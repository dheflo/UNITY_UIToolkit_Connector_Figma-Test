using System;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaStringsTests
    {
        [Test]
        public void EveryNoteKind_FormatsWithCountAndItems([Values] FigmaImportNoteKind kind)
        {
            var note = new FigmaImportNote
            {
                kind = kind,
                count = 3,
                items = new[] { "Alpha", "Beta", "Gamma" },
                counts = new[] { 1, 2, 3 },
                extra = new[] { "x", "y", "z" }
            };
            var text = FigmaStrings.Format(note);

            Assert.IsNotEmpty(text);
            Assert.AreNotEqual(kind.ToString(), text, "kind has no template");
            StringAssert.DoesNotContain("{", text);
            if (kind != FigmaImportNoteKind.MissingFonts && kind != FigmaImportNoteKind.TokenCollision)
                StringAssert.Contains("3", text);
            if (kind != FigmaImportNoteKind.ImagePending && kind != FigmaImportNoteKind.DroppedGap
                && kind != FigmaImportNoteKind.DroppedShadows && kind != FigmaImportNoteKind.DroppedBackgroundBlur
                && kind != FigmaImportNoteKind.DroppedGradients && kind != FigmaImportNoteKind.DroppedTextStrokes)
                StringAssert.Contains("Alpha", text);
        }

        [Test]
        public void LongLists_AreTruncatedWithACount()
        {
            var items = new string[9];
            for (var i = 0; i < items.Length; ++i)
                items[i] = "Item" + i;
            var text = FigmaStrings.Format(new FigmaImportNote
                { kind = FigmaImportNoteKind.MissingFonts, count = 9, items = items });

            StringAssert.Contains("Item5", text);
            StringAssert.DoesNotContain("Item6", text);
            StringAssert.Contains("+3 more", text);
        }

        [Test]
        public void SingularAndPlural_PickTheirOwnSentence()
        {
            var one = FigmaStrings.Format(new FigmaImportNote
                { kind = FigmaImportNoteKind.MappingDecay, count = 1, items = new[] { "A" } });
            var two = FigmaStrings.Format(new FigmaImportNote
                { kind = FigmaImportNoteKind.MappingDecay, count = 2, items = new[] { "A", "B" } });

            StringAssert.StartsWith("1 mapping matched", one);
            StringAssert.StartsWith("2 mappings matched", two);
        }

        [Test]
        public void DroppedLayers_NameEachOwnerWithItsCount()
        {
            var text = FigmaStrings.Format(new FigmaImportNote
            {
                kind = FigmaImportNoteKind.DiscardedLayers,
                count = 2,
                items = new[] { "Home", "Save" },
                counts = new[] { 1, 2 }
            });

            StringAssert.Contains("Home (1 layer)", text);
            StringAssert.Contains("Save (2 layers)", text);
        }

        [Test]
        public void MissingFonts_AreTheWarnings()
        {
            foreach (FigmaImportNoteKind kind in Enum.GetValues(typeof(FigmaImportNoteKind)))
                Assert.AreEqual(kind == FigmaImportNoteKind.MissingFonts,
                    FigmaStrings.IsWarning(new FigmaImportNote { kind = kind }), kind.ToString());
        }

        // A reason without its own sentence would fall through to the generic one and misreport.
        [Test]
        public void EverySyncWarningReason_HasItsOwnSentence()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (FigmaSyncWarningReason reason in Enum.GetValues(typeof(FigmaSyncWarningReason)))
            {
                var text = FigmaStrings.Format(new FigmaSyncWarning { reason = reason, props = new[] { "p" } });
                Assert.IsTrue(seen.Add(text), reason + " shares its sentence with another reason");
            }
        }

        [Test]
        public void SyncWarning_ListsPropsAndReason()
        {
            var text = FigmaStrings.Format(new FigmaSyncWarning
            {
                reason = FigmaSyncWarningReason.InstanceLockedLayout,
                props = new[] { "width", "height" }
            });

            Assert.AreEqual("Not synced: width, height (Figma locks layout inside instances; edit the component)", text);
        }

        [Test]
        public void Counts_UseSingularAndPlural()
        {
            Assert.AreEqual("1 change", FigmaStrings.ChangeCount(1));
            Assert.AreEqual("2 changes", FigmaStrings.ChangeCount(2));
            Assert.AreEqual("1 change in 2 files - Push to send to Figma.", FigmaStrings.SyncHead(1, 2, true));
            Assert.AreEqual("Active mapping: (none)", FigmaStrings.ActiveMapping(null));
        }
    }
}
