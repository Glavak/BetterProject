using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class SelectionModelTests
    {
        static readonly int[] Order = { 10, 20, 30, 40 };

        [Test] public void Click_Plain_SelectsOnly()
        {
            var s = new SelectionModel();
            s.Click(10, false, false, Order);
            s.Click(20, false, false, Order);
            CollectionAssert.AreEqual(new[] { 20 }, s.Selected);
            Assert.AreEqual(20, s.Anchor);
        }
        [Test] public void Click_Toggle_AddsAndRemoves()
        {
            var s = new SelectionModel();
            s.Click(10, false, false, Order);
            s.Click(30, true, false, Order);
            CollectionAssert.AreEquivalent(new[] { 10, 30 }, s.Selected);
            Assert.AreEqual(30, s.Anchor);
            s.Click(10, true, false, Order);
            CollectionAssert.AreEqual(new[] { 30 }, s.Selected);
        }
        [Test] public void Click_Range_FromAnchor()
        {
            var s = new SelectionModel();
            s.Click(20, false, false, Order);
            s.Click(40, false, true, Order);
            CollectionAssert.AreEqual(new[] { 20, 30, 40 }, s.Selected);
            Assert.AreEqual(20, s.Anchor);
        }
        [Test] public void Click_Range_Backwards()
        {
            var s = new SelectionModel();
            s.Click(40, false, false, Order);
            s.Click(20, false, true, Order);
            CollectionAssert.AreEquivalent(new[] { 20, 30, 40 }, s.Selected);
            Assert.AreEqual(40, s.Anchor);
        }
        [Test] public void Click_Range_AnchorGone_ActsPlain()
        {
            var s = new SelectionModel();
            s.Click(99, false, false, Order);
            s.Click(30, false, true, Order);
            CollectionAssert.AreEqual(new[] { 30 }, s.Selected);
            Assert.AreEqual(30, s.Anchor);
        }
        [Test] public void Set_ReplacesSelection()
        {
            var s = new SelectionModel();
            s.Click(10, false, false, Order);
            s.Set(new[] { 20, 30 });
            CollectionAssert.AreEquivalent(new[] { 20, 30 }, s.Selected);
        }
        [Test] public void Retain_DropsMissing()
        {
            var s = new SelectionModel();
            s.Set(new[] { 10, 20, 30 });
            s.Retain(new[] { 10, 30 });
            CollectionAssert.AreEquivalent(new[] { 10, 30 }, s.Selected);
        }
    }
}
