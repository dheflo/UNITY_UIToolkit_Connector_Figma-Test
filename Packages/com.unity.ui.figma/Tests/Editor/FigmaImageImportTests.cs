using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaImageImportTests
    {
        const string k_Name = "FigmaImageUnitTest";
        const string k_Folder = "Assets/Figma/" + k_Name;
        const string k_NodeId = "img:1";

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
        }

        [Test]
        public void RasterNode_Imports_AsBackgroundImage_WithoutErrors()
        {
            FigmaImageStore.EnsureInitialized(Application.dataPath);
            Assert.IsTrue(FigmaImageStore.Stage(k_NodeId, MakePng()), "staging the PNG should succeed");

            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0,\"nodes\":[" +
                "{\"id\":\"i:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\",\"css\":[" +
                    "{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"flex-direction\",\"v\":\"column\"}]}," +
                "{\"id\":\"" + k_NodeId + "\",\"parentIndex\":0,\"type\":\"RECTANGLE\",\"name\":\"hero\",\"raster\":true,\"css\":[" +
                    "{\"k\":\"width\",\"v\":\"100px\"},{\"k\":\"height\",\"v\":\"60px\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_Folder + "/images/img-1.png");
            Assert.IsNotNull(texture, "the staged PNG should be promoted into the asset folder and imported");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Folder + "/" + k_Name + ".uss");
            Assert.IsNotNull(styleSheet);
            Assert.IsFalse(styleSheet.importedWithErrors, "generated USS must import without errors");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Folder + "/" + k_Name + ".uxml");
            Assert.IsNotNull(tree);
            Assert.IsFalse(tree.importedWithErrors, "generated UXML (incl. inline styles) must import without errors");

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_Name, k_Name + ".uxml"));
            StringAssert.Contains("background-image", uxml, "a raster node should emit an inline background-image");
        }

        static byte[] MakePng()
        {
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            texture.Apply();
            var bytes = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return bytes;
        }
    }
}
