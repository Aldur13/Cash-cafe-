using CashCafe.Data;
using CashCafe.Domain;

namespace CashCafe.Data.Tests;

/// <summary>
/// A café in a temporary database file. A real file rather than an in-memory database,
/// because the things most worth testing here — WAL, triggers, transactions — are
/// properties of the real thing.
/// </summary>
public sealed class TestCafe : IDisposable
{
    private readonly string _path;

    public TestCafe()
    {
        _path = Path.Combine(Path.GetTempPath(), $"cashcafe-test-{Guid.NewGuid():N}.db");
        Database = new CafeDatabase(_path);
        Database.Migrate("test");

        Students = new StudentRepository(Database);
        Items = new ItemRepository(Database);
        Ledger = new LedgerRepository(Database);
        Logins = new StudentLoginRepository(Database);
        Settings = new SettingsRepository(Database);
        Reports = new ReportRepository(Database);
        Verifier = new DatabaseVerifier(Database);
    }

    public CafeDatabase Database { get; }
    public StudentRepository Students { get; }
    public ItemRepository Items { get; }
    public LedgerRepository Ledger { get; }
    public StudentLoginRepository Logins { get; }
    public SettingsRepository Settings { get; }
    public ReportRepository Reports { get; }
    public DatabaseVerifier Verifier { get; }

    public CafeSettings Config => Settings.Load();

    public long AddStudent(string first, string last, decimal openingKronor = 0, string? className = null)
    {
        var id = Students.Create(first, last, className, "test");
        if (openingKronor != 0)
            Ledger.Deposit(id, Money.FromKronor(openingKronor), DepositMethod.Cash, "opening", "test", "test");
        return id;
    }

    public long AddItem(string name, decimal priceKronor) =>
        Items.Create(name, Money.FromKronor(priceKronor), null, null, "test");

    public Student Student(long id) => Students.Find(id)!;
    public Money Balance(long id) => Students.Find(id)!.Balance;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
