using NUnit.Framework;
using static Unity.UIToolkit.Figma.Tests.FigmaTestBuilders;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaPropertyCatalogTests
    {
        [Test]
        public void Record_CreditsSubtreePropsToEnclosingInstances()
        {
            // The props ride a nested typography instance; a record matching the outer button's name
            // must still see them, since its rows can reach them.
            var ctx = Context(
                Node(-1, "FRAME", "Screen"),
                Node(0, "INSTANCE", "My Button", componentKey: "k1"),
                Node(1, "INSTANCE", "Text", componentKey: "kt",
                    props: Props("Label#1:2", "TEXT", "Go", "Rich#3", "BOOLEAN", "true")));
            FigmaPropertyCatalog.Record(ctx);

            var foundLabel = false;
            var foundRich = false;
            foreach (var p in FigmaPropertyCatalog.PropsFor("my-button"))
            {
                if (p.name == "Label" && p.type == "TEXT")
                    foundLabel = true;
                if (p.name == "Rich" && p.type == "BOOLEAN")
                    foundRich = true;
            }
            Assert.IsTrue(foundLabel, "suffix-stripped TEXT prop recorded against the outer instance");
            Assert.IsTrue(foundRich, "BOOLEAN prop recorded against the outer instance");
        }

        [Test]
        public void PropsFor_UnknownOrEmptyName_IsEmpty()
        {
            Assert.IsEmpty(FigmaPropertyCatalog.PropsFor("never-imported-component"));
            Assert.IsEmpty(FigmaPropertyCatalog.PropsFor(""));
        }
    }
}
