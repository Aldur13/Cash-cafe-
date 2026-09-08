using CashCafe.Data;
using CashCafe.Domain;
using CashCafe.Domain.Rules;

namespace CashCafe.Backup.Tests;

/// <summary>A café with a little trading in it, plus somewhere to put backups.</summary>
public sealed class BackupTestCafe : IDisposable
{
    private readonly string _root;

    public BackupTestCafe()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cashcafe-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        DatabasePath = Path.Combine(_root, "cashcafe.db");
        Database = new CafeDatabase(DatabasePath);
        Database.Migrate("test");

        Settings = new SettingsRepository(Database);
        Students = new StudentRepository(Database);
        Items = new ItemRepository(Database);
        Ledger = new LedgerRepository(Database);
        Verifier = new DatabaseVerifier(Database);
        Service = new BackupService(Database, Settings, Students, Ledger);

        Local = new FolderDestination(Path.Combine(_root, "backups"), "Local folder");
        OffSite = new FolderDestination(Path.Combine(_root, "offsite"), "School server", requiresEncryption: true);
    }

    public string DatabasePath { get; }
    public string Root => _root;
    public CafeDatabase Database { get; }
    public SettingsRepository Settings { get; }
    public StudentRepository Students { get; }
    public ItemRepository Items { get; }
    public LedgerRepository Ledger { get; }
    public DatabaseVerifier Verifier { get; }
    public BackupService Service { get; }
    public FolderDestination Local { get; }
    public FolderDestination OffSite { get; }

    public const string Passphrase = "kaffe-och-bulle-2026";

    public long AddStudent(string first, string last, decimal opening = 0)
    {
        var id = Students.Create(first, last, null, "test");
        if (opening != 0)
            Ledger.Deposit(id, Money.FromKronor(opening), DepositMethod.Swish, "opening", "test", "test");
        return id;
    }

    public void Trade()
    {
        var carl = AddStudent("Carl", "Jacobs", 50);
        AddStudent("Astrid", "Lindqvist", 120);
        var toast = Items.Create("Toast", Money.FromKronor(10), null, null, "test");

        Ledger.ExecutePurchase(carl,
            new[] { new BasketLine { LineNo = 1, ItemId = toast, ItemName = "Toast", UnitPrice = Money.FromKronor(10) } },
            Settings.Load(), "Café", "test");
    }

    public Money TotalBalances() => Money.Sum(Students.All(includeInactive: true).Select(s => s.Balance));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* the OS will get it */ }
    }
}
