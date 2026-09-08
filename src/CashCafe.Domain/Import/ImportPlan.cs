namespace CashCafe.Domain.Import;

public enum ImportAction
{
    /// <summary>The name is not in the café yet: create the student with this opening balance.</summary>
    CreateStudent,

    /// <summary>Already there and the numbers agree. Nothing to write.</summary>
    Skip,

    /// <summary>Already there with a different balance: write the difference as a correction.</summary>
    AdjustToFileValue,

    /// <summary>Already there with a different balance: leave the café's number alone.</summary>
    KeepExisting,

    /// <summary>Treat the file's number as a top-up rather than a total.</summary>
    AddToBalance,

    /// <summary>Blank row, a totals line, or something a person chose to leave out.</summary>
    Ignore,

    /// <summary>The row could not be read. Import is blocked until every one of these is resolved.</summary>
    Error,
}

/// <summary>Why a row is being shown the way it is, in words the preview can print.</summary>
public enum ImportStatus
{
    NewStudent,
    ExistsSameBalance,
    ExistsDifferentBalance,
    PossibleDuplicate,
    DuplicateInFile,
    SkippedNoName,
    SkippedLooksLikeTotal,
    Error,
}

public sealed record ImportRow
{
    public required ParsedRow Source { get; init; }
    public ImportStatus Status { get; init; }
    public ImportAction Action { get; init; }

    /// <summary>The student this row was matched to, if any.</summary>
    public long? MatchedStudentId { get; init; }

    public string? MatchedStudentName { get; init; }
    public Money? ExistingBalance { get; init; }
    public string? Note { get; init; }

    /// <summary>What this row would actually write, once the action is applied.</summary>
    public Money EffectiveChange => Action switch
    {
        ImportAction.CreateStudent => Source.Amount ?? Money.Zero,
        ImportAction.AdjustToFileValue => (Source.Amount ?? Money.Zero) - (ExistingBalance ?? Money.Zero),
        ImportAction.AddToBalance => Source.Amount ?? Money.Zero,
        _ => Money.Zero,
    };
}

/// <summary>
/// The whole import, decided but not yet written. This is what the preview screen shows,
/// and nothing reaches the database until a person has looked at it and pressed Import.
/// </summary>
public sealed record ImportPreview
{
    public required IReadOnlyList<ImportRow> Rows { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? SheetName { get; init; }

    public int RowsRead => Rows.Count;
    public int Errors => Rows.Count(r => r.Status == ImportStatus.Error);
    public int WillCreate => Rows.Count(r => r.Action == ImportAction.CreateStudent);
    public int WillAdjust => Rows.Count(r => r.Action is ImportAction.AdjustToFileValue or ImportAction.AddToBalance);
    public int Skipped => Rows.Count(r => r.Action == ImportAction.Ignore);

    /// <summary>The total of the amounts in the file — the number to compare with the sheet.</summary>
    public Money TotalInFile => Money.Sum(
        Rows.Where(r => r.Source.IsUsable && r.Source.Amount is not null).Select(r => r.Source.Amount!.Value));

    /// <summary>How much the café's books move if this import is applied.</summary>
    public Money TotalChange => Money.Sum(Rows.Select(r => r.EffectiveChange));

    /// <summary>
    /// Import is blocked while any row is unreadable. A person must fix or exclude it —
    /// importing "most of" a balance sheet is how a café ends up with numbers nobody trusts.
    /// </summary>
    public bool CanImport => Errors == 0 && Rows.Any(r => r.Action != ImportAction.Ignore);

    public ImportPreview With(int rowNumber, ImportAction action)
    {
        var updated = Rows.Select(r => r.Source.RowNumber == rowNumber ? r with { Action = action } : r).ToList();
        return this with { Rows = updated };
    }

    /// <summary>Applies one choice to every row that is waiting for the same decision.</summary>
    public ImportPreview WithAll(ImportStatus status, ImportAction action)
    {
        var updated = Rows.Select(r => r.Status == status ? r with { Action = action } : r).ToList();
        return this with { Rows = updated };
    }
}
