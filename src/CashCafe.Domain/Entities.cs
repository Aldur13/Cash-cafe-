namespace CashCafe.Domain;

/// <summary>A person who can buy things in the café.</summary>
public sealed record Student
{
    public long Id { get; init; }
    public required string FirstName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public required string DisplayName { get; init; }
    public required string SearchName { get; init; }
    public string? ClassName { get; init; }

    /// <summary>
    /// A cache of the sum of this student's ledger rows. The ledger is the truth;
    /// this is verified against it on every start and by the database verifier.
    /// </summary>
    public Money Balance { get; init; }

    /// <summary>Per-student floor. Null means "use the café default".</summary>
    public Money? CreditLimit { get; init; }

    public bool IsActive { get; init; } = true;
    public string? Note { get; init; }
    public long? MergedIntoId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public string CreatedBy { get; init; } = "system";
    public DateTimeOffset? LastActivityUtc { get; init; }

    /// <summary>Set when the student has linked a school account for the website.</summary>
    public bool HasLogin { get; init; }
}

/// <summary>Something the café sells.</summary>
public sealed record Item
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string SearchName { get; init; }
    public string? Category { get; init; }
    public Money Price { get; init; }
    public string? ShortcutKey { get; init; }

    /// <summary>False when sold out for the day. Hidden from the till, kept in reports.</summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>True when removed but already sold at least once, so history stays intact.</summary>
    public bool IsArchived { get; init; }

    public int SortOrder { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

/// <summary>One row in the ledger. Once written it is never changed or removed.</summary>
public sealed record Transaction
{
    public long Id { get; init; }
    public long StudentId { get; init; }
    public TransactionType Type { get; init; }

    /// <summary>Negative when money leaves the student.</summary>
    public Money Amount { get; init; }

    /// <summary>The student's balance immediately after this row.</summary>
    public Money BalanceAfter { get; init; }

    public DateTimeOffset OccurredUtc { get; init; }
    public DateTimeOffset RecordedUtc { get; init; }
    public string Operator { get; init; } = "Café";
    public string SessionId { get; init; } = string.Empty;
    public DepositMethod? Method { get; init; }
    public string? Reference { get; init; }
    public string? Reason { get; init; }
    public long? ReversesId { get; init; }
    public long? ImportId { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<TransactionLine> Lines { get; init; } = Array.Empty<TransactionLine>();
}

/// <summary>
/// One item row inside a purchase. The name and unit price are stored as they were
/// at the time of sale, so later price changes and renames cannot rewrite history.
/// </summary>
public sealed record TransactionLine
{
    public long Id { get; init; }
    public long TransactionId { get; init; }
    public int LineNo { get; init; }
    public long? ItemId { get; init; }
    public required string ItemName { get; init; }
    public Money UnitPrice { get; init; }
    public int Quantity { get; init; }
    public Money LineTotal { get; init; }
    public bool IsCustom { get; init; }
}

/// <summary>
/// A student's link to a school Google or Microsoft account, used only by the website.
/// The café stores no password of any kind — sign-in happens at the provider.
/// </summary>
public sealed record StudentLogin
{
    public long Id { get; init; }
    public long StudentId { get; init; }

    /// <summary>"google" or "microsoft".</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The provider's stable subject id, written the first time the person signs in.
    /// Null while the link is still just a registered address waiting to be claimed.
    /// </summary>
    public string? ProviderSubject { get; init; }

    /// <summary>Lower-cased school email, kept only so an admin can see which account is linked.</summary>
    public required string Email { get; init; }

    public DateTimeOffset LinkedUtc { get; init; }
    public string LinkedBy { get; init; } = "admin";

    /// <summary>When the address was first signed in with, binding it to one account.</summary>
    public DateTimeOffset? ClaimedUtc { get; init; }

    public bool IsClaimed => ProviderSubject is not null;
    public DateTimeOffset? LastSeenUtc { get; init; }
    public bool IsEnabled { get; init; } = true;
}

/// <summary>One line of the permanent record of who did what, and when.</summary>
public sealed record AuditEntry
{
    public long Id { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public required string Actor { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public required string Action { get; init; }
    public string? EntityType { get; init; }
    public long? EntityId { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Detail { get; init; }
}

/// <summary>A price an item once had, and for how long.</summary>
public sealed record PriceHistoryEntry
{
    public long ItemId { get; init; }
    public required string ItemName { get; init; }
    public Money Price { get; init; }
    public DateTimeOffset ValidFromUtc { get; init; }
    public DateTimeOffset? ValidToUtc { get; init; }
    public required string ChangedBy { get; init; }
    public string? Reason { get; init; }
}
