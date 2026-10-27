using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaTokensImportTests
    {
        const string k_Name = "FigmaTokensUnitTest";
        const string k_Folder = "Assets/Figma/" + k_Name;
        const string k_VarName = "FigmaVarBindingsUnitTest";
        const string k_VarFolder = "Assets/Figma/" + k_VarName;
        const string k_ModeName = "FigmaModeUnitTest";
        const string k_ModeFolder = "Assets/Figma/" + k_ModeName;
        const string k_InjectName = "FigmaVarNameInjectionUnitTest";
        const string k_InjectFolder = "Assets/Figma/" + k_InjectName;
        const string k_TokenInjectName = "FigmaTokenValueInjectionUnitTest";
        const string k_TokenInjectFolder = "Assets/Figma/" + k_TokenInjectName;

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
            if (AssetDatabase.IsValidFolder(k_VarFolder))
                AssetDatabase.DeleteAsset(k_VarFolder);
            if (AssetDatabase.IsValidFolder(k_ModeFolder))
                AssetDatabase.DeleteAsset(k_ModeFolder);
            if (AssetDatabase.IsValidFolder(k_InjectFolder))
                AssetDatabase.DeleteAsset(k_InjectFolder);
            if (AssetDatabase.IsValidFolder(k_TokenInjectFolder))
                AssetDatabase.DeleteAsset(k_TokenInjectFolder);
        }

        [Test]
        public void TokenValue_WithUssStructuralCharacters_IsDropped()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_TokenInjectName + "\",\"rootIndex\":0," +
                "\"variables\":[{\"name\":\"color/evil\",\"type\":\"COLOR\",\"value\":\"red } * { display: none\"}," +
                              "{\"name\":\"color/good\",\"type\":\"COLOR\",\"value\":\"#0d99ff\"}]," +
                "\"nodes\":[" +
                "{\"id\":\"t:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_TokenInjectName + "\"," +
                    "\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);

            var tokensPath = Path.Combine(Application.dataPath, "Figma", k_TokenInjectName, "tokens.uss");
            var tokens = File.ReadAllText(tokensPath);
            Assert.IsFalse(tokens.Contains("display: none"));
            StringAssert.Contains("--color-good: #0d99ff", tokens);

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_TokenInjectFolder + "/tokens.uss");
            Assert.IsNotNull(sheet);
            Assert.IsFalse(sheet.importedWithErrors);
        }

        [Test]
        public void VarRefPropertyName_WithUssStructuralCharacters_IsDropped()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_InjectName + "\",\"rootIndex\":0," +
                "\"variables\":[{\"name\":\"color/primary\",\"type\":\"COLOR\",\"value\":\"rgba(13, 153, 255, 1)\"}]," +
                "\"nodes\":[" +
                "{\"id\":\"i:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_InjectName + "\"," +
                    "\"varRefs\":[{\"k\":\"color } * { display: none\",\"v\":\"color/primary\"}," +
                                  "{\"k\":\"background-color\",\"v\":\"color/primary\"}]," +
                    "\"css\":[{\"k\":\"display\",\"v\":\"flex\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var emitted = string.Empty;
            foreach (var file in Directory.GetFiles(Path.Combine(Application.dataPath, "Figma", k_InjectName)))
                if (file.EndsWith(".uss") || file.EndsWith(".uxml"))
                    emitted += File.ReadAllText(file);

            Assert.IsFalse(emitted.Contains("display: none"));
            StringAssert.Contains("background-color: var(", emitted);
        }

        [Test]
        public void Tokens_And_VarRefs_ImportWithoutErrors()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_Name + "\",\"rootIndex\":0," +
                "\"variables\":[{\"name\":\"color/primary\",\"type\":\"COLOR\",\"value\":\"rgba(13, 153, 255, 1)\"}]," +
                "\"nodes\":[" +
                "{\"id\":\"t:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_Name + "\"," +
                    "\"varRefs\":[{\"k\":\"background-color\",\"v\":\"color/primary\"}]," +
                    "\"css\":[{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"background-color\",\"v\":\"#123456\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var tokens = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Folder + "/tokens.uss");
            Assert.IsNotNull(tokens, "tokens.uss should be generated and imported");
            Assert.IsFalse(tokens.importedWithErrors, "tokens.uss must import without errors");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Folder + "/" + k_Name + ".uxml");
            Assert.IsNotNull(tree);
            Assert.IsFalse(tree.importedWithErrors, "UXML with inline var() must import without errors");

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_Name, k_Name + ".uxml"));
            StringAssert.Contains("var(--color-primary)", uxml);
        }

        [Test]
        public void LayoutAndTextVarRefs_ImportWithoutErrors()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_VarName + "\",\"rootIndex\":0,\"variables\":[" +
                "{\"name\":\"size/panel-width\",\"type\":\"FLOAT\",\"value\":\"320px\"}," +
                "{\"name\":\"space/md\",\"type\":\"FLOAT\",\"value\":\"16px\"}," +
                "{\"name\":\"text/lg\",\"type\":\"FLOAT\",\"value\":\"24px\"}]," +
                "\"nodes\":[" +
                "{\"id\":\"v:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_VarName + "\"," +
                    "\"varRefs\":[{\"k\":\"width\",\"v\":\"size/panel-width\"},{\"k\":\"padding-left\",\"v\":\"space/md\"}]," +
                    "\"css\":[{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"width\",\"v\":\"320px\"},{\"k\":\"padding\",\"v\":\"16px\"}]}," +
                "{\"id\":\"v:1\",\"parentIndex\":0,\"type\":\"TEXT\",\"name\":\"Heading\",\"text\":\"Settings\"," +
                    "\"varRefs\":[{\"k\":\"font-size\",\"v\":\"text/lg\"}]," +
                    "\"css\":[{\"k\":\"color\",\"v\":\"#ffffff\"},{\"k\":\"font-size\",\"v\":\"24px\"}]}" +
                "]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_VarFolder + "/" + k_VarName + ".uxml");
            Assert.IsNotNull(tree);
            Assert.IsFalse(tree.importedWithErrors,
                "inline layout/text var() (incl. padding longhand override) must import without errors");

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_VarName, k_VarName + ".uxml"));
            StringAssert.Contains("width: var(--size-panel-width)", uxml);
            StringAssert.Contains("padding-left: var(--space-md)", uxml);
            StringAssert.Contains("font-size: var(--text-lg)", uxml);
            StringAssert.DoesNotContain("width: 320px", uxml);
        }

        [Test]
        public void MultiMode_FansOutPerModeFiles()
        {
            var json =
                "{\"schema\":1,\"fileName\":\"" + k_ModeName + "\",\"rootIndex\":0," +
                "\"variables\":[{\"id\":\"vp\",\"name\":\"neutral-000\",\"type\":\"COLOR\",\"value\":\"rgba(0, 0, 0, 1)\",\"aliasOf\":\"\"}]," +
                "\"modeVariables\":[{\"id\":\"vbg\",\"name\":\"bg/primary\",\"type\":\"COLOR\",\"values\":[" +
                    "{\"mode\":\"Dark\",\"value\":\"rgba(0, 0, 0, 1)\",\"aliasOf\":\"neutral-000\"}," +
                    "{\"mode\":\"Light\",\"value\":\"rgba(255, 255, 255, 1)\",\"aliasOf\":\"neutral-000\"}]}]," +
                "\"modes\":[\"Dark\",\"Light\"],\"defaultMode\":\"Dark\"," +
                "\"nodes\":[{\"id\":\"m:0\",\"parentIndex\":-1,\"type\":\"FRAME\",\"name\":\"" + k_ModeName + "\"," +
                    "\"varRefs\":[{\"k\":\"background-color\",\"v\":\"bg/primary\"}]," +
                    "\"css\":[{\"k\":\"display\",\"v\":\"flex\"},{\"k\":\"background-color\",\"v\":\"#000000\"}]}]}";

            var result = FigmaImportPipeline.Import(json);
            StringAssert.Contains("\"ok\":true", result);
            StringAssert.Contains("\"ussErrors\":false", result);

            foreach (var file in new[] { "tokens.uss", "dark.uss", "light.uss" })
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_ModeFolder + "/" + file);
                Assert.IsNotNull(sheet, file + " should be generated and imported");
                Assert.IsFalse(sheet.importedWithErrors, file + " must import without errors");
            }

            var uxml = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_ModeName, k_ModeName + ".uxml"));
            StringAssert.Contains("<Style src=\"tokens.uss\"", uxml);
            StringAssert.Contains("<Style src=\"dark.uss\"", uxml);
            StringAssert.DoesNotContain("<Style src=\"light.uss\"", uxml);

            var dark = File.ReadAllText(Path.Combine(Application.dataPath, "Figma", k_ModeName, "dark.uss"));
            StringAssert.Contains("--bg-primary: var(--neutral-000);", dark);
        }
    }
}
