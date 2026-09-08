using CashCafe.Domain;

namespace CashCafe.Excel;

internal enum ItemColumnRole
{
    Ignore,
    Name,
    Category,
    Price,
    Shortcut,
    Available,
}

/// <summary>The words a menu sheet's headers might use, in Swedish and English.</summary>
internal static class ItemHeaderNames
{
    private static readonly (ItemColumnRole Role, string[] Words)[] Known =
    {
        (ItemColumnRole.Name, new[] { "namn", "vara", "artikel", "produkt", "item", "name", "product" }),
        (ItemColumnRole.Category, new[] { "kategori", "category", "grupp", "group" }),
        (ItemColumnRole.Price, new[] { "pris", "price", "kr", "kronor", "kostnad", "cost" }),
        (ItemColumnRole.Shortcut, new[] { "kortkommando", "shortcut", "key", "genvag", "snabbtangent" }),
        (ItemColumnRole.Available, new[] { "tillsalu", "till salu", "saljs", "available", "onsale", "on sale", "aktiv" }),
    };

    public static ItemColumnRole Match(string? header)
    {
        var normalized = SearchNormalizer.Normalize(header);
        if (normalized.Length == 0) return ItemColumnRole.Ignore;

        foreach (var (role, words) in Known)
            if (words.Any(w => normalized == SearchNormalizer.Normalize(w)))
                return role;

        foreach (var (role, words) in Known)
            if (words.Any(w => normalized.StartsWith(SearchNormalizer.Normalize(w), StringComparison.Ordinal)))
                return role;

        return ItemColumnRole.Ignore;
    }
}
