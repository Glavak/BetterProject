using System;
using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class AssetRefTests
    {
        const string G = "0123456789abcdef0123456789abcdef";

        [Test] public void Ctor_AcceptsLowercaseHexGuid() => Assert.AreEqual("0123456789abcdef0123456789abcdef", new AssetRef("0123456789abcdef0123456789abcdef").Guid);
        [Test] public void Ctor_RejectsBadGuid([Values(null, "", "xyz", "0123456789ABCDEF0123456789ABCDEF", "0123")] string g) => Assert.Throws<ArgumentException>(() => new AssetRef(g));
        [Test] public void Equality_ByGuidAndLocalId()
        {
            Assert.AreEqual(new AssetRef(G, 5), new AssetRef(G, 5));
            Assert.AreNotEqual(new AssetRef(G, 5), new AssetRef(G, 0));
        }
        [Test] public void IsSubAsset_WhenLocalIdNonZero() { Assert.IsFalse(new AssetRef(G).IsSubAsset); Assert.IsTrue(new AssetRef(G, -7).IsSubAsset); }
    }
}
