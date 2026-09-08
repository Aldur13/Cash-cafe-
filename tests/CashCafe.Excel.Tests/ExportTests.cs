using CashCafe.Domain;
using ClosedXML.Excel;

namespace CashCafe.Excel.Tests;

public class ExportTests
{
    private static ExportData Sample()
    {
        var carl = new Student
        {
            Id = 41, FirstName = "Carl", LastName = "Jacobs", DisplayName = "Carl Jacobs",
            SearchName = "carl jacobs", ClassName = "9B", Balance = Money.FromKronor(40),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        var omar = new Student
        {
            Id = 42, FirstName = "Omar", LastName = "Haddad", DisplayName = "Omar Haddad",
            SearchName = "omar haddad", ClassName = "9B", Balance = Money.FromKronor(-10),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        var purchase = new Transaction
        {
            Id = 1043, StudentId = 41, Type = TransactionType.Purchase,
            Amount = Money.FromKronor(-40), BalanceAfter = Money.FromKronor(10),
            OccurredUtc = DateTimeOffset.UtcNow, RecordedUtc = DateTimeOffset.UtcNow,
            Operator = "Café (till 1)",
            Lines = new[]
            {
                new TransactionLine
                {
                    LineNo = 1, ItemId = 3, ItemName = "Toast",
                    UnitPrice = Money.FromKronor(10), Quantity = 1, LineTotal = Money.FromKronor(10),
                },
                new TransactionLine
                {
                    LineNo = 2, ItemId = 7, ItemName = "Juice",
                    UnitPrice = Money.FromKronor(15), Quantity = 2, LineTotal = Money.FromKronor(30),
                },
            },
        };

        var deposit = new Transaction
        {
            Id = 1000, StudentId = 41, Type = TransactionType.Deposit,
            Amount = Money.FromKronor(80), BalanceAfter = Money.FromKronor(80),
            OccurredUtc = DateTimeOffset.UtcNow.AddHours(-1), RecordedUtc = DateTimeOffset.UtcNow.AddHours(-1),
            Method = DepositMethod.Swish, Reference = "SW-4471", Operator = "Admin",
        };

        return new ExportData
        {
            CafeName = "Skolans Café",
            Students = new[] { carl, omar },
            Transactions = new[] { deposit, purchase },
            Items = new[]
            {
                new Item { Id = 3, Name = "Toast", SearchName = "toast", Price = Money.FromKronor(12), Category = "Mat" },
            },
        };
    }

    private static XLWorkbook Written(Action<WorkbookExporter, MemoryStream> write)
    {
        var stream = new MemoryStream();
        write(new WorkbookExporter(), stream);
        stream.Position = 0;
        return new XLWorkbook(stream);
    }

    [Fact]
    public void The_full_export_has_every_sheet_the_wiki_promises()
    {
        using var workbook = Written((exporter, stream) => exporter.WriteEverything(Sample(), stream));

        workbook.Worksheets.Select(w => w.Name).Should().BeEquivalentTo(
            "Students", "Transactions", "TransactionLines", "Items",
            "PriceHistory", "Deposits", "AuditLog", "Summary");
    }

    [Fact]
    public void Money_is_written_as_a_number_so_a_column_can_be_summed()
    {
        using var workbook = Written((exporter, stream) => exporter.WriteEverything(Sample(), stream));
        var cell = workbook.Worksheet("Students").Cell(2, 4);

        cell.DataType.Should().Be(XLDataType.Number, "a text column cannot be summed in Excel");
        cell.GetDouble().Should().Be(40.0);
        cell.Style.NumberFormat.Format.Should().Contain("0.00");
    }

    [Fact]
    public void A_negative_balance_survives_as_a_negative_number()
    {
        using var workbook = Written((exporter, stream) => exporter.WriteEverything(Sample(), stream));
        var omar = workbook.Worksheet("Students").Cell(3, 4);

        omar.GetDouble().Should().Be(-10.0);
    }

    [Fact]
    public void A_sold_line_keeps_the_price_it_was_sold_at()
    {
        // Toast is 12 kr now; it was sold at 10 kr. The export must say 10.
        using var workbook = Written((exporter, stream) => exporter.WriteEverything(Sample(), stream));
        var lines = workbook.Worksheet("TransactionLines");

        lines.Cell(2, 4).GetString().Should().Be("Toast");
        lines.Cell(2, 5).GetDouble().Should().Be(10.0);
        workbook.Worksheet("Items").Cell(2, 4).GetDouble().Should().Be(12.0, "the current price is different");
    }

    [Fact]
    public void The_summary_proves_the_workbook_adds_up()
    {
        using var workbook = Written((exporter, stream) => exporter.WriteEverything(Sample(), stream));
        var summary = workbook.Worksheet("Summary");

        summary.Cell(8, 2).GetDouble().Should().Be(80.0);    // money in
        summary.Cell(9, 2).GetDouble().Should().Be(40.0);    // money out
        summary.Cell(12, 2).GetString().Should().StartWith("NEJ",
            "the sample's balances deliberately do not match its two transactions, and the file says so");
    }

    [Fact]
    public void The_balances_export_is_the_old_sheet_done_properly()
    {
        using var workbook = Written((exporter, stream) => exporter.WriteBalances(Sample(), stream));
        var sheet = workbook.Worksheet("Saldon");

        sheet.Cell(1, 1).GetString().Should().Be("Namn");
        sheet.Cell(2, 1).GetString().Should().Be("Carl Jacobs");
        sheet.Cell(2, 2).GetString().Should().Be("9B");
        sheet.Cell(2, 3).GetDouble().Should().Be(40.0);
        sheet.Cell(4, 3).GetDouble().Should().Be(30.0, "40 and -10 make 30");
    }

    [Fact]
    public void The_legacy_format_writes_the_line_the_cafe_is_used_to()
    {
        using var workbook = Written((exporter, stream) =>
            exporter.WriteBalances(Sample(), stream, legacySingleColumn: true));

        workbook.Worksheet("Saldon").Cell(1, 1).GetString().Should().Be("Carl Jacobs 40,00 kr");
    }

    [Fact]
    public void A_statement_lists_one_students_history_and_nobody_elses()
    {
        var data = Sample();
        using var workbook = Written((exporter, stream) =>
            exporter.WriteStatement(data, data.Students[0], stream));

        var sheet = workbook.Worksheet("Kontoutdrag");
        sheet.Cell(2, 1).GetString().Should().Be("Carl Jacobs");
        sheet.Cell(7, 3).GetString().Should().Be("Insättning (Swish) — SW-4471",
            "a bare reference number explains nothing to the parent it is printed for");
        sheet.Cell(8, 3).GetString().Should().Be("Toast, Juice ×2");
    }

    [Fact]
    public void The_csv_is_written_the_way_swedish_excel_expects()
    {
        var stream = new MemoryStream();
        new CsvExporter().WriteBalances(Sample(), stream);

        var bytes = stream.ToArray();
        bytes[..3].Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF }, "a byte order mark, or Excel mangles å ä ö");

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        text.Should().Contain("Namn;Klass;Saldo");
        text.Should().Contain("Carl Jacobs;9B;40,00");
        text.Should().Contain("-10,00", "a hyphen, so the cell is still a number");
    }

    [Fact]
    public void A_name_that_looks_like_a_formula_is_neutralised_in_csv()
    {
        var data = Sample() with
        {
            Students = new[]
            {
                new Student
                {
                    Id = 1, FirstName = "=cmd", LastName = "x", DisplayName = "=cmd|calc",
                    SearchName = "cmd", Balance = Money.Zero, CreatedUtc = DateTimeOffset.UtcNow,
                },
            },
        };

        var stream = new MemoryStream();
        new CsvExporter().WriteBalances(data, stream);
        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        text.Should().Contain("'=cmd|calc", "a leading = would be run as a formula on open");
    }
}
