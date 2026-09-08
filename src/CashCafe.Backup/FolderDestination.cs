namespace CashCafe.Backup;

/// <summary>
/// A plain folder: the local backup folder, a USB stick, a school file share, or a folder
/// that Google Drive or OneDrive syncs.
///
/// That last case is why backups are written once and never touched again. A sync client
/// copying a file while it is being written is the single most common way people corrupt a
/// desktop database — which is also why the live database must never live in such a folder,
/// and why the backup service refuses to run if it finds one that does.
/// </summary>
public sealed class FolderDestination(string path, string? name = null, bool requiresEncryption = false)
    : IBackupDestination
{
    public string Name { get; } = name ?? path;
    public string Path { get; } = path;
    public bool RequiresEncryption { get; } = requiresEncryption;

    public DestinationHealth Test()
    {
        try
        {
            Directory.CreateDirectory(Path);

            var probe = System.IO.Path.Combine(Path, $".cashcafe-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            return new DestinationHealth(true, $"{Name} is reachable and writable.");
        }
        catch (Exception ex)
        {
            return new DestinationHealth(false, $"{Name} is not usable: {ex.Message}");
        }
    }

    public StoredBackup Write(string fileName, Stream content)
    {
        Directory.CreateDirectory(Path);
        var full = System.IO.Path.Combine(Path, fileName);

        // Written to a temporary name and moved into place, so a sync client or a person
        // browsing the folder never sees a half-written backup that looks complete.
        var staging = full + ".part";

        using (var file = File.Create(staging))
            content.CopyTo(file);

        File.Move(staging, full, overwrite: true);

        var info = new FileInfo(full);
        return new StoredBackup(fileName, fileName, info.Length, info.CreationTimeUtc);
    }

    public IReadOnlyList<StoredBackup> List()
    {
        if (!Directory.Exists(Path)) return Array.Empty<StoredBackup>();

        return new DirectoryInfo(Path)
            .GetFiles("*" + BackupService.Extension)
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new StoredBackup(f.Name, f.Name, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    public Stream OpenRead(string id) => File.OpenRead(System.IO.Path.Combine(Path, id));

    public void Delete(string id)
    {
        var full = System.IO.Path.Combine(Path, id);
        if (File.Exists(full)) File.Delete(full);
    }
}
