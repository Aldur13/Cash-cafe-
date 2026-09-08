using System.Collections.ObjectModel;
using CashCafe.App.Core.Services;
using CashCafe.Backup;
using CashCafe.Data;
using CashCafe.Domain;
using CashCafe.Domain.Import;
using CashCafe.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashCafe.App.Core.ViewModels;

/// <summary>One deposit, flattened with the student's name for the admin Deposits tab.</summary>
public sealed record DepositRow
{
    public required long TransactionId { get; init; }
    public required long StudentId { get; init; }
    public required string StudentName { get; init; }
    public string? ClassName { get; init; }
    public required Money Amount { get; init; }
    public DepositMethod? Method { get; init; }
    public string? Reference { get; init; }
    public required DateTimeOffset OccurredUtc { get; init; }
    public required string Operator { get; init; }
    public bool IsReversed { get; init; }
}

/// <summary>
/// The admin panel behind the PIN: items and prices, students, deposits, reports, import,
/// export, backups, settings and the audit log.
///
/// Every operation here goes through the repositories, which means every one of them is
/// audited and none of them can edit history — this class has no privileged path of its own.
/// </summary>
public sealed partial class AdminViewModel : ObservableObject
{
    private readonly CafeContext _cafe;
    private readonly ImportService _imports;
    private readonly BackupService _backups;
    private readonly AdminSession _session;

    public AdminViewModel(
        CafeContext cafe,
        ImportService imports,
        BackupService backups,
        AdminSession session)
    {
        _cafe = cafe;
        _imports = imports;
        _backups = backups;
        _session = session;

        Reload();
    }

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private ImportPreview? _pendingImport;
    [ObservableProperty] private ItemImportPreview? _pendingItemImport;
    [ObservableProperty] private DaySummary? _today;

    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<AuditEntry> History { get; } = new();
    public ObservableCollection<Student> InTheRed { get; } = new();
    public ObservableCollection<DepositRow> Deposits { get; } = new();

    public CafeSettings Settings => _cafe.Settings;

    /// <summary>
    /// A warning the admin panel shows and does not let you dismiss: the live database is in
    /// a folder a sync client watches, which will corrupt it sooner or later.
    /// </summary>
    public bool DatabaseIsInASyncedFolder => _backups.DatabaseIsInASyncedFolder();

    public void Reload()
    {
        _cafe.Refresh();
        _session.Touch();

        Items.Clear();
        foreach (var item in _cafe.Items.All(includeUnavailable: true)) Items.Add(item);

        Students.Clear();
        foreach (var student in _cafe.Students.All(includeInactive: true)) Students.Add(student);

        History.Clear();
        foreach (var entry in _cafe.Audit.Read(limit: 200)) History.Add(entry);

        InTheRed.Clear();
        foreach (var student in _cafe.Reports.InTheRed()) InTheRed.Add(student);

        Deposits.Clear();
        foreach (var deposit in RecentDeposits(200)) Deposits.Add(deposit);

        Today = _cafe.Reports.Day(DateOnly.FromDateTime(DateTime.Today));
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>The last N deposits across every student, newest first — for the admin
    /// Deposits tab, which has no single-student filter the way a statement does.</summary>
    private IReadOnlyList<DepositRow> RecentDeposits(int limit)
    {
        var students = _cafe.Students.All(includeInactive: true).ToDictionary(s => s.Id);

        var deposits = students.Values
            .SelectMany(s => _cafe.Ledger.History(s.Id, limit)
                .Where(t => t.Type == TransactionType.Deposit)
                .Select(t => (Student: s, Transaction: t)))
            .OrderByDescending(x => x.Transaction.OccurredUtc)
            .Take(limit)
            .ToList();

        var reversed = _cafe.Ledger.ReversedTransactionIds(deposits.Select(d => d.Transaction.Id));

        return deposits.Select(d => new DepositRow
        {
            TransactionId = d.Transaction.Id,
            StudentId = d.Student.Id,
            StudentName = d.Student.DisplayName,
            ClassName = d.Student.ClassName,
            Amount = d.Transaction.Amount,
            Method = d.Transaction.Method,
            Reference = d.Transaction.Reference,
            OccurredUtc = d.Transaction.OccurredUtc,
            Operator = d.Transaction.Operator,
            IsReversed = reversed.Contains(d.Transaction.Id),
        }).ToList();
    }

    // ---- Items -------------------------------------------------------------------------

    public void AddItem(string name, Money price, string? category = null)
    {
        _cafe.Items.Create(name, price, category, Actor);
        Reload();
        Success($"Added {name} at {price}.");
    }

    /// <summary>
    /// Changes a price from now on. Past sales keep what they were sold at, because each sale
    /// stores its own unit price — so this can never rewrite a report.
    /// </summary>
    public void ChangePrice(long itemId, Money newPrice, string reason)
    {
        _cafe.Items.ChangePrice(itemId, newPrice, reason, Actor);
        Reload();
        Success($"Price changed to {newPrice}. Sales already made keep the price they were sold at.");
    }

    public void SetItemAvailable(long itemId, bool available)
    {
        _cafe.Items.SetAvailable(itemId, available, Actor);
        Reload();
        Success(available ? "Back on sale." : "Marked sold out — hidden from the till.");
    }

    public void RemoveItem(long itemId)
    {
        var deleted = _cafe.Items.Remove(itemId, Actor);
        Reload();
        Success(deleted
            ? "Removed — it had never been sold."
            : "Archived rather than deleted, because it has been sold before and history keeps it.");
    }

    // ---- Students ----------------------------------------------------------------------

    public long AddStudent(string firstName, string lastName, string? className)
    {
        var id = _cafe.Students.Create(firstName, lastName, className, Actor);
        Reload();
        Success($"Added {firstName} {lastName}.");
        return id;
    }

    public void SetStudentActive(long studentId, bool active)
    {
        _cafe.Students.SetActive(studentId, active, Actor);
        Reload();
        Success(active ? "Reactivated." : "Deactivated — hidden from the till, history kept.");
    }

    public void SetCreditLimit(long studentId, Money? limit, string reason)
    {
        _cafe.Students.SetCreditLimit(studentId, limit, reason, Actor);
        Reload();
        Success(limit is null
            ? "Back to the café's normal limit."
            : $"This student's limit is now {limit}.");
    }

    /// <summary>
    /// A correction — the only way to change a balance without a purchase or a deposit, and
    /// the one place a person's judgement enters the numbers. The reason is required, and it
    /// shows up in the audit summary for exactly that reason.
    /// </summary>
    public bool Correct(long studentId, Money amount, string reason)
    {
        var result = _cafe.Ledger.Adjust(studentId, amount, reason, Actor, _cafe.SessionId);
        Reload();

        if (!result.Succeeded)
        {
            Error(result.Message);
            return false;
        }

        Success(result.Message);
        return true;
    }

    public bool Deposit(long studentId, Money amount, DepositMethod method, string? reference)
    {
        var result = _cafe.Ledger.Deposit(studentId, amount, method, reference, Actor, _cafe.SessionId);
        Reload();

        if (!result.Succeeded)
        {
            Error(result.Message);
            return false;
        }

        Success(result.Message);
        return true;
    }

    /// <summary>Reverses any transaction, with no undo window — the admin's version of undo.</summary>
    public bool Reverse(long transactionId, string reason)
    {
        var result = _cafe.Ledger.Reverse(transactionId, reason, Actor, _cafe.SessionId);
        Reload();

        if (!result.Succeeded)
        {
            Error(result.Message);
            return false;
        }

        Success(result.Message);
        return true;
    }

    // ---- Import ------------------------------------------------------------------------

    /// <summary>
    /// Reads a spreadsheet and works out what importing it would do. Nothing is written:
    /// the preview is shown, a person looks at it, and only then is <see cref="ConfirmImport"/>
    /// called.
    /// </summary>
    public ImportPreview PrepareImport(Stream file, string fileName, string? sheetName = null)
    {
        var reader = new WorkbookReader();

        file.Position = 0;
        var layout = reader.DetectLayout(file, sheetName);

        file.Position = 0;
        var rows = reader.Read(file, layout);

        PendingImport = ImportPlanner.Plan(rows, _cafe.Students.All(includeInactive: true), fileName, layout.SheetName);

        Success($"{PendingImport.RowsRead} row(s) read. Nothing has been imported yet — check the preview.");
        return PendingImport;
    }

    public bool ConfirmImport(string archivedPath)
    {
        if (PendingImport is null)
        {
            Error("There is no import waiting.");
            return false;
        }

        var result = _imports.Apply(PendingImport, archivedPath, Actor, _cafe.SessionId);
        PendingImport = null;
        Reload();

        if (result.StudentsCreated == 0 && result.TransactionsWritten == 0)
        {
            Error(result.Message);
            return false;
        }

        Success(result.Message);
        return true;
    }

    public void UndoImport(long importId)
    {
        var result = _imports.Undo(importId, Actor, _cafe.SessionId);
        Reload();
        Success(result.Message);
    }

    // ---- Menu import (Excel) ------------------------------------------------------------

    /// <summary>
    /// Reads a menu spreadsheet — Name and Price, at least — and works out what it would
    /// change. Nothing is written until <see cref="ConfirmItemImport"/> is called.
    /// </summary>
    public ItemImportPreview? PrepareItemImport(Stream file, string fileName, string? sheetName = null)
    {
        var reader = new ItemWorkbookReader();

        file.Position = 0;
        var read = reader.Read(file, sheetName);

        if (read is null)
        {
            Error("No 'Name' and 'Price' columns could be found in the first 10 rows of this sheet.");
            return null;
        }

        var (readSheetName, rows) = read.Value;
        PendingItemImport = ItemImportPlanner.Plan(rows, _cafe.Items.All(includeArchived: true), fileName, readSheetName);

        Success($"{PendingItemImport.RowsRead} row(s) read. Nothing has been imported yet — check the preview.");
        return PendingItemImport;
    }

    /// <summary>
    /// Writes the pending menu import: adds new items, and changes the price, category,
    /// or on-sale flag of any existing item the file disagrees with. Each price change
    /// goes through <see cref="ChangePrice"/>, so it is audited exactly like a price
    /// typed in by hand, and past sales keep the price they were sold at.
    /// </summary>
    public bool ConfirmItemImport()
    {
        if (PendingItemImport is null)
        {
            Error("There is no menu import waiting.");
            return false;
        }

        var created = 0;
        var updated = 0;

        foreach (var row in PendingItemImport.Rows)
        {
            switch (row.Action)
            {
                case ItemImportAction.CreateItem:
                    _cafe.Items.Create(row.Source.Name!, row.Source.Price!.Value, row.Source.Category, Actor);
                    created++;
                    break;

                case ItemImportAction.Update:
                    if (row.Source.Price is { } price && price != row.ExistingPrice)
                        _cafe.Items.ChangePrice(row.MatchedItemId!.Value, price, "Excel import", Actor);
                    if (row.Source.Available is { } available)
                        _cafe.Items.SetAvailable(row.MatchedItemId!.Value, available, Actor);
                    updated++;
                    break;
            }
        }

        PendingItemImport = null;
        Reload();

        Success($"Menu import done: {created} item(s) added, {updated} updated.");
        return true;
    }

    public IReadOnlyList<(long Id, string FileName, DateTimeOffset When, string By, int Rows, Money Total, bool Undone)>
        ImportHistory() => _imports.History();

    // ---- Export ------------------------------------------------------------------------

    /// <summary>Gathers everything an export needs. Read-only: an export can never change data.</summary>
    public ExportData BuildExport(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        var students = _cafe.Students.All(includeInactive: true);
        var transactions = students
            .SelectMany(s => _cafe.Ledger.History(s.Id, limit: int.MaxValue, since: fromUtc))
            .Where(t => toUtc is null || t.OccurredUtc < toUtc)
            .ToList();

        return new ExportData
        {
            CafeName = _cafe.Settings.CafeName,
            Students = students,
            Transactions = transactions,
            Items = _cafe.Items.All(includeUnavailable: true, includeArchived: true),
            PriceHistory = _cafe.Audit.PriceHistory(),
            AuditLog = _cafe.Audit.Read(fromUtc, toUtc, limit: int.MaxValue),
            ReversedTransactionIds = _cafe.Ledger.ReversedTransactionIds(transactions.Select(t => t.Id)),
            FilterDescription = fromUtc is null ? "Everything" : $"{fromUtc:yyyy-MM-dd} to {toUtc:yyyy-MM-dd}",
        };
    }

    // ---- Backup ------------------------------------------------------------------------

    public BackupOutcome BackupNow(IBackupDestination destination, string? passphrase)
    {
        var result = _backups.Create(destination, passphrase, reason: "manual");

        if (result.Succeeded) Success(result.Message);
        else Error(result.Message);

        return result;
    }

    public VerificationOutcome VerifyBackup(IBackupDestination destination, string? passphrase)
    {
        var result = _backups.VerifyLatest(destination, passphrase);

        if (result.Succeeded) Success(result.Message);
        else Error(result.Message);

        return result;
    }

    /// <summary>
    /// Restores a backup over the live database. A safety copy of the current state is taken
    /// first, always — see <see cref="BackupService.Restore"/>.
    /// </summary>
    public RestoreOutcome RestoreBackup(
        IBackupDestination destination,
        string backupId,
        string? passphrase,
        IBackupDestination safetyCopyTo)
    {
        var result = _backups.Restore(destination, backupId, passphrase, safetyCopyTo);

        if (result.Succeeded) Success(result.Message);
        else Error(result.Message);

        return result;
    }

    // ---- Settings ----------------------------------------------------------------------

    /// <summary>
    /// Changes the café-wide floor. It can never be positive: a positive floor would refuse
    /// students who legitimately have nothing, which is not what a floor is for.
    /// </summary>
    public bool SetMinimumBalance(Money floor)
    {
        if (floor > Money.Zero)
        {
            Error("The lowest balance can never be a positive number.");
            return false;
        }

        _cafe.SettingsRepository.Set("minimum_balance_ore", floor.Ore.ToString(), Actor);
        Reload();
        Success($"Students can now go down to {floor}.");
        return true;
    }

    public void SetBusyness(BusynessLevel level)
    {
        _cafe.SettingsRepository.SetBusyness(level, Actor, _cafe.SessionId);
        Reload();
        Success($"Students now see: {level.ToEnglish()}.");
    }

    public void SetStudentSiteEnabled(bool enabled)
    {
        _cafe.SettingsRepository.SetFlag("student_site_enabled", enabled, Actor);
        Reload();
        Success(enabled
            ? "The student site is on."
            : "The student site is off — everyone signed in loses access at once.");
    }

    [RelayCommand]
    private void CloseAdmin() => _session.Close();

    private static string Actor => "Admin";

    private void Success(string text)
    {
        Message = text;
        MessageIsError = false;
    }

    private void Error(string text)
    {
        Message = text;
        MessageIsError = true;
    }
}
