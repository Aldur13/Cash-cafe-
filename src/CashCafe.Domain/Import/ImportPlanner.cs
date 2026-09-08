namespace CashCafe.Domain.Import;

/// <summary>
/// Decides what an import would do, given the rows read from a file and the students the
/// café already has. Pure: it touches no file and no database, so every decision it makes
/// can be tested directly and shown to a person before anything is written.
/// </summary>
public static class ImportPlanner
{
    /// <summary>
    /// How similar two names must be before we mention them as a possible duplicate.
    /// Deliberately never applied automatically — "Carl Jacobs" and "Carl Jacobsson" are
    /// similar and are also two different people.
    /// </summary>
    private const double DuplicateThreshold = 0.92;

    public static ImportPreview Plan(
        IReadOnlyList<ParsedRow> rows,
        IReadOnlyList<Student> existingStudents,
        string fileName,
        string? sheetName = null)
    {
        var byName = existingStudents
            .Where(s => s.MergedIntoId is null)
            .GroupBy(s => s.SearchName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var seenInFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var planned = new List<ImportRow>(rows.Count);

        foreach (var row in rows)
        {
            planned.Add(PlanRow(row, byName, existingStudents, seenInFile));
        }

        return new ImportPreview { Rows = planned, FileName = fileName, SheetName = sheetName };
    }

    private static ImportRow PlanRow(
        ParsedRow row,
        Dictionary<string, Student> byName,
        IReadOnlyList<Student> allStudents,
        Dictionary<string, int> seenInFile)
    {
        if (row.Error is not null)
            return new ImportRow { Source = row, Status = ImportStatus.Error, Action = ImportAction.Error, Note = row.Error };

        if (string.IsNullOrWhiteSpace(row.Name))
            return new ImportRow { Source = row, Status = ImportStatus.SkippedNoName, Action = ImportAction.Ignore };

        if (row.LooksLikeTotal)
            return new ImportRow
            {
                Source = row,
                Status = ImportStatus.SkippedLooksLikeTotal,
                Action = ImportAction.Ignore,
                Note = "Looks like a totals row — untick if this really is a student",
            };

        var searchName = SearchNormalizer.Normalize(row.Name);

        if (seenInFile.TryGetValue(searchName, out var firstRow))
            return new ImportRow
            {
                Source = row,
                Status = ImportStatus.DuplicateInFile,
                Action = ImportAction.Ignore,
                Note = $"The same name is already on row {firstRow}",
            };

        seenInFile[searchName] = row.RowNumber;

        var match = FindExisting(searchName, byName);

        if (match is null)
        {
            var similar = FindSimilar(searchName, allStudents);
            if (similar is not null)
                return new ImportRow
                {
                    Source = row,
                    Status = ImportStatus.PossibleDuplicate,
                    Action = ImportAction.CreateStudent,
                    MatchedStudentId = similar.Id,
                    MatchedStudentName = similar.DisplayName,
                    ExistingBalance = similar.Balance,
                    Note = $"Very similar to {similar.DisplayName} — check before importing",
                };

            return new ImportRow { Source = row, Status = ImportStatus.NewStudent, Action = ImportAction.CreateStudent };
        }

        var amount = row.Amount ?? Money.Zero;

        if (amount == match.Balance)
            return new ImportRow
            {
                Source = row,
                Status = ImportStatus.ExistsSameBalance,
                Action = ImportAction.Skip,
                MatchedStudentId = match.Id,
                MatchedStudentName = match.DisplayName,
                ExistingBalance = match.Balance,
            };

        return new ImportRow
        {
            Source = row,
            Status = ImportStatus.ExistsDifferentBalance,
            Action = ImportAction.AdjustToFileValue,
            MatchedStudentId = match.Id,
            MatchedStudentName = match.DisplayName,
            ExistingBalance = match.Balance,
            Note = $"Café says {match.Balance}, the file says {amount}",
        };
    }

    /// <summary>
    /// An exact match on the normalised name, or the same name with the parts swapped —
    /// "Jacobs Carl" in a sheet sorted by surname is the same person as "Carl Jacobs".
    /// </summary>
    private static Student? FindExisting(string searchName, Dictionary<string, Student> byName)
    {
        if (byName.TryGetValue(searchName, out var exact)) return exact;

        var words = searchName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2) return null;

        var swapped = $"{words[1]} {words[0]}";
        return byName.TryGetValue(swapped, out var reversed) ? reversed : null;
    }

    private static Student? FindSimilar(string searchName, IReadOnlyList<Student> students)
    {
        Student? best = null;
        var bestScore = DuplicateThreshold;

        foreach (var student in students)
        {
            var score = Similarity(searchName, student.SearchName);
            if (score < bestScore) continue;

            bestScore = score;
            best = student;
        }

        return best;
    }

    /// <summary>
    /// Jaro-Winkler similarity: it rewards a shared prefix, which is what makes it good at
    /// spotting the same person typed slightly differently rather than two different people
    /// who happen to share letters.
    /// </summary>
    internal static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var window = Math.Max(a.Length, b.Length) / 2 - 1;
        if (window < 0) window = 0;

        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var from = Math.Max(0, i - window);
            var to = Math.Min(b.Length - 1, i + window);

            for (var j = from; j <= to; j++)
            {
                if (bMatched[j] || a[i] != b[j]) continue;

                aMatched[i] = true;
                bMatched[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        var transpositions = 0;
        var k = 0;

        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatched[i]) continue;
            while (!bMatched[k]) k++;
            if (a[i] != b[k]) transpositions++;
            k++;
        }

        double m = matches;
        var jaro = (m / a.Length + m / b.Length + (m - transpositions / 2.0) / m) / 3.0;

        var prefix = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(a.Length, b.Length)) && a[i] == b[i]; i++) prefix++;

        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
