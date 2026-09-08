using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

/// <summary>
/// Reads the audit log. There is deliberately no write method here — entries are written
/// by the repositories inside the same transaction as the change they describe, so an
/// entry can never exist without its change or the other way round.
/// </summary>
public sealed class AuditRepository(CafeDatabase database)
{
    public IReadOnlyList<AuditEntry> Read(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? action = null,
        int limit = 500)
    {
        var conditions = new List<string>();
        if (fromUtc is not null) conditions.Add("occurred_utc >= $from");
        if (toUtc is not null) conditions.Add("occurred_utc < $to");
        if (action is not null) conditions.Add("action = $action");

        var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

        using var connection = database.Open();
        return connection.Query<Rows.AuditRow>(
                $"SELECT * FROM audit_log{where} ORDER BY occurred_utc DESC, id DESC LIMIT $limit",
                new
                {
                    from = fromUtc is null ? null : Rows.Format(fromUtc.Value),
                    to = toUtc is null ? null : Rows.Format(toUtc.Value),
                    action,
                    limit,
                })
            .Select(r => r.ToDomain()).ToList();
    }

    public IReadOnlyList<PriceHistoryEntry> PriceHistory(long? itemId = null)
    {
        using var connection = database.Open();
        return connection.Query<Rows.PriceRow>(
                """
                SELECT p.item_id, i.name AS item_name, p.price_ore, p.valid_from_utc,
                       p.valid_to_utc, p.changed_by, p.reason
                FROM price_history p JOIN items i ON i.id = p.item_id
                """ + (itemId is null ? string.Empty : " WHERE p.item_id = $itemId") + """

                ORDER BY i.name, p.valid_from_utc
                """, new { itemId })
            .Select(r => r.ToDomain()).ToList();
    }
}
