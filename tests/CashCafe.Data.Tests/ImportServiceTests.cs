using CashCafe.Domain;
using CashCafe.Domain.Import;
using FluentAssertions;
using Xunit;

namespace CashCafe.Data.Tests;

public class ImportServiceTests
{
    private static ParsedRow Row(int number, string name, decimal amount, string? className = null) => new()
    {
        RowNumber = number,
        RawText = $"{name} {amount} kr",
        Name = name,
        ClassName = className,
        Amount = Money.FromKronor(amount),
    };

    private static ImportPreview PlanAgainst(TestCafe cafe, params ParsedRow[] rows) =>
        ImportPlanner.Plan(rows, cafe.Students.All(includeInactive: true), "gamla-listan.xlsx", "Blad1");

    [Fact]
    public void Importing_the_old_sheet_creates_students_with_their_opening_balances()
    {
        using var cafe = new TestCafe();

        var preview = PlanAgainst(cafe,
            Row(1, "Carl Jacobs", 50, "9B"),
            Row(2, "Astrid Lindqvist", 12.50m, "8A"),
            Row(3, "Omar Haddad", -10, "9B"));

        var result = cafe.Imports.Apply(preview, "/archive/gamla-listan.xlsx", "Admin", "s");

        result.StudentsCreated.Should().Be(3);
        result.TotalChange.Should().Be(Money.FromKronor(52.50m));

        var students = cafe.Students.All();
        students.Should().HaveCount(3);
        students.Single(s => s.DisplayName == "Carl Jacobs").Balance.Should().Be(Money.FromKronor(50));
        students.Single(s => s.DisplayName == "Carl Jacobs").ClassName.Should().Be("9B");
        students.Single(s => s.DisplayName == "Omar Haddad").Balance.Should().Be(Money.FromKronor(-10));
    }

    [Fact]
    public void Every_imported_balance_can_be_traced_back_to_a_row_in_the_file()
    {
        using var cafe = new TestCafe();
        var preview = PlanAgainst(cafe, Row(7, "Carl Jacobs", 50));

        cafe.Imports.Apply(preview, "/archive/gamla-listan.xlsx", "Admin", "s");

        var carl = cafe.Students.All().Single();
        var opening = cafe.Ledger.History(carl.Id).Single();

        opening.Type.Should().Be(TransactionType.Import);
        opening.Reference.Should().Be("Opening balance from gamla-listan.xlsx, row 7");
        opening.ImportId.Should().NotBeNull();
    }

    [Fact]
    public void A_student_who_already_exists_gets_a_correction_for_the_difference()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 30);

        var preview = PlanAgainst(cafe, Row(2, "Carl Jacobs", 50));
        preview.Rows[0].Action.Should().Be(ImportAction.AdjustToFileValue);

        cafe.Imports.Apply(preview, "/archive/f.xlsx", "Admin", "s");

        cafe.Balance(carl).Should().Be(Money.FromKronor(50));
        cafe.Ledger.History(carl).Should().HaveCount(2, "the opening deposit and the import correction");
    }

    [Fact]
    public void An_import_with_an_unreadable_row_writes_nothing_at_all()
    {
        using var cafe = new TestCafe();

        var preview = PlanAgainst(cafe,
            Row(2, "Carl Jacobs", 50),
            new ParsedRow { RowNumber = 3, RawText = "Astrid femtio", Name = "Astrid", Error = "not an amount" });

        var result = cafe.Imports.Apply(preview, "/archive/f.xlsx", "Admin", "s");

        result.StudentsCreated.Should().Be(0);
        result.Message.Should().Contain("could not be read");
        cafe.Students.All().Should().BeEmpty("an import is all or nothing");
    }

    [Fact]
    public void The_whole_import_can_be_undone_as_one_batch()
    {
        using var cafe = new TestCafe();

        var preview = PlanAgainst(cafe, Row(1, "Carl Jacobs", 50), Row(2, "Astrid Lindqvist", 20));
        var applied = cafe.Imports.Apply(preview, "/archive/f.xlsx", "Admin", "s");

        cafe.Students.All().Sum(s => s.Balance.Ore).Should().Be(7000);

        var undone = cafe.Imports.Undo(applied.ImportId, "Admin", "s");

        undone.TransactionsWritten.Should().Be(2);
        cafe.Students.All().Sum(s => s.Balance.Ore).Should().Be(0);
    }

    [Fact]
    public void Undoing_an_import_reverses_rather_than_deletes()
    {
        using var cafe = new TestCafe();
        var applied = cafe.Imports.Apply(PlanAgainst(cafe, Row(1, "Carl Jacobs", 50)),
            "/archive/f.xlsx", "Admin", "s");

        cafe.Imports.Undo(applied.ImportId, "Admin", "s");

        var carl = cafe.Students.All(includeInactive: true).Single();
        var history = cafe.Ledger.History(carl.Id);

        history.Should().HaveCount(2);
        history.Should().Contain(t => t.Type == TransactionType.Import);
        history.Should().Contain(t => t.Type == TransactionType.Reversal);
        cafe.Verifier.Verify().IsClean.Should().BeTrue();
    }

    [Fact]
    public void An_import_cannot_be_undone_twice()
    {
        using var cafe = new TestCafe();
        var applied = cafe.Imports.Apply(PlanAgainst(cafe, Row(1, "Carl Jacobs", 50)),
            "/archive/f.xlsx", "Admin", "s");

        cafe.Imports.Undo(applied.ImportId, "Admin", "s").TransactionsWritten.Should().Be(1);
        cafe.Imports.Undo(applied.ImportId, "Admin", "s").Message.Should().Contain("already been undone");

        cafe.Students.All(includeInactive: true).Single().Balance.Should().Be(Money.Zero);
    }

    [Fact]
    public void The_import_history_records_what_was_imported_and_by_whom()
    {
        using var cafe = new TestCafe();
        cafe.Imports.Apply(PlanAgainst(cafe, Row(1, "Carl Jacobs", 50)), "/archive/f.xlsx", "Fröken Ek", "s");

        var history = cafe.Imports.History();

        history.Should().ContainSingle();
        history[0].FileName.Should().Be("gamla-listan.xlsx");
        history[0].By.Should().Be("Fröken Ek");
        history[0].Total.Should().Be(Money.FromKronor(50));
        history[0].Undone.Should().BeFalse();
    }

    [Fact]
    public void Importing_leaves_the_cafe_verifiably_consistent()
    {
        using var cafe = new TestCafe();

        cafe.Imports.Apply(PlanAgainst(cafe,
            Row(1, "Carl Jacobs", 50),
            Row(2, "Astrid Lindqvist", 12.50m),
            Row(3, "Omar Haddad", -10)), "/archive/f.xlsx", "Admin", "s");

        cafe.Verifier.Verify().IsClean.Should().BeTrue();
    }
}
