using CashCafe.App.Core.Services;
using CashCafe.App.Core.ViewModels;
using CashCafe.Backup;
using CashCafe.Data;
using CashCafe.Domain;

namespace CashCafe.App.Tests;

/// <summary>A whole café behind the screens: real database, real repositories, real rules.</summary>
public sealed class TillFixture : IDisposable
{
    private readonly string _root;

    public TillFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cashcafe-app-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        Database = new CafeDatabase(Path.Combine(_root, "cashcafe.db"));
        Database.Migrate("test");

        var students = new StudentRepository(Database);
        var items = new ItemRepository(Database);
        var ledger = new LedgerRepository(Database);
        var settings = new SettingsRepository(Database);
        var reports = new ReportRepository(Database);
        var audit = new AuditRepository(Database);

        Students = students;
        Items = items;
        Ledger = ledger;
        SettingsRepository = settings;
        Audit = audit;

        Context = new CafeContext(students, items, ledger, settings, reports, audit);
        Context.Refresh();

        Session = new AdminSession(settings, audit);
        Backups = new BackupService(Database, settings, students, ledger);
        BackupFolder = new FolderDestination(Path.Combine(_root, "backups"), "Local");
        Admin = new AdminViewModel(Context, new ImportService(Database), Backups, Session);
    }

    public CafeDatabase Database { get; }
    public StudentRepository Students { get; }
    public ItemRepository Items { get; }
    public LedgerRepository Ledger { get; }
    public SettingsRepository SettingsRepository { get; }
    public AuditRepository Audit { get; }
    public CafeContext Context { get; }
    public AdminSession Session { get; }
    public BackupService Backups { get; }
    public FolderDestination BackupFolder { get; }
    public AdminViewModel Admin { get; }

    public CafeViewModel NewTill()
    {
        Context.Refresh();
        return new CafeViewModel(Context);
    }

    public long AddStudent(string first, string last, decimal opening = 0, string? className = null)
    {
        var id = Students.Create(first, last, className, "test");
        if (opening != 0)
            Ledger.Deposit(id, Money.FromKronor(opening), DepositMethod.Swish, "opening", "test", "test");

        Context.Refresh();
        return id;
    }

    public Item AddItem(string name, decimal price)
    {
        var id = Items.Create(name, Money.FromKronor(price), null, "test");
        Context.Refresh();
        return Items.Find(id)!;
    }

    public Money Balance(long id) => Students.Find(id)!.Balance;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
