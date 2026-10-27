using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaSyncManifestTests
    {
        [Test]
        public void Orphans_ExcludesCurrentAndFilesOwnedByOtherScreens()
        {
            var prior = new List<string> { "a.uxml", "b.uxml", "images/x.png", "tokens.uss" };
            var current = new HashSet<string> { "a.uxml" };
            var protectedByOthers = new HashSet<string> { "tokens.uss" };
            CollectionAssert.AreEquivalent(new[] { "b.uxml", "images/x.png" },
                FigmaSyncManifest.Orphans(prior, current, protectedByOthers));
        }

        [Test]
        public void Orphans_NullPrior_IsEmpty()
        {
            CollectionAssert.IsEmpty(FigmaSyncManifest.Orphans(null, new HashSet<string>(), null));
        }

        [Test]
        public void ContentHash_IsStableAndSensitive()
        {
            Assert.AreEqual(FigmaSyncManifest.ContentHash("abc"), FigmaSyncManifest.ContentHash("abc"));
            Assert.AreNotEqual(FigmaSyncManifest.ContentHash("abc"), FigmaSyncManifest.ContentHash("abd"));
        }

        [Test]
        public void RemoveScreensByAsset_DropsMatchingEntriesOnly()
        {
            var manifest = new FigmaSyncManifest();
            manifest.screens.Add(new FigmaScreenManifest { assetName = "Keep", rootNodeId = "r:1" });
            manifest.screens.Add(new FigmaScreenManifest { assetName = "Gone", rootNodeId = "r:2" });
            manifest.screens.Add(new FigmaScreenManifest { assetName = "Gone", rootNodeId = "r:3" });

            Assert.AreEqual(2, manifest.RemoveScreensByAsset("Gone"), "both Gone entries removed");
            CollectionAssert.AreEquivalent(new[] { "Keep" }, manifest.screens.ConvertAll(s => s.assetName));
            Assert.AreEqual(0, manifest.RemoveScreensByAsset("Missing"), "unknown name is a no-op");
        }

        [Test]
        public void AdvanceTokenBaseline_RecordsAppliedValue()
        {
            var manifest = new FigmaSyncManifest();
            manifest.screens.Add(new FigmaScreenManifest
            {
                tokens = new List<FigmaTokenRecord>
                {
                    new FigmaTokenRecord { id = "v:1", name = "--a", value = "rgb(0, 0, 0)" }
                }
            });

            Assert.IsTrue(manifest.AdvanceTokenBaseline("v:1", "rgb(255, 0, 0)"));
            Assert.AreEqual("rgb(255, 0, 0)", manifest.screens[0].tokens[0].value);
            Assert.IsFalse(manifest.AdvanceTokenBaseline("v:1", "rgb(255, 0, 0)"), "no-op when already at value");
            Assert.IsFalse(manifest.AdvanceTokenBaseline("v:unknown", "x"), "no-op for unknown id");
        }

        [Test]
        public void AdvanceNodeBaseline_UpdatesExistingProp_AndAppendsNewOne()
        {
            var manifest = new FigmaSyncManifest();
            manifest.screens.Add(new FigmaScreenManifest
            {
                nodes = new List<FigmaNodeRecord>
                {
                    new FigmaNodeRecord
                    {
                        figmaNodeId = "1:2", cssClass = "btn",
                        props = new[] { new FigmaCssProp { k = "color", v = "#000" } }
                    }
                }
            });

            Assert.IsTrue(manifest.AdvanceNodeBaseline("1:2", "color", "#FFF"), "updates a recorded prop");
            Assert.AreEqual("#FFF", Prop(manifest, "color"));
            Assert.IsFalse(manifest.AdvanceNodeBaseline("1:2", "color", "#FFF"), "no-op when already at value");

            Assert.IsTrue(manifest.AdvanceNodeBaseline("1:2", "border-radius", "8px"), "appends a prop absent at import");
            Assert.AreEqual("8px", Prop(manifest, "border-radius"));
            Assert.AreEqual(2, manifest.screens[0].nodes[0].props.Length);

            Assert.IsFalse(manifest.AdvanceNodeBaseline("9:9", "color", "#FFF"), "no-op for unknown node");
        }

        static string Prop(FigmaSyncManifest manifest, string key)
        {
            foreach (var p in manifest.screens[0].nodes[0].props)
                if (p.k == key)
                    return p.v;
            return null;
        }

        const string k_File = "FigmaReimportUnitTest";
        const string k_Folder = "Assets/Figma/" + k_File;

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
        }

        [Test]
        public void ReImport_RenamedScreen_DeletesItsOldFiles()
        {
            ImportRoot("r:S", "AA");
            Assert.IsTrue(GeneratedExists("AA.uxml"), "first import should produce AA.uxml");

            ImportRoot("r:S", "BB");
            Assert.IsTrue(GeneratedExists("BB.uxml"), "re-import should produce BB.uxml");
            Assert.IsFalse(GeneratedExists("AA.uxml"), "the renamed screen's old uxml must be cleaned up");
            Assert.IsFalse(GeneratedExists("AA.uss"), "and its stylesheet");
        }

        [Test]
        public void ReImport_DifferentScreen_CoexistsWithoutErrors()
        {
            ImportRoot("r:AA", "AA");
            ImportRoot("r:BB", "BB");

            Assert.IsTrue(GeneratedExists("AA.uxml") && GeneratedExists("AA.uss"),
                "pushing a different screen must not delete the first screen's files");
            Assert.IsTrue(GeneratedExists("BB.uxml") && GeneratedExists("BB.uss"),
                "the second screen should be produced too");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Folder + "/AA.uxml");
            Assert.IsNotNull(tree, "AA.uxml should still be a valid asset");
            Assert.IsFalse(tree.importedWithErrors, "AA.uxml must import without errors (its USS was not deleted)");
        }

        static void ImportRoot(string rootId, string rootName)
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_File + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"" + rootId + "\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + rootName +
                    "\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}" +
                "]}";
            FigmaImportPipeline.Import(json);
        }

        static bool GeneratedExists(string relative)
        {
            return File.Exists(Path.Combine(Application.dataPath, "Figma", k_File, relative));
        }
    }
}
