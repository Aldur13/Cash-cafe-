using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

public sealed class StudentRepository(CafeDatabase database)
{
    public IReadOnlyList<Student> All(bool includeInactive = false)
    {
        using var connection = database.Open();
        var sql = Rows.StudentSelect +
                  (includeInactive ? string.Empty : " WHERE s.is_active = 1") +
                  " ORDER BY s.display_name";
        return connection.Query<Rows.StudentRow>(sql).Select(r => r.ToDomain()).ToList();
    }

    public Student? Find(long id)
    {
        using var connection = database.Open();
        return connection.QuerySingleOrDefault<Rows.StudentRow>(
            Rows.StudentSelect + " WHERE s.id = $id", new { id })?.ToDomain();
    }

    /// <summary>
    /// Creates a student. Called from admin, from the import, and from the counter when
    /// someone is served who is not on the list yet.
    /// </summary>
    public long Create(string firstName, string lastName, string? className, string createdBy, Money? creditLimit = null)
    {
        var displayName = string.IsNullOrWhiteSpace(lastName) ? firstName.Trim() : $"{firstName.Trim()} {lastName.Trim()}";
        var now = Rows.Format(DateTimeOffset.UtcNow);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO students (first_name, last_name, display_name, search_name, class_name,
                                  balance_ore, credit_limit_ore, is_active, created_utc, created_by)
            VALUES ($first, $last, $display, $search, $class, 0, $limit, 1, $now, $by);
            SELECT last_insert_rowid();
            """,
            new
            {
                first = firstName.Trim(),
                last = lastName.Trim(),
                display = displayName,
                search = SearchNormalizer.Normalize(displayName),
                @class = className,
                limit = creditLimit?.Ore,
                now,
                by = createdBy,
            }, transaction);

        Audit.Write(connection, transaction, createdBy, "session", "STUDENT_CREATE", "student", id,
            null, displayName, className);

        transaction.Commit();
        return id;
    }

    public void SetActive(long id, bool active, string actor)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute("UPDATE students SET is_active = $active WHERE id = $id",
            new { id, active = active ? 1 : 0 }, transaction);

        Audit.Write(connection, transaction, actor, "session", active ? "STUDENT_ACTIVATE" : "STUDENT_DEACTIVATE",
            "student", id, null, null, null);

        transaction.Commit();
    }

    /// <summary>
    /// Sets a student's own floor, which overrides the café default in either direction:
    /// zero means they may never go negative, −50 kr means they are allowed a larger tab.
    /// </summary>
    public void SetCreditLimit(long id, Money? limit, string reason, string actor)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var previous = connection.QuerySingleOrDefault<long?>(
            "SELECT credit_limit_ore FROM students WHERE id = $id", new { id }, transaction);

        connection.Execute("UPDATE students SET credit_limit_ore = $limit WHERE id = $id",
            new { id, limit = limit?.Ore }, transaction);

        Audit.Write(connection, transaction, actor, "session", "CREDIT_LIMIT", "student", id,
            previous?.ToString(), limit?.Ore.ToString(), reason);

        transaction.Commit();
    }

    public IReadOnlyList<Student> Search(string query, int limit = StudentSearch.DefaultLimit) =>
        StudentSearch.Find(All(), query, limit);
}
