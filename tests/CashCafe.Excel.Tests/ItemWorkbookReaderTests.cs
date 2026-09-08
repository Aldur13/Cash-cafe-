using CashCafe.Domain;

namespace CashCafe.Excel.Tests;

public class ItemWorkbookReaderTests
{
    private readonly ItemWorkbookReader _reader = new();

    [Fact]
    public void A_menu_sheet_with_swedish_headers_is_read()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Namn";
            sheet.Cell(1, 2).Value = "Kategori";
            sheet.Cell(1, 3).Value = "Pris";
            sheet.Cell(1, 4).Value = "Till salu";

            sheet.Cell(2, 1).Value = "Toast";
            sheet.Cell(2, 2).Value = "Mat";
            sheet.Cell(2, 3).Value = 10;
            sheet.Cell(2, 4).Value = "Ja";

            sheet.Cell(3, 1).Value = "Juice";
            sheet.Cell(3, 2).Value = "Dryck";
            sheet.Cell(3, 3).Value = 15;
            sheet.Cell(3, 4).Value = "Nej";
        });

        var result = _reader.Read(file);

        result.Should().NotBeNull();
        var rows = result!.Value.Rows;
        rows.Should().HaveCount(2);

        rows[0].Name.Should().Be("Toast");
        rows[0].Category.Should().Be("Mat");
        rows[0].Price.Should().Be(Money.FromKronor(10));
        rows[0].Available.Should().BeTrue();

        rows[1].Name.Should().Be("Juice");
        rows[1].Available.Should().BeFalse();
    }

    [Fact]
    public void English_headers_are_also_recognised()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Name";
            sheet.Cell(1, 2).Value = "Price";
            sheet.Cell(2, 1).Value = "Toast";
            sheet.Cell(2, 2).Value = 10;
        });

        var result = _reader.Read(file);

        result.Should().NotBeNull();
        result!.Value.Rows.Should().ContainSingle(r => r.Name == "Toast" && r.Price == Money.FromKronor(10));
    }

    [Fact]
    public void A_sheet_without_a_name_or_price_column_returns_null()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Something";
            sheet.Cell(1, 2).Value = "Else";
            sheet.Cell(2, 1).Value = "x";
            sheet.Cell(2, 2).Value = "y";
        });

        var result = _reader.Read(file);

        result.Should().BeNull();
    }

    [Fact]
    public void A_row_with_an_unreadable_price_is_flagged_as_an_error()
    {
        using var file = SheetBuilder.Build(sheet =>
        {
            sheet.Cell(1, 1).Value = "Namn";
            sheet.Cell(1, 2).Value = "Pris";
            sheet.Cell(2, 1).Value = "Toast";
            sheet.Cell(2, 2).Value = "gratis";
        });

        var result = _reader.Read(file);

        result!.Value.Rows[0].Error.Should().NotBeNull();
    }
}
