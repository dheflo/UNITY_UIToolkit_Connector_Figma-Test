using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.UIToolkit.Figma.Tests
{
    class FigmaScanCacheTests
    {
        [Test]
        public void GetOrCompute_RecomputesOnlyWhenSignatureChanges()
        {
            var cache = new FigmaScanCache<int>();
            var calls = 0;

            var a = cache.GetOrCompute("dir", "sig1", () => { calls++; return new List<int> { 1 }; });
            var b = cache.GetOrCompute("dir", "sig1", () => { calls++; return new List<int> { 2 }; });
            Assert.AreEqual(1, calls, "same signature reuses the cached diff");
            CollectionAssert.AreEqual(new[] { 1 }, a);
            CollectionAssert.AreEqual(new[] { 1 }, b, "returns the cached result, not the second lambda's");

            var c = cache.GetOrCompute("dir", "sig2", () => { calls++; return new List<int> { 3 }; });
            Assert.AreEqual(2, calls, "changed signature recomputes");
            CollectionAssert.AreEqual(new[] { 3 }, c);
        }

        [Test]
        public void GetOrCompute_KeysPerFolder()
        {
            var cache = new FigmaScanCache<int>();
            var calls = 0;
            cache.GetOrCompute("A", "s", () => { calls++; return new List<int> { 1 }; });
            cache.GetOrCompute("B", "s", () => { calls++; return new List<int> { 2 }; });
            Assert.AreEqual(2, calls, "different folders cache independently even with the same signature");
        }
    }
}
