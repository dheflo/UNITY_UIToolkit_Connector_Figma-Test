using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaImportPipelineTests
    {
        const string k_Name = "FigmaPipelineUnitTest";
        const string k_Folder = "Assets/Figma/" + k_Name;

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
        }

        [Test]
        public void Import_RefusesPayload_WhoseSchemaThisPackageDoesNotRead()
        {
            var unreadable = FigmaImportPipeline.ReadableSchemas[0] + 1;
            var json =
                "{\"schema\":" + unreadable + ",\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);

            StringAssert.Contains("\"ok\":false", result);
            StringAssert.Contains("\"schemaMismatch\":true", result);
            Assert.IsFalse(AssetDatabase.IsValidFolder(k_Folder), "a refused payload must not write anything");
        }

        [Test]
        public void FramedText_Imports_AsContainer_WithoutUssErrors()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[" +
                    "{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"flex-direction\",\"v\":\"column\"}," +
                    "{\"k\":\"background-color\",\"v\":\"#222222\"},{\"k\":\"padding\",\"v\":\"12px\"}]}," +
                "{\"id\":\"1:1\",\"parentIndex\":0,\"type\":\"FRAME\",\"name\":\"CTA\",\"css\":[" +
                    "{\"k\":\"background-color\",\"v\":\"#0d99ff\"},{\"k\":\"border-radius\",\"v\":\"6px\"}]}," +
                "{\"id\":\"1:2\",\"parentIndex\":1,\"type\":\"TEXT\",\"name\":\"t\",\"text\":\"GO\",\"css\":[" +
                    "{\"k\":\"color\",\"v\":\"#ffffff\"},{\"k\":\"font-size\",\"v\":\"14px\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Folder + "/" + k_Name + ".uss");
            Assert.IsNotNull(styleSheet, "generated .uss should import");
            Assert.IsFalse(styleSheet.importedWithErrors, "generated USS must import without errors");

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_Name, k_Name + ".uxml"));
            StringAssert.Contains("<ui:Label text=\"GO\"", uxml,
                "a framed single text now imports as a container with a Label child, not a guessed Button");
            StringAssert.DoesNotContain("<ui:Button", uxml, "shape no longer promotes a node to a Button");
        }

        [Test]
        public void Import_EscapesControlCharactersFromFigmaNames()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[" +
                    "{\"k\":\"background-color\",\"v\":\"#123456\"}]," +
                    "\"variants\":[{\"state\":\"Odd\\nState\\tName\",\"css\":[" +
                    "{\"k\":\"background-color\",\"v\":\"#654321\"}]}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);

            Assert.AreEqual(-1, result.IndexOf('\n'), "a raw newline would break the response JSON");
            Assert.AreEqual(-1, result.IndexOf('\t'), "a raw tab would break the response JSON");
            StringAssert.Contains("Odd\\nState\\tName", result, "the name survives, escaped");
        }

        [Test]
        public void Import_RecordsNodeBaselines_InManifest()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[" +
                    "{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"background-color\",\"v\":\"#123456\"}," +
                    "{\"k\":\"border-radius\",\"v\":\"8px\"}]}" +
                "]}";

            StringAssert.Contains("\"ok\":true", FigmaImportPipeline.Import(json));

            var manifestPath = Path.Combine(Application.dataPath, "Figma", k_Name, ".figma-manifest.json");
            Assert.IsTrue(File.Exists(manifestPath), "manifest sidecar should exist");
            var manifest = FigmaManifestIO.Parse(File.ReadAllText(manifestPath));
            Assert.AreEqual(1, manifest.screens.Count);
            Assert.AreEqual(1, manifest.screens[0].nodes.Count);
            var node = manifest.screens[0].nodes[0];
            Assert.AreEqual(k_Name, node.doc);
            Assert.AreEqual("1", node.path);
            Assert.IsTrue(System.Array.Exists(node.props, p => p.k == "background-color"),
                "the supported props should be recorded as the baseline");
        }

        [Test]
        public void TaggedTab_Imports_WithoutErrors()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[" +
                    "{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:1\",\"parentIndex\":0,\"type\":\"FRAME\",\"name\":\"Section\",\"control\":\"Tab\",\"css\":[" +
                    "{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:2\",\"parentIndex\":1,\"type\":\"TEXT\",\"name\":\"t\",\"text\":\"Details\",\"css\":[" +
                    "{\"k\":\"color\",\"v\":\"#ffffff\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_Name, k_Name + ".uxml"));
            StringAssert.Contains("<ui:Tab", uxml);
        }

        [Test]
        public void TabView_WithTabs_Imports_Nested()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:1\",\"parentIndex\":0,\"type\":\"FRAME\",\"name\":\"Tabs\",\"control\":\"TabView\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:2\",\"parentIndex\":1,\"type\":\"FRAME\",\"name\":\"Tab\",\"control\":\"Tab\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:3\",\"parentIndex\":2,\"type\":\"TEXT\",\"name\":\"t\",\"text\":\"Overview\",\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"}]}," +
                "{\"id\":\"1:4\",\"parentIndex\":1,\"type\":\"FRAME\",\"name\":\"Details\",\"control\":\"Tab\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:5\",\"parentIndex\":4,\"type\":\"TEXT\",\"name\":\"h\",\"text\":\"Stats\",\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"}]}," +
                "{\"id\":\"1:6\",\"parentIndex\":4,\"type\":\"TEXT\",\"name\":\"b\",\"text\":\"All time\",\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_Name, k_Name + ".uxml"));
            StringAssert.Contains("<ui:TabView", uxml);
            StringAssert.Contains("<ui:Tab label=\"Overview\"", uxml);
            StringAssert.Contains("<ui:Tab label=\"Details\"", uxml);
            StringAssert.Contains("text=\"Stats\"", uxml, "a tab with a real page keeps it");
        }

        [Test]
        public void Composites_Import_WithoutUssErrors()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"1:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:1\",\"parentIndex\":0,\"type\":\"FRAME\",\"name\":\"Prefs\",\"control\":\"GroupBox\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:2\",\"parentIndex\":1,\"type\":\"TEXT\",\"name\":\"t\",\"text\":\"On\",\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"}]}," +
                "{\"id\":\"1:3\",\"parentIndex\":0,\"type\":\"FRAME\",\"name\":\"Pick\",\"control\":\"RadioButtonGroup\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:4\",\"parentIndex\":3,\"type\":\"FRAME\",\"name\":\"A\",\"control\":\"RadioButton\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:5\",\"parentIndex\":4,\"type\":\"TEXT\",\"name\":\"t\",\"text\":\"Option A\",\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"}]}," +
                "{\"id\":\"1:6\",\"parentIndex\":0,\"type\":\"FRAME\",\"name\":\"Format\",\"control\":\"ToggleButtonGroup\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:7\",\"parentIndex\":6,\"type\":\"FRAME\",\"name\":\"Bold\",\"control\":\"Button\",\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}," +
                "{\"id\":\"1:8\",\"parentIndex\":7,\"type\":\"TEXT\",\"name\":\"t\",\"text\":\"B\",\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_Name, k_Name + ".uxml"));
            StringAssert.Contains("<ui:GroupBox text=\"Prefs\"", uxml);
            StringAssert.Contains("<ui:RadioButtonGroup label=\"Pick\"", uxml);
            StringAssert.Contains("<ui:RadioButton label=\"Option A\"", uxml);
            StringAssert.Contains("<ui:ToggleButtonGroup label=\"Format\"", uxml);
        }
    }
}
