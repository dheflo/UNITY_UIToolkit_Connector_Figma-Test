using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaPseudoStateTests
    {
        [Test]
        public void RealStates_MapToPseudoClasses()
        {
            Assert.AreEqual(":hover", FigmaPseudoState.Map("Hover"));
            Assert.AreEqual(":active", FigmaPseudoState.Map("Pressed"));
            Assert.AreEqual(":active", FigmaPseudoState.Map("Active"));
            Assert.AreEqual(":disabled", FigmaPseudoState.Map("Disabled"));
            Assert.AreEqual(":checked", FigmaPseudoState.Map("Selected"));
            Assert.AreEqual(":checked", FigmaPseudoState.Map("Checked"));
            Assert.AreEqual(":focus", FigmaPseudoState.Map("Focused"));
        }

        [Test]
        public void Mapping_IsCaseInsensitive()
        {
            Assert.AreEqual(":hover", FigmaPseudoState.Map("HOVER"));
            Assert.AreEqual(":active", FigmaPseudoState.Map("pressed"));
            Assert.AreEqual(":disabled", FigmaPseudoState.Map("  Disabled  "));
        }

        [Test]
        public void RestingStates_MapToEmpty()
        {
            Assert.AreEqual("", FigmaPseudoState.Map("Default"));
            Assert.AreEqual("", FigmaPseudoState.Map("Normal"));
            Assert.AreEqual("", FigmaPseudoState.Map("Enabled"));
            Assert.AreEqual("", FigmaPseudoState.Map(null));
            Assert.AreEqual("", FigmaPseudoState.Map(""));
        }

        [Test]
        public void UnknownState_MapsToNull()
        {
            Assert.IsNull(FigmaPseudoState.Map("Warning"));
            Assert.IsNull(FigmaPseudoState.Map("Loading"));
        }
    }
}
