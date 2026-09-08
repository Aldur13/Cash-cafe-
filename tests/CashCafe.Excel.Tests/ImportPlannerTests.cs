using CashCafe.Domain;
using CashCafe.Domain.Import;

namespace CashCafe.Excel.Tests;

public class ImportPlannerTests
{
    private static Student Existing(long id, string name, decimal balance) => new()
    {
        Id = id,
        FirstName = name.Split(' ')[0],
        LastName = name.Split(' ')[^1],
        DisplayName = name,
        SearchName = SearchNormalizer.Normalize(name),
        Balance = Money.FromKronor(balance),
    };

    private static ParsedRow Row(int number, string name, decimal amount) => new()
    {
        RowNumber = number,
        RawText = $"{name} {amount}",
        Name = name,
        Amount = Money.FromKronor(amount),
    };

    [Fact]
    public void A_name_the_cafe_does_not_have_becomes_a_new_student()
    {
        var preview = ImportPlanner.Plan(new[] { Row(2, "Carl Jacobs", 50) }, Array.Empty<Student>(), "gamla.xlsx");

        preview.Rows.Should().ContainSingle();
        preview.Rows[0].Status.Should().Be(ImportStatus.NewStudent);
        preview.Rows[0].Action.Should().Be(ImportAction.CreateStudent);
        preview.TotalChange.Should().Be(Money.FromKronor(50));
        preview.CanImport.Should().BeTrue();
    }

    [Fact]
    public void A_matching_balance_is_skipped()
    {
        var preview = ImportPlanner.Plan(
            new[] { Row(2, "Carl Jacobs", 50) },
            new[] { Existing(1, "Carl Jacobs", 50) },
            "gamla.xlsx");

        preview.Rows[0].Status.Should().Be(ImportStatus.ExistsSameBalance);
        preview.Rows[0].Action.Should().Be(ImportAction.Skip);
        preview.TotalChange.Should().Be(Money.Zero);
    }

    [Fact]
    public void A_different_balance_becomes_a_correction_for_the_difference()
    {
        var preview = ImportPlanner.Plan(
            new[] { Row(2, "Carl Jacobs", 50) },
            new[] { Existing(1, "Carl Jacobs", 30) },
            "gamla.xlsx");

        var row = preview.Rows[0];
        row.Status.Should().Be(ImportStatus.ExistsDifferentBalance);
        row.Action.Should().Be(ImportAction.AdjustToFileValue);
        row.EffectiveChange.Should().Be(Money.FromKronor(20), "the file says 50 and the café says 30");
        row.Note.Should().Contain("30,00 kr").And.Contain("50,00 kr");
    }

    [Fact]
    public void Keeping_the_cafes_value_writes_nothing()
    {
        var preview = ImportPlanner.Plan(
                new[] { Row(2, "Carl Jacobs", 50) },
                new[] { Existing(1, "Carl Jacobs", 30) },
                "gamla.xlsx")
            .WithAll(ImportStatus.ExistsDifferentBalance, ImportAction.KeepExisting);

        preview.TotalChange.Should().Be(Money.Zero);
    }

    [Fact]
    public void Treating_the_file_as_top_ups_adds_rather_than_sets()
    {
        var preview = ImportPlanner.Plan(
                new[] { Row(2, "Carl Jacobs", 50) },
                new[] { Existing(1, "Carl Jacobs", 30) },
                "veckans-swish.xlsx")
            .WithAll(ImportStatus.ExistsDifferentBalance, ImportAction.AddToBalance);

        preview.Rows[0].EffectiveChange.Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_name_with_the_parts_swapped_is_the_same_person()
    {
        var preview = ImportPlanner.Plan(
            new[] { Row(2, "Jacobs Carl", 50) },
            new[] { Existing(1, "Carl Jacobs", 50) },
            "sorterad-pa-efternamn.xlsx");

        preview.Rows[0].Status.Should().Be(ImportStatus.ExistsSameBalance);
        preview.Rows[0].MatchedStudentId.Should().Be(1);
    }

    [Fact]
    public void A_very_similar_name_is_flagged_but_never_merged_automatically()
    {
        var preview = ImportPlanner.Plan(
            new[] { Row(2, "Carl Jacobsson", 50) },
            new[] { Existing(1, "Carl Jacobs", 30) },
            "gamla.xlsx");

        var row = preview.Rows[0];
        row.Status.Should().Be(ImportStatus.PossibleDuplicate);
        row.Action.Should().Be(ImportAction.CreateStudent, "similar is not the same person — a human decides");
        row.MatchedStudentName.Should().Be("Carl Jacobs");
    }

    [Fact]
    public void The_same_name_twice_in_one_file_is_flagged()
    {
        var preview = ImportPlanner.Plan(
            new[] { Row(2, "Carl Jacobs", 50), Row(7, "carl  JACOBS", 20) },
            Array.Empty<Student>(),
            "gamla.xlsx");

        preview.Rows[1].Status.Should().Be(ImportStatus.DuplicateInFile);
        preview.Rows[1].Action.Should().Be(ImportAction.Ignore);
        preview.Rows[1].Note.Should().Contain("row 2");
    }

    [Fact]
    public void An_unreadable_row_blocks_the_whole_import()
    {
        var rows = new[]
        {
            Row(2, "Carl Jacobs", 50),
            new ParsedRow { RowNumber = 3, RawText = "Astrid femtio", Name = "Astrid", Error = "not an amount" },
        };

        var preview = ImportPlanner.Plan(rows, Array.Empty<Student>(), "gamla.xlsx");

        preview.Errors.Should().Be(1);
        preview.CanImport.Should().BeFalse("importing most of a balance sheet is worse than importing none of it");
    }

    [Fact]
    public void Totals_rows_and_blanks_are_left_out_of_the_figures()
    {
        var rows = new[]
        {
            Row(2, "Carl Jacobs", 50),
            new ParsedRow { RowNumber = 3, RawText = string.Empty },
            new ParsedRow { RowNumber = 4, RawText = "Summa 50", Name = "Summa", Amount = Money.FromKronor(50), LooksLikeTotal = true },
        };

        var preview = ImportPlanner.Plan(rows, Array.Empty<Student>(), "gamla.xlsx");

        preview.Skipped.Should().Be(2);
        preview.TotalInFile.Should().Be(Money.FromKronor(50), "the totals row is not a student");
        preview.WillCreate.Should().Be(1);
    }

    [Fact]
    public void A_single_row_can_be_changed_without_touching_the_others()
    {
        var preview = ImportPlanner.Plan(
            new[] { Row(2, "Carl Jacobs", 50), Row(3, "Astrid Lindqvist", 20) },
            Array.Empty<Student>(),
            "gamla.xlsx");

        var edited = preview.With(3, ImportAction.Ignore);

        edited.WillCreate.Should().Be(1);
        edited.TotalChange.Should().Be(Money.FromKronor(50));
    }
}
