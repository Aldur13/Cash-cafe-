using System.Text;
using CashCafe.Domain;

namespace CashCafe.Excel;

/// <summary>
/// The plain-text export, and the long-term safety net: a file that will still be readable
/// when this program is gone.
///
/// Written the way Swedish Excel expects — UTF-8 with a byte order mark, semicolon between
/// fields, comma inside numbers — because a CSV nobody can open without a wizard is not
/// much of a safety net.
/// </summary>
public sealed class CsvExporter
{
    private const char Separator = ';';

    public void WriteBalances(ExportData data, Stream destination)
    {
        using var writer = NewWriter(destination);
        WriteRow(writer, "Namn", "Klass", "Saldo");

        foreach (var student in data.Students.OrderBy(s => s.DisplayName, StringComparer.CurrentCulture))
            WriteRow(writer, student.DisplayName, student.ClassName ?? string.Empty, Number(student.Balance));
    }

    public void WriteTransactions(ExportData data, Stream destination)
    {
        using var writer = NewWriter(destination);
        var names = data.Students.ToDictionary(s => s.Id, s => s.DisplayName);

        WriteRow(writer, "TransactionId", "Datum", "Elev", "Typ", "Beskrivning", "Belopp",
            "SaldoEfter", "Metod", "Referens", "Anledning", "Makulerad", "Operator");

        foreach (var transaction in data.Transactions.OrderBy(t => t.OccurredUtc).ThenBy(t => t.Id))
            WriteRow(writer,
                transaction.Id.ToString(),
                transaction.OccurredUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                names.GetValueOrDefault(transaction.StudentId, string.Empty),
                transaction.Type.ToString().ToUpperInvariant(),
                Describe(transaction),
                Number(transaction.Amount),
                Number(transaction.BalanceAfter),
                transaction.Method?.ToString() ?? string.Empty,
                transaction.Reference ?? string.Empty,
                transaction.Reason ?? string.Empty,
                data.ReversedTransactionIds.Contains(transaction.Id) ? "Ja" : string.Empty,
                transaction.Operator);
    }

    private static StreamWriter NewWriter(Stream destination) =>
        new(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

    private static void WriteRow(TextWriter writer, params string[] fields) =>
        writer.WriteLine(string.Join(Separator, fields.Select(Escape)));

    /// <summary>
    /// Quotes a field when it contains something that would otherwise break the row. The
    /// leading-character guard stops a spreadsheet treating a name as a formula.
    /// </summary>
    private static string Escape(string field)
    {
        if (field.Length > 0 && field[0] is '=' or '+' or '@' or '\t' or '\r')
            field = "'" + field;

        var needsQuotes = field.Contains(Separator) || field.Contains('"') ||
                          field.Contains('\n') || field.Contains('\r');

        return needsQuotes ? '"' + field.Replace("\"", "\"\"") + '"' : field;
    }

    private static string Number(Money amount) => amount.ToString(withCurrency: false);

    private static string Describe(Transaction transaction) => transaction.Lines.Count > 0
        ? string.Join(", ", transaction.Lines.Select(l => l.Quantity > 1 ? $"{l.ItemName} x{l.Quantity}" : l.ItemName))
        : transaction.Reason ?? transaction.Reference ?? string.Empty;
}
