namespace CashCafe.Domain.Import;

public enum ItemImportAction
{
    /// <summary>The name is not on the menu yet: add it.</summary>
    CreateItem,

    /// <summary>Already on the menu and nothing in the row differs: nothing to write.</summary>
    KeepExisting,

    /// <summary>Already on the menu with a different price, category, shortcut, or on-sale flag.</summary>
    Update,

    /// <summary>Blank row, or something a person chose to leave out.</summary>
    Ignore,

    /// <summary>The row could not be read. Import is blocked until every one of these is resolved.</summary>
    Error,
}

public enum ItemImportStatus
{
    NewItem,
    ExistsUnchanged,
    ExistsDifferent,
    DuplicateInFile,
    SkippedNoName,
    Error,
}

/// <summary>One row of the menu, as read out of a spreadsheet, before any decision is made.</summary>
public sealed record ParsedItemRow
{
    public int RowNumber { get; init; }
    public string RawText { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Category { get; init; }
    public Money? Price { get; init; }
    public string? Shortcut { get; init; }
    public bool? Available { get; init; }
    public string? Error { get; init; }

    public bool IsUsable => Error is null && !string.IsNullOrWhiteSpace(Name) && Price is not null;
}

public sealed record ItemImportRow
{
    public required ParsedItemRow Source { get; init; }
    public ItemImportStatus Status { get; init; }
    public ItemImportAction Action { get; init; }

    /// <summary>The menu item this row was matched to, if any.</summary>
    public long? MatchedItemId { get; init; }

    public string? MatchedItemName { get; init; }
    public Money? ExistingPrice { get; init; }
    public string? Note { get; init; }
}

/// <summary>The whole menu import, decided but not yet written.</summary>
public sealed record ItemImportPreview
{
    public required IReadOnlyList<ItemImportRow> Rows { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? SheetName { get; init; }

    public int RowsRead => Rows.Count;
    public int Errors => Rows.Count(r => r.Status == ItemImportStatus.Error);
    public int WillCreate => Rows.Count(r => r.Action == ItemImportAction.CreateItem);
    public int WillUpdate => Rows.Count(r => r.Action == ItemImportAction.Update);
    public int Skipped => Rows.Count(r => r.Action == ItemImportAction.Ignore);

    public bool CanImport => Errors == 0 && Rows.Any(r => r.Action != ItemImportAction.Ignore);
}
