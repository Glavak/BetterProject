using System;
using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class RefListJsonTests
    {
        const string G = "0123456789abcdef0123456789abcdef";
        const string G2 = "fedcba9876543210fedcba9876543210";

        [Test] public void WriteFavourites_ExactFormat()
        {
            var l = new FavouritesList();
            l.Add(new AssetRef(G, 0), "Player.prefab");
            var expected = "{\n  \"version\": 1,\n  \"favourites\": [\n    { \"guid\": \"" + G + "\", \"localId\": \"0\", \"name\": \"Player.prefab\" }\n  ]\n}\n";
            Assert.AreEqual(expected, RefListJson.WriteFavourites(l));
        }
        [Test] public void WriteFavourites_MultipleEntries_CommaSeparated()
        {
            var l = new FavouritesList();
            l.Add(new AssetRef(G), "A"); l.Add(new AssetRef(G2, 5), "B");
            var expected = "{\n  \"version\": 1,\n  \"favourites\": [\n" +
                "    { \"guid\": \"" + G + "\", \"localId\": \"0\", \"name\": \"A\" },\n" +
                "    { \"guid\": \"" + G2 + "\", \"localId\": \"5\", \"name\": \"B\" }\n  ]\n}\n";
            Assert.AreEqual(expected, RefListJson.WriteFavourites(l));
        }
        [Test] public void WriteFavourites_Empty()
            => Assert.AreEqual("{\n  \"version\": 1,\n  \"favourites\": []\n}\n", RefListJson.WriteFavourites(new FavouritesList()));
        [Test] public void WriteRecents_ExactFormat()
        {
            var l = new RecentsList();
            l.Push(new AssetRef(G2, 7)); l.Push(new AssetRef(G));
            var expected = "{\n  \"version\": 1,\n  \"recents\": [\n" +
                "    { \"guid\": \"" + G + "\", \"localId\": \"0\" },\n" +
                "    { \"guid\": \"" + G2 + "\", \"localId\": \"7\" }\n  ]\n}\n";
            Assert.AreEqual(expected, RefListJson.WriteRecents(l));
        }
        [Test] public void WriteRecents_Empty()
            => Assert.AreEqual("{\n  \"version\": 1,\n  \"recents\": []\n}\n", RefListJson.WriteRecents(new RecentsList()));

        [Test] public void Favourites_RoundTrip_LargeAndNegativeLocalId()
        {
            var ids = new[] { long.MaxValue, long.MinValue, -2345678901234567890L };
            var l = new FavouritesList();
            for (int i = 0; i < ids.Length; i++) l.Add(new AssetRef(G, ids[i]), "n" + i);
            var r = RefListJson.ReadFavourites(RefListJson.WriteFavourites(l));
            Assert.AreEqual(3, r.Items.Count);
            for (int i = 0; i < ids.Length; i++) Assert.AreEqual(ids[i], r.Items[i].Ref.LocalId);
        }
        [Test] public void Favourites_RoundTrip_EscapesName()
        {
            const string name = "a\"b\\c\nd\té😀\u0001";
            var l = new FavouritesList();
            l.Add(new AssetRef(G), name);
            var json = RefListJson.WriteFavourites(l);
            StringAssert.Contains("\\u0001", json);
            StringAssert.Contains("é😀", json);
            Assert.AreEqual(name, RefListJson.ReadFavourites(json).Items[0].Name);
        }
        [Test] public void Favourites_WriteIsStable()
        {
            var l = new FavouritesList();
            l.Add(new AssetRef(G, -3), "x\"y"); l.Add(new AssetRef(G2), "");
            var once = RefListJson.WriteFavourites(l);
            Assert.AreEqual(once, RefListJson.WriteFavourites(RefListJson.ReadFavourites(once)));
        }
        [Test] public void ReadFavourites_ToleratesWhitespaceAndCrLf()
        {
            var json = "  {\r\n \"version\" : 1 ,\r\n\t\"favourites\":[\r\n { \"guid\":\"" + G + "\" , \"localId\":\"9\", \"name\":\"N\" }\r\n ]\r\n}\r\n";
            var r = RefListJson.ReadFavourites(json);
            Assert.AreEqual(1, r.Items.Count);
            Assert.AreEqual(new AssetRef(G, 9), r.Items[0].Ref);
            Assert.AreEqual("N", r.Items[0].Name);
        }
        [Test] public void ReadFavourites_ConflictMarkers_Throws()
            => Assert.Throws<FormatException>(() => RefListJson.ReadFavourites("<<<<<<< HEAD\n{}\n=======\n{}\n>>>>>>> x"));

        [Test] public void Read_Rejects([Values(
            "", "{}", "[]", "null", "{\"version\":2,\"favourites\":[]}", "{\"favourites\":[]}",
            "{\"version\":\"1\",\"favourites\":[]}",
            "{\"version\":1,\"favourites\":{}}",
            "{\"version\":1,\"favourites\":[1]}",
            "{\"version\":1,\"favourites\":[{\"guid\":\"bad\",\"localId\":\"0\",\"name\":\"\"}]}",
            "{\"version\":1,\"favourites\":[{\"guid\":5,\"localId\":\"0\",\"name\":\"\"}]}",
            "{\"version\":1,\"favourites\":[{\"guid\":\"0123456789abcdef0123456789abcdef\",\"localId\":\"1.5\",\"name\":\"\"}]}",
            "{\"version\":1,\"favourites\":[{\"guid\":\"0123456789abcdef0123456789abcdef\",\"localId\":0,\"name\":\"\"}]}",
            "{\"version\":1,\"favourites\":[{\"guid\":\"0123456789abcdef0123456789abcdef\",\"localId\":\"99999999999999999999\",\"name\":\"\"}]}",
            "{\"version\":1,\"favourites\":[{\"guid\":\"0123456789abcdef0123456789abcdef\",\"localId\":\"0\",\"name\":\"\"}]} trailing",
            "{\"version\":1,\"favourites\":[",
            "{\"version\":1,\"favourites\":[{\"guid\":\"0123456789abcdef0123456789abcdef\",\"localId\":\"0\",\"name\":\"\\q\"}]}"
        )] string json)
            => Assert.Throws<FormatException>(() => RefListJson.ReadFavourites(json));

        [Test] public void ReadRecents_Rejects([Values("", "{}", "{\"version\":1,\"favourites\":[]}", "{\"version\":1,\"recents\":[{\"guid\":\"bad\",\"localId\":\"0\"}]}")] string json)
            => Assert.Throws<FormatException>(() => RefListJson.ReadRecents(json));

        [Test] public void ReadFavourites_DuplicateEntries_KeepsFirst()
        {
            var json = "{\"version\":1,\"favourites\":[" +
                "{\"guid\":\"" + G + "\",\"localId\":\"0\",\"name\":\"first\"}," +
                "{\"guid\":\"" + G + "\",\"localId\":\"0\",\"name\":\"second\"}]}";
            var r = RefListJson.ReadFavourites(json);
            Assert.AreEqual(1, r.Items.Count);
            Assert.AreEqual("first", r.Items[0].Name);
        }
        [Test] public void Recents_RoundTrip_PreservesOrder()
        {
            var l = new RecentsList();
            l.Push(new AssetRef(G, 1)); l.Push(new AssetRef(G2, -2)); l.Push(new AssetRef(G, 3));
            var r = RefListJson.ReadRecents(RefListJson.WriteRecents(l));
            CollectionAssert.AreEqual(l.Items, r.Items);
        }
        [Test] public void ReadRecents_MoreThanLimit_Truncates()
        {
            var sb = new System.Text.StringBuilder("{\"version\":1,\"recents\":[");
            for (int i = 0; i < 12; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"guid\":\"" + i.ToString("x32") + "\",\"localId\":\"0\"}");
            }
            sb.Append("]}");
            var r = RefListJson.ReadRecents(sb.ToString());
            Assert.AreEqual(10, r.Items.Count);
            Assert.AreEqual(new AssetRef(0.ToString("x32")), r.Items[0]);
            Assert.AreEqual(new AssetRef(9.ToString("x32")), r.Items[9]);
        }
        [Test] public void ReadRecents_CustomLimit_Truncates()
        {
            var l = new RecentsList(5);
            for (int i = 0; i < 5; i++) l.Push(new AssetRef(i.ToString("x32")));
            var r = RefListJson.ReadRecents(RefListJson.WriteRecents(l), 3);
            Assert.AreEqual(3, r.Limit);
            CollectionAssert.AreEqual(new[] { l.Items[0], l.Items[1], l.Items[2] }, r.Items);
        }
        [Test] public void Recents_CustomKey_RoundTrip()
        {
            var l = new RecentsList();
            l.Push(new AssetRef(G, 0));
            string json = RefListJson.WriteRecents(l, "newAssets");
            StringAssert.Contains("\"newAssets\": [", json);
            CollectionAssert.AreEqual(l.Items, RefListJson.ReadRecents(json, RecentsList.DefaultLimit, "newAssets").Items);
            Assert.Throws<FormatException>(() => RefListJson.ReadRecents(json)); // "recents" key missing
        }
    }
}
