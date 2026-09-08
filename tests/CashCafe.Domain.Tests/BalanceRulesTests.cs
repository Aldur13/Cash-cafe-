using CashCafe.Domain;
using CashCafe.Domain.Rules;
using FluentAssertions;
using Xunit;

namespace CashCafe.Domain.Tests;

public class BalanceRulesTests
{
    private static readonly Money Floor = Money.FromKronor(-10);

    [Fact]
    public void A_normal_purchase_is_allowed()
    {
        var check = BalanceRules.CheckPurchase(Money.FromKronor(50), Floor, Money.FromKronor(40));

        check.IsAllowed.Should().BeTrue();
        check.BalanceAfter.Should().Be(Money.FromKronor(10));
        check.CanSpend.Should().Be(Money.FromKronor(60));
    }

    [Fact]
    public void Landing_exactly_on_the_floor_is_allowed()
    {
        // 50 kr with a -10 kr floor means exactly 60 kr may be spent.
        var check = BalanceRules.CheckPurchase(Money.FromKronor(50), Floor, Money.FromKronor(60));

        check.IsAllowed.Should().BeTrue();
        check.BalanceAfter.Should().Be(Floor);
    }

    [Fact]
    public void One_ore_past_the_floor_is_refused()
    {
        var check = BalanceRules.CheckPurchase(Money.FromKronor(50), Floor, new Money(6001));

        check.IsAllowed.Should().BeFalse();
        check.Shortfall.Ore.Should().Be(1);
    }

    [Fact]
    public void A_student_already_in_debt_can_still_spend_up_to_the_floor()
    {
        var check = BalanceRules.CheckPurchase(Money.FromKronor(-5), Floor, Money.FromKronor(5));

        check.IsAllowed.Should().BeTrue();
        check.CanSpend.Should().Be(Money.FromKronor(5));
        check.BalanceAfter.Should().Be(Floor);
    }

    [Fact]
    public void A_student_at_the_floor_can_buy_nothing_at_all()
    {
        var check = BalanceRules.CheckPurchase(Floor, Floor, new Money(1));

        check.IsAllowed.Should().BeFalse();
        check.CanSpend.Should().Be(Money.Zero);
    }

    [Fact]
    public void The_refusal_message_names_the_numbers_staff_need()
    {
        var message = BalanceRules.RefusalMessage("Carl Jacobs", Money.FromKronor(50), Floor, Money.FromKronor(72.50m));

        message.Should().Contain("Carl Jacobs")
               .And.Contain("50,00 kr")     // what they have
               .And.Contain("60,00 kr")     // what they can spend
               .And.Contain("72,50 kr")     // what it costs
               .And.Contain("12,50 kr");    // how much too much
    }

    [Fact]
    public void A_per_student_credit_limit_overrides_the_cafe_default()
    {
        var settings = CafeSettings.Defaults;

        BalanceRules.EffectiveFloor(null, settings).Should().Be(Money.FromKronor(-10));
        BalanceRules.EffectiveFloor(Money.FromKronor(-50), settings).Should().Be(Money.FromKronor(-50));
        BalanceRules.EffectiveFloor(Money.Zero, settings).Should().Be(Money.Zero);
    }

    [Fact]
    public void A_student_limited_to_zero_cannot_go_negative_at_all()
    {
        var check = BalanceRules.CheckPurchase(Money.FromKronor(5), Money.Zero, Money.FromKronor(10));

        check.IsAllowed.Should().BeFalse();
        check.CanSpend.Should().Be(Money.FromKronor(5));
    }

    [Fact]
    public void The_cafe_floor_can_never_be_set_positive()
    {
        var act = () => CafeSettings.Defaults.WithMinimumBalance(Money.FromKronor(10));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Deposits_must_be_positive()
    {
        BalanceRules.IsValidDeposit(Money.FromKronor(100)).Should().BeTrue();
        BalanceRules.IsValidDeposit(Money.Zero).Should().BeFalse();
        BalanceRules.IsValidDeposit(Money.FromKronor(-1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(5000, 4000, true)]    // 50 kr, buys 40 kr
    [InlineData(5000, 6000, true)]    // exactly to the floor
    [InlineData(5000, 6001, false)]   // one öre too far
    [InlineData(0, 1000, true)]       // nothing, buys a 10 kr toast, lands on the floor
    [InlineData(0, 1001, false)]
    [InlineData(-1000, 1, false)]     // already at the floor
    public void The_floor_holds_across_the_boundary(long balanceOre, long totalOre, bool allowed)
    {
        BalanceRules.CheckPurchase(new Money(balanceOre), Floor, new Money(totalOre))
            .IsAllowed.Should().Be(allowed);
    }
}
