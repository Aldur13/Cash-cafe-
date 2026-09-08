using CashCafe.Domain;
using CashCafe.Domain.Rules;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CashCafe.Data;

public enum LedgerOutcome
{
    Committed,

    /// <summary>The purchase would take the student below their floor.</summary>
    RefusedByFloor,

    /// <summary>An admin changed a price while the purchase was being built.</summary>
    PriceChanged,

    /// <summary>Something else moved the balance between reading it and writing.</summary>
    BalanceMovedUnderneath,

    StudentNotFound,
    StudentInactive,
    NothingToDo,
    AlreadyReversed,
    OutsideUndoWindow,
}

public sealed record LedgerResult(
    LedgerOutcome Outcome,
    long TransactionId,
    Money BalanceAfter,
    string Message)
{
    public bool Succeeded => Outcome == LedgerOutcome.Committed;

    public static LedgerResult Fail(LedgerOutcome outcome, string message) =>
        new(outcome, 0, Money.Zero, message);
}

/// <summary>
/// Every write to the ledger goes through here.
///
/// The invariant this class exists to hold: a transaction row, its lines, the student's
/// cached balance and the audit entry are written together or not at all — and the −10 kr
/// floor is re-checked against freshly read numbers inside that same database transaction,
/// not merely on the screen that submitted it.
/// </summary>
public sealed class LedgerRepository(CafeDatabase database)
{
    /// <summary>
    /// Commits a purchase from the till.
    ///
    /// Prices are re-read and compared with what the counter had on screen: if an admin
    /// changed one in another window, the purchase is refused rather than charging an
    /// amount the student was never shown.
    /// </summary>
    public LedgerResult ExecutePurchase(
        long studentId,
        IReadOnlyList<BasketLine> lines,
        CafeSettings settings,
        string @operator,
        string sessionId,
        DateTimeOffset? occurredUtc = null)
    {
        var filled = lines.Where(l => !l.IsEmpty).ToList();
        if (filled.Count == 0) return LedgerResult.Fail(LedgerOutcome.NothingToDo, "Add at least one item");

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var student = connection.QuerySingleOrDefault<Rows.StudentRow>(
            Rows.StudentSelect + " WHERE s.id = $id", new { id = studentId }, transaction)?.ToDomain();

        if (student is null) return LedgerResult.Fail(LedgerOutcome.StudentNotFound, "Student not found");
        if (!student.IsActive) return LedgerResult.Fail(LedgerOutcome.StudentInactive, "This student is deactivated");

        // Re-read the prices we are about to charge.
        foreach (var line in filled.Where(l => l.ItemId is not null && !l.IsCustom))
        {
            var current = connection.QuerySingleOrDefault<long?>(
                "SELECT price_ore FROM items WHERE id = $id", new { id = line.ItemId }, transaction);

            if (current is null)
                return LedgerResult.Fail(LedgerOutcome.PriceChanged, $"{line.ItemName} is no longer on sale");

            if (current.Value != line.UnitPrice.Ore)
                return LedgerResult.Fail(LedgerOutcome.PriceChanged,
                    $"The price of {line.ItemName} changed to {new Money(current.Value)}. Check the purchase again.");
        }

        var total = Money.Sum(filled.Select(l => l.LineTotal));
        var floor = BalanceRules.EffectiveFloor(student.CreditLimit, settings);
        var check = BalanceRules.CheckPurchase(student.Balance, floor, total);

        if (!check.IsAllowed)
            return LedgerResult.Fail(LedgerOutcome.RefusedByFloor,
                BalanceRules.RefusalMessage(student.DisplayName, student.Balance, floor, total));

        var when = occurredUtc ?? DateTimeOffset.UtcNow;

        var transactionId = InsertTransaction(connection, transaction, new TransactionInsert(
            StudentId: studentId,
            Type: TransactionType.Purchase,
            Amount: total.Negated(),
            BalanceAfter: check.BalanceAfter,
            OccurredUtc: when,
            Operator: @operator,
            SessionId: sessionId));

        var lineNo = 1;
        foreach (var line in filled)
        {
            connection.Execute(
                """
                INSERT INTO transaction_lines (transaction_id, line_no, item_id, item_name,
                                               unit_price_ore, quantity, line_total_ore, is_custom)
                VALUES ($tx, $no, $itemId, $name, $price, $qty, $total, $custom)
                """,
                new
                {
                    tx = transactionId,
                    no = lineNo++,
                    itemId = line.IsCustom ? null : line.ItemId,
                    name = line.ItemName,
                    price = line.UnitPrice.Ore,
                    qty = line.Quantity,
                    total = line.LineTotal.Ore,
                    custom = line.IsCustom ? 1 : 0,
                }, transaction);
        }

        if (!UpdateBalance(connection, transaction, studentId, student.Balance, check.BalanceAfter, when))
            return LedgerResult.Fail(LedgerOutcome.BalanceMovedUnderneath,
                "The balance changed while this purchase was being made. Please try again.");

        var summary = string.Join(", ", filled.Select(l => l.Quantity > 1 ? $"{l.ItemName} ×{l.Quantity}" : l.ItemName));
        Audit.Write(connection, transaction, @operator, sessionId, "PURCHASE", "transaction", transactionId,
            student.Balance.ToString(), check.BalanceAfter.ToString(), summary);

        transaction.Commit();

        return new LedgerResult(LedgerOutcome.Committed, transactionId, check.BalanceAfter,
            $"{student.DisplayName} — {total} paid. New balance {check.BalanceAfter}.");
    }

    /// <summary>Records money arriving. A deposit is never refused, but it must be positive.</summary>
    public LedgerResult Deposit(
        long studentId,
        Money amount,
        DepositMethod method,
        string? reference,
        string @operator,
        string sessionId,
        DateTimeOffset? occurredUtc = null,
        string? note = null)
    {
        if (!BalanceRules.IsValidDeposit(amount))
            return LedgerResult.Fail(LedgerOutcome.NothingToDo, "A deposit must be more than 0 kr");

        return Simple(studentId, TransactionType.Deposit, amount, @operator, sessionId,
            occurredUtc, method, reference, reason: null, note: note);
    }

    /// <summary>
    /// An admin correction: the only way to change a balance without a purchase or a deposit.
    /// A reason is required, and the database rejects the row without one.
    /// </summary>
    public LedgerResult Adjust(
        long studentId,
        Money amount,
        string reason,
        string @operator,
        string sessionId,
        DateTimeOffset? occurredUtc = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return LedgerResult.Fail(LedgerOutcome.NothingToDo, "A correction needs a written reason");

        if (amount.IsZero)
            return LedgerResult.Fail(LedgerOutcome.NothingToDo, "A correction of 0 kr changes nothing");

        return Simple(studentId, TransactionType.Adjustment, amount, @operator, sessionId,
            occurredUtc, method: null, reference: null, reason: reason, note: null);
    }

    /// <summary>
    /// Cancels an earlier transaction by writing its opposite. Nothing is deleted: both rows
    /// stay in the history, linked, so the record shows the mistake and the fix.
    /// </summary>
    public LedgerResult Reverse(
        long transactionId,
        string reason,
        string @operator,
        string sessionId,
        TimeSpan? undoWindow = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return LedgerResult.Fail(LedgerOutcome.NothingToDo, "A reversal needs a reason");

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var original = connection.QuerySingleOrDefault<Rows.TransactionRow>(
            "SELECT * FROM transactions WHERE id = $id", new { id = transactionId }, transaction)?.ToDomain();

        if (original is null)
            return LedgerResult.Fail(LedgerOutcome.NothingToDo, "That transaction does not exist");

        if (original.Type == TransactionType.Reversal)
            return LedgerResult.Fail(LedgerOutcome.NothingToDo,
                "A reversal cannot be reversed. Reverse the transaction it points at instead.");

        var alreadyReversed = connection.ExecuteScalar<long>(
            "SELECT count(*) FROM transactions WHERE reverses_id = $id",
            new { id = transactionId }, transaction) > 0;

        if (alreadyReversed)
            return LedgerResult.Fail(LedgerOutcome.AlreadyReversed, "That transaction has already been reversed");

        if (undoWindow is not null && DateTimeOffset.UtcNow - original.OccurredUtc > undoWindow)
            return LedgerResult.Fail(LedgerOutcome.OutsideUndoWindow,
                "Too long ago to undo here — an administrator can still reverse it");

        var student = connection.QuerySingle<Rows.StudentRow>(
            Rows.StudentSelect + " WHERE s.id = $id", new { id = original.StudentId }, transaction).ToDomain();

        var balanceAfter = student.Balance + original.Amount.Negated();
        var when = DateTimeOffset.UtcNow;

        var reversalId = InsertTransaction(connection, transaction, new TransactionInsert(
            StudentId: original.StudentId,
            Type: TransactionType.Reversal,
            Amount: original.Amount.Negated(),
            BalanceAfter: balanceAfter,
            OccurredUtc: when,
            Operator: @operator,
            SessionId: sessionId,
            Reason: reason,
            ReversesId: transactionId));

        if (!UpdateBalance(connection, transaction, original.StudentId, student.Balance, balanceAfter, when))
            return LedgerResult.Fail(LedgerOutcome.BalanceMovedUnderneath,
                "The balance changed while this was being reversed. Please try again.");

        Audit.Write(connection, transaction, @operator, sessionId, "REVERSAL", "transaction", reversalId,
            student.Balance.ToString(), balanceAfter.ToString(), $"reverses #{transactionId}: {reason}");

        transaction.Commit();

        return new LedgerResult(LedgerOutcome.Committed, reversalId, balanceAfter,
            $"Reversed. {student.DisplayName} is back to {balanceAfter}.");
    }

    public IReadOnlyList<Transaction> History(long studentId, int limit = 100, DateTimeOffset? since = null)
    {
        using var connection = database.Open();

        var sql = """
            SELECT * FROM transactions
            WHERE student_id = $id
            """ + (since is null ? string.Empty : " AND occurred_utc >= $since") + """

            ORDER BY occurred_utc DESC, id DESC
            LIMIT $limit
            """;

        var transactions = connection.Query<Rows.TransactionRow>(
            sql, new { id = studentId, limit, since = since is null ? null : Rows.Format(since.Value) }).ToList();

        if (transactions.Count == 0) return Array.Empty<Transaction>();

        var ids = transactions.Select(t => t.id).ToArray();
        var lines = connection.Query<Rows.LineRow>(
                "SELECT * FROM transaction_lines WHERE transaction_id IN @ids ORDER BY line_no", new { ids })
            .GroupBy(l => l.transaction_id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TransactionLine>)g.Select(l => l.ToDomain()).ToList());

        return transactions
            .Select(t => t.ToDomain(lines.TryGetValue(t.id, out var l) ? l : null))
            .ToList();
    }

    /// <summary>Which transactions have been cancelled, so the UI can strike them through.</summary>
    public IReadOnlySet<long> ReversedTransactionIds(IEnumerable<long> transactionIds)
    {
        var ids = transactionIds.ToArray();
        if (ids.Length == 0) return new HashSet<long>();

        using var connection = database.Open();
        return connection.Query<long>(
                "SELECT reverses_id FROM transactions WHERE reverses_id IN @ids", new { ids })
            .ToHashSet();
    }

    private LedgerResult Simple(
        long studentId,
        TransactionType type,
        Money amount,
        string @operator,
        string sessionId,
        DateTimeOffset? occurredUtc,
        DepositMethod? method,
        string? reference,
        string? reason,
        string? note)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var student = connection.QuerySingleOrDefault<Rows.StudentRow>(
            Rows.StudentSelect + " WHERE s.id = $id", new { id = studentId }, transaction)?.ToDomain();

        if (student is null) return LedgerResult.Fail(LedgerOutcome.StudentNotFound, "Student not found");

        var balanceAfter = student.Balance + amount;
        var when = occurredUtc ?? DateTimeOffset.UtcNow;

        var transactionId = InsertTransaction(connection, transaction, new TransactionInsert(
            StudentId: studentId,
            Type: type,
            Amount: amount,
            BalanceAfter: balanceAfter,
            OccurredUtc: when,
            Operator: @operator,
            SessionId: sessionId,
            Method: method,
            Reference: reference,
            Reason: reason,
            Note: note));

        if (!UpdateBalance(connection, transaction, studentId, student.Balance, balanceAfter, when))
            return LedgerResult.Fail(LedgerOutcome.BalanceMovedUnderneath,
                "The balance changed underneath this change. Please try again.");

        Audit.Write(connection, transaction, @operator, sessionId, Rows.TypeName(type), "transaction",
            transactionId, student.Balance.ToString(), balanceAfter.ToString(), reason ?? reference);

        transaction.Commit();

        return new LedgerResult(LedgerOutcome.Committed, transactionId, balanceAfter,
            $"{student.DisplayName} — new balance {balanceAfter}.");
    }

    private sealed record TransactionInsert(
        long StudentId,
        TransactionType Type,
        Money Amount,
        Money BalanceAfter,
        DateTimeOffset OccurredUtc,
        string Operator,
        string SessionId,
        DepositMethod? Method = null,
        string? Reference = null,
        string? Reason = null,
        long? ReversesId = null,
        long? ImportId = null,
        string? Note = null);

    private static long InsertTransaction(SqliteConnection connection, SqliteTransaction transaction, TransactionInsert insert) =>
        connection.ExecuteScalar<long>(
            """
            INSERT INTO transactions (student_id, type, amount_ore, balance_after_ore, occurred_utc,
                                      recorded_utc, operator, session_id, method, reference, reason,
                                      reverses_id, import_id, note)
            VALUES ($student, $type, $amount, $after, $occurred, $recorded, $operator, $session,
                    $method, $reference, $reason, $reverses, $import, $note);
            SELECT last_insert_rowid();
            """,
            new
            {
                student = insert.StudentId,
                type = Rows.TypeName(insert.Type),
                amount = insert.Amount.Ore,
                after = insert.BalanceAfter.Ore,
                occurred = Rows.Format(insert.OccurredUtc),
                recorded = Rows.Format(DateTimeOffset.UtcNow),
                @operator = insert.Operator,
                session = insert.SessionId,
                method = insert.Method?.ToString(),
                reference = insert.Reference,
                reason = insert.Reason,
                reverses = insert.ReversesId,
                import = insert.ImportId,
                note = insert.Note,
            }, transaction);

    /// <summary>
    /// Updates the cached balance, but only if it still holds the value we based the
    /// arithmetic on. Zero rows affected means something else moved it in between, and the
    /// caller rolls the whole thing back rather than writing a number derived from stale data.
    /// </summary>
    private static bool UpdateBalance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long studentId,
        Money expected,
        Money updated,
        DateTimeOffset when) =>
        connection.Execute(
            """
            UPDATE students SET balance_ore = $new, last_activity_utc = $when
            WHERE id = $id AND balance_ore = $expected
            """,
            new
            {
                id = studentId,
                @new = updated.Ore,
                expected = expected.Ore,
                when = Rows.Format(when),
            }, transaction) == 1;
}
