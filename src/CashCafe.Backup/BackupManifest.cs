using System.Text.Json.Serialization;

namespace CashCafe.Backup;

/// <summary>
/// What is inside a backup, written as JSON next to the database so a person — or a future
/// program that has never heard of this one — can tell what they are holding.
/// </summary>
public sealed record BackupManifest
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = "cashcafe-backup-1";

    [JsonPropertyName("cafeName")]
    public string CafeName { get; init; } = string.Empty;

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; init; } = "0.1.0";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("students")]
    public int Students { get; init; }

    [JsonPropertyName("transactions")]
    public int Transactions { get; init; }

    [JsonPropertyName("totalBalanceOre")]
    public long TotalBalanceOre { get; init; }

    /// <summary>SHA-256 of the database file inside the archive, so a restore can prove it.</summary>
    [JsonPropertyName("databaseSha256")]
    public string DatabaseSha256 { get; init; } = string.Empty;

    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "scheduled";
}
