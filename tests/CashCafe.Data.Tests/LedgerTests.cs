using CashCafe.Data;
using CashCafe.Domain;
using CashCafe.Domain.Rules;
using FluentAssertions;
using Xunit;

namespace CashCafe.Data.Tests;

public class LedgerTests
{
    private static BasketLine Line(int no, long itemId, string name, decimal price, int qty = 1) => new()
    {
        LineNo = no,
        ItemId = itemId,
        ItemName = name,
        UnitPrice = Money.FromKronor(price),
        Quantity = qty,
    };

    [Fact]
    public void A_purchase_moves_the_balance_and_records_its_lines()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);
        var juice = cafe.AddItem("Juice", 15);

        var result = cafe.Ledger.ExecutePurchase(carl,
            new[] { Line(1, toast, "Toast", 10), Line(2, juice, "Juice", 15, qty: 2) },
            cafe.Config, "Café (till 1)", "session-1");

        result.Succeeded.Should().BeTrue();
        result.BalanceAfter.Should().Be(Money.FromKronor(10));
        cafe.Balance(carl).Should().Be(Money.FromKronor(10));

        var history = cafe.Ledger.History(carl);
        var purchase = history.Single(t => t.Type == TransactionType.Purchase);
        purchase.Amount.Should().Be(Money.FromKronor(-40));
        purchase.Lines.Should().HaveCount(2);
        purchase.Lines[1].Quantity.Should().Be(2);
        purchase.Lines[1].LineTotal.Should().Be(Money.FromKronor(30));
    }

    [Fact]
    public void The_floor_is_enforced_in_the_database_not_only_on_screen()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var big = cafe.AddItem("Tårta", 72.50m);

        var result = cafe.Ledger.ExecutePurchase(carl,
            new[] { Line(1, big, "Tårta", 72.50m) },
            cafe.Config, "Café (till 1)", "session-1");

        result.Outcome.Should().Be(LedgerOutcome.RefusedByFloor);
        result.Message.Should().Contain("12,50 kr");
        cafe.Balance(carl).Should().Be(Money.FromKronor(50), "a refused purchase changes nothing");
        cafe.Ledger.History(carl).Should().ContainSingle("only the opening deposit");
    }

    [Fact]
    public void A_purchase_landing_exactly_on_the_floor_is_committed()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var item = cafe.AddItem("Stor beställning", 60);

        var result = cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, item, "Stor beställning", 60) },
            cafe.Config, "Café", "s");

        result.Succeeded.Should().BeTrue();
        cafe.Balance(carl).Should().Be(Money.FromKronor(-10));
    }

    [Fact]
    public void A_price_changed_in_another_window_stops_the_purchase()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);

        // The till has 10 kr on screen; an admin raises the price before Execute.
        cafe.Items.ChangePrice(toast, Money.FromKronor(12), "new supplier", "admin");

        var result = cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) },
            cafe.Config, "Café", "s");

        result.Outcome.Should().Be(LedgerOutcome.PriceChanged);
        result.Message.Should().Contain("12,00 kr");
        cafe.Balance(carl).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_deposit_adds_money_and_keeps_its_reference()
    {
        using var cafe = new TestCafe();
        var astrid = cafe.AddStudent("Astrid", "Lindqvist");

        cafe.Ledger.Deposit(astrid, Money.FromKronor(100), DepositMethod.Swish, "SW-4471", "Admin", "s");

        cafe.Balance(astrid).Should().Be(Money.FromKronor(100));
        var deposit = cafe.Ledger.History(astrid).Single();
        deposit.Method.Should().Be(DepositMethod.Swish);
        deposit.Reference.Should().Be("SW-4471");
    }

    [Fact]
    public void A_deposit_must_be_positive()
    {
        using var cafe = new TestCafe();
        var astrid = cafe.AddStudent("Astrid", "Lindqvist");

        cafe.Ledger.Deposit(astrid, Money.FromKronor(-50), DepositMethod.Cash, null, "Admin", "s")
            .Outcome.Should().Be(LedgerOutcome.NothingToDo);
    }

    [Fact]
    public void Undo_writes_a_reversal_and_deletes_nothing()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);

        var purchase = cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) },
            cafe.Config, "Café", "s");

        var undo = cafe.Ledger.Reverse(purchase.TransactionId, "Undo at counter", "Café", "s");

        undo.Succeeded.Should().BeTrue();
        cafe.Balance(carl).Should().Be(Money.FromKronor(50));

        var history = cafe.Ledger.History(carl);
        history.Should().HaveCount(3, "deposit, purchase and reversal all stay in the record");
        history.Should().Contain(t => t.Type == TransactionType.Purchase);
        history.Single(t => t.Type == TransactionType.Reversal).ReversesId.Should().Be(purchase.TransactionId);
    }

    [Fact]
    public void The_same_transaction_cannot_be_reversed_twice()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);

        var purchase = cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) },
            cafe.Config, "Café", "s");

        cafe.Ledger.Reverse(purchase.TransactionId, "first", "Café", "s").Succeeded.Should().BeTrue();
        cafe.Ledger.Reverse(purchase.TransactionId, "second", "Café", "s")
            .Outcome.Should().Be(LedgerOutcome.AlreadyReversed);

        cafe.Balance(carl).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_reversal_cannot_itself_be_reversed()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);

        var purchase = cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) },
            cafe.Config, "Café", "s");
        var reversal = cafe.Ledger.Reverse(purchase.TransactionId, "undo", "Café", "s");

        cafe.Ledger.Reverse(reversal.TransactionId, "undo the undo", "Café", "s")
            .Outcome.Should().Be(LedgerOutcome.NothingToDo);
    }

    [Fact]
    public void Undo_is_refused_outside_the_window_but_an_admin_can_still_reverse()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);

        var purchase = cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) },
            cafe.Config, "Café", "s", occurredUtc: DateTimeOffset.UtcNow.AddHours(-2));

        cafe.Ledger.Reverse(purchase.TransactionId, "Undo at counter", "Café", "s",
                undoWindow: TimeSpan.FromMinutes(15))
            .Outcome.Should().Be(LedgerOutcome.OutsideUndoWindow);

        // An admin reverses without a window.
        cafe.Ledger.Reverse(purchase.TransactionId, "Wrong student", "Admin", "s")
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_correction_needs_a_reason()
    {
        using var cafe = new TestCafe();
        var omar = cafe.AddStudent("Omar", "Haddad", 10);

        cafe.Ledger.Adjust(omar, Money.FromKronor(7), "  ", "Admin", "s")
            .Outcome.Should().Be(LedgerOutcome.NothingToDo);

        cafe.Ledger.Adjust(omar, Money.FromKronor(7), "Excel rounding, agreed with Omar", "Admin", "s")
            .Succeeded.Should().BeTrue();

        cafe.Balance(omar).Should().Be(Money.FromKronor(17));
    }

    [Fact]
    public void A_deactivated_student_cannot_be_charged()
    {
        using var cafe = new TestCafe();
        var gone = cafe.AddStudent("Gone", "Student", 50);
        var toast = cafe.AddItem("Toast", 10);
        cafe.Students.SetActive(gone, false, "admin");

        cafe.Ledger.ExecutePurchase(gone, new[] { Line(1, toast, "Toast", 10) }, cafe.Config, "Café", "s")
            .Outcome.Should().Be(LedgerOutcome.StudentInactive);
    }

    [Fact]
    public void A_purchase_stores_the_price_it_was_sold_at()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 100);
        var toast = cafe.AddItem("Toast", 10);

        cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) }, cafe.Config, "Café", "s");
        cafe.Items.ChangePrice(toast, Money.FromKronor(20), "inflation", "admin");

        var line = cafe.Ledger.History(carl).Single(t => t.Type == TransactionType.Purchase).Lines[0];

        line.UnitPrice.Should().Be(Money.FromKronor(10), "history keeps the price that was charged");
        cafe.Items.Find(toast)!.Price.Should().Be(Money.FromKronor(20));
    }

    [Fact]
    public void A_per_student_credit_limit_is_honoured_by_the_write_path()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 5);
        var toast = cafe.AddItem("Toast", 10);
        cafe.Students.SetCreditLimit(carl, Money.Zero, "cash only, agreed with guardian", "admin");

        cafe.Ledger.ExecutePurchase(carl, new[] { Line(1, toast, "Toast", 10) }, cafe.Config, "Café", "s")
            .Outcome.Should().Be(LedgerOutcome.RefusedByFloor);
    }
}
