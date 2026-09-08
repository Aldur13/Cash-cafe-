using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

public sealed record VerificationProblem(string Check, string Detail, bool IsFatal);

public sealed record VerificationReport(IReadOnlyList<VerificationProblem> Problems)
{
    public bool IsClean => Problems.Count == 0;

    /// <summary>True when the program must refuse to start rather than trust these numbers.</summary>
    public bool HasFatalProblem => Problems.Any(p => p.IsFatal);
}

/// <summary>
/// Proves that the database still says what it should.
///
/// This runs on every start and from the admin panel. The important checks are the first
/// two: SQLite's own structural check, and whether every cached balance still equals the
/// sum of that student's ledger. If the second one fails, a balance is wrong, and the
/// program stops rather than carrying on with a number nobody can explain.
/// </summary>
public sealed class DatabaseVerifier(CafeDatabase database)
{
    public VerificationReport Verify()
    {
        var problems = new List<VerificationProblem>();
        using var connection = database.Open();

        // 1. SQLite's own structural check.
        var integrity = connection.Query<string>("PRAGMA integrity_check").ToList();
        if (integrity.Count != 1 || integrity[0] != "ok")
            problems.Add(new VerificationProblem("integrity_check", string.Join("; ", integrity), IsFatal: true));

        // 2. Every cached balance equals the sum of that student's ledger. This is the one
        //    that matters: it is the promise the whole design rests on.
        var drifted = connection.Query<(long id, string name, long cached, long actual)>(
            """
            SELECT s.id, s.display_name, s.balance_ore,
                   coalesce((SELECT sum(t.amount_ore) FROM transactions t WHERE t.student_id = s.id), 0)
            FROM students s
            WHERE s.balance_ore <> coalesce(
                (SELECT sum(t.amount_ore) FROM transactions t WHERE t.student_id = s.id), 0)
            """).ToList();

        foreach (var row in drifted)
            problems.Add(new VerificationProblem("balance vs ledger",
                $"{row.name} (#{row.id}): stored {new Money(row.cached)}, ledger says {new Money(row.actual)}",
                IsFatal: true));

        // 3. A purchase total equals the sum of its lines.
        var mismatchedTotals = connection.Query<(long id, long amount, long lines)>(
            """
            SELECT t.id, t.amount_ore,
                   coalesce((SELECT sum(l.line_total_ore) FROM transaction_lines l
                             WHERE l.transaction_id = t.id), 0)
            FROM transactions t
            WHERE t.type = 'PURCHASE'
              AND -t.amount_ore <> coalesce((SELECT sum(l.line_total_ore) FROM transaction_lines l
                                             WHERE l.transaction_id = t.id), 0)
            """).ToList();

        foreach (var row in mismatchedTotals)
            problems.Add(new VerificationProblem("purchase total vs lines",
                $"Transaction #{row.id}: charged {new Money(-row.amount)}, lines add up to {new Money(row.lines)}",
                IsFatal: false));

        // 4. Every line total is its unit price times its quantity.
        var badLines = connection.Query<long>(
            "SELECT id FROM transaction_lines WHERE line_total_ore <> unit_price_ore * quantity").ToList();

        foreach (var id in badLines)
            problems.Add(new VerificationProblem("line arithmetic", $"Line #{id} does not multiply out", IsFatal: false));

        // 5. Every reversal points at a transaction that exists and is not itself a reversal.
        var badReversals = connection.Query<long>(
            """
            SELECT r.id FROM transactions r
            WHERE r.reverses_id IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM transactions o
                              WHERE o.id = r.reverses_id AND o.type <> 'REVERSAL')
            """).ToList();

        foreach (var id in badReversals)
            problems.Add(new VerificationProblem("reversal target",
                $"Reversal #{id} does not point at a real transaction", IsFatal: false));

        // 6. balance_after forms a correct running total per student, in time order.
        var runningTotalBreaks = connection.Query<(long id, string name, long recorded, long expected)>(
            """
            WITH ordered AS (
                SELECT t.id, s.display_name AS name, t.balance_after_ore AS recorded,
                       sum(t.amount_ore) OVER (PARTITION BY t.student_id
                                               ORDER BY t.occurred_utc, t.id) AS expected
                FROM transactions t JOIN students s ON s.id = t.student_id
            )
            SELECT id, name, recorded, expected FROM ordered WHERE recorded <> expected
            """).ToList();

        foreach (var row in runningTotalBreaks)
            problems.Add(new VerificationProblem("running balance",
                $"Transaction #{row.id} ({row.name}) records {new Money(row.recorded)} " +
                $"but the running total is {new Money(row.expected)}",
                IsFatal: false));

        return new VerificationReport(problems);
    }

    /// <summary>
    /// Recomputes every cached balance from the ledger. This is what <c>--repair</c> runs:
    /// it never touches the ledger itself, because the ledger is the part that is correct.
    /// </summary>
    public int RepairCachedBalances()
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var repaired = connection.Execute(
            """
            UPDATE students
            SET balance_ore = coalesce(
                (SELECT sum(t.amount_ore) FROM transactions t WHERE t.student_id = students.id), 0)
            WHERE balance_ore <> coalesce(
                (SELECT sum(t.amount_ore) FROM transactions t WHERE t.student_id = students.id), 0)
            """, transaction: transaction);

        if (repaired > 0)
            Audit.Write(connection, transaction, "System", "repair", "REPAIR_BALANCES", null, null,
                null, null, $"{repaired} balance(s) recomputed from the ledger");

        transaction.Commit();
        return repaired;
    }
}
