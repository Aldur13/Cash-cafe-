namespace CashCafe.Domain;

/// <summary>The kinds of row that can appear in the ledger. Nothing else may.</summary>
public enum TransactionType
{
    /// <summary>Money in. Always positive.</summary>
    Deposit,

    /// <summary>A sale at the counter. Always negative.</summary>
    Purchase,

    /// <summary>An admin correction. Requires a written reason.</summary>
    Adjustment,

    /// <summary>Cancels an earlier transaction. Requires a reason and a target.</summary>
    Reversal,

    /// <summary>Created by an Excel import (opening balances and import corrections).</summary>
    Import,
}

/// <summary>How money reached the café. Recorded on deposits for reconciliation.</summary>
public enum DepositMethod
{
    Swish,
    Cash,
    BankTransfer,
    Correction,
    Other,
}

/// <summary>
/// How busy the café is right now. Set by staff with a slider in the admin panel
/// and shown to students on the site so they know whether to come now or wait.
/// </summary>
public enum BusynessLevel
{
    Closed = 0,
    Quiet = 1,
    Steady = 2,
    Busy = 3,
    Packed = 4,
}

public static class BusynessLevelExtensions
{
    public static string ToSwedish(this BusynessLevel level) => level switch
    {
        BusynessLevel.Closed => "Stängt",
        BusynessLevel.Quiet => "Lugnt",
        BusynessLevel.Steady => "Lagom",
        BusynessLevel.Busy => "Mycket att göra",
        BusynessLevel.Packed => "Full rulle",
        _ => level.ToString(),
    };

    public static string ToEnglish(this BusynessLevel level) => level switch
    {
        BusynessLevel.Closed => "Closed",
        BusynessLevel.Quiet => "Quiet",
        BusynessLevel.Steady => "Steady",
        BusynessLevel.Busy => "Busy",
        BusynessLevel.Packed => "Packed",
        _ => level.ToString(),
    };

    /// <summary>Roughly how long students should expect to queue. Shown under the indicator.</summary>
    public static string QueueHint(this BusynessLevel level) => level switch
    {
        BusynessLevel.Closed => "The café is closed right now.",
        BusynessLevel.Quiet => "No queue — come now.",
        BusynessLevel.Steady => "A short queue, a minute or two.",
        BusynessLevel.Busy => "Busy — expect to wait a few minutes.",
        BusynessLevel.Packed => "Very busy — come back later if you can.",
        _ => string.Empty,
    };
}
