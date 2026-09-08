using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashCafe.Data;
using CashCafe.Domain;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CashCafe.Backup;

public sealed record BackupOutcome(bool Succeeded, string FileName, long SizeBytes, string Message)
{
    public static BackupOutcome Failed(string message) => new(false, string.Empty, 0, message);
}

public sealed record RestoreOutcome(bool Succeeded, string Message, BackupManifest? Manifest = null);

public sealed record VerificationOutcome(bool Succeeded, string Message);

/// <summary>
/// Makes, checks and restores backups.
///
/// A backup is a consistent snapshot taken through SQLite's own online backup API — never a
/// file copy, which can catch the database mid-write and produce something that looks fine
/// until the day you need it. The snapshot is zipped with a manifest and two plain CSV
/// files, and encrypted before it goes anywhere off this computer.
/// </summary>
public sealed class BackupService(
    CafeDatabase database,
    SettingsRepository settings,
    StudentRepository students,
    LedgerRepository ledger)
{
    public const string Extension = ".cafebak";

    /// <summary>
    /// Folder names that mean a desktop sync client is watching. Used to refuse to run with
    /// the live database inside one — see <see cref="FolderDestination"/> for why.
    /// </summary>
    private static readonly string[] SyncFolderMarkers =
        { "google drive", "googledrive", "onedrive", "dropbox", "icloud drive", "nextcloud" };

    public BackupOutcome Create(
        IBackupDestination destination,
        string? passphrase = null,
        string reason = "scheduled",
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var encrypt = destination.RequiresEncryption || passphrase is not null;

        if (destination.RequiresEncryption && string.IsNullOrEmpty(passphrase))
            return BackupOutcome.Failed(
                $"{destination.Name} leaves this computer, so it needs a backup passphrase before it can be used.");

        try
        {
            using var archive = new MemoryStream();
            var manifest = BuildArchive(archive, timestamp, reason, encrypt);
            archive.Position = 0;

            using var payload = new MemoryStream();

            if (encrypt)
            {
                BackupCrypto.Encrypt(archive, payload, passphrase!);
                payload.Position = 0;
            }
            else
            {
                archive.CopyTo(payload);
                payload.Position = 0;
            }

            var fileName = $"CashCafe_{timestamp.ToLocalTime():yyyy-MM-dd_HHmm}{Extension}";
            var stored = destination.Write(fileName, payload);

            return new BackupOutcome(true, stored.Name, stored.SizeBytes,
                $"Backed up {manifest.Students} student(s) and {manifest.Transactions} transaction(s) " +
                $"to {destination.Name}{(encrypt ? ", encrypted" : string.Empty)}.");
        }
        catch (Exception ex)
        {
            return BackupOutcome.Failed($"Backup to {destination.Name} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the most recent backup, decrypts it, checks the database inside against the
    /// checksum in its manifest and runs SQLite's integrity check on it.
    ///
    /// This is the difference between having backups and having restorable backups, so it
    /// runs weekly on its own rather than waiting for somebody to think of it.
    /// </summary>
    public VerificationOutcome VerifyLatest(IBackupDestination destination, string? passphrase = null)
    {
        var latest = destination.List().FirstOrDefault();
        if (latest is null) return new VerificationOutcome(false, $"There are no backups in {destination.Name} yet.");

        try
        {
            using var stored = destination.OpenRead(latest.Id);
            using var archive = OpenArchive(stored, passphrase);

            var manifest = ReadManifest(archive);
            var databaseEntry = archive.GetEntry("cashcafe.db")
                                ?? throw new InvalidDataException("The backup has no database in it.");

            var temporary = Path.Combine(Path.GetTempPath(), $"cashcafe-verify-{Guid.NewGuid():N}.db");

            try
            {
                using (var entry = databaseEntry.Open())
                using (var file = File.Create(temporary))
                    entry.CopyTo(file);

                var hash = FileHash(temporary);
                if (!string.IsNullOrEmpty(manifest.DatabaseSha256) && hash != manifest.DatabaseSha256)
                    return new VerificationOutcome(false,
                        $"{latest.Name} does not match its own checksum — treat it as damaged.");

                var check = new CafeDatabase(temporary);
                using var connection = check.Open();
                var integrity = connection.QuerySingle<string>("PRAGMA integrity_check");

                if (integrity != "ok")
                    return new VerificationOutcome(false, $"{latest.Name} fails SQLite's integrity check: {integrity}");

                var balances = connection.ExecuteScalar<long?>("SELECT sum(balance_ore) FROM students") ?? 0;
                if (balances != manifest.TotalBalanceOre)
                    return new VerificationOutcome(false,
                        $"{latest.Name} holds {new Money(balances)} but its manifest says {new Money(manifest.TotalBalanceOre)}.");

                return new VerificationOutcome(true,
                    $"{latest.Name} opens, matches its checksum and holds {manifest.Students} student(s) " +
                    $"totalling {new Money(manifest.TotalBalanceOre)}.");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (var file in new[] { temporary, temporary + "-wal", temporary + "-shm" })
                    if (File.Exists(file)) File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            return new VerificationOutcome(false, $"{latest.Name} could not be verified: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores a backup over the live database.
    ///
    /// The order matters: the backup is fully checked *before* anything is touched, and a
    /// safety copy of the current state is taken before it is replaced. A restore that
    /// destroys today's takings because yesterday's file turned out to be corrupt would be
    /// the worst possible failure of a backup system.
    /// </summary>
    public RestoreOutcome Restore(
        IBackupDestination destination,
        string backupId,
        string? passphrase = null,
        IBackupDestination? safetyCopyTo = null)
    {
        try
        {
            using var stored = destination.OpenRead(backupId);
            using var archive = OpenArchive(stored, passphrase);

            var manifest = ReadManifest(archive);
            var databaseEntry = archive.GetEntry("cashcafe.db")
                                ?? throw new InvalidDataException("The backup has no database in it.");

            var staging = database.DatabasePath + ".restoring";

            using (var entry = databaseEntry.Open())
            using (var file = File.Create(staging))
                entry.CopyTo(file);

            try
            {
                if (!string.IsNullOrEmpty(manifest.DatabaseSha256) && FileHash(staging) != manifest.DatabaseSha256)
                    return new RestoreOutcome(false, "That backup does not match its own checksum. Nothing was changed.");

                var candidate = new CafeDatabase(staging);
                using (var connection = candidate.Open())
                {
                    if (connection.QuerySingle<string>("PRAGMA integrity_check") != "ok")
                        return new RestoreOutcome(false, "That backup fails an integrity check. Nothing was changed.");
                }

                SqliteConnection.ClearAllPools();

                if (safetyCopyTo is not null)
                    Create(safetyCopyTo, reason: "pre-restore");

                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var sidecar = database.DatabasePath + suffix;
                    if (File.Exists(sidecar)) File.Delete(sidecar);
                }

                File.Move(staging, database.DatabasePath, overwrite: true);

                return new RestoreOutcome(true,
                    $"Restored the backup from {manifest.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} — " +
                    $"{manifest.Students} student(s), {manifest.Transactions} transaction(s).", manifest);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
        catch (Exception ex)
        {
            return new RestoreOutcome(false, $"Restore failed, and nothing was changed: {ex.Message}");
        }
    }

    /// <summary>
    /// Keeps the last N daily backups, one per week for N weeks, and one per month for N
    /// months — roughly a year of history in about ten megabytes. Deleting is deliberately
    /// the last thing this class does and the only thing it removes.
    /// </summary>
    public int ApplyRetention(
        IBackupDestination destination,
        int dailyToKeep = 14,
        int weeklyToKeep = 8,
        int monthlyToKeep = 12,
        DateTimeOffset? now = null)
    {
        var today = (now ?? DateTimeOffset.UtcNow).Date;
        var all = destination.List().OrderByDescending(b => b.CreatedUtc).ToList();
        var keep = new HashSet<string>();

        foreach (var backup in all.Where(b => (today - b.CreatedUtc.Date).TotalDays < dailyToKeep))
            keep.Add(backup.Id);

        // The newest backup in each of the last N calendar weeks, then each of the last N months.
        foreach (var week in all.GroupBy(b => System.Globalization.ISOWeek.GetWeekOfYear(b.CreatedUtc.Date))
                     .Take(weeklyToKeep))
            keep.Add(week.First().Id);

        foreach (var month in all.GroupBy(b => (b.CreatedUtc.Year, b.CreatedUtc.Month)).Take(monthlyToKeep))
            keep.Add(month.First().Id);

        var removed = 0;
        foreach (var backup in all.Where(b => !keep.Contains(b.Id)))
        {
            destination.Delete(backup.Id);
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// True when the live database sits inside a folder a desktop sync client watches, which
    /// is the most reliable way there is to corrupt it. The admin panel shows this as a
    /// warning that does not go away.
    /// </summary>
    public bool DatabaseIsInASyncedFolder()
    {
        var path = Path.GetFullPath(database.DatabasePath).ToLowerInvariant();
        return SyncFolderMarkers.Any(marker => path.Contains(marker, StringComparison.Ordinal));
    }

    private BackupManifest BuildArchive(Stream destination, DateTimeOffset now, string reason, bool encrypted)
    {
        var snapshot = Path.Combine(Path.GetTempPath(), $"cashcafe-snapshot-{Guid.NewGuid():N}.db");

        try
        {
            TakeSnapshot(snapshot);

            var config = settings.Load();
            var allStudents = students.All(includeInactive: true);
            var schemaVersion = SchemaVersionOf(snapshot);
            var transactionCount = TransactionCountOf(snapshot);

            var manifest = new BackupManifest
            {
                CafeName = config.CafeName,
                CreatedUtc = now,
                SchemaVersion = schemaVersion,
                Students = allStudents.Count,
                Transactions = transactionCount,
                TotalBalanceOre = Money.Sum(allStudents.Select(s => s.Balance)).Ore,
                DatabaseSha256 = FileHash(snapshot),
                Encrypted = encrypted,
                Reason = reason,
            };

            using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddFile(zip, "cashcafe.db", snapshot);
                AddText(zip, "manifest.json", JsonSerializer.Serialize(manifest,
                    new JsonSerializerOptions { WriteIndented = true }));

                // Plain text copies, so that in ten years — with no copy of this program and
                // nothing that can open a .cafebak — Notepad can still say who had what.
                AddText(zip, "balances.csv", BalancesCsv(allStudents));
                AddText(zip, "transactions.csv", TransactionsCsv(allStudents));
            }

            return manifest;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { snapshot, snapshot + "-wal", snapshot + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>
    /// SQLite's own online backup API, which cooperates with a database that is being written
    /// to. Copying the file instead would eventually catch a sale halfway through.
    /// </summary>
    private void TakeSnapshot(string path)
    {
        using var source = database.Open();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());

        target.Open();
        source.BackupDatabase(target);
    }

    private static int SchemaVersionOf(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return CafeDatabase.CurrentVersion(connection);
    }

    private static int TransactionCountOf(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection.ExecuteScalar<int>("SELECT count(*) FROM transactions");
    }

    private string TransactionsCsv(IReadOnlyList<Student> allStudents)
    {
        var text = new StringBuilder("StudentId;Name;DateTime;Type;Amount;BalanceAfter;Description\n");

        foreach (var student in allStudents)
            foreach (var transaction in ledger.History(student.Id, limit: int.MaxValue).OrderBy(t => t.OccurredUtc))
            {
                var description = transaction.Lines.Count > 0
                    ? string.Join(" + ", transaction.Lines.Select(l => $"{l.ItemName} x{l.Quantity}"))
                    : transaction.Reason ?? transaction.Reference ?? string.Empty;

                text.Append(student.Id).Append(';')
                    .Append(Csv(student.DisplayName)).Append(';')
                    .Append(transaction.OccurredUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append(';')
                    .Append(transaction.Type.ToString().ToUpperInvariant()).Append(';')
                    .Append(transaction.Amount.ToString(withCurrency: false)).Append(';')
                    .Append(transaction.BalanceAfter.ToString(withCurrency: false)).Append(';')
                    .Append(Csv(description)).Append('\n');
            }

        return text.ToString();
    }

    private static string BalancesCsv(IReadOnlyList<Student> allStudents)
    {
        var text = new StringBuilder("StudentId;Name;Class;Balance;Active\n");

        foreach (var student in allStudents.OrderBy(s => s.DisplayName, StringComparer.Ordinal))
            text.Append(student.Id).Append(';')
                .Append(Csv(student.DisplayName)).Append(';')
                .Append(Csv(student.ClassName ?? string.Empty)).Append(';')
                .Append(student.Balance.ToString(withCurrency: false)).Append(';')
                .Append(student.IsActive ? "Ja" : "Nej").Append('\n');

        return text.ToString();
    }

    private static string Csv(string field) =>
        field.Contains(';') || field.Contains('"') ? '"' + field.Replace("\"", "\"\"") + '"' : field;

    private static ZipArchive OpenArchive(Stream stored, string? passphrase)
    {
        if (!BackupCrypto.LooksEncrypted(stored))
            return new ZipArchive(CopyToMemory(stored), ZipArchiveMode.Read);

        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidDataException("This backup is encrypted — the passphrase is needed to open it.");

        var plain = new MemoryStream();
        BackupCrypto.Decrypt(stored, plain, passphrase);
        plain.Position = 0;

        return new ZipArchive(plain, ZipArchiveMode.Read);
    }

    private static MemoryStream CopyToMemory(Stream source)
    {
        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }

    private static BackupManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json")
                    ?? throw new InvalidDataException("The backup has no manifest — it may not be a Cash Café backup.");

        using var stream = entry.Open();
        return JsonSerializer.Deserialize<BackupManifest>(stream)
               ?? throw new InvalidDataException("The backup's manifest could not be read.");
    }

    private static void AddFile(ZipArchive zip, string name, string path)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        using var source = File.OpenRead(path);
        source.CopyTo(target);
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        using var writer = new StreamWriter(target, new UTF8Encoding(true));
        writer.Write(content);
    }

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
