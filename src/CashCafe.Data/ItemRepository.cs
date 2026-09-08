using CashCafe.Domain;
using Dapper;

namespace CashCafe.Data;

public sealed class ItemRepository(CafeDatabase database)
{
    public IReadOnlyList<Item> All(bool includeUnavailable = false, bool includeArchived = false)
    {
        using var connection = database.Open();

        var conditions = new List<string>();
        if (!includeUnavailable) conditions.Add("is_available = 1");
        if (!includeArchived) conditions.Add("is_archived = 0");

        var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

        return connection.Query<Rows.ItemRow>($"SELECT * FROM items{where} ORDER BY sort_order, name")
            .Select(r => r.ToDomain()).ToList();
    }

    public Item? Find(long id)
    {
        using var connection = database.Open();
        return connection.QuerySingleOrDefault<Rows.ItemRow>(
            "SELECT * FROM items WHERE id = $id", new { id })?.ToDomain();
    }

    public long Create(string name, Money price, string? category, string actor)
    {
        var now = Rows.Format(DateTimeOffset.UtcNow);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO items (name, search_name, category, price_ore, is_available,
                               is_archived, sort_order, created_utc)
            VALUES ($name, $search, $category, $price, 1, 0,
                    (SELECT coalesce(max(sort_order), 0) + 10 FROM items), $now);
            SELECT last_insert_rowid();
            """,
            new
            {
                name = name.Trim(),
                search = SearchNormalizer.Normalize(name),
                category,
                price = price.Ore,
                now,
            }, transaction);

        connection.Execute(
            """
            INSERT INTO price_history (item_id, price_ore, valid_from_utc, changed_by, reason)
            VALUES ($id, $price, $now, $actor, 'Item created')
            """,
            new { id, price = price.Ore, now, actor }, transaction);

        Audit.Write(connection, transaction, actor, "session", "ITEM_CREATE", "item", id,
            null, $"{name} {price}", null);

        transaction.Commit();
        return id;
    }

    /// <summary>
    /// Changes an item's price from now on. Past sales keep the price they were sold at,
    /// because each sale stores its own unit price — so this can never rewrite a report.
    /// </summary>
    public void ChangePrice(long id, Money newPrice, string? reason, string actor)
    {
        var now = Rows.Format(DateTimeOffset.UtcNow);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var old = connection.QuerySingle<long>("SELECT price_ore FROM items WHERE id = $id",
            new { id }, transaction);

        if (old == newPrice.Ore)
        {
            transaction.Rollback();
            return;
        }

        connection.Execute("UPDATE items SET price_ore = $price WHERE id = $id",
            new { id, price = newPrice.Ore }, transaction);

        connection.Execute(
            "UPDATE price_history SET valid_to_utc = $now WHERE item_id = $id AND valid_to_utc IS NULL",
            new { id, now }, transaction);

        connection.Execute(
            """
            INSERT INTO price_history (item_id, price_ore, valid_from_utc, changed_by, reason)
            VALUES ($id, $price, $now, $actor, $reason)
            """,
            new { id, price = newPrice.Ore, now, actor, reason }, transaction);

        Audit.Write(connection, transaction, actor, "session", "PRICE_CHANGE", "item", id,
            new Money(old).ToString(), newPrice.ToString(), reason);

        transaction.Commit();
    }

    /// <summary>Sold out for the day: hidden from the till at once, kept in every report.</summary>
    public void SetAvailable(long id, bool available, string actor)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute("UPDATE items SET is_available = $available WHERE id = $id",
            new { id, available = available ? 1 : 0 }, transaction);

        Audit.Write(connection, transaction, actor, "session",
            available ? "ITEM_AVAILABLE" : "ITEM_SOLD_OUT", "item", id, null, null, null);

        transaction.Commit();
    }

    /// <summary>
    /// Removes an item. One that has never been sold is deleted outright; one that has been
    /// sold is archived instead, because deleting it would leave past purchases pointing at
    /// nothing. The caller is told which of the two happened so it can say so.
    /// </summary>
    public bool Remove(long id, string actor)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var timesSold = connection.ExecuteScalar<long>(
            "SELECT count(*) FROM transaction_lines WHERE item_id = $id", new { id }, transaction);

        var deleted = timesSold == 0;

        if (deleted)
        {
            connection.Execute("DELETE FROM price_history WHERE item_id = $id", new { id }, transaction);
            connection.Execute("DELETE FROM items WHERE id = $id", new { id }, transaction);
        }
        else
        {
            connection.Execute("UPDATE items SET is_archived = 1, is_available = 0 WHERE id = $id",
                new { id }, transaction);
        }

        Audit.Write(connection, transaction, actor, "session", deleted ? "ITEM_DELETE" : "ITEM_ARCHIVE",
            "item", id, null, null, $"sold {timesSold} times");

        transaction.Commit();
        return deleted;
    }
}
