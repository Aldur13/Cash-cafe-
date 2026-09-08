using ClosedXML.Excel;

namespace CashCafe.Excel.Tests;

/// <summary>Builds the deliberately awful spreadsheets the importer has to cope with.</summary>
internal static class SheetBuilder
{
    public static MemoryStream Build(Action<IXLWorksheet> fill, string sheetName = "Blad1")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        fill(sheet);

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>The café's own layout: name and amount together in one cell.</summary>
    public static MemoryStream OldCafeSheet() => Build(sheet =>
    {
        sheet.Cell(1, 1).Value = "Carl Jacobs 50 kr";
        sheet.Cell(2, 1).Value = "Astrid Lindqvist 12,50 kr";
        sheet.Cell(3, 1).Value = "Omar Haddad -10 kr";
        sheet.Cell(4, 1).Value = "Åsa Öberg 1 250,50 kr";
    });

    /// <summary>A tidier sheet with real headers.</summary>
    public static MemoryStream ColumnSheet() => Build(sheet =>
    {
        sheet.Cell(1, 1).Value = "Namn";
        sheet.Cell(1, 2).Value = "Klass";
        sheet.Cell(1, 3).Value = "Saldo";

        sheet.Cell(2, 1).Value = "Carl Jacobs";
        sheet.Cell(2, 2).Value = "9B";
        sheet.Cell(2, 3).Value = 50;

        sheet.Cell(3, 1).Value = "Astrid Lindqvist";
        sheet.Cell(3, 2).Value = "8A";
        sheet.Cell(3, 3).Value = 12.5;

        sheet.Cell(4, 1).Value = "Omar Haddad";
        sheet.Cell(4, 2).Value = "9B";
        sheet.Cell(4, 3).Value = -10;
    });
}
