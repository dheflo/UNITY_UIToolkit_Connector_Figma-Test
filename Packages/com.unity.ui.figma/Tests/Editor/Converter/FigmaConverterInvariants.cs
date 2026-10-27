using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    /// <summary>
    /// Rules the emitter must never break, checked on every case whatever its own test asserts. They stand
    /// in for what a recorded document used to catch for free: a change nobody thought to assert.
    /// </summary>
    static class FigmaConverterInvariants
    {
        public static void Check(FigmaEmitted emitted)
        {
            var uxml = emitted.FileNames.Where(f => f.EndsWith(".uxml")).ToList();

            foreach (var file in uxml)
            {
                foreach (var instance in emitted.Instances(file))
                {
                    // An override naming an element the template does not expose silently does nothing.
                    var slots = emitted.TextSlots(instance.template + ".uxml");
                    Assert.LessOrEqual(instance.overrides.Count, slots.Count,
                        file + " overrides " + instance.overrides.Count + " elements but "
                        + instance.template + ".uxml exposes " + slots.Count);
                }

                // A declared template nobody instantiates is a file and a USS rule nothing references.
                foreach (var declared in emitted.TemplateDeclarations(file))
                    Assert.IsTrue(emitted.Instances(file).Any(i => i.template == declared),
                        file + " declares template " + declared + " but never instantiates it");
            }

            // A class with no rule behind it means the USS filter dropped something the UXML needed.
            if (!emitted.FileNames.Contains("screen.uss"))
                return;
            var defined = new HashSet<string>(emitted.UssClasses("screen.uss"));
            foreach (var file in uxml)
                foreach (var used in emitted.Classes(file))
                    Assert.IsTrue(defined.Contains(used) || used.StartsWith("unity-"),
                        file + " uses class '" + used + "' with no rule in screen.uss");
        }
    }

    /// <summary>
    /// The invariants against documents that break them, so a suite where every case passes still says
    /// something. Without these, Check could assert nothing and nobody would notice.
    /// </summary>
    class FigmaConverterInvariantTests
    {
        static FigmaEmitted Docs(params (string file, string text)[] docs)
        {
            var map = new Dictionary<string, string>();
            foreach (var d in docs)
                map[d.file] = d.text;
            return new FigmaEmitted("synthetic", map);
        }

        const string k_Uss = ".card {\n    width: 300px;\n}\n";

        [Test]
        public void OrphanTemplate_IsCaught()
        {
            var screen = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n"
                + "  <Template name=\"Card\" src=\"Card.uxml\" />\n"
                + "  <ui:VisualElement />\n</ui:UXML>";
            var card = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement class=\"card\" /></ui:UXML>";

            var e = Assert.Throws<AssertionException>(
                () => FigmaConverterInvariants.Check(Docs(("screen.uxml", screen), ("Card.uxml", card), ("screen.uss", k_Uss))));
            StringAssert.Contains("never instantiates it", e.Message);
        }

        [Test]
        public void OverrideWithNoSlot_IsCaught()
        {
            var screen = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n"
                + "  <Template name=\"Card\" src=\"Card.uxml\" />\n"
                + "  <ui:Instance template=\"Card\">\n"
                + "    <AttributeOverrides element-name=\"text-0\" text=\"A\" />\n"
                + "    <AttributeOverrides element-name=\"text-1\" text=\"B\" />\n"
                + "  </ui:Instance>\n</ui:UXML>";
            var card = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement class=\"card\">"
                + "<ui:Label name=\"text-0\" /></ui:VisualElement></ui:UXML>";

            var e = Assert.Throws<AssertionException>(
                () => FigmaConverterInvariants.Check(Docs(("screen.uxml", screen), ("Card.uxml", card), ("screen.uss", k_Uss))));
            StringAssert.Contains("exposes", e.Message);
        }

        [Test]
        public void ClassWithNoRule_IsCaught()
        {
            var screen = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">"
                + "<ui:VisualElement class=\"missing\" /></ui:UXML>";

            var e = Assert.Throws<AssertionException>(
                () => FigmaConverterInvariants.Check(Docs(("screen.uxml", screen), ("screen.uss", k_Uss))));
            StringAssert.Contains("no rule in screen.uss", e.Message);
        }

        [Test]
        public void SoundDocuments_Pass()
        {
            var screen = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n"
                + "  <Template name=\"Card\" src=\"Card.uxml\" />\n"
                + "  <ui:Instance template=\"Card\">\n"
                + "    <AttributeOverrides element-name=\"text-0\" text=\"A\" />\n"
                + "  </ui:Instance>\n</ui:UXML>";
            var card = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement class=\"card\">"
                + "<ui:Label name=\"text-0\" /></ui:VisualElement></ui:UXML>";

            Assert.DoesNotThrow(
                () => FigmaConverterInvariants.Check(Docs(("screen.uxml", screen), ("Card.uxml", card), ("screen.uss", k_Uss))));
        }
    }
}
