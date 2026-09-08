using CashCafe.Domain;

namespace CashCafe.Excel.Tests;

/// <summary>
/// The amounts real spreadsheets contain. Every case here came from the wiki's table of
/// what the importer promises to read.
/// </summary>
public class MoneyParserTests
{
    [Theory]
    [InlineData("50", 5000)]
    [InlineData("50 kr", 5000)]
    [InlineData("50kr", 5000)]
    [InlineData("50 SEK", 5000)]
    [InlineData("50:-", 5000)]
    [InlineData("50 kronor", 5000)]
    [InlineData("50,5", 5050)]
    [InlineData("50.5", 5050)]
    [InlineData("50,50", 5050)]
    [InlineData("12,50", 1250)]
    [InlineData("0", 0)]
    [InlineData("0,01", 1)]
    public void Plain_amounts_are_read(string text, long expectedOre)
    {
        var result = MoneyParser.Parse(text);

        result.Success.Should().BeTrue(result.Error);
        result.Value.Ore.Should().Be(expectedOre);
    }

    [Theory]
    [InlineData("1 250", 125000)]        // ordinary space
    [InlineData("1 250", 125000)]   // non-breaking space, what Excel actually writes
    [InlineData("1 250,50", 125050)]
    [InlineData("1.250,50", 125050)]
    [InlineData("1,250.50", 125050)]
    [InlineData("1'250", 125000)]
    public void Thousands_separators_are_understood(string text, long expectedOre)
    {
        MoneyParser.Parse(text).Value.Ore.Should().Be(expectedOre);
    }

    [Theory]
    [InlineData("-10", -1000)]
    [InlineData("-10 kr", -1000)]
    [InlineData("−10", -1000)]      // U+2212, what a Swedish-formatted cell produces
    [InlineData("–10", -1000)]      // en dash, what a person types
    [InlineData("(10)", -1000)]     // accounting brackets
    [InlineData("(10) kr", -1000)]
    public void Negatives_are_read_however_they_are_written(string text, long expectedOre)
    {
        MoneyParser.Parse(text).Value.Ore.Should().Be(expectedOre);
    }

    [Theory]
    [InlineData("50,555")]
    [InlineData("10,999")]
    public void More_than_two_decimals_is_an_error_not_a_rounding(string text)
    {
        var result = MoneyParser.Parse(text);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("two decimals");
    }

    [Theory]
    [InlineData("femtio")]
    [InlineData("ca 50")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("50 kr each")]
    [InlineData("-")]
    public void Anything_ambiguous_is_refused(string text)
    {
        MoneyParser.Parse(text).Success.Should().BeFalse();
    }
}
