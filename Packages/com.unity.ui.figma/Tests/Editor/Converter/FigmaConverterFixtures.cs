using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Unity.UIToolkit.Figma.Tests
{
    /// <summary>
    /// Loads a converter case and emits it. Shared by the tests and by the dump menu item, so what you
    /// read when authoring a case is what the tests assert.
    /// </summary>
    static class FigmaConverterFixtures
    {
        const string k_Input = "input.json";

        public static string CasesDir()
        {
            var info = PackageInfo.FindForAssembly(typeof(FigmaConverterFixtures).Assembly);
            return info == null ? null : Path.Combine(info.resolvedPath, "Tests", "Editor", "Converter", "TestFiles");
        }

        public static IEnumerable<string> Cases()
        {
            var dir = CasesDir();
            if (dir == null || !Directory.Exists(dir))
                yield break;
            var dirs = Directory.GetDirectories(dir);
            System.Array.Sort(dirs, System.StringComparer.Ordinal);
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, k_Input)))
                    yield return Path.GetFileName(d);
        }

        public static FigmaEmitted Emit(string name, FigmaImportSettings settings = null)
        {
            var dir = CasesDir();
            Assert.IsNotNull(dir, "could not resolve the package root. Is the package under Assets rather than installed?");
            var input = Path.Combine(dir, name, k_Input);
            Assert.IsTrue(File.Exists(input), "no case named " + name);
            return new FigmaEmitted(name, EmitDocuments(File.ReadAllText(input), settings));
        }

        /// <summary>
        /// The importer's emission for one payload, with no file IO. Mirrors FigmaImportPipeline.Import,
        /// which interleaves the same sequence with writing; keep the two in step.
        /// </summary>
        public static Dictionary<string, string> EmitDocuments(string json, FigmaImportSettings settings = null)
        {
            var message = JsonUtility.FromJson<FigmaImportMessage>(json);
            FigmaNodeDerive.Apply(message.nodes);
            var context = FigmaImportPipeline.BuildContext(message);

            var produced = new Dictionary<string, string>();

            var tokensUss = FigmaTokens.BuildTokensUss(message.variables);
            var tokenStyleSrcs = new List<string>();
            if (!string.IsNullOrEmpty(tokensUss))
                tokenStyleSrcs.Add("tokens.uss");

            foreach (var mode in message.modes ?? new string[0])
            {
                var text = FigmaTokens.BuildModeUss(message.modeVariables, mode);
                if (!string.IsNullOrEmpty(text))
                    produced[FigmaTokens.ModeFileName(mode)] = text;
            }

            var screenStyleSrcs = new List<string>(tokenStyleSrcs);
            screenStyleSrcs.AddRange(FigmaImportPipeline.LinkedModeFiles(message, produced.Keys));

            var library = FigmaComponentLibrary.Build(context, settings, "screen");
            FigmaComponentStyleModel.Build(context, library, family => null, index => null);

            var allUxml = new List<string>();
            var uxml = FigmaToUxml.BuildScreen(context, "screen.uss", screenStyleSrcs, settings, library);
            allUxml.Add(uxml);
            produced["screen.uxml"] = uxml;

            foreach (var template in library.Templates)
            {
                var text = FigmaToUxml.BuildTemplate(context, "screen.uss", tokenStyleSrcs, settings, library, template);
                allUxml.Add(text);
                produced[template.name + ".uxml"] = text;
            }

            produced["screen.uss"] = FigmaToUss.Build(context, settings, FigmaToUss.UsedClasses(allUxml));
            if (!string.IsNullOrEmpty(tokensUss))
                produced["tokens.uss"] = tokensUss;

            return produced;
        }

        /// <summary>
        /// Writes what every case emits under Library/ and opens it, so you can read the UXML and USS a
        /// payload produces while writing assertions about it. Only compiled for a project that opts into
        /// this package's tests, so it never reaches a consumer.
        /// </summary>
        [MenuItem("Window/UI Toolkit/Connector Tests/Dump Converter Cases")]
        static void DumpCases()
        {
            // Library is derived data: gitignored, beside the project, and not swept up by the OS.
            var root = Path.Combine(FigmaPipelineConnector.ProjectRoot(), "Library", "FigmaConverterCases");
            if (Directory.Exists(root))
                Directory.Delete(root, true);

            var log = new System.Text.StringBuilder("[Figma] converter cases emitted to ").Append(root);
            var count = 0;
            foreach (var name in Cases())
            {
                var emitted = Emit(name);
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                foreach (var file in emitted.FileNames)
                    File.WriteAllText(Path.Combine(dir, file), emitted.Text(file));

                log.Append("\n  ").Append(name).Append(": ").Append(string.Join(", ", emitted.FileNames));
                count++;
            }

            if (count == 0)
            {
                Debug.LogWarning("[Figma] no converter cases found under Tests/Editor/Converter/TestFiles");
                return;
            }

            Debug.Log(log.ToString());
            EditorUtility.RevealInFinder(root + Path.DirectorySeparatorChar);
        }
    }
}
