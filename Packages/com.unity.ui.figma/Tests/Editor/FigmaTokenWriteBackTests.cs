using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaTokenWriteBackTests
    {
        [SetUp]
        public void ClearQueue()
        {
            FigmaTokenWriteBack.Clear();
        }

        [Test]
        public void Enqueue_Pending_Ack_RoundTrip()
        {
            FigmaTokenWriteBack.Enqueue(new[]
            {
                new FigmaTokenChange { id = "V:1", name = "--a", type = "COLOR", value = "rgba(1,1,1,1)", file = "F" },
                new FigmaTokenChange { id = "V:2", name = "--b", type = "FLOAT", value = "8px", file = "F" }
            });
            Assert.AreEqual(2, FigmaTokenWriteBack.Count);

            FigmaTokenWriteBack.Ack(new[] { "F|V:1" });
            var pending = FigmaTokenWriteBack.Pending();
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("V:2", pending[0].id);
        }

        [Test]
        public void Enqueue_LatestWinsPerVariable()
        {
            FigmaTokenWriteBack.Enqueue(new[] { new FigmaTokenChange { id = "V:1", value = "8px", file = "F" } });
            FigmaTokenWriteBack.Enqueue(new[] { new FigmaTokenChange { id = "V:1", value = "12px", file = "F" } });
            Assert.AreEqual(1, FigmaTokenWriteBack.Count);
            Assert.AreEqual("12px", FigmaTokenWriteBack.Pending()[0].value);
        }

        [Test]
        public void Ack_IsFileScoped_LeavesOtherFileQueued()
        {
            FigmaTokenWriteBack.Enqueue(new[]
            {
                new FigmaTokenChange { id = "V:1", value = "8px", file = "FileA" },
                new FigmaTokenChange { id = "V:1", value = "9px", file = "FileB" }
            });
            Assert.AreEqual(2, FigmaTokenWriteBack.Count, "same id in two files are distinct changes");

            FigmaTokenWriteBack.Ack(new[] { "FileA|V:1" });

            var pending = FigmaTokenWriteBack.Pending();
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("FileB", pending[0].file, "acking FileA leaves FileB's same-id change queued");
        }

        const string k_Name = "FigmaWriteBackUnitTest";
        const string k_Folder = "Assets/Figma/" + k_Name;

        static string ImportOneToken()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0," +
                "\"variables\":[{\"id\":\"V:1\",\"name\":\"color/primary\",\"type\":\"COLOR\",\"value\":\"rgba(1, 2, 3, 1)\"}]," +
                "\"nodes\":[{\"id\":\"n:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\"," +
                    "\"varRefs\":[{\"k\":\"background-color\",\"v\":\"color/primary\"}]," +
                    "\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}]}";
            FigmaImportPipeline.Import(json);
            FigmaTokenWriteBack.Clear();
            var tokensPath = Path.Combine(Application.dataPath, "Figma", k_Name, "tokens.uss");
            Assert.IsTrue(File.Exists(tokensPath), "import should have written tokens.uss");
            return tokensPath;
        }

        static void CleanupImport()
        {
            FigmaTokenWriteBack.Clear();
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
        }

        [Test]
        public void Push_ExposesEdit_DetectionAloneDoesNot_AndDropsOnRevert()
        {
            try
            {
                var tokensPath = ImportOneToken();
                var original = File.ReadAllText(tokensPath);
                File.WriteAllText(tokensPath, original.Replace("rgba(1, 2, 3, 1)", "rgba(9, 9, 9, 1)"));

                Assert.IsTrue(FigmaTokenWriteBack.Scan().Exists(c => c.id == "V:1"), "edit should be detected by Scan");
                Assert.IsEmpty(FigmaTokenWriteBack.Reconcile(), "an unpushed edit must not be exposed to Figma");

                FigmaTokenWriteBack.Push(new[] { k_Name + "|V:1" });
                Assert.IsTrue(FigmaTokenWriteBack.Reconcile().Exists(c => c.id == "V:1" && c.value.Contains("9")),
                    "a pushed edit is exposed on reconcile");

                File.WriteAllText(tokensPath, original);
                Assert.IsFalse(FigmaTokenWriteBack.Reconcile().Exists(c => c.id == "V:1"),
                    "reverting a pushed edit drops it from the queue");
            }
            finally { CleanupImport(); }
        }

        [Test]
        public void Push_ThenAck_AdvancesBaseline_NoReappear()
        {
            try
            {
                var tokensPath = ImportOneToken();
                File.WriteAllText(tokensPath, File.ReadAllText(tokensPath).Replace("rgba(1, 2, 3, 1)", "rgba(9, 9, 9, 1)"));
                FigmaTokenWriteBack.Push(new[] { k_Name + "|V:1" });
                Assert.IsNotEmpty(FigmaTokenWriteBack.Reconcile(), "pushed change is queued");

                FigmaTokenWriteBack.Ack(new[] { k_Name + "|V:1" });

                Assert.IsEmpty(FigmaTokenWriteBack.Scan().FindAll(c => c.file == k_Name), "ack advanced the baseline; no diff");
                Assert.IsEmpty(FigmaTokenWriteBack.Reconcile(), "acked change does not reappear");
            }
            finally { CleanupImport(); }
        }

        [Test]
        public void Ack_UsesPushedSnapshotValue_NotCurrentFile()
        {
            try
            {
                var tokensPath = ImportOneToken();
                var original = File.ReadAllText(tokensPath);
                File.WriteAllText(tokensPath, original.Replace("rgba(1, 2, 3, 1)", "rgba(9, 9, 9, 1)"));
                FigmaTokenWriteBack.Push(new[] { k_Name + "|V:1" });

                File.WriteAllText(tokensPath, original.Replace("rgba(1, 2, 3, 1)", "rgba(5, 5, 5, 1)"));
                FigmaTokenWriteBack.Ack(new[] { k_Name + "|V:1" });

                Assert.IsTrue(FigmaTokenWriteBack.Scan().Exists(c => c.id == "V:1" && c.value.Contains("5")),
                    "the later edit survives as a fresh diff (ack used the pushed snapshot, not the current file)");
            }
            finally { CleanupImport(); }
        }
    }
}
