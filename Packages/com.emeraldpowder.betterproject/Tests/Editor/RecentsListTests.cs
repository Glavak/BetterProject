using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class RecentsListTests
    {
        static AssetRef R(int i) => new AssetRef(i.ToString("x32"));

        [Test] public void Push_NewItemGoesFirst()
        {
            var l = new RecentsList();
            Assert.IsTrue(l.Push(R(1)));
            Assert.IsTrue(l.Push(R(2)));
            CollectionAssert.AreEqual(new[] { R(2), R(1) }, l.Items);
        }
        [Test] public void Push_Existing_MovesToTop()
        {
            var l = new RecentsList();
            l.Push(R(1)); l.Push(R(2));
            Assert.IsTrue(l.Push(R(1)));
            CollectionAssert.AreEqual(new[] { R(1), R(2) }, l.Items);
        }
        [Test] public void Push_AlreadyTop_ReturnsFalse()
        {
            var l = new RecentsList();
            l.Push(R(1));
            Assert.IsFalse(l.Push(R(1)));
            Assert.AreEqual(1, l.Items.Count);
        }
        [Test] public void PushRange_SameSetTwice_SecondReturnsFalse()
        {
            var l = new RecentsList();
            Assert.IsTrue(l.PushRange(new[] { R(1), R(2) }));
            Assert.IsFalse(l.PushRange(new[] { R(1), R(2) }));
            CollectionAssert.AreEqual(new[] { R(2), R(1) }, l.Items);
        }
        [Test] public void PushRange_NewItem_ReturnsTrue()
        {
            var l = new RecentsList();
            l.PushRange(new[] { R(1), R(2) });
            Assert.IsTrue(l.PushRange(new[] { R(1), R(3) }));
            CollectionAssert.AreEqual(new[] { R(3), R(1), R(2) }, l.Items);
        }
        [Test] public void PushRange_Reorder_ReturnsTrue()
        {
            var l = new RecentsList();
            l.PushRange(new[] { R(1), R(2) });
            Assert.IsTrue(l.PushRange(new[] { R(2), R(1) }));
            CollectionAssert.AreEqual(new[] { R(1), R(2) }, l.Items);
        }
        [Test] public void PushRange_Empty_ReturnsFalse() => Assert.IsFalse(new RecentsList().PushRange(new AssetRef[0]));
        [Test] public void Push_Eleventh_DropsOldest()
        {
            var l = new RecentsList();
            for (int i = 0; i <= 10; i++) l.Push(R(i));
            Assert.AreEqual(RecentsList.DefaultLimit, l.Items.Count);
            Assert.AreEqual(R(10), l.Items[0]);
            CollectionAssert.DoesNotContain(l.Items, R(0));
        }
        [Test] public void Push_CustomLimit_DropsOldest()
        {
            var l = new RecentsList(3);
            for (int i = 0; i < 5; i++) l.Push(R(i));
            CollectionAssert.AreEqual(new[] { R(4), R(3), R(2) }, l.Items);
        }
        [Test] public void Ctor_LimitBelowOne_Throws() => Assert.Throws<System.ArgumentOutOfRangeException>(() => new RecentsList(0));
        [Test] public void SetLimit_Lower_DropsOldest()
        {
            var l = new RecentsList(5);
            for (int i = 0; i < 5; i++) l.Push(R(i));
            Assert.IsTrue(l.SetLimit(2));
            CollectionAssert.AreEqual(new[] { R(4), R(3) }, l.Items);
            Assert.AreEqual(2, l.Limit);
        }
        [Test] public void SetLimit_Higher_KeepsItems()
        {
            var l = new RecentsList(2);
            l.Push(R(1)); l.Push(R(2));
            Assert.IsFalse(l.SetLimit(20));
            l.Push(R(3));
            CollectionAssert.AreEqual(new[] { R(3), R(2), R(1) }, l.Items);
        }
        [Test] public void Recents_RemoveWhere_DropsUnresolved()
        {
            var l = new RecentsList();
            l.Push(R(1)); l.Push(R(2)); l.Push(R(3));
            Assert.AreEqual(1, l.RemoveWhere(r => r.Equals(R(2))));
            CollectionAssert.AreEqual(new[] { R(3), R(1) }, l.Items);
        }
        [Test] public void Remove_Works()
        {
            var l = new RecentsList();
            l.Push(R(1));
            Assert.IsTrue(l.Remove(R(1)));
            Assert.IsFalse(l.Remove(R(1)));
        }
        [Test] public void Clear_NonEmpty_ReturnsTrue()
        {
            var l = new RecentsList();
            l.Push(R(1));
            Assert.IsTrue(l.Clear());
            Assert.AreEqual(0, l.Items.Count);
        }
        [Test] public void Clear_Empty_ReturnsFalse() => Assert.IsFalse(new RecentsList().Clear());
    }
}
