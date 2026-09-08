using System.Text.RegularExpressions;
using CashCafe.Domain;
using CashCafe.Domain.Import;
using ClosedXML.Excel;

namespace CashCafe.Excel;

/// <summary>
/// Turns a spreadsheet into <see cref="ParsedRow"/>s. It knows about files and cells and
/// nothing about the café: deciding what to *do* with a row is the planner's job.
/// </summary>
public sealed partial class WorkbookReader
{
    /// <summary>
    /// Splits "Carl Jacobs 50 kr" into a name and an amount.
    ///
    /// The rule is: the amount is the trailing number, and the name is everything before it.
    ///
    /// Note what is deliberately *not* in the separator class: a hyphen. It has to belong to
    /// the amount, because "Omar Haddad -10 kr" is a debt of ten kronor, and a separator class
    /// that ate the minus would import it as a credit of ten — the balance wrong by 20 kr, in
    /// the student's favour, silently. A dash with a space after it ("Carl Jacobs - 50 kr") is
    /// punctuation rather than a sign, and gets trimmed off the end of the name instead.
    /// </summary>
    [GeneratedRegex(@"^(?<name>.+?)[\s.:;]*(?<amount>[-−–—]?\(?\d[\d\s .,']*\)?)\s*(kr|sek|kronor|:-)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameAndAmount();

    private static readonly char[] NameTrailers = { ' ', '\t', '-', '\u2013', '\u2014', '.', ':', ';', ',' };

    /// <summary>Lists the sheets in a workbook, most likely candidate first.</summary>
    public IReadOnlyList<string> SheetNames(Stream file)
    {
        using var workbook = new XLWorkbook(file);
        return workbook.Worksheets.Select(w => w.Name).ToList();
    }

    /// <summary>
    /// Works out how to read a sheet: header row, data start, and what each column holds.
    /// Everything it decides is shown to a person, who can change any of it.
    /// </summary>
    public SheetLayout DetectLayout(Stream file, string? sheetName = null)
    {
        using var workbook = new XLWorkbook(file);
        var sheet = sheetName is null ? workbook.Worksheets.First() : workbook.Worksheet(sheetName);
        return DetectLayout(sheet);
    }

    internal static SheetLayout DetectLayout(IXLWorksheet sheet)
    {
        var used = sheet.RangeUsed();
        if (used is null)
            return new SheetLayout { SheetName = sheet.Name, HeaderRow = 0, FirstDataRow = 1 };

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = Math.Min(used.LastRow().RowNumber(), firstRow + 50);
        var firstColumn = used.FirstColumn().ColumnNumber();
        var lastColumn = used.LastColumn().ColumnNumber();

        // Look for a row whose cells read like column headers.
        for (var row = firstRow; row <= Math.Min(firstRow + 10, lastRow); row++)
        {
            var roles = new Dictionary<int, ColumnRole>();

            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var role = HeaderNames.Match(sheet.Cell(row, column).GetFormattedString());
                if (role != ColumnRole.Ignore) roles[column] = role;
            }

            var hasName = roles.ContainsValue(ColumnRole.Name) ||
                          roles.ContainsValue(ColumnRole.FirstName) ||
                          roles.ContainsValue(ColumnRole.LastName);

            if (hasName)
                return new SheetLayout
                {
                    SheetName = sheet.Name,
                    HeaderRow = row,
                    FirstDataRow = row + 1,
                    Columns = roles,
                };
        }

        // No headers. If there is one column of text, it is the café's own layout with the
        // name and the amount together; otherwise guess name-then-amount by position.
        var textColumns = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
            .Where(c => Enumerable.Range(firstRow, Math.Min(10, lastRow - firstRow + 1))
                .Any(r => !sheet.Cell(r, c).IsEmpty()))
            .ToList();

        if (textColumns.Count == 1)
            return new SheetLayout
            {
                SheetName = sheet.Name,
                HeaderRow = 0,
                FirstDataRow = firstRow,
                IsSingleColumn = true,
                Columns = new Dictionary<int, ColumnRole> { [textColumns[0]] = ColumnRole.Name },
            };

        var guessed = new Dictionary<int, ColumnRole>
        {
            [textColumns.First()] = ColumnRole.Name,
        };

        if (textColumns.Count > 1) guessed[textColumns.Last()] = ColumnRole.Balance;

        return new SheetLayout
        {
            SheetName = sheet.Name,
            HeaderRow = 0,
            FirstDataRow = firstRow,
            Columns = guessed,
        };
    }

    public IReadOnlyList<ParsedRow> Read(Stream file, SheetLayout layout)
    {
        using var workbook = new XLWorkbook(file);
        var sheet = workbook.Worksheet(layout.SheetName);
        var used = sheet.RangeUsed();
        if (used is null) return Array.Empty<ParsedRow>();

        var lastRow = used.LastRow().RowNumber();
        var rows = new List<ParsedRow>();

        for (var rowNumber = layout.FirstDataRow; rowNumber <= lastRow; rowNumber++)
            rows.Add(layout.IsSingleColumn
                ? ReadSingleColumn(sheet, rowNumber, layout)
                : ReadColumns(sheet, rowNumber, layout));

        return rows;
    }

    private static ParsedRow ReadSingleColumn(IXLWorksheet sheet, int rowNumber, SheetLayout layout)
    {
        var column = layout.ColumnFor(ColumnRole.Name) ?? 1;
        var text = sheet.Cell(rowNumber, column).GetFormattedString().Trim();

        if (text.Length == 0) return new ParsedRow { RowNumber = rowNumber, RawText = string.Empty };

        var match = NameAndAmount().Match(text);
        if (!match.Success)
            return new ParsedRow
            {
                RowNumber = rowNumber,
                RawText = text,
                Name = text,
                Error = $"No amount found in '{text}'",
                LooksLikeTotal = HeaderNames.LooksLikeTotal(text),
            };

        var name = match.Groups["name"].Value.TrimEnd(NameTrailers).Trim();
        var parsed = MoneyParser.Parse(match.Groups["amount"].Value);

        return new ParsedRow
        {
            RowNumber = rowNumber,
            RawText = text,
            Name = name,
            Amount = parsed.Success ? parsed.Value : null,
            Error = parsed.Success ? null : parsed.Error,
            LooksLikeTotal = HeaderNames.LooksLikeTotal(name),
        };
    }

    private static ParsedRow ReadColumns(IXLWorksheet sheet, int rowNumber, SheetLayout layout)
    {
        var name = Text(sheet, rowNumber, layout.ColumnFor(ColumnRole.Name));

        if (name.Length == 0)
        {
            var first = Text(sheet, rowNumber, layout.ColumnFor(ColumnRole.FirstName));
            var last = Text(sheet, rowNumber, layout.ColumnFor(ColumnRole.LastName));
            name = string.Join(' ', new[] { first, last }.Where(p => p.Length > 0));
        }

        var className = Text(sheet, rowNumber, layout.ColumnFor(ColumnRole.Class));
        var balanceColumn = layout.ColumnFor(ColumnRole.Balance);
        var rawBalance = balanceColumn is null ? string.Empty : Text(sheet, rowNumber, balanceColumn);
        var raw = string.Join(" | ", new[] { name, className, rawBalance }.Where(p => p.Length > 0));

        if (name.Length == 0) return new ParsedRow { RowNumber = rowNumber, RawText = raw };

        var looksLikeTotal = HeaderNames.LooksLikeTotal(name);

        if (balanceColumn is null)
            return new ParsedRow
            {
                RowNumber = rowNumber,
                RawText = raw,
                Name = name,
                ClassName = className.Length > 0 ? className : null,
                LooksLikeTotal = looksLikeTotal,
            };

        var cell = sheet.Cell(rowNumber, balanceColumn.Value);
        var amount = ReadAmountCell(cell);

        return new ParsedRow
        {
            RowNumber = rowNumber,
            RawText = raw,
            Name = name,
            ClassName = className.Length > 0 ? className : null,
            Amount = amount.Success ? amount.Value : null,
            Error = amount.Success ? null : amount.Error,
            LooksLikeTotal = looksLikeTotal,
        };
    }

    /// <summary>
    /// Reads an amount from a cell. A real numeric cell is taken as its underlying value
    /// rather than the text on screen, so a column formatted to no decimals cannot quietly
    /// turn 12,50 into 13. Anything else falls back to parsing the text.
    /// </summary>
    private static MoneyParseResult ReadAmountCell(IXLCell cell)
    {
        if (cell.IsEmpty()) return MoneyParseResult.Ok(Money.Zero);

        if (cell.DataType == XLDataType.Number && cell.Value.IsNumber)
        {
            var value = (decimal)cell.Value.GetNumber();
            var rounded = decimal.Round(value, 2, MidpointRounding.ToEven);

            return rounded != value
                ? MoneyParseResult.Fail($"{value} has more than two decimals — fix it in the sheet rather than let us guess")
                : MoneyParseResult.Ok(Money.FromKronor(value));
        }

        return MoneyParser.Parse(cell.GetFormattedString());
    }

    private static string Text(IXLWorksheet sheet, int row, int? column) =>
        column is null ? string.Empty : sheet.Cell(row, column.Value).GetFormattedString().Trim();
}
