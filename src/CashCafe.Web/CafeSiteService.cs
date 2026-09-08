using CashCafe.Data;
using CashCafe.Domain;

namespace CashCafe.Web;

public sealed record HistoryEntry(
    DateTimeOffset When,
    string Description,
    Money Amount,
    Money BalanceAfter,
    bool IsReversed,
    bool IsReversal);

public sealed record StudentView(
    string Name,
    string? ClassName,
    Money Balance,
    Money CanSpend,
    Money Floor,
    bool IsLow,
    IReadOnlyList<HistoryEntry> History);

/// <summary>
/// What the site is allowed to show, and nothing more.
///
/// Every read here is scoped to one student id taken from the signed-in session — the site
/// has no route, query string or form field that names a student, so there is nothing to
/// tamper with to see somebody else's balance.
/// </summary>
public sealed class CafeSiteService(
    StudentRepository students,
    LedgerRepository ledger,
    SettingsRepository settings)
{
    public CafeSettings Settings => settings.Load();

    public StudentView? ForStudent(long studentId)
    {
        var student = students.Find(studentId);
        if (student is null || !student.IsActive) return null;

        var config = settings.Load();
        var floor = Domain.Rules.BalanceRules.EffectiveFloor(student.CreditLimit, config);

        var history = config.StudentSiteShowHistory
            ? BuildHistory(studentId, config)
            : Array.Empty<HistoryEntry>();

        return new StudentView(
            student.DisplayName,
            student.ClassName,
            student.Balance,
            Domain.Rules.BalanceRules.CanSpend(student.Balance, floor),
            floor,
            student.Balance <= config.LowBalanceWarning,
            history);
    }

    private IReadOnlyList<HistoryEntry> BuildHistory(long studentId, CafeSettings config)
    {
        var since = config.StudentSiteHistoryDays > 0
            ? DateTimeOffset.UtcNow.AddDays(-config.StudentSiteHistoryDays)
            : (DateTimeOffset?)null;

        var transactions = ledger.History(studentId, limit: 200, since: since);
        var reversed = ledger.ReversedTransactionIds(transactions.Select(t => t.Id));

        return transactions.Select(t => new HistoryEntry(
            t.OccurredUtc.ToLocalTime(),
            Describe(t),
            t.Amount,
            t.BalanceAfter,
            reversed.Contains(t.Id),
            t.Type == TransactionType.Reversal)).ToList();
    }

    /// <summary>
    /// The line a student reads. Purchases list what was bought, because "-40,00 kr" on its
    /// own is exactly the sort of unexplained number this system replaced.
    /// </summary>
    private static string Describe(Transaction transaction) => transaction.Type switch
    {
        TransactionType.Purchase when transaction.Lines.Count > 0 =>
            string.Join(", ", transaction.Lines.Select(l => l.Quantity > 1 ? $"{l.ItemName} ×{l.Quantity}" : l.ItemName)),
        TransactionType.Purchase => "Purchase",
        TransactionType.Deposit => transaction.Method is null ? "Money in" : $"Money in ({transaction.Method})",
        TransactionType.Import => "Opening balance",
        TransactionType.Adjustment => $"Correction — {transaction.Reason}",
        TransactionType.Reversal => $"Cancelled — {transaction.Reason}",
        _ => transaction.Type.ToString(),
    };
}
