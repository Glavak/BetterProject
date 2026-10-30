using NUnit.Framework;

namespace BetterProject.Core.Tests
{
    public class KeyboardLayoutsTests
    {
        // Columns: 0 = Russian, 1 = AZERTY-ish (q<->a), 2 = a non-BMP layout on 'h' only.
        const string Mapping =
            "h р \U00010330\n" +
            "e у \n" +
            "l д \n" +
            "o щ \n" +
            "; ж \n" +
            "q йa \n" +
            "a фq \n";

        [Test] public void Parse_SkipsEmptyColumns()
        {
            Assert.AreEqual(3, KeyboardLayouts.Parse(Mapping).LayoutCount);
        }
        [Test] public void Variants_RussianToUs()
        {
            var v = KeyboardLayouts.Parse(Mapping).Variants("Руддщж");
            Assert.AreEqual("hello;", v[0]);
        }
        [Test] public void Variants_NotReversed()
        {
            var v = KeyboardLayouts.Parse(Mapping).Variants("hello;");
            CollectionAssert.DoesNotContain(v, "руддщж");
        }
        [Test] public void Variants_SkipsUnchangedAndDuplicates()
        {
            // Columns 0 and 2 leave "qa" unchanged; column 1 swaps q/a.
            var v = KeyboardLayouts.Parse(Mapping).Variants("QA");
            CollectionAssert.AreEqual(new[] { "aq" }, v);
        }
        [Test] public void Variants_KeepsUnmappedChars()
        {
            var v = KeyboardLayouts.Parse(Mapping).Variants("р-1");
            Assert.AreEqual("h-1", v[0]);
        }
        [Test] public void Variants_SkipsLayoutMissingNonAsciiChar()
        {
            // Column 1 maps q/a but not р: "рq" would come out half-converted.
            var v = KeyboardLayouts.Parse(Mapping).Variants("рq");
            CollectionAssert.AreEqual(new[] { "hq" }, v);
        }
        [Test] public void Variants_NonBmpColumnsStayAligned()
        {
            var v = KeyboardLayouts.Parse(Mapping).Variants("\U00010330");
            CollectionAssert.AreEqual(new[] { "h" }, v);
        }
        [Test] public void Variants_EmptyOrNoMapping()
        {
            Assert.IsEmpty(KeyboardLayouts.Parse(Mapping).Variants(""));
            Assert.IsEmpty(KeyboardLayouts.Parse("").Variants("руддщ"));
        }
    }
}
