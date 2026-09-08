using CashCafe.Domain;
using ClosedXML.Excel;

namespace CashCafe.Excel;

/// <summary>
/// Writes the café's data out as a spreadsheet.
///
/// Two rules run through all of it. Money is written as a real number with a money format,
/// never as text, so a column can be summed without anyone reformatting it. And dates are
/// written as real dates in ISO order, so they sort correctly wherever the file ends up.
/// </summary>
public sealed class WorkbookExporter
{
    private const string MoneyFormat = "# ##0.00";
    private const string DateFormat = "yyyy-mm-dd hh:mm";

    /// <summary>The whole café in one workbook — the archive file.</summary>
    public void WriteEverything(ExportData data, Stream destination)
    {
        using var workbook = new XLWorkbook();

        WriteStudents(workbook, data);
        WriteTransactions(workbook, data);
        WriteTransactionLines(workbook, data);
        WriteItems(workbook, data);
        WritePriceHistory(workbook, data);
        WriteDeposits(workbook, data);
        WriteAuditLog(workbook, data);
        WriteSummary(workbook, data);

        workbook.SaveAs(destination);
    }

    /// <summary>
    /// Name and balance, and nothing else — the direct replacement for the old sheet, and
    /// the file to hand someone who just wants to see who has what.
    /// </summary>
    public void WriteBalances(ExportData data, Stream destination, bool legacySingleColumn = false)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Saldon");

        if (legacySingleColumn)
        {
            // The café's original format: "Carl Jacobs 50 kr" in one cell. Offered because
            // somebody will always want to read it the way they always have.
            var row = 1;
            foreach (var student in data.Students.OrderBy(s => s.DisplayName, StringComparer.CurrentCulture))
                sheet.Cell(row++, 1).Value = $"{student.DisplayName} {student.Balance}";

            sheet.Column(1).AdjustToContents();
            workbook.SaveAs(destination);
            return;
        }

        Header(sheet, "Namn", "Klass", "Saldo");

        var line = 2;
        foreach (var student in data.Students.OrderBy(s => s.DisplayName, StringComparer.CurrentCulture))
        {
            sheet.Cell(line, 1).Value = student.DisplayName;
            sheet.Cell(line, 2).Value = student.ClassName ?? string.Empty;
            MoneyCell(sheet.Cell(line, 3), student.Balance);
            line++;
        }

        Total(sheet, line, 3, Money.Sum(data.Students.Select(s => s.Balance)), "Totalt");
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(destination);
    }

    /// <summary>
    /// The menu on its own — what you hand someone to edit prices in Excel and bring back in,
    /// or to set up a new café's items in one go instead of clicking Add item thirty times.
    /// Archived items are left out: there is no way to bring one back through import, so
    /// including it would be a promise the re-import cannot keep.
    /// </summary>
    public void WriteMenu(IReadOnlyList<Item> items, Stream destination)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Varor");

        Header(sheet, "Namn", "Kategori", "Pris", "Kortkommando", "Till salu");

        var line = 2;
        foreach (var item in items.Where(i => !i.IsArchived).OrderBy(i => i.SortOrder).ThenBy(i => i.Name))
        {
            sheet.Cell(line, 1).Value = item.Name;
            sheet.Cell(line, 2).Value = item.Category ?? string.Empty;
            MoneyCell(sheet.Cell(line, 3), item.Price);
            sheet.Cell(line, 4).Value = item.ShortcutKey ?? string.Empty;
            sheet.Cell(line, 5).Value = item.IsAvailable ? "Ja" : "Nej";
            line++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(destination);
    }

    /// <summary>One student's complete history — what you give a parent who asks.</summary>
    public void WriteStatement(ExportData data, Student student, Stream destination)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Kontoutdrag");

        sheet.Cell(1, 1).Value = data.CafeName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = student.DisplayName;
        sheet.Cell(2, 2).Value = student.ClassName ?? string.Empty;
        sheet.Cell(3, 1).Value = "Saldo";
        MoneyCell(sheet.Cell(3, 2), student.Balance);
        sheet.Cell(4, 1).Value = $"Utskrivet {data.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

        Header(sheet, 6, "Datum", "Typ", "Beskrivning", "Belopp", "Saldo efter");

        var line = 7;
        foreach (var transaction in data.Transactions
                     .Where(t => t.StudentId == student.Id)
                     .OrderBy(t => t.OccurredUtc))
        {
            DateCell(sheet.Cell(line, 1), transaction.OccurredUtc);
            sheet.Cell(line, 2).Value = transaction.Type.ToString();
            sheet.Cell(line, 3).Value = Describe(transaction);
            MoneyCell(sheet.Cell(line, 4), transaction.Amount);
            MoneyCell(sheet.Cell(line, 5), transaction.BalanceAfter);

            if (data.ReversedTransactionIds.Contains(transaction.Id))
                sheet.Row(line).Style.Font.Strikethrough = true;

            line++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(destination);
    }

    private void WriteStudents(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("Students");
        Header(sheet, "StudentId", "Name", "Class", "Balance", "CreditLimit", "Active",
            "HasSiteLogin", "Created", "LastActivity", "Note");

        var line = 2;
        foreach (var student in data.Students.OrderBy(s => s.DisplayName, StringComparer.CurrentCulture))
        {
            sheet.Cell(line, 1).Value = student.Id;
            sheet.Cell(line, 2).Value = student.DisplayName;
            sheet.Cell(line, 3).Value = student.ClassName ?? string.Empty;
            MoneyCell(sheet.Cell(line, 4), student.Balance);
            if (student.CreditLimit is not null) MoneyCell(sheet.Cell(line, 5), student.CreditLimit.Value);
            sheet.Cell(line, 6).Value = student.IsActive ? "Ja" : "Nej";
            sheet.Cell(line, 7).Value = student.HasLogin ? "Ja" : "Nej";
            DateCell(sheet.Cell(line, 8), student.CreatedUtc);
            if (student.LastActivityUtc is not null) DateCell(sheet.Cell(line, 9), student.LastActivityUtc.Value);
            sheet.Cell(line, 10).Value = student.Note ?? string.Empty;
            line++;
        }

        sheet.Columns().AdjustToContents();
    }

    private void WriteTransactions(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("Transactions");
        Header(sheet, "TransactionId", "DateTime", "StudentId", "StudentName", "Type", "Amount",
            "BalanceAfter", "Method", "Reference", "Reason", "ReversesTransactionId", "Cancelled",
            "Operator", "ImportId");

        var names = data.Students.ToDictionary(s => s.Id, s => s.DisplayName);
        var line = 2;

        foreach (var transaction in data.Transactions.OrderBy(t => t.OccurredUtc).ThenBy(t => t.Id))
        {
            sheet.Cell(line, 1).Value = transaction.Id;
            DateCell(sheet.Cell(line, 2), transaction.OccurredUtc);
            sheet.Cell(line, 3).Value = transaction.StudentId;
            sheet.Cell(line, 4).Value = names.GetValueOrDefault(transaction.StudentId, string.Empty);
            sheet.Cell(line, 5).Value = transaction.Type.ToString().ToUpperInvariant();
            MoneyCell(sheet.Cell(line, 6), transaction.Amount);
            MoneyCell(sheet.Cell(line, 7), transaction.BalanceAfter);
            sheet.Cell(line, 8).Value = transaction.Method?.ToString() ?? string.Empty;
            sheet.Cell(line, 9).Value = transaction.Reference ?? string.Empty;
            sheet.Cell(line, 10).Value = transaction.Reason ?? string.Empty;
            if (transaction.ReversesId is not null) sheet.Cell(line, 11).Value = transaction.ReversesId.Value;
            sheet.Cell(line, 12).Value = data.ReversedTransactionIds.Contains(transaction.Id) ? "Ja" : string.Empty;
            sheet.Cell(line, 13).Value = transaction.Operator;
            if (transaction.ImportId is not null) sheet.Cell(line, 14).Value = transaction.ImportId.Value;
            line++;
        }

        sheet.Columns().AdjustToContents();
    }

    private void WriteTransactionLines(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("TransactionLines");
        Header(sheet, "TransactionId", "LineNo", "ItemId", "ItemName", "UnitPrice", "Quantity", "LineTotal");

        var line = 2;
        foreach (var transaction in data.Transactions.OrderBy(t => t.Id))
            foreach (var item in transaction.Lines)
            {
                sheet.Cell(line, 1).Value = transaction.Id;
                sheet.Cell(line, 2).Value = item.LineNo;
                if (item.ItemId is not null) sheet.Cell(line, 3).Value = item.ItemId.Value;
                // The name and price as they were at the time of sale, not as they are now.
                sheet.Cell(line, 4).Value = item.ItemName;
                MoneyCell(sheet.Cell(line, 5), item.UnitPrice);
                sheet.Cell(line, 6).Value = item.Quantity;
                MoneyCell(sheet.Cell(line, 7), item.LineTotal);
                line++;
            }

        sheet.Columns().AdjustToContents();
    }

    private void WriteItems(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("Items");
        Header(sheet, "ItemId", "Name", "Category", "Price", "Shortcut", "Available", "Archived");

        var line = 2;
        foreach (var item in data.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Name))
        {
            sheet.Cell(line, 1).Value = item.Id;
            sheet.Cell(line, 2).Value = item.Name;
            sheet.Cell(line, 3).Value = item.Category ?? string.Empty;
            MoneyCell(sheet.Cell(line, 4), item.Price);
            sheet.Cell(line, 5).Value = item.ShortcutKey ?? string.Empty;
            sheet.Cell(line, 6).Value = item.IsAvailable ? "Ja" : "Nej";
            sheet.Cell(line, 7).Value = item.IsArchived ? "Ja" : "Nej";
            line++;
        }

        sheet.Columns().AdjustToContents();
    }

    private void WritePriceHistory(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("PriceHistory");
        Header(sheet, "ItemId", "ItemName", "Price", "ValidFrom", "ValidTo", "ChangedBy", "Reason");

        var line = 2;
        foreach (var price in data.PriceHistory)
        {
            sheet.Cell(line, 1).Value = price.ItemId;
            sheet.Cell(line, 2).Value = price.ItemName;
            MoneyCell(sheet.Cell(line, 3), price.Price);
            DateCell(sheet.Cell(line, 4), price.ValidFromUtc);
            if (price.ValidToUtc is not null) DateCell(sheet.Cell(line, 5), price.ValidToUtc.Value);
            sheet.Cell(line, 6).Value = price.ChangedBy;
            sheet.Cell(line, 7).Value = price.Reason ?? string.Empty;
            line++;
        }

        sheet.Columns().AdjustToContents();
    }

    private void WriteDeposits(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("Deposits");
        Header(sheet, "TransactionId", "DateTime", "StudentName", "Amount", "Method", "Reference", "Operator");

        var names = data.Students.ToDictionary(s => s.Id, s => s.DisplayName);
        var line = 2;

        foreach (var deposit in data.Transactions
                     .Where(t => t.Type is TransactionType.Deposit or TransactionType.Import)
                     .OrderBy(t => t.OccurredUtc))
        {
            sheet.Cell(line, 1).Value = deposit.Id;
            DateCell(sheet.Cell(line, 2), deposit.OccurredUtc);
            sheet.Cell(line, 3).Value = names.GetValueOrDefault(deposit.StudentId, string.Empty);
            MoneyCell(sheet.Cell(line, 4), deposit.Amount);
            sheet.Cell(line, 5).Value = deposit.Method?.ToString() ?? string.Empty;
            sheet.Cell(line, 6).Value = deposit.Reference ?? string.Empty;
            sheet.Cell(line, 7).Value = deposit.Operator;
            line++;
        }

        sheet.Columns().AdjustToContents();
    }

    private void WriteAuditLog(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("AuditLog");
        Header(sheet, "DateTime", "Actor", "Action", "Entity", "EntityId", "Before", "After", "Detail");

        var line = 2;
        foreach (var entry in data.AuditLog.OrderBy(a => a.OccurredUtc))
        {
            DateCell(sheet.Cell(line, 1), entry.OccurredUtc);
            sheet.Cell(line, 2).Value = entry.Actor;
            sheet.Cell(line, 3).Value = entry.Action;
            sheet.Cell(line, 4).Value = entry.EntityType ?? string.Empty;
            if (entry.EntityId is not null) sheet.Cell(line, 5).Value = entry.EntityId.Value;
            sheet.Cell(line, 6).Value = entry.OldValue ?? string.Empty;
            sheet.Cell(line, 7).Value = entry.NewValue ?? string.Empty;
            sheet.Cell(line, 8).Value = entry.Detail ?? string.Empty;
            line++;
        }

        sheet.Columns().AdjustToContents();
    }

    /// <summary>
    /// The sheet that lets the workbook prove itself: totals, counts, and the reconciliation
    /// check. If deposits minus sales does not equal the sum of the balances, the file says so
    /// in a cell rather than leaving someone to notice.
    /// </summary>
    private void WriteSummary(XLWorkbook workbook, ExportData data)
    {
        var sheet = workbook.AddWorksheet("Summary");

        sheet.Cell(1, 1).Value = data.CafeName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = "Exported";
        DateCell(sheet.Cell(2, 2), data.GeneratedUtc);
        sheet.Cell(3, 1).Value = "Program version";
        sheet.Cell(3, 2).Value = data.AppVersion;
        sheet.Cell(4, 1).Value = "Filter";
        sheet.Cell(4, 2).Value = data.FilterDescription ?? "Everything";

        var balances = Money.Sum(data.Students.Select(s => s.Balance));
        var inflow = Money.Sum(data.Transactions.Where(t => t.Amount > Money.Zero).Select(t => t.Amount));
        var outflow = Money.Sum(data.Transactions.Where(t => t.Amount < Money.Zero).Select(t => t.Amount)).Negated();

        sheet.Cell(6, 1).Value = "Students";
        sheet.Cell(6, 2).Value = data.Students.Count;
        sheet.Cell(7, 1).Value = "Transactions";
        sheet.Cell(7, 2).Value = data.Transactions.Count;
        sheet.Cell(8, 1).Value = "Money in";
        MoneyCell(sheet.Cell(8, 2), inflow);
        sheet.Cell(9, 1).Value = "Money out";
        MoneyCell(sheet.Cell(9, 2), outflow);
        sheet.Cell(10, 1).Value = "Sum of balances";
        MoneyCell(sheet.Cell(10, 2), balances);

        sheet.Cell(12, 1).Value = "Reconciles (in − out = balances)";
        var reconciles = inflow - outflow == balances;
        sheet.Cell(12, 2).Value = reconciles ? "Ja" : "NEJ — something is wrong, see the wiki";
        sheet.Cell(12, 2).Style.Font.Bold = !reconciles;

        sheet.Columns().AdjustToContents();
    }

    /// <summary>
    /// The line a person reads on a statement. A purchase lists what was bought; a deposit
    /// says how the money arrived and quotes the reference after it. A bare "SW-4471" in the
    /// description column explains nothing to the parent the statement was printed for.
    /// </summary>
    private static string Describe(Transaction transaction)
    {
        if (transaction.Lines.Count > 0)
            return string.Join(", ", transaction.Lines.Select(l =>
                l.Quantity > 1 ? $"{l.ItemName} ×{l.Quantity}" : l.ItemName));

        var description = transaction.Type switch
        {
            TransactionType.Deposit => transaction.Method is null ? "Insättning" : $"Insättning ({transaction.Method})",
            TransactionType.Import => "Ingående saldo",
            TransactionType.Adjustment => "Rättelse",
            TransactionType.Reversal => "Makulerad",
            _ => transaction.Type.ToString(),
        };

        var detail = transaction.Reason ?? transaction.Reference;
        return detail is null ? description : $"{description} — {detail}";
    }

    private static void Header(IXLWorksheet sheet, params string[] titles) => Header(sheet, 1, titles);

    private static void Header(IXLWorksheet sheet, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            cell.Value = titles[i];
            cell.Style.Font.Bold = true;
        }

        sheet.SheetView.FreezeRows(row);
    }

    private static void MoneyCell(IXLCell cell, Money amount)
    {
        cell.Value = amount.Kronor;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void DateCell(IXLCell cell, DateTimeOffset when)
    {
        cell.Value = when.ToLocalTime().DateTime;
        cell.Style.DateFormat.Format = DateFormat;
    }

    private static void Total(IXLWorksheet sheet, int row, int column, Money amount, string label)
    {
        sheet.Cell(row, column - 1).Value = label;
        sheet.Cell(row, column - 1).Style.Font.Bold = true;
        MoneyCell(sheet.Cell(row, column), amount);
        sheet.Cell(row, column).Style.Font.Bold = true;
    }
}
