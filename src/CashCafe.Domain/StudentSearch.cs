namespace CashCafe.Domain;

public sealed record SearchHit<T>(T Value, int Rank, DateTimeOffset? LastActivityUtc);

/// <summary>
/// The live search behind the till's student box and item box: it filters from the
/// first keystroke, tolerates a typo, and puts the people you served most recently
/// at the top so the regulars rise there by themselves.
///
/// It runs over an in-memory list, so it stays instant with thousands of students
/// and never issues a database query per keystroke.
/// </summary>
public static class StudentSearch
{
    public const int DefaultLimit = 8;

    // Lower rank sorts first. These mirror the table in the wiki.
    private const int RankExact = 0;
    private const int RankWordPrefix = 1;
    private const int RankInitials = 2;
    private const int RankContains = 3;
    private const int RankFuzzy = 4;
    private const int NoMatch = int.MaxValue;

    public static IReadOnlyList<Student> Find(IEnumerable<Student> students, string query, int limit = DefaultLimit)
    {
        var normalized = SearchNormalizer.Normalize(query);
        if (normalized.Length == 0)
        {
            return students
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.LastActivityUtc ?? DateTimeOffset.MinValue)
                .ThenBy(s => s.DisplayName, StringComparer.CurrentCulture)
                .Take(limit)
                .ToList();
        }

        var terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return students
            .Where(s => s.IsActive)
            .Select(s => new SearchHit<Student>(s, RankOf(s.SearchName, terms, normalized), s.LastActivityUtc))
            .Where(h => h.Rank != NoMatch)
            .OrderBy(h => h.Rank)
            .ThenByDescending(h => h.LastActivityUtc ?? DateTimeOffset.MinValue)
            .ThenBy(h => h.Value.DisplayName, StringComparer.CurrentCulture)
            .Take(limit)
            .Select(h => h.Value)
            .ToList();
    }

    public static IReadOnlyList<Item> FindItems(IEnumerable<Item> items, string query, int limit = DefaultLimit)
    {
        var available = items.Where(i => i.IsAvailable && !i.IsArchived);
        var normalized = SearchNormalizer.Normalize(query);

        if (normalized.Length == 0)
            return available.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Take(limit).ToList();

        var terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return available
            .Select(i => new SearchHit<Item>(i, RankOf(i.SearchName, terms, normalized), null))
            .Where(h => h.Rank != NoMatch)
            .OrderBy(h => h.Rank)
            .ThenBy(h => h.Value.SortOrder)
            .ThenBy(h => h.Value.Name, StringComparer.CurrentCulture)
            .Take(limit)
            .Select(h => h.Value)
            .ToList();
    }

    /// <summary>
    /// Ranks one candidate against the typed terms. Every term must match somewhere,
    /// so "jac carl" finds Carl Jacobs while "carl x" finds nobody. The rank returned
    /// is the worst (highest) of the per-term ranks.
    /// </summary>
    internal static int RankOf(string candidateSearchName, string[] terms, string wholeQuery)
    {
        if (candidateSearchName.Length == 0) return NoMatch;
        if (candidateSearchName == wholeQuery) return RankExact;

        var words = candidateSearchName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Select(w => w[0]));

        // Typing initials only: "cj" for Carl Jacobs.
        if (terms.Length == 1 && terms[0].Length >= 2 && initials.StartsWith(terms[0], StringComparison.Ordinal))
            return RankInitials;

        var worst = RankExact;

        foreach (var term in terms)
        {
            var rank = RankTerm(words, candidateSearchName, term);
            if (rank == NoMatch) return NoMatch;
            if (rank > worst) worst = rank;
        }

        return worst;
    }

    private static int RankTerm(string[] words, string candidate, string term)
    {
        if (words.Any(w => w.StartsWith(term, StringComparison.Ordinal))) return RankWordPrefix;
        if (candidate.Contains(term, StringComparison.Ordinal)) return RankContains;

        // One typo, and only for terms long enough that a typo is the likely explanation.
        if (term.Length >= 4 && words.Any(w => IsWithinOneEdit(w, term))) return RankFuzzy;

        return NoMatch;
    }

    /// <summary>
    /// True when the two words are at most one edit apart: an insertion, a deletion, a
    /// substitution, or a swap of two neighbouring characters.
    ///
    /// The swap matters more than it looks. Typing "jacbos" for "jacobs" is the single most
    /// common mistake anyone makes at speed, and plain edit distance counts it as two edits,
    /// so a search that forgives only one would miss it. Forgiving two real edits instead
    /// would start matching the wrong student, which at a till is worse than no match.
    /// </summary>
    internal static bool IsWithinOneEdit(string a, string b)
    {
        if (a == b) return true;

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (longer.Length - shorter.Length > 1) return false;

        var i = 0;
        var j = 0;
        var edited = false;

        while (i < shorter.Length && j < longer.Length)
        {
            if (shorter[i] == longer[j])
            {
                i++;
                j++;
                continue;
            }

            if (edited) return false;
            edited = true;

            if (shorter.Length == longer.Length)
            {
                // Equal lengths: either two neighbours swapped, or one character mistyped.
                var isTransposition = i + 1 < shorter.Length
                                      && shorter[i] == longer[j + 1]
                                      && shorter[i + 1] == longer[j];
                if (isTransposition)
                {
                    i += 2;
                    j += 2;
                    continue;
                }

                i++;
            }

            // Different lengths: skip the extra character in the longer word.
            j++;
        }

        return true;
    }
}
