using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaPathGuardTests
    {
        string m_Root;
        string m_Outside;

        [SetUp]
        public void SetUp()
        {
            var temp = Path.Combine(Path.GetTempPath(), "FigmaPathGuardTests-" + Path.GetRandomFileName());
            m_Root = Path.Combine(temp, "root");
            m_Outside = Path.Combine(temp, "outside");
            Directory.CreateDirectory(m_Root);
            Directory.CreateDirectory(m_Outside);
        }

        [TearDown]
        public void TearDown()
        {
            var temp = Path.GetDirectoryName(m_Root);
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }

        [Test]
        public void Resolve_AllowsGeneratedDocumentUnderRoot()
        {
            Assert.IsTrue(FigmaPathGuard.TryResolve(m_Root, "screen/Main.uxml", out var full));
            StringAssert.EndsWith("Main.uxml", full);
        }

        [Test]
        public void Resolve_RejectsTraversalOutsideRoot()
        {
            Assert.IsFalse(FigmaPathGuard.TryResolve(m_Root, "../outside/Escaped.uxml", out _));
        }

        [Test]
        public void Resolve_RejectsRootedPath()
        {
            Assert.IsFalse(FigmaPathGuard.TryResolve(m_Root, Path.Combine(m_Outside, "Escaped.uxml"), out _));
        }

        [Test]
        public void Resolve_RejectsDisallowedExtensions()
        {
            Assert.IsFalse(FigmaPathGuard.TryResolve(m_Root, "screen/Evil.cs", out _));
            Assert.IsFalse(FigmaPathGuard.TryResolve(m_Root, "screen/Evil.asset", out _));
        }

        [Test]
        public void Resolve_RejectsPathThroughSymlinkedDirectory()
        {
            var link = Path.Combine(m_Root, "linked");
            if (!TryCreateDirectorySymlink(m_Outside, link))
            {
                Assert.Ignore("symlink creation is not permitted in this environment");
                return;
            }

            if ((new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint) == 0)
            {
                Assert.Ignore("this platform does not report symlinks as reparse points");
                return;
            }

            Assert.IsFalse(FigmaPathGuard.TryResolve(m_Root, "linked/Escaped.uxml", out _),
                "a link under the root points outside it, so containment no longer holds");
        }

        static bool TryCreateDirectorySymlink(string target, string link)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    info.FileName = "cmd.exe";
                    info.Arguments = "/c mklink /D \"" + link + "\" \"" + target + "\"";
                }
                else
                {
                    info.FileName = "/bin/ln";
                    info.Arguments = "-s \"" + target + "\" \"" + link + "\"";
                }

                using (var process = System.Diagnostics.Process.Start(info))
                {
                    if (process == null)
                        return false;
                    process.WaitForExit(5000);
                }

                return Directory.Exists(link);
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
