namespace CashCafe.Backup;

public sealed record StoredBackup(string Id, string Name, long SizeBytes, DateTimeOffset CreatedUtc);

public sealed record DestinationHealth(bool Reachable, string Message);

/// <summary>
/// Somewhere backups are kept. Deliberately tiny — write, list, read, delete — because
/// every extra thing a destination is allowed to do is a thing that can fail at 23:00
/// while nobody is watching.
/// </summary>
public interface IBackupDestination
{
    string Name { get; }

    /// <summary>True when this destination leaves the café computer and must be encrypted.</summary>
    bool RequiresEncryption { get; }

    DestinationHealth Test();

    StoredBackup Write(string fileName, Stream content);

    IReadOnlyList<StoredBackup> List();

    Stream OpenRead(string id);

    void Delete(string id);
}
