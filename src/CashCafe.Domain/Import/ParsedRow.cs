namespace CashCafe.Domain.Import;

/// <summary>
/// One row as it was read out of a spreadsheet, before any decision has been made about
/// what to do with it. Keeps the original text so the preview can show a person exactly
/// what was in the cell next to what we made of it.
/// </summary>
public sealed record ParsedRow
{
    public int RowNumber { get; init; }
    public string RawText { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? ClassName { get; init; }
    public Money? Amount { get; init; }
    public string? Error { get; init; }

    /// <summary>True when the row looks like a "Summa"/"Total" line rather than a person.</summary>
    public bool LooksLikeTotal { get; init; }

    public bool IsUsable => Error is null && !string.IsNullOrWhiteSpace(Name) && !LooksLikeTotal;
}
