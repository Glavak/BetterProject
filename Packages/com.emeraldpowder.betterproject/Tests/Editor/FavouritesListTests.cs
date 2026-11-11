using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class FavouritesListTests
    {
        const string G1 = "0123456789abcdef0123456789abcdef";
        const string G2 = "fedcba9876543210fedcba9876543210";

        [Test] public void Add_AppendsInOrder()
        {
            var l = new FavouritesList();
            var a = new AssetRef(G1); var b = new AssetRef(G2);
            Assert.IsTrue(l.Add(a, "A"));
            Assert.IsTrue(l.Add(b, "B"));
            Assert.AreEqual(2, l.Items.Count);
            Assert.AreEqual(a, l.Items[0].Ref);
            Assert.AreEqual(b, l.Items[1].Ref);
            Assert.AreEqual("B", l.Items[1].Name);
        }
        [Test] public void Add_Duplicate_ReturnsFalse()
        {
            var l = new FavouritesList();
            Assert.IsTrue(l.Add(new AssetRef(G1), "A"));
            Assert.IsFalse(l.Add(new AssetRef(G1), "Other"));
            Assert.AreEqual(1, l.Items.Count);
            Assert.AreEqual("A", l.Items[0].Name);
        }
        [Test] public void Add_NullName_BecomesEmpty()
        {
            var l = new FavouritesList();
            l.Add(new AssetRef(G1), null);
            Assert.AreEqual("", l.Items[0].Name);
        }
        [Test] public void Remove_Present_ReturnsTrue()
        {
            var l = new FavouritesList();
            l.Add(new AssetRef(G1), "A");
            Assert.IsTrue(l.Remove(new AssetRef(G1)));
            Assert.AreEqual(0, l.Items.Count);
        }
        [Test] public void Remove_Absent_ReturnsFalse()
        {
            var l = new FavouritesList();
            Assert.IsFalse(l.Remove(new AssetRef(G1)));
        }

        [Test] public void AddSearch_TrimsAndRejectsDuplicateOrEmpty()
        {
            var l = new FavouritesList();
            Assert.IsTrue(l.AddSearch("  t:Scene "));
            Assert.IsFalse(l.AddSearch("t:Scene"));
            Assert.IsFalse(l.AddSearch("   "));
            Assert.AreEqual(1, l.Items.Count);
            Assert.IsTrue(l.Items[0].IsSearch);
            Assert.AreEqual("t:Scene", l.Items[0].Query);
            Assert.AreEqual("All Scenes", l.Items[0].Name);
            Assert.IsTrue(l.ContainsSearch(" t:Scene"));
        }
        [Test] public void Search_And_Ref_DoNotCollide()
        {
            var l = new FavouritesList();
            l.AddSearch("player");
            Assert.IsFalse(l.Contains(default));
            Assert.IsTrue(l.Add(new AssetRef(G1), "A"));
            Assert.IsFalse(l.RemoveSearch("other"));
            Assert.IsTrue(l.RemoveSearch("player"));
            Assert.AreEqual(1, l.Items.Count);
            Assert.IsFalse(l.Items[0].IsSearch);
        }
        [TestCase("t:Scene", "All Scenes")]
        [TestCase("t:prefab", "All Prefabs")]
        [TestCase("T: Material", "All Materials")]
        [TestCase("t:Mesh", "All Meshes")]
        [TestCase("t:Font", "All Fonts")]
        [TestCase("t:AnimatorController", "All AnimatorControllers")]
        [TestCase("t:Body", "All Bodies")]
        [TestCase("t:Ray", "All Rays")]
        [TestCase("t:UnityEngine.TextAsset", "All TextAssets")]
        [TestCase("t:Scene player", "t:Scene player")]
        [TestCase("player", "player")]
        [TestCase("t:", "t:")]
        [TestCase("l:Art", "l:Art")]
        public void SearchName(string query, string expected) => Assert.AreEqual(expected, FavouriteEntry.SearchName(query));
    }
}
