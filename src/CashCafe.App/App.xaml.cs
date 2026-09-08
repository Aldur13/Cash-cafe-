using System;
using System.IO;
using System.Linq;
using System.Threading;

using System.Windows;
using CashCafe.App.Core.Services;
using CashCafe.App.Core.ViewModels;
using CashCafe.App.Views;
using CashCafe.Backup;
using CashCafe.Data;

namespace CashCafe.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Global\CashCafe.SingleInstance";

    private Mutex? _instanceMutex;

    public static CafeContext Cafe { get; private set; } = null!;
    public static AdminSession Admin { get; private set; } = null!;
    public static BackupService Backups { get; private set; } = null!;
    public static ImportService Imports { get; private set; } = null!;
    public static FolderDestination LocalBackups { get; private set; } = null!;

    /// <summary>
    /// The startup sequence, in the order the wiki describes. Each step exists because of a
    /// specific way a café day can go wrong, so none of them is skipped to save a second.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var arguments = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();

        if (arguments.Contains("--portable")) CafePaths.UsePortableMode();

        // 1. One instance only. Two programs writing one SQLite file is how a café loses a day.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            MessageBox.Show(
                "Cash Café is already running on this computer. Look for it in the taskbar.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        CafePaths.EnsureFolders();

        var database = new CafeDatabase(CafePaths.Database);
        var settings = new SettingsRepository(database);
        var students = new StudentRepository(database);
        var items = new ItemRepository(database);
        var ledger = new LedgerRepository(database);
        var reports = new ReportRepository(database);
        var audit = new AuditRepository(database);
        var verifier = new DatabaseVerifier(database);

        Backups = new BackupService(database, settings, students, ledger);
        Imports = new ImportService(database);
        LocalBackups = new FolderDestination(CafePaths.Backups, "This computer");

        // 2. A backup before any migration, so an update can always be undone.
        var schemaBefore = SchemaVersion(database);
        if (schemaBefore > 0 && schemaBefore < database.HighestKnownMigration())
            Backups.Create(LocalBackups, reason: $"pre-update-{schemaBefore}");

        try
        {
            database.Migrate(typeof(App).Assembly.GetName().Version?.ToString() ?? "0.1.0");
        }
        catch (Exception ex)
        {
            Fail($"The café database could not be updated, and nothing was changed.\n\n{ex.Message}");
            return;
        }

        // 3. Repair mode: recompute cached balances from the ledger, which is the truth.
        if (arguments.Contains("--repair"))
        {
            var repaired = verifier.RepairCachedBalances();
            MessageBox.Show($"{repaired} balance(s) recomputed from the ledger.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 4. Do the numbers still add up? If not, stop rather than trade on figures we cannot
        //    explain — with the exact student named, and the repair command spelled out.
        var report = verifier.Verify();
        if (report.HasFatalProblem)
        {
            Fail("The café's numbers do not add up, so the program has not opened.\n\n" +
                 string.Join("\n", report.Problems.Where(p => p.IsFatal).Take(5).Select(p => "• " + p.Detail)) +
                 "\n\nNothing has been changed. Run CashCafe.exe --repair to rebuild the balances " +
                 "from the ledger, or restore the most recent backup.");
            return;
        }

        Cafe = new CafeContext(students, items, ledger, settings, reports, audit);
        Cafe.Refresh();

        Admin = new AdminSession(settings, audit)
        {
            IdleTimeout = TimeSpan.FromMinutes(10),
        };

        Converters.BalanceBrushConverter.LowThreshold = Cafe.Settings.LowBalanceWarning;

        // 5. First run: choose an admin PIN before anything else can happen.
        if (!Admin.IsConfigured && !PinDialog.SetUpFirstPin(Admin))
        {
            Shutdown();
            return;
        }

        new MainWindow { DataContext = new CafeViewModel(Cafe) }.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A backup on the way out, so closing up for the day is also closing up safely.
        try
        {
            if (Cafe is not null) Backups?.Create(LocalBackups, reason: "on-close");
        }
        catch
        {
            // Never block shutting down over a backup; the failure is already on the health dot.
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static int SchemaVersion(CafeDatabase database)
    {
        try
        {
            using var connection = database.Open();
            return CafeDatabase.CurrentVersion(connection);
        }
        catch
        {
            return 0;
        }
    }

    private void Fail(string message)
    {
        MessageBox.Show(message, "Cash Café", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown();
    }
}
