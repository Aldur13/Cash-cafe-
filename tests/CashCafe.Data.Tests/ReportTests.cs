using CashCafe.Domain;
using CashCafe.Domain.Rules;
using FluentAssertions;
using Xunit;

namespace CashCafe.Data.Tests;

public class ReportTests
{
    private static BasketLine Line(long itemId, string name, decimal price, int qty = 1) => new()
    {
        LineNo = 1,
        ItemId = itemId,
        ItemName = name,
        UnitPrice = Money.FromKronor(price),
        Quantity = qty,
    };

    [Fact]
    public void The_day_report_shows_what_sold_and_to_whom()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 100, "9B");
        var astrid = cafe.AddStudent("Astrid", "Lindqvist", 100, "8A");
        var toast = cafe.AddItem("Toast", 10);
        var juice = cafe.AddItem("Juice", 15);

        cafe.Ledger.ExecutePurchase(carl, new[] { Line(toast, "Toast", 10) }, cafe.Config, "Café", "s");
        cafe.Ledger.ExecutePurchase(astrid, new[] { Line(juice, "Juice", 15, qty: 2) }, cafe.Config, "Café", "s");

        var day = cafe.Reports.Day(DateOnly.FromDateTime(DateTime.Today));

        day.Sold.Should().Be(Money.FromKronor(40));
        day.Received.Should().Be(Money.FromKronor(200));
        day.PurchaseCount.Should().Be(2);

        day.ByItem.Should().HaveCount(2);
        day.ByItem[0].ItemName.Should().Be("Juice", "the report is ordered by value");
        day.ByItem[0].Quantity.Should().Be(2);
        day.ByItem[0].Value.Should().Be(Money.FromKronor(30));

        day.ByStudent.Should().HaveCount(2);
        day.ByStudent.Single(s => s.StudentName == "Carl Jacobs").ClassName.Should().Be("9B");
    }

    [Fact]
    public void An_undone_purchase_is_not_counted_as_a_sale()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 100);
        var toast = cafe.AddItem("Toast", 10);

        var purchase = cafe.Ledger.ExecutePurchase(carl, new[] { Line(toast, "Toast", 10) },
            cafe.Config, "Café", "s");

        cafe.Reports.Day(DateOnly.FromDateTime(DateTime.Today)).Sold.Should().Be(Money.FromKronor(10));

        cafe.Ledger.Reverse(purchase.TransactionId, "Wrong student", "Café", "s");

        var after = cafe.Reports.Day(DateOnly.FromDateTime(DateTime.Today));
        after.Sold.Should().Be(Money.Zero, "a purchase that was undone was not a sale");
        after.ByItem.Should().BeEmpty();
        after.PurchaseCount.Should().Be(0);
    }

    [Fact]
    public void The_in_the_red_list_is_worst_first()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 5);
        var omar = cafe.AddStudent("Omar", "Haddad", 0);
        cafe.AddStudent("Astrid", "Lindqvist", 50);

        var toast = cafe.AddItem("Toast", 10);
        cafe.Ledger.ExecutePurchase(carl, new[] { Line(toast, "Toast", 10) }, cafe.Config, "Café", "s");
        cafe.Ledger.ExecutePurchase(omar, new[] { Line(toast, "Toast", 10) }, cafe.Config, "Café", "s");

        var red = cafe.Reports.InTheRed();

        red.Should().HaveCount(2);
        red[0].DisplayName.Should().Be("Omar Haddad", "-10,00 kr is worse than -5,00 kr");
        red[0].Balance.Should().Be(Money.FromKronor(-10));
        red[1].Balance.Should().Be(Money.FromKronor(-5));
    }

    [Fact]
    public void Yesterdays_trading_does_not_appear_in_todays_report()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 100);
        var toast = cafe.AddItem("Toast", 10);

        cafe.Ledger.ExecutePurchase(carl, new[] { Line(toast, "Toast", 10) }, cafe.Config, "Café", "s",
            occurredUtc: DateTimeOffset.UtcNow.AddDays(-1));

        cafe.Reports.Day(DateOnly.FromDateTime(DateTime.Today)).Sold.Should().Be(Money.Zero);
        cafe.Reports.Day(DateOnly.FromDateTime(DateTime.Today.AddDays(-1))).Sold.Should().Be(Money.FromKronor(10));
    }
}
