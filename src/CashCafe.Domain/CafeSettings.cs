namespace CashCafe.Domain;

/// <summary>Café-wide settings. Defaults are the ones the café asked for.</summary>
public sealed record CafeSettings
{
    public string CafeName { get; init; } = "Skolans Café";

    /// <summary>
    /// The lowest a balance may go through purchases. Must never be positive:
    /// a positive floor would block students who legitimately have 0 kr.
    /// </summary>
    public Money MinimumBalance { get; init; } = Money.FromKronor(-10);

    public int MaxLinesPerPurchase { get; init; } = 10;
    public int MaxQuantityPerLine { get; init; } = 99;
    public TimeSpan UndoWindow { get; init; } = TimeSpan.FromMinutes(15);
    public Money LargePurchaseWarning { get; init; } = Money.FromKronor(500);
    public Money LowBalanceWarning { get; init; } = Money.FromKronor(20);

    /// <summary>How busy the café is right now, set by staff on the admin slider.</summary>
    public BusynessLevel Busyness { get; init; } = BusynessLevel.Closed;

    public DateTimeOffset? BusynessUpdatedUtc { get; init; }

    /// <summary>Student website master switch. Off until the school turns it on.</summary>
    public bool StudentSiteEnabled { get; init; }

    /// <summary>Whether the site shows a student their own purchase history.</summary>
    public bool StudentSiteShowHistory { get; init; } = true;

    /// <summary>Whether the site shows the busyness indicator.</summary>
    public bool StudentSiteShowBusyness { get; init; } = true;

    /// <summary>How many days of history the site shows. 0 = all of it.</summary>
    public int StudentSiteHistoryDays { get; init; } = 90;

    public static CafeSettings Defaults => new();

    /// <summary>Rejects a floor above zero, which the rules never allow.</summary>
    public CafeSettings WithMinimumBalance(Money floor) =>
        floor > Money.Zero
            ? throw new ArgumentOutOfRangeException(nameof(floor), "The minimum balance can never be positive.")
            : this with { MinimumBalance = floor };
}
