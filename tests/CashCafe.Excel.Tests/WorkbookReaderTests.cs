using CashCafe.Domain;

namespace CashCafe.Excel.Tests;

public class WorkbookReaderTests
{
    private readonly WorkbookReader _reader = new();

    [Fact]
    public void The_cafes_own_single_column_sheet_is_read()
    {
        using var file = SheetBuilder.OldCafeSheet();
        var layout = _reader.DetectLayout(file);

        layout.IsSingleColumn.Should().BeTrue();

        file.Position = 0;
        var rows = _reader.Read(file, layout);

        rows.Should().HaveCount(4);
        rows[0].Name.Should().Be("Carl Jacobs");
        rows[0].Amount.Should().Be(Money.FromKronor(50));
        rows[1].Amount.Should().Be(Money.FromKronor(12.50m));
        rows[2].Amount.Should().Be(Money.FromKronor(-10));
        rows[3].Name.Should().Be("Åsa Öberg");
        rows[3].Amount.Should().Be(Money.FromKronor(1250.50m));
    }

    [Fact]
    public void Headers_are_detected_in_swedish()
    {
        using var file = SheetBuilder.ColumnSheet();
        var layout = _reader.DetectLayout(file);

        layout.IsSingleColumn.Should().BeFalse();
        layout.HeaderRow.Should().Be(1);
        layout.FirstDataRow.Should().Be(2);
        layout.ColumnFor(ColumnRole.Name).Should().Be(1);
        layout.ColumnFor(ColumnRole.Class).Should().Be(2);
        layout.ColumnFor(ColumnRole.Balance).Should().Be(3);
    }

    [Fact]
    public void A_column_sheet_is_read_with_classes()
    {
        using var file = SheetBuilder.ColumnSheet();
        var layout = _reader.DetectLayout(file);
        file.Position = 0;

        var rows = _reader.Read(file, layout);

        rows.Should().HaveCount(3);
        rows[0].Name.Should().Be("Carl Jacobs");
        rows[0].ClassName.Should().Be("9B");
        rows[0].Amount.Should().Be(Money.FromKronor(50));
        rows[1].Amount.Should().Be(Money.FromKronor(12.50m));
        rows[2].Amount.Should().Be(Money.FromKronor(-10));
    }

    [Fact]
    public void Headers_are_detected_in_english_too()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Student";
            sheet.Cell(1, 2).Value = "Balance";
            sheet.Cell(2, 1).Value = "Carl Jacobs";
            sheet.Cell(2, 2).Value = 50;
        });

        var layout = _reader.DetectLayout(file);

        layout.ColumnFor(ColumnRole.Name).Should().Be(1);
        layout.ColumnFor(ColumnRole.Balance).Should().Be(2);
    }

    [Fact]
    public void Separate_first_and_last_name_columns_are_joined()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Förnamn";
            sheet.Cell(1, 2).Value = "Efternamn";
            sheet.Cell(1, 3).Value = "Saldo";
            sheet.Cell(2, 1).Value = "Carl";
            sheet.Cell(2, 2).Value = "Jacobs";
            sheet.Cell(2, 3).Value = 50;
        });

        var layout = _reader.DetectLayout(file);
        file.Position = 0;
        var rows = _reader.Read(file, layout);

        rows[0].Name.Should().Be("Carl Jacobs");
        rows[0].Amount.Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_number_cell_is_read_by_value_not_by_what_the_formatting_shows()
    {
        // A cell holding 12.50 formatted to zero decimals displays "13". Reading the text
        // would quietly lose 50 öre of somebody's money.
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Namn";
            sheet.Cell(1, 2).Value = "Saldo";
            sheet.Cell(2, 1).Value = "Carl Jacobs";
            sheet.Cell(2, 2).Value = 12.5;
            sheet.Cell(2, 2).Style.NumberFormat.Format = "0";
        });

        var layout = _reader.DetectLayout(file);
        file.Position = 0;

        _reader.Read(file, layout)[0].Amount.Should().Be(Money.FromKronor(12.50m));
    }

    [Fact]
    public void A_cell_with_more_than_two_decimals_is_an_error_row()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Namn";
            sheet.Cell(1, 2).Value = "Saldo";
            sheet.Cell(2, 1).Value = "Carl Jacobs";
            sheet.Cell(2, 2).Value = 50.555;
        });

        var layout = _reader.DetectLayout(file);
        file.Position = 0;
        var row = _reader.Read(file, layout)[0];

        row.Error.Should().Contain("two decimals");
        row.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void A_formula_is_read_as_its_result()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Namn";
            sheet.Cell(1, 2).Value = "Saldo";
            sheet.Cell(2, 1).Value = "Carl Jacobs";
            sheet.Cell(2, 2).FormulaA1 = "=20+30";
        });

        var layout = _reader.DetectLayout(file);
        file.Position = 0;

        _reader.Read(file, layout)[0].Amount.Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void Blank_rows_and_totals_rows_are_flagged_rather_than_imported()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Namn";
            sheet.Cell(1, 2).Value = "Saldo";
            sheet.Cell(2, 1).Value = "Carl Jacobs";
            sheet.Cell(2, 2).Value = 50;
            // row 3 deliberately blank
            sheet.Cell(4, 1).Value = "Summa";
            sheet.Cell(4, 2).Value = 50;
        });

        var layout = _reader.DetectLayout(file);
        file.Position = 0;
        var rows = _reader.Read(file, layout);

        rows.Should().HaveCount(3);
        rows[0].IsUsable.Should().BeTrue();
        rows[1].Name.Should().BeNullOrEmpty("row 3 is blank");
        rows[2].LooksLikeTotal.Should().BeTrue();
        rows[2].IsUsable.Should().BeFalse();
    }

    [Fact]
    public void A_name_that_cannot_be_split_from_an_amount_is_an_error_not_a_guess()
    {
        using var file = SheetBuilder.Build(sheet => sheet.Cell(1, 1).Value = "Carl Jacobs femtio kronor");

        var layout = _reader.DetectLayout(file);
        file.Position = 0;
        var row = _reader.Read(file, layout)[0];

        row.IsUsable.Should().BeFalse();
        row.Error.Should().NotBeNull();
    }

    [Fact]
    public void Sheets_can_be_listed_and_chosen()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        workbook.AddWorksheet("Tom");
        var real = workbook.AddWorksheet("Saldon");
        real.Cell(1, 1).Value = "Namn";
        real.Cell(1, 2).Value = "Saldo";
        real.Cell(2, 1).Value = "Carl Jacobs";
        real.Cell(2, 2).Value = 50;

        using var file = new MemoryStream();
        workbook.SaveAs(file);
        file.Position = 0;

        _reader.SheetNames(file).Should().Contain(new[] { "Tom", "Saldon" });

        file.Position = 0;
        var layout = _reader.DetectLayout(file, "Saldon");
        file.Position = 0;

        _reader.Read(file, layout)[0].Name.Should().Be("Carl Jacobs");
    }
}
