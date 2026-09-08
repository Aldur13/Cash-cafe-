using CashCafe.Domain;
using CashCafe.Domain.Import;
using ClosedXML.Excel;

namespace CashCafe.Excel;

/// <summary>
/// Reads a menu spreadsheet — the kind <see cref="WorkbookExporter.WriteMenu"/> writes, or one
/// a person built by hand with a "Name" and "Price" column. Deciding what to do with a row is
/// <see cref="ItemImportPlanner"/>'s job; this only turns cells into <see cref="ParsedItemRow"/>s.
/// </summary>
public sealed class ItemWorkbookReader
{
    public IReadOnlyList<string> SheetNames(Stream file)
    {
        using var workbook = new XLWorkbook(file);
        return workbook.Worksheets.Select(w => w.Name).ToList();
    }

    /// <summary>
    /// Finds the header row and reads every row under it until the sheet runs out. Returns
    /// null if no row with both a Name and a Price column could be found in the first 10 rows.
    /// </summary>
    public (string SheetName, IReadOnlyList<ParsedItemRow> Rows)? Read(Stream file, string? sheetName = null)
    {
        using var workbook = new XLWorkbook(file);
        var sheet = sheetName is null ? workbook.Worksheets.First() : workbook.Worksheet(sheetName);

        var used = sheet.RangeUsed();
        if (used is null) return (sheet.Name, Array.Empty<ParsedItemRow>());

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var firstColumn = used.FirstColumn().ColumnNumber();
        var lastColumn = used.LastColumn().ColumnNumber();

        Dictionary<int, ItemColumnRole>? columns = null;
        var headerRow = 0;

        for (var row = firstRow; row <= Math.Min(firstRow + 10, lastRow); row++)
        {
            var roles = new Dictionary<int, ItemColumnRole>();
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var role = ItemHeaderNames.Match(sheet.Cell(row, column).GetFormattedString());
                if (role != ItemColumnRole.Ignore) roles[column] = role;
            }

            if (roles.ContainsValue(ItemColumnRole.Name) && roles.ContainsValue(ItemColumnRole.Price))
            {
                columns = roles;
                headerRow = row;
                break;
            }
        }

        if (columns is null) return null;

        var nameColumn = columns.First(c => c.Value == ItemColumnRole.Name).Key;
        var priceColumn = columns.First(c => c.Value == ItemColumnRole.Price).Key;
        var categoryColumn = columns.FirstOrDefault(c => c.Value == ItemColumnRole.Category).Key;
        var availableColumn = columns.FirstOrDefault(c => c.Value == ItemColumnRole.Available).Key;

        var rows = new List<ParsedItemRow>();

        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var name = sheet.Cell(rowNumber, nameColumn).GetFormattedString().Trim();
            var rawPrice = sheet.Cell(rowNumber, priceColumn).GetFormattedString().Trim();

            if (name.Length == 0 && rawPrice.Length == 0) continue;

            Money? price = null;
            string? error = null;

            if (rawPrice.Length > 0)
            {
                var parsed = MoneyParser.Parse(rawPrice);
                if (parsed.Success) price = parsed.Value;
                else error = $"'{rawPrice}' is not an amount";
            }
            else if (name.Length > 0)
            {
                error = "No price.";
            }

            var category = categoryColumn > 0
                ? sheet.Cell(rowNumber, categoryColumn).GetFormattedString().Trim()
                : null;

            bool? available = null;
            if (availableColumn > 0)
            {
                var text = sheet.Cell(rowNumber, availableColumn).GetFormattedString().Trim();
                if (text.Length > 0) available = ParseYesNo(text);
            }

            rows.Add(new ParsedItemRow
            {
                RowNumber = rowNumber,
                RawText = $"{name} {rawPrice}".Trim(),
                Name = name.Length == 0 ? null : name,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                Price = price,
                Available = available,
                Error = error,
            });
        }

        return (sheet.Name, rows);
    }

    private static bool ParseYesNo(string text) => text.Trim().ToLowerInvariant() switch
    {
        "ja" or "yes" or "y" or "true" or "1" or "x" => true,
        "nej" or "no" or "n" or "false" or "0" => false,
        _ => true,
    };
}
