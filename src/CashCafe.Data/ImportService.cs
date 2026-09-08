using System.Security.Cryptography;
using System.Text.Json;
using CashCafe.Domain;
using CashCafe.Domain.Import;
using Dapper;

namespace CashCafe.Data;

public sealed record ImportResult(
    long ImportId,
    int StudentsCreated,
    int TransactionsWritten,
    Money TotalChange,
    string Message);

/// <summary>
/// Applies an <see cref="ImportPreview"/> that a person has looked at and approved.
///
/// The whole import is one database transaction: it all lands or none of it does. Every row
/// it writes carries the same import id, which is what makes the whole batch undoable later
/// — as reversals, so even the undo stays visible in the history.
/// </summary>
public sealed class ImportService(CafeDatabase database)
{
    public ImportResult Apply(
        ImportPreview preview,
        string archivedPath,
        string actor,
        string sessionId,
        DateTimeOffset? occurredUtc = null)
    {
        if (!preview.CanImport)
            return new ImportResult(0, 0, 0, Money.Zero,
                preview.Errors > 0
                    ? $"{preview.Errors} row(s) could not be read. Fix or exclude them first."
                    : "There is nothing to import.");

        var when = occurredUtc ?? DateTimeOffset.UtcNow;
        var timestamp = Rows.Format(when);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var importId = connection.ExecuteScalar<long>(
            """
            INSERT INTO imports (file_name, archived_path, file_sha256, sheet_name, mapping_json,
                                 rows_read, students_created, transactions_created, total_ore,
                                 imported_utc, imported_by)
            VALUES ($file, $archived, $hash, $sheet, $mapping, $rows, 0, 0, 0, $now, $actor);
            SELECT last_insert_rowid();
            """,
            new
            {
                file = preview.FileName,
                archived = archivedPath,
                hash = FileHash(archivedPath),
                sheet = preview.SheetName,
                mapping = JsonSerializer.Serialize(new { preview.SheetName, preview.RowsRead }),
                rows = preview.RowsRead,
                now = timestamp,
                actor,
            }, transaction);

        var created = 0;
        var written = 0;

        foreach (var row in preview.Rows)
        {
            switch (row.Action)
            {
                case ImportAction.CreateStudent:
                {
                    var studentId = CreateStudent(connection, transaction, row, timestamp, actor);
                    created++;

                    var opening = row.Source.Amount ?? Money.Zero;
                    if (!opening.IsZero)
                    {
                        WriteLedgerRow(connection, transaction, studentId, opening, opening, when, actor,
                            sessionId, importId,
                            $"Opening balance from {preview.FileName}, row {row.Source.RowNumber}");
                        written++;
                    }

                    break;
                }

                case ImportAction.AdjustToFileValue or ImportAction.AddToBalance:
                {
                    var change = row.EffectiveChange;
                    if (change.IsZero) break;

                    var studentId = row.MatchedStudentId!.Value;
                    var balanceBefore = connection.QuerySingle<long>(
                        "SELECT balance_ore FROM students WHERE id = $id", new { id = studentId }, transaction);

                    var balanceAfter = new Money(balanceBefore) + change;

                    WriteLedgerRow(connection, transaction, studentId, change, balanceAfter, when, actor,
                        sessionId, importId,
                        $"Import from {preview.FileName}, row {row.Source.RowNumber}");

                    connection.Execute(
                        """
                        UPDATE students SET balance_ore = $new, last_activity_utc = $now
                        WHERE id = $id AND balance_ore = $expected
                        """,
                        new { id = studentId, @new = balanceAfter.Ore, expected = balanceBefore, now = timestamp },
                        transaction);

                    written++;
                    break;
                }
            }
        }

        var total = preview.TotalChange;

        connection.Execute(
            """
            UPDATE imports SET students_created = $created, transactions_created = $written, total_ore = $total
            WHERE id = $id
            """,
            new { id = importId, created, written, total = total.Ore }, transaction);

        Audit.Write(connection, transaction, actor, sessionId, "IMPORT", "import", importId,
            null, total.ToString(), $"{preview.FileName}: {created} created, {written} transactions");

        transaction.Commit();

        return new ImportResult(importId, created, written, total,
            $"Imported {preview.FileName}: {created} new student(s), {written} transaction(s), {total} in total.");
    }

    /// <summary>
    /// Rolls a whole import back by reversing every transaction it wrote. Students it created
    /// are deactivated rather than deleted, because a student who has since bought something
    /// is no longer only the import's to remove.
    /// </summary>
    public ImportResult Undo(long importId, string actor, string sessionId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var alreadyUndone = connection.QuerySingleOrDefault<string?>(
            "SELECT undone_utc FROM imports WHERE id = $id", new { id = importId }, transaction);

        if (alreadyUndone is not null)
            return new ImportResult(importId, 0, 0, Money.Zero, "That import has already been undone.");

        var written = connection.Query<Rows.TransactionRow>(
                "SELECT * FROM transactions WHERE import_id = $id ORDER BY id DESC",
                new { id = importId }, transaction)
            .Select(r => r.ToDomain()).ToList();

        var now = DateTimeOffset.UtcNow;
        var timestamp = Rows.Format(now);
        var reversed = 0;
        var total = Money.Zero;

        foreach (var original in written)
        {
            var alreadyReversed = connection.ExecuteScalar<long>(
                "SELECT count(*) FROM transactions WHERE reverses_id = $id",
                new { id = original.Id }, transaction) > 0;

            if (alreadyReversed) continue;

            var balanceBefore = connection.QuerySingle<long>(
                "SELECT balance_ore FROM students WHERE id = $id",
                new { id = original.StudentId }, transaction);

            var change = original.Amount.Negated();
            var balanceAfter = new Money(balanceBefore) + change;

            connection.Execute(
                """
                INSERT INTO transactions (student_id, type, amount_ore, balance_after_ore, occurred_utc,
                                          recorded_utc, operator, session_id, reason, reverses_id, import_id)
                VALUES ($student, 'REVERSAL', $amount, $after, $now, $now, $actor, $session,
                        $reason, $reverses, $import)
                """,
                new
                {
                    student = original.StudentId,
                    amount = change.Ore,
                    after = balanceAfter.Ore,
                    now = timestamp,
                    actor,
                    session = sessionId,
                    reason = $"Undo of import #{importId}",
                    reverses = original.Id,
                    import = importId,
                }, transaction);

            connection.Execute(
                "UPDATE students SET balance_ore = $new WHERE id = $id AND balance_ore = $expected",
                new { id = original.StudentId, @new = balanceAfter.Ore, expected = balanceBefore }, transaction);

            total += change;
            reversed++;
        }

        connection.Execute("UPDATE imports SET undone_utc = $now WHERE id = $id",
            new { id = importId, now = timestamp }, transaction);

        Audit.Write(connection, transaction, actor, sessionId, "IMPORT_UNDO", "import", importId,
            null, total.ToString(), $"{reversed} transaction(s) reversed");

        transaction.Commit();

        return new ImportResult(importId, 0, reversed, total,
            $"Import #{importId} undone: {reversed} transaction(s) reversed.");
    }

    public IReadOnlyList<(long Id, string FileName, DateTimeOffset When, string By, int Rows, Money Total, bool Undone)> History()
    {
        using var connection = database.Open();
        return connection.Query<(long id, string file_name, string imported_utc, string imported_by, long rows_read, long total_ore, string? undone_utc)>(
                "SELECT id, file_name, imported_utc, imported_by, rows_read, total_ore, undone_utc FROM imports ORDER BY id DESC")
            .Select(r => (r.id, r.file_name, Rows.ParseTime(r.imported_utc), r.imported_by,
                (int)r.rows_read, new Money(r.total_ore), r.undone_utc is not null))
            .ToList();
    }

    private static long CreateStudent(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        ImportRow row,
        string timestamp,
        string actor)
    {
        var name = row.Source.Name!.Trim();
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return connection.ExecuteScalar<long>(
            """
            INSERT INTO students (first_name, last_name, display_name, search_name, class_name,
                                  balance_ore, is_active, created_utc, created_by)
            VALUES ($first, $last, $display, $search, $class, $balance, 1, $now, $by);
            SELECT last_insert_rowid();
            """,
            new
            {
                first = parts[0],
                last = parts.Length > 1 ? parts[1] : string.Empty,
                display = name,
                search = SearchNormalizer.Normalize(name),
                @class = row.Source.ClassName,
                balance = (row.Source.Amount ?? Money.Zero).Ore,
                now = timestamp,
                by = actor,
            }, transaction);
    }

    private static void WriteLedgerRow(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        long studentId,
        Money amount,
        Money balanceAfter,
        DateTimeOffset when,
        string actor,
        string sessionId,
        long importId,
        string reference) =>
        connection.Execute(
            """
            INSERT INTO transactions (student_id, type, amount_ore, balance_after_ore, occurred_utc,
                                      recorded_utc, operator, session_id, reference, import_id)
            VALUES ($student, 'IMPORT', $amount, $after, $when, $when, $actor, $session, $reference, $import)
            """,
            new
            {
                student = studentId,
                amount = amount.Ore,
                after = balanceAfter.Ore,
                when = Rows.Format(when),
                actor,
                session = sessionId,
                reference,
                import = importId,
            }, transaction);

    /// <summary>
    /// The hash of the file this import came from, so a number can always be traced back to
    /// a specific file — and so a re-import of a quietly edited copy is detectable.
    /// </summary>
    private static string FileHash(string path)
    {
        if (!File.Exists(path)) return string.Empty;

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
