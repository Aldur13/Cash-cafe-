using Dapper;
using Microsoft.Data.Sqlite;

namespace CashCafe.Data;

/// <summary>
/// Writes the audit log. Every state-changing repository call goes through here, inside the
/// same database transaction as the change itself — so a change without its log entry, or a
/// log entry without its change, cannot exist.
/// </summary>
internal static class Audit
{
    public static void Write(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string actor,
        string sessionId,
        string action,
        string? entityType,
        long? entityId,
        string? oldValue,
        string? newValue,
        string? detail) =>
        connection.Execute(
            """
            INSERT INTO audit_log (occurred_utc, actor, session_id, action, entity_type, entity_id,
                                   old_value, new_value, detail)
            VALUES ($when, $actor, $session, $action, $entityType, $entityId, $old, $new, $detail)
            """,
            new
            {
                when = Rows.Format(DateTimeOffset.UtcNow),
                actor,
                session = sessionId,
                action,
                entityType,
                entityId,
                old = oldValue,
                @new = newValue,
                detail,
            }, transaction);
}
