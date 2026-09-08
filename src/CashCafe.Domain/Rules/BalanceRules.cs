namespace CashCafe.Domain.Rules;

/// <summary>The outcome of checking a purchase against a student's balance.</summary>
public sealed record PurchaseCheck
{
    public bool IsAllowed { get; private init; }

    /// <summary>The balance the student would have if this purchase went through.</summary>
    public Money BalanceAfter { get; private init; }

    /// <summary>How much the student may still spend before hitting their floor.</summary>
    public Money CanSpend { get; private init; }

    /// <summary>How much too expensive the purchase is. Zero when allowed.</summary>
    public Money Shortfall { get; private init; }

    public static PurchaseCheck Allowed(Money balanceAfter, Money canSpend) =>
        new() { IsAllowed = true, BalanceAfter = balanceAfter, CanSpend = canSpend, Shortfall = Money.Zero };

    public static PurchaseCheck Refused(Money balanceAfter, Money canSpend, Money shortfall) =>
        new() { IsAllowed = false, BalanceAfter = balanceAfter, CanSpend = canSpend, Shortfall = shortfall };
}

/// <summary>
/// The money rules. This is the only place the −10 kr floor is implemented, and it is
/// called from exactly two places: the till view model (to enable or disable Execute
/// and explain why) and the repository (inside the database transaction, immediately
/// before writing). Because both call the same function, the message on screen and the
/// rule that is actually enforced can never disagree.
/// </summary>
public static class BalanceRules
{
    /// <summary>The floor that applies to one student: their own limit, or the café default.</summary>
    public static Money EffectiveFloor(Money? studentCreditLimit, CafeSettings settings) =>
        studentCreditLimit ?? settings.MinimumBalance;

    /// <summary>What a student may still spend before reaching their floor.</summary>
    public static Money CanSpend(Money balance, Money floor) => balance - floor;

    /// <summary>
    /// The rule: a purchase is allowed only while the resulting balance stays at or
    /// above the floor. Landing exactly on −10,00 kr is allowed; one öre further is not.
    /// </summary>
    public static PurchaseCheck CheckPurchase(Money balance, Money floor, Money purchaseTotal)
    {
        if (purchaseTotal < Money.Zero)
            throw new ArgumentOutOfRangeException(nameof(purchaseTotal), "A purchase total is never negative.");

        var after = balance - purchaseTotal;
        var canSpend = CanSpend(balance, floor);

        return after >= floor
            ? PurchaseCheck.Allowed(after, canSpend)
            : PurchaseCheck.Refused(after, canSpend, floor - after);
    }

    /// <summary>
    /// The message shown at the counter when a purchase is refused. It names the numbers
    /// staff need in order to explain it to the student without doing arithmetic.
    /// </summary>
    public static string RefusalMessage(string studentName, Money balance, Money floor, Money purchaseTotal)
    {
        var check = CheckPurchase(balance, floor, purchaseTotal);
        if (check.IsAllowed) return string.Empty;

        return $"{studentName} has {balance} and can spend {check.CanSpend} before reaching the " +
               $"{floor} limit. This purchase is {purchaseTotal} — {check.Shortfall} too much.";
    }

    /// <summary>Deposits are never refused, but they must be positive.</summary>
    public static bool IsValidDeposit(Money amount) => amount > Money.Zero;
}
