namespace CashCafe.Excel;

public enum ColumnRole
{
    Ignore,
    Name,
    FirstName,
    LastName,
    Class,
    Balance,
}

/// <summary>
/// How to read one sheet: which row the headers are on, where the data starts, and what
/// each column means. Detected automatically, and always shown to a person who can change it.
/// </summary>
public sealed record SheetLayout
{
    public string SheetName { get; init; } = string.Empty;
    public int HeaderRow { get; init; }
    public int FirstDataRow { get; init; } = 1;

    /// <summary>Column number (1-based) to what it holds.</summary>
    public IReadOnlyDictionary<int, ColumnRole> Columns { get; init; } = new Dictionary<int, ColumnRole>();

    /// <summary>
    /// True for the layout the café's own sheet uses: one column holding "Carl Jacobs 50 kr",
    /// with the name and the amount in the same cell.
    /// </summary>
    public bool IsSingleColumn { get; init; }

    public int? ColumnFor(ColumnRole role) =>
        Columns.FirstOrDefault(c => c.Value == role) is { Key: > 0 } found ? found.Key : null;

    public bool HasBalance => IsSingleColumn || ColumnFor(ColumnRole.Balance) is not null;
}
