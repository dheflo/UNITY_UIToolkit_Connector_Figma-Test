using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaImportReportStoreTests
    {
        [Test]
        public void SaveThenLoad_RoundTripsFields()
        {
            FigmaImportReportStore.Save("MyScreen", 42, ok: true, ussErrors: false,
                new List<FigmaImportNote>
                {
                    new FigmaImportNote { kind = FigmaImportNoteKind.MissingFonts, count = 1, items = new[] { "Inter" } },
                    new FigmaImportNote { kind = FigmaImportNoteKind.ImagePending, count = 3 }
                });

            var report = FigmaImportReportStore.Load();
            Assert.IsNotNull(report);
            Assert.AreEqual("MyScreen", report.screen);
            Assert.AreEqual(42, report.nodes);
            Assert.IsTrue(report.ok);
            Assert.IsFalse(report.ussErrors);
            Assert.AreEqual(2, report.notes.Length);
            Assert.AreEqual(FigmaImportNoteKind.MissingFonts, report.notes[0].kind);
            Assert.AreEqual(new[] { "Inter" }, report.notes[0].items);
            Assert.AreEqual(3, report.notes[1].count);
            Assert.IsFalse(string.IsNullOrEmpty(report.when));
        }

        [Test]
        public void Load_ReturnsNull_WhenNothingSaved()
        {
            FigmaImportReportStore.Clear();
            Assert.IsNull(FigmaImportReportStore.Load());
        }
    }
}
