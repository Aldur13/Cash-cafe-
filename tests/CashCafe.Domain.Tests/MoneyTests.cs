using CashCafe.Domain;
using FluentAssertions;
using Xunit;

namespace CashCafe.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Kronor_are_stored_as_whole_ore()
    {
        Money.FromKronor(10).Ore.Should().Be(1000);
        Money.FromKronor(12.50m).Ore.Should().Be(1250);
        Money.FromKronor(-10).Ore.Should().Be(-1000);
    }

    [Fact]
    public void Formatting_is_swedish()
    {
        Money.FromKronor(40).ToString().Should().Be("40,00 kr");
        Money.FromKronor(12.5m).ToString().Should().Be("12,50 kr");
        // An ASCII hyphen, not the typographic minus modern sv-SE data would give us,
        // so exported negatives still parse as numbers in Excel.
        Money.FromKronor(-10).ToString().Should().Be("-10,00 kr");
        Money.FromKronor(-10).ToString().Should().NotContain("\u2212");
        // A non-breaking space is what sv-SE uses as the thousands separator.
        Money.FromKronor(1250.5m).ToString().Should().Match("1?250,50 kr");
    }

    [Fact]
    public void Repeated_addition_of_ore_never_drifts()
    {
        // The reason money is an integer: 0,10 kr added 1000 times is exactly 100 kr.
        var total = Money.Zero;
        for (var i = 0; i < 1000; i++) total += Money.FromKronor(0.10m);

        total.Should().Be(Money.FromKronor(100));
        total.Ore.Should().Be(10_000);
    }

    [Fact]
    public void Sum_of_a_ledger_equals_the_balance()
    {
        var ledger = new[]
        {
            Money.FromKronor(50),    // deposit
            Money.FromKronor(-10),   // toast
            Money.FromKronor(-30),   // two juice
        };

        Money.Sum(ledger).Should().Be(Money.FromKronor(10));
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(99, 99_000)]
    public void Multiplying_by_a_quantity_is_exact(int quantity, long expectedOre)
    {
        (Money.FromKronor(10) * quantity).Ore.Should().Be(expectedOre);
    }
}
