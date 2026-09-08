using CashCafe.Domain;
using CashCafe.Domain.Rules;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CashCafe.Data.Tests;

/// <summary>
/// The append-only guarantee is the foundation of the whole design, so it is tested by
/// trying to break it directly in SQL — not through the repositories that promise not to.
/// </summary>
public class AppendOnlyTests
{
    private static long APurchase(TestCafe cafe, long student, long item) =>
        cafe.Ledger.ExecutePurchase(student,
            new[] { new BasketLine { LineNo = 1, ItemId = item, ItemName = "Toast", UnitPrice = Money.FromKronor(10) } },
            cafe.Config, "Café", "s").TransactionId;

    [Fact]
    public void A_transaction_cannot_be_updated_even_with_raw_sql()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var id = APurchase(cafe, carl, cafe.AddItem("Toast", 10));

        using var connection = cafe.Database.Open();
        var act = () => connection.Execute("UPDATE transactions SET amount_ore = 0 WHERE id = $id", new { id });

        act.Should().Throw<SqliteException>().WithMessage("*append-only*");
    }

    [Fact]
    public void A_transaction_cannot_be_deleted_even_with_raw_sql()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var id = APurchase(cafe, carl, cafe.AddItem("Toast", 10));

        using var connection = cafe.Database.Open();
        var act = () => connection.Execute("DELETE FROM transactions WHERE id = $id", new { id });

        act.Should().Throw<SqliteException>().WithMessage("*cannot be deleted*");
    }

    [Fact]
    public void Transaction_lines_are_append_only_too()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        APurchase(cafe, carl, cafe.AddItem("Toast", 10));

        using var connection = cafe.Database.Open();

        var update = () => connection.Execute("UPDATE transaction_lines SET quantity = 99");
        var delete = () => connection.Execute("DELETE FROM transaction_lines");

        update.Should().Throw<SqliteException>();
        delete.Should().Throw<SqliteException>();
    }

    [Fact]
    public void The_audit_log_cannot_be_edited()
    {
        using var cafe = new TestCafe();
        cafe.AddStudent("Carl", "Jacobs", 50);

        using var connection = cafe.Database.Open();

        var update = () => connection.Execute("UPDATE audit_log SET actor = 'someone else'");
        var delete = () => connection.Execute("DELETE FROM audit_log");

        update.Should().Throw<SqliteException>().WithMessage("*append-only*");
        delete.Should().Throw<SqliteException>().WithMessage("*append-only*");
    }

    [Fact]
    public void A_deposit_cannot_be_written_negative_even_with_raw_sql()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs");

        using var connection = cafe.Database.Open();
        var act = () => connection.Execute(
            """
            INSERT INTO transactions (student_id, type, amount_ore, balance_after_ore, occurred_utc,
                                      recorded_utc, operator, session_id)
            VALUES ($id, 'DEPOSIT', -500, -500, '2026-09-12T09:00:00Z', '2026-09-12T09:00:00Z', 'x', 'y')
            """, new { id = carl });

        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void A_correction_without_a_reason_is_rejected_by_the_database()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs");

        using var connection = cafe.Database.Open();
        var act = () => connection.Execute(
            """
            INSERT INTO transactions (student_id, type, amount_ore, balance_after_ore, occurred_utc,
                                      recorded_utc, operator, session_id)
            VALUES ($id, 'ADJUSTMENT', 700, 700, '2026-09-12T09:00:00Z', '2026-09-12T09:00:00Z', 'x', 'y')
            """, new { id = carl });

        act.Should().Throw<SqliteException>();
    }
}

public class VerifierTests
{
    [Fact]
    public void A_healthy_cafe_verifies_clean()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var toast = cafe.AddItem("Toast", 10);

        cafe.Ledger.ExecutePurchase(carl,
            new[] { new BasketLine { LineNo = 1, ItemId = toast, ItemName = "Toast", UnitPrice = Money.FromKronor(10) } },
            cafe.Config, "Café", "s");

        cafe.Verifier.Verify().IsClean.Should().BeTrue();
    }

    [Fact]
    public void A_balance_tampered_with_outside_the_program_is_caught_and_repairable()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);

        // What someone editing the file with an external tool would do.
        using (var connection = cafe.Database.Open())
            connection.Execute("UPDATE students SET balance_ore = 999999 WHERE id = $id", new { id = carl });

        var report = cafe.Verifier.Verify();
        report.IsClean.Should().BeFalse();
        report.HasFatalProblem.Should().BeTrue();
        report.Problems.Should().Contain(p => p.Check == "balance vs ledger" && p.Detail.Contains("Carl Jacobs"));

        cafe.Verifier.RepairCachedBalances().Should().Be(1);

        cafe.Balance(carl).Should().Be(Money.FromKronor(50), "the ledger is the truth");
        cafe.Verifier.Verify().IsClean.Should().BeTrue();
    }

    [Fact]
    public void Reconciliation_adds_up_across_a_day_of_trading()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var astrid = cafe.AddStudent("Astrid", "Lindqvist", 100);
        var toast = cafe.AddItem("Toast", 10);

        foreach (var student in new[] { carl, astrid })
            cafe.Ledger.ExecutePurchase(student,
                new[] { new BasketLine { LineNo = 1, ItemId = toast, ItemName = "Toast", UnitPrice = Money.FromKronor(10) } },
                cafe.Config, "Café", "s");

        var (opening, inflow, outflow, closing, reconciles) =
            cafe.Reports.Reconcile(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        reconciles.Should().BeTrue();
        opening.Should().Be(Money.Zero);
        inflow.Should().Be(Money.FromKronor(150));
        outflow.Should().Be(Money.FromKronor(20));
        closing.Should().Be(Money.FromKronor(130));
    }
}
