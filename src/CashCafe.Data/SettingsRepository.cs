using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

/// <summary>Reads and writes the café-wide settings, including the busyness slider.</summary>
public sealed class SettingsRepository(CafeDatabase database)
{
    public CafeSettings Load()
    {
        using var connection = database.Open();
        var values = connection.Query<(string key, string value)>("SELECT key, value FROM settings")
            .ToDictionary(r => r.key, r => r.value, StringComparer.Ordinal);

        var busynessUpdated = connection.QuerySingleOrDefault<string?>(
            "SELECT updated_utc FROM settings WHERE key = 'busyness'");

        var defaults = CafeSettings.Defaults;

        return new CafeSettings
        {
            CafeName = Text(values, "cafe_name", defaults.CafeName),
            MinimumBalance = new Money(Number(values, "minimum_balance_ore", defaults.MinimumBalance.Ore)),
            MaxLinesPerPurchase = (int)Number(values, "max_lines_per_purchase", defaults.MaxLinesPerPurchase),
            UndoWindow = TimeSpan.FromMinutes(Number(values, "undo_window_minutes", (long)defaults.UndoWindow.TotalMinutes)),
            LargePurchaseWarning = new Money(Number(values, "large_purchase_warning_ore", defaults.LargePurchaseWarning.Ore)),
            LowBalanceWarning = new Money(Number(values, "low_balance_warning_ore", defaults.LowBalanceWarning.Ore)),
            Busyness = (BusynessLevel)Math.Clamp(Number(values, "busyness", 0), 0, 4),
            BusynessUpdatedUtc = busynessUpdated is null ? null : Rows.ParseTime(busynessUpdated),
            StudentSiteEnabled = Flag(values, "student_site_enabled", defaults.StudentSiteEnabled),
            StudentSiteShowHistory = Flag(values, "student_site_show_history", defaults.StudentSiteShowHistory),
            StudentSiteShowBusyness = Flag(values, "student_site_show_busyness", defaults.StudentSiteShowBusyness),
            StudentSiteHistoryDays = (int)Number(values, "student_site_history_days", defaults.StudentSiteHistoryDays),
        };
    }

    /// <summary>
    /// Moves the busyness slider. Staff do this several times a day, so it is a single
    /// cheap write — but it is still audited, because "who said we were closed at 11:30?"
    /// is a question someone eventually asks.
    /// </summary>
    public void SetBusyness(BusynessLevel level, string actor, string sessionId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var previous = connection.QuerySingleOrDefault<string?>(
            "SELECT value FROM settings WHERE key = 'busyness'", transaction: transaction);

        Write(connection, transaction, "busyness", ((int)level).ToString(), actor);

        connection.Execute(
            """
            INSERT INTO audit_log (occurred_utc, actor, session_id, action, entity_type, old_value, new_value, detail)
            VALUES ($when, $actor, $session, 'BUSYNESS', 'setting', $old, $new, $detail)
            """,
            new
            {
                when = Rows.Format(DateTimeOffset.UtcNow),
                actor,
                session = sessionId,
                old = previous,
                @new = ((int)level).ToString(),
                detail = level.ToEnglish(),
            }, transaction);

        transaction.Commit();
    }

    /// <summary>Reads one raw setting, or null when it has never been written.</summary>
    public string? TryGet(string key)
    {
        using var connection = database.Open();
        return connection.QuerySingleOrDefault<string?>(
            "SELECT value FROM settings WHERE key = $key", new { key });
    }

    public void Set(string key, string value, string actor)
    {
        using var connection = database.Open();
        Write(connection, null, key, value, actor);
    }

    public void SetFlag(string key, bool value, string actor) => Set(key, value ? "1" : "0", actor);

    private static void Write(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction,
        string key,
        string value,
        string actor) =>
        connection.Execute(
            """
            INSERT INTO settings (key, value, updated_utc, updated_by) VALUES ($key, $value, $when, $actor)
            ON CONFLICT(key) DO UPDATE SET value = $value, updated_utc = $when, updated_by = $actor
            """,
            new { key, value, when = Rows.Format(DateTimeOffset.UtcNow), actor }, transaction);

    private static string Text(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static long Number(IReadOnlyDictionary<string, string> values, string key, long fallback) =>
        values.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool Flag(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value) ? value is "1" or "true" : fallback;
}
