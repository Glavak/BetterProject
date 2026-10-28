using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class BreadcrumbCollapseTests
    {
        static readonly float[] W = { 50, 40, 60, 30 }; // total 180

        [TestCase(200f, 0)]
        [TestCase(180f, 0)]
        [TestCase(179f, 1)]   // drop 50 -> 130 + 20 = 150
        [TestCase(150f, 1)]
        [TestCase(149f, 2)]   // drop 90 -> 90 + 20 = 110
        [TestCase(110f, 2)]
        [TestCase(109f, 3)]   // only last remains
        [TestCase(0f, 3)]     // last is never hidden
        public void HiddenCount(float available, int expected) =>
            Assert.AreEqual(expected, BreadcrumbCollapse.HiddenCount(W, available, 20));

        [Test]
        public void SingleOrEmpty()
        {
            Assert.AreEqual(0, BreadcrumbCollapse.HiddenCount(new float[0], 10, 20));
            Assert.AreEqual(0, BreadcrumbCollapse.HiddenCount(new[] { 500f }, 10, 20));
        }
    }
}
