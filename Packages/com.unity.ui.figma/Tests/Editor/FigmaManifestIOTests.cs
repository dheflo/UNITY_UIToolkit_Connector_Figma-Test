using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaManifestIOTests
    {
        string m_Dir;
        string m_Path;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(Path.GetTempPath(), "FigmaManifestIOTests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(m_Dir);
            m_Path = Path.Combine(m_Dir, ".figma-manifest.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir))
                Directory.Delete(m_Dir, true);
        }

        static FigmaSyncManifest Manifest(string assetName)
        {
            return new FigmaSyncManifest
            {
                fileName = "File",
                screens = new List<FigmaScreenManifest>
                {
                    new FigmaScreenManifest { assetName = assetName, rootNodeId = "1:0" }
                }
            };
        }

        [Test]
        public void Write_RoundTrips()
        {
            FigmaManifestIO.Write(m_Path, Manifest("Screen"));

            var read = FigmaManifestIO.ReadFile(m_Path);
            Assert.IsNotNull(read);
            Assert.AreEqual("Screen", read.screens[0].assetName);
        }

        [Test]
        public void Write_LeavesNoTempFileBehind()
        {
            FigmaManifestIO.Write(m_Path, Manifest("Screen"));
            FigmaManifestIO.Write(m_Path, Manifest("Screen2"));

            Assert.IsFalse(File.Exists(m_Path + ".tmp"), "the temp file is consumed by the replace");
        }

        [Test]
        public void Rewrite_KeepsThePreviousCopyAsBackup()
        {
            FigmaManifestIO.Write(m_Path, Manifest("First"));
            FigmaManifestIO.Write(m_Path, Manifest("Second"));

            Assert.IsTrue(File.Exists(m_Path + ".bak"));
            Assert.AreEqual("Second", FigmaManifestIO.ReadFile(m_Path).screens[0].assetName);
        }

        [Test]
        public void CorruptManifest_RecoversFromBackup()
        {
            FigmaManifestIO.Write(m_Path, Manifest("First"));
            FigmaManifestIO.Write(m_Path, Manifest("Second"));
            File.WriteAllText(m_Path, "{ truncated"); // an interrupted write

            LogAssert.ignoreFailingMessages = true;
            var read = FigmaManifestIO.ReadFile(m_Path);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(read, "a corrupt manifest must not read as 'no baseline'");
            Assert.AreEqual("First", read.screens[0].assetName, "the backup carries the previous baseline");
        }

        [Test]
        public void MissingManifest_IsNull()
        {
            Assert.IsNull(FigmaManifestIO.ReadFile(m_Path));
        }
    }
}
