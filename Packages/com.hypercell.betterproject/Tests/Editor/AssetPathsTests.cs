using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class AssetPathsTests
    {
        [Test] public void Parent_Cases()
        {
            Assert.AreEqual("Assets/A", AssetPaths.Parent("Assets/A/B"));
            Assert.AreEqual("", AssetPaths.Parent("Assets"));
        }
        [Test] public void Breadcrumbs_ThreeLevels()
        {
            var b = AssetPaths.Breadcrumbs("Assets/Art/Props");
            Assert.AreEqual(3, b.Count);
            Assert.AreEqual(("Assets", "Assets"), b[0]);
            Assert.AreEqual(("Art", "Assets/Art"), b[1]);
            Assert.AreEqual(("Props", "Assets/Art/Props"), b[2]);
            var one = AssetPaths.Breadcrumbs("Assets");
            Assert.AreEqual(1, one.Count);
            Assert.AreEqual(("Assets", "Assets"), one[0]);
        }
        [Test] public void IsProjectAssetPath_Cases()
        {
            Assert.IsTrue(AssetPaths.IsProjectAssetPath("Assets"));
            Assert.IsTrue(AssetPaths.IsProjectAssetPath("Assets/A/b.png"));
            Assert.IsTrue(AssetPaths.IsProjectAssetPath("Packages/com.x/a.cs"));
            Assert.IsTrue(AssetPaths.IsProjectAssetPath("Packages/com.x"));
            Assert.IsFalse(AssetPaths.IsProjectAssetPath("Packages"));
            Assert.IsFalse(AssetPaths.IsProjectAssetPath("ProjectSettings/TagManager.asset"));
            Assert.IsFalse(AssetPaths.IsProjectAssetPath("Library/x"));
            Assert.IsFalse(AssetPaths.IsProjectAssetPath(""));
            Assert.IsFalse(AssetPaths.IsProjectAssetPath(null));
        }
        [Test] public void CanMoveInto_Allowed() => Assert.IsTrue(AssetPaths.CanMoveInto("Assets/A/x.png", "Assets/B"));
        [Test] public void CanMoveInto_Self() => Assert.IsFalse(AssetPaths.CanMoveInto("Assets/A", "Assets/A"));
        [Test] public void CanMoveInto_Descendant() => Assert.IsFalse(AssetPaths.CanMoveInto("Assets/A", "Assets/A/B"));
        [Test] public void CanMoveInto_CaseVariantDescendant() => Assert.IsFalse(AssetPaths.CanMoveInto("Assets/A", "assets/a/B"));
        [Test] public void CanMoveInto_SameParent() => Assert.IsFalse(AssetPaths.CanMoveInto("Assets/A/x.png", "Assets/A"));
        [Test] public void CanMoveInto_SiblingWithSharedPrefix() => Assert.IsTrue(AssetPaths.CanMoveInto("Assets/A", "Assets/AB"));
        [Test] public void CanMoveInto_NonProjectPath_False()
        {
            Assert.IsFalse(AssetPaths.CanMoveInto("Library/x", "Assets/B"));
            Assert.IsFalse(AssetPaths.CanMoveInto("Assets/A", "ProjectSettings"));
        }
    }
}
