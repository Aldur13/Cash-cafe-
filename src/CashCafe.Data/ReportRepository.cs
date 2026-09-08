using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

public sealed record ItemSales(string ItemName, int Quantity, Money Value);
public sealed record StudentSales(long StudentId, string StudentName, string? ClassName, int Items, Money Value);

public sealed record DaySummary(
    DateOnly Date,
    Money Sold,
    Money Received,
    int PurchaseCount,
    IReadOnlyList<ItemSales> ByItem,
    IReadOnlyList<StudentSales> ByStudent);

/// <summary>
/// The reports behind the admin panel's Today and Reports tabs.
///
/// Reversed transactions are excluded from sales figures — a purchase that was undone was
/// not a sale — while both rows stay in the history, which is where the record belongs.
/// </summary>
public sealed class ReportRepository(CafeDatabase database)
{
    private const string NotReversed = """
        AND NOT EXISTS (SELECT 1 FROM transactions r WHERE r.reverses_id = t.id)
        AND t.type <> 'REVERSAL'
        """;

    public DaySummary Day(DateOnly date)
    {
        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(1);
        return Range(date, from, to);
    }

    public DaySummary Range(DateOnly label, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        using var connection = database.Open();
        var window = new { from = Rows.Format(fromUtc), to = Rows.Format(toUtc) };

        var sold = connection.ExecuteScalar<long?>(
            $"""
             SELECT -sum(t.amount_ore) FROM transactions t
             WHERE t.type = 'PURCHASE' AND t.occurred_utc >= $from AND t.occurred_utc < $to {NotReversed}
             """, window) ?? 0;

        var received = connection.ExecuteScalar<long?>(
            $"""
             SELECT sum(t.amount_ore) FROM transactions t
             WHERE t.type IN ('DEPOSIT','IMPORT') AND t.occurred_utc >= $from AND t.occurred_utc < $to {NotReversed}
             """, window) ?? 0;

        var purchases = connection.ExecuteScalar<int>(
            $"""
             SELECT count(*) FROM transactions t
             WHERE t.type = 'PURCHASE' AND t.occurred_utc >= $from AND t.occurred_utc < $to {NotReversed}
             """, window);

        var byItem = connection.Query<(string name, long qty, long value)>(
            $"""
             SELECT l.item_name, sum(l.quantity), sum(l.line_total_ore)
             FROM transaction_lines l JOIN transactions t ON t.id = l.transaction_id
             WHERE t.type = 'PURCHASE' AND t.occurred_utc >= $from AND t.occurred_utc < $to {NotReversed}
             GROUP BY l.item_name ORDER BY sum(l.line_total_ore) DESC
             """, window)
            .Select(r => new ItemSales(r.name, (int)r.qty, new Money(r.value))).ToList();

        var byStudent = connection.Query<(long id, string name, string? cls, long items, long value)>(
            $"""
             SELECT s.id, s.display_name, s.class_name,
                    coalesce(sum(l.quantity), 0), -sum(t.amount_ore)
             FROM transactions t
             JOIN students s ON s.id = t.student_id
             LEFT JOIN transaction_lines l ON l.transaction_id = t.id
             WHERE t.type = 'PURCHASE' AND t.occurred_utc >= $from AND t.occurred_utc < $to {NotReversed}
             GROUP BY s.id, s.display_name, s.class_name
             ORDER BY -sum(t.amount_ore) DESC
             """, window)
            .Select(r => new StudentSales(r.id, r.name, r.cls, (int)r.items, new Money(r.value))).ToList();

        return new DaySummary(label, new Money(sold), new Money(received), purchases, byItem, byStudent);
    }

    /// <summary>Students currently in the red, worst first. The list to act on.</summary>
    public IReadOnlyList<Student> InTheRed()
    {
        using var connection = database.Open();
        return connection.Query<Rows.StudentRow>(
                Rows.StudentSelect + " WHERE s.balance_ore < 0 AND s.is_active = 1 ORDER BY s.balance_ore")
            .Select(r => r.ToDomain()).ToList();
    }

    /// <summary>
    /// Opening + deposits − sales = closing. If this does not hold, something is wrong, and
    /// the whole point of the report is to say so out loud rather than quietly balance.
    /// </summary>
    public (Money Opening, Money In, Money Out, Money Closing, bool Reconciles) Reconcile(
        DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        using var connection = database.Open();
        var window = new { from = Rows.Format(fromUtc), to = Rows.Format(toUtc) };

        var opening = connection.ExecuteScalar<long?>(
            "SELECT sum(amount_ore) FROM transactions WHERE occurred_utc < $from", window) ?? 0;

        var inflow = connection.ExecuteScalar<long?>(
            """
            SELECT sum(amount_ore) FROM transactions
            WHERE amount_ore > 0 AND occurred_utc >= $from AND occurred_utc < $to
            """, window) ?? 0;

        var outflow = connection.ExecuteScalar<long?>(
            """
            SELECT -sum(amount_ore) FROM transactions
            WHERE amount_ore < 0 AND occurred_utc >= $from AND occurred_utc < $to
            """, window) ?? 0;

        var closing = connection.ExecuteScalar<long?>(
            "SELECT sum(amount_ore) FROM transactions WHERE occurred_utc < $to", window) ?? 0;

        return (new Money(opening), new Money(inflow), new Money(outflow), new Money(closing),
            opening + inflow - outflow == closing);
    }
}
