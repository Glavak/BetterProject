using System.Collections.Generic;
using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class DetailsColumnsTests
    {
        static string Order(DetailsColumns c)
        {
            var keys = new List<string>();
            foreach (var col in c.All) keys.Add((col.Visible ? "" : "-") + DetailsColumns.Key(col.Id));
            return string.Join(",", keys);
        }

        static DetailsColumns AllShown()
        {
            var c = new DetailsColumns();
            foreach (var col in new List<DetailsColumns.Column>(c.All)) c.SetVisible(col.Id, true);
            return c;
        }

        [Test] public void Default_NameSizeInfoShown()
            => Assert.AreEqual("name,-type,size,-modified,-labels,info", Order(new DetailsColumns()));

        [Test] public void Name_CannotBeHidden()
        {
            var c = new DetailsColumns();
            Assert.IsFalse(c.SetVisible(DetailsColumn.Name, false));
            Assert.IsTrue(c.IsVisible(DetailsColumn.Name));
        }

        [Test] public void SetVisible_ReportsChange()
        {
            var c = new DetailsColumns();
            Assert.IsTrue(c.SetVisible(DetailsColumn.Size, false));
            Assert.IsFalse(c.SetVisible(DetailsColumn.Size, false));
            Assert.AreEqual(2, c.Visible().Count);
        }

        [Test] public void SetWidth_Clamped()
        {
            var c = new DetailsColumns();
            c.SetWidth(DetailsColumn.Type, 5);
            Assert.AreEqual(DetailsColumns.MinWidth, c.WidthOf(DetailsColumn.Type));
            c.SetWidth(DetailsColumn.Type, 100000);
            Assert.AreEqual(DetailsColumns.MaxWidth, c.WidthOf(DetailsColumn.Type));
        }

        [Test] public void MoveTo_Front()
        {
            var c = AllShown();
            Assert.IsTrue(c.MoveTo(DetailsColumn.Size, 0));
            Assert.AreEqual("size,name,type,modified,labels,info", Order(c));
        }

        [Test] public void MoveTo_End()
        {
            var c = AllShown();
            Assert.IsTrue(c.MoveTo(DetailsColumn.Name, 6));
            Assert.AreEqual("type,size,modified,labels,info,name", Order(c));
        }

        [Test] public void MoveTo_Right()
        {
            var c = AllShown();
            Assert.IsTrue(c.MoveTo(DetailsColumn.Type, 4)); // before labels
            Assert.AreEqual("name,size,modified,type,labels,info", Order(c));
        }

        [Test] public void MoveTo_OwnGaps_NoChange()
        {
            var c = AllShown();
            Assert.IsFalse(c.MoveTo(DetailsColumn.Type, 1));
            Assert.IsFalse(c.MoveTo(DetailsColumn.Type, 2));
            Assert.AreEqual("name,type,size,modified,labels,info", Order(c));
        }

        [Test] public void MoveTo_GapsCountShownColumnsOnly()
        {
            var c = AllShown();
            c.SetVisible(DetailsColumn.Size, false);
            c.SetVisible(DetailsColumn.Modified, false);
            // Shown: name, type, labels, info. Gap 3 = before info.
            Assert.IsTrue(c.MoveTo(DetailsColumn.Name, 3));
            Assert.AreEqual("type,-size,-modified,labels,name,info", Order(c));
        }

        [Test] public void MoveTo_Hidden_NoChange()
        {
            var c = AllShown();
            c.SetVisible(DetailsColumn.Info, false);
            Assert.IsFalse(c.MoveTo(DetailsColumn.Info, 0));
        }

        [Test] public void Serialize_RoundTrip()
        {
            var c = AllShown();
            c.MoveTo(DetailsColumn.Info, 1);
            c.SetVisible(DetailsColumn.Labels, false);
            c.SetWidth(DetailsColumn.Type, 133.5f);
            string s = c.Serialize();
            Assert.AreEqual(s, DetailsColumns.Parse(s).Serialize());
            Assert.AreEqual("name,info,type,size,modified,-labels", Order(DetailsColumns.Parse(s)));
            Assert.AreEqual(133.5f, DetailsColumns.Parse(s).WidthOf(DetailsColumn.Type));
        }

        [Test] public void Parse_Tolerant()
        {
            var c = DetailsColumns.Parse("bogus:5,-name:abc,size:90,size:10");
            Assert.AreEqual("name,size,-type,-modified,-labels,info", Order(c));
            Assert.AreEqual(90f, c.WidthOf(DetailsColumn.Size));
            Assert.AreEqual(200f, c.WidthOf(DetailsColumn.Name));
            Assert.AreEqual("name,-type,size,-modified,-labels,info", Order(DetailsColumns.Parse(null)));
        }

        [Test] public void FormatDuration_Cases()
        {
            Assert.AreEqual("0.53 s", DetailsColumns.FormatDuration(0.531));
            Assert.AreEqual("59.99 s", DetailsColumns.FormatDuration(59.99));
            Assert.AreEqual("1:05", DetailsColumns.FormatDuration(65.2));
            Assert.AreEqual("1:01:01", DetailsColumns.FormatDuration(3661));
            Assert.AreEqual("", DetailsColumns.FormatDuration(-1));
        }
    }
}
