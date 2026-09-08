using CashCafe.Domain;

namespace CashCafe.Excel;

/// <summary>
/// The words schools actually put at the top of these columns, in Swedish and English.
/// Matched after normalising, so capitals, accents and punctuation do not matter.
/// </summary>
internal static class HeaderNames
{
    private static readonly (ColumnRole Role, string[] Words)[] Known =
    {
        (ColumnRole.FirstName, new[] { "fornamn", "first name", "firstname", "given name" }),
        (ColumnRole.LastName, new[] { "efternamn", "last name", "lastname", "surname", "family name" }),
        (ColumnRole.Name, new[] { "namn", "elev", "student", "name", "full name", "elevnamn", "for och efternamn", "person" }),
        (ColumnRole.Class, new[] { "klass", "class", "grupp", "group", "avdelning" }),
        (ColumnRole.Balance, new[] { "saldo", "belopp", "balance", "summa", "kr", "kronor", "amount", "tillgodo", "behallning", "kvar" }),
    };

    public static ColumnRole Match(string? header)
    {
        var normalized = SearchNormalizer.Normalize(header);
        if (normalized.Length == 0) return ColumnRole.Ignore;

        // First and last name are checked before the general "name", so a sheet with both
        // does not have "Efternamn" swallowed by the looser match.
        foreach (var (role, words) in Known)
            if (words.Any(w => normalized == w))
                return role;

        foreach (var (role, words) in Known)
            if (words.Any(w => normalized.StartsWith(w, StringComparison.Ordinal)))
                return role;

        return ColumnRole.Ignore;
    }

    private static readonly string[] TotalWords =
        { "total", "totalt", "summa", "s a", "sa", "delsumma", "sum", "grand total", "alla" };

    /// <summary>True when a name cell is really a totals line rather than a person.</summary>
    public static bool LooksLikeTotal(string? name)
    {
        var normalized = SearchNormalizer.Normalize(name);
        if (normalized.Length == 0) return false;

        return TotalWords.Any(w => normalized == w || normalized.StartsWith(w + " ", StringComparison.Ordinal));
    }
}
