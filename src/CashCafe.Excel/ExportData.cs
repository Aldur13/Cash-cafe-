using CashCafe.Domain;

namespace CashCafe.Excel;

/// <summary>
/// Everything an export writes. Gathered by the caller from the repositories, so this
/// project stays a file-format concern and never touches the database.
/// </summary>
public sealed record ExportData
{
    public required string CafeName { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; init; } = "0.1.0";
    public string? FilterDescription { get; init; }

    public IReadOnlyList<Student> Students { get; init; } = Array.Empty<Student>();
    public IReadOnlyList<Transaction> Transactions { get; init; } = Array.Empty<Transaction>();
    public IReadOnlyList<Item> Items { get; init; } = Array.Empty<Item>();
    public IReadOnlyList<PriceHistoryEntry> PriceHistory { get; init; } = Array.Empty<PriceHistoryEntry>();
    public IReadOnlyList<AuditEntry> AuditLog { get; init; } = Array.Empty<AuditEntry>();

    /// <summary>Transactions that were cancelled, so the export can mark them.</summary>
    public IReadOnlySet<long> ReversedTransactionIds { get; init; } = new HashSet<long>();
}
