using System.Collections.ObjectModel;
using CashCafe.App.Core.Services;
using CashCafe.Data;
using CashCafe.Domain;
using CashCafe.Domain.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashCafe.App.Core.ViewModels;

/// <summary>One item row as the till screen shows it.</summary>
public sealed partial class ItemRowViewModel : ObservableObject
{
    [ObservableProperty] private int _lineNo;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private Item? _item;
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private Money _unitPrice = Money.Zero;
    [ObservableProperty] private string? _customDescription;

    public bool IsEmpty => Item is null && CustomDescription is null;
    public Money LineTotal => UnitPrice * Quantity;
    public string DisplayName => Item?.Name ?? CustomDescription ?? string.Empty;

    public BasketLine ToBasketLine() => new()
    {
        LineNo = LineNo,
        ItemId = Item?.Id,
        ItemName = DisplayName,
        UnitPrice = UnitPrice,
        Quantity = Quantity,
        IsCustom = Item is null && CustomDescription is not null,
    };
}

public sealed record RecentPurchase(long TransactionId, string StudentName, Money Amount, DateTimeOffset When);

/// <summary>
/// The till. Search a student, add up to ten item rows, press Execute.
///
/// It owns no money rules of its own: the floor, the totals and what makes Execute possible
/// all come from <see cref="PurchaseBasket"/> in the domain, and the same rules are checked
/// again inside the database write. This class is the screen, not the law.
/// </summary>
public sealed partial class CafeViewModel : ObservableObject
{
    private readonly CafeContext _cafe;
    private PurchaseBasket _basket;

    public CafeViewModel(CafeContext cafe)
    {
        _cafe = cafe;
        _basket = new PurchaseBasket(cafe.Settings);
        Rows = new ObservableCollection<ItemRowViewModel>();
        RefreshAvailableItems();
        SyncRows();
    }

    [ObservableProperty] private string _studentQuery = string.Empty;
    [ObservableProperty] private Student? _student;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private long? _lastTransactionId;

    public ObservableCollection<ItemRowViewModel> Rows { get; }
    public ObservableCollection<Student> StudentMatches { get; } = new();
    public ObservableCollection<Item> ItemMatches { get; } = new();
    public ObservableCollection<RecentPurchase> RecentPurchases { get; } = new();

    /// <summary>Everything on sale right now, for the item dropdown on each row.</summary>
    public ObservableCollection<Item> AvailableItems { get; } = new();

    public Money Total => _basket.Total;
    public int ItemCount => _basket.ItemCount;

    public Money BalanceNow => Student?.Balance ?? Money.Zero;
    public Money BalanceAfter => BalanceNow - Total;

    public Money CanSpend => Student is null
        ? Money.Zero
        : BalanceRules.CanSpend(Student.Balance,
            BalanceRules.EffectiveFloor(Student.CreditLimit, _cafe.Settings));

    public BasketValidation Validation => _basket.Validate();
    public bool CanExecute => Validation.CanExecute;

    /// <summary>The line under the Execute button: why it is disabled, or nothing when it is not.</summary>
    public string ExecuteBlockedReason => Validation.CanExecute ? string.Empty : Validation.Message;

    public bool StudentIsInDebt => Student?.Balance.IsNegative ?? false;
    public bool StudentIsLow => Student is not null && Student.Balance <= _cafe.Settings.LowBalanceWarning;
    public string? StudentNote => Student?.Note;

    /// <summary>True when the purchase is unusually large and worth a second look.</summary>
    public bool NeedsLargePurchaseConfirmation => Total >= _cafe.Settings.LargePurchaseWarning;

    partial void OnStudentQueryChanged(string value) => SearchStudents(value);

    partial void OnStudentChanged(Student? value)
    {
        _basket.SelectStudent(value);
        NotifyTotals();
    }

    public void SearchStudents(string query)
    {
        StudentMatches.Clear();
        foreach (var match in Domain.StudentSearch.Find(_cafe.ActiveStudents, query))
            StudentMatches.Add(match);
    }

    public void SearchItems(string query)
    {
        ItemMatches.Clear();
        foreach (var match in Domain.StudentSearch.FindItems(_cafe.AvailableItems, query))
            ItemMatches.Add(match);
    }

    private void RefreshAvailableItems()
    {
        AvailableItems.Clear();
        foreach (var item in _cafe.AvailableItems) AvailableItems.Add(item);
    }

    [RelayCommand]
    public void SelectStudent(Student? student)
    {
        Student = student;
        StudentQuery = student?.DisplayName ?? string.Empty;
        StudentMatches.Clear();
        ClearMessage();
    }

    public void SetItem(int lineNo, Item item, int quantity = 1)
    {
        _basket.SetLine(lineNo, BasketLine.For(lineNo, item, quantity));
        SyncRows();
    }

    public void SetCustomAmount(int lineNo, string description, Money amount, int quantity = 1)
    {
        _basket.SetLine(lineNo, BasketLine.Custom(lineNo, description, amount, quantity));
        SyncRows();
    }

    public void SetQuantity(int lineNo, int quantity)
    {
        var row = Rows.FirstOrDefault(r => r.LineNo == lineNo);
        if (row is null || row.IsEmpty) return;

        var line = row.ToBasketLine() with { Quantity = quantity };
        _basket.SetLine(lineNo, line);
        SyncRows();
    }

    [RelayCommand]
    public void RemoveRow(int lineNo)
    {
        _basket.RemoveLine(lineNo);
        SyncRows();
    }

    [RelayCommand]
    public void Clear()
    {
        _basket = new PurchaseBasket(_cafe.Settings);
        Student = null;
        StudentQuery = string.Empty;
        StudentMatches.Clear();
        ItemMatches.Clear();
        SyncRows();
        ClearMessage();
    }

    /// <summary>
    /// Commits the purchase. Everything that could refuse it has already been checked on
    /// screen, but the repository checks it all again against freshly read numbers — so a
    /// price an admin changed in another window, or a balance that moved underneath, is
    /// caught here rather than charged.
    /// </summary>
    /// <summary>The command the Execute button is bound to.</summary>
    [RelayCommand(CanExecute = nameof(CanExecute))]
    private void Commit() => Execute();

    public bool Execute()
    {
        var validation = _basket.Validate();
        if (!validation.CanExecute)
        {
            ShowError(validation.Message);
            return false;
        }

        var result = _cafe.Ledger.ExecutePurchase(
            Student!.Id,
            Rows.Where(r => !r.IsEmpty).Select(r => r.ToBasketLine()).ToList(),
            _cafe.Settings,
            _cafe.TillName,
            _cafe.SessionId);

        if (!result.Succeeded)
        {
            // A changed price means the screen is out of date, so refresh rather than leave
            // the operator looking at numbers that are no longer true.
            if (result.Outcome == LedgerOutcome.PriceChanged) _cafe.Refresh();

            ShowError(result.Message);
            return false;
        }

        var studentName = Student.DisplayName;
        var total = validation.Total;

        _cafe.RefreshStudent(Student.Id);
        LastTransactionId = result.TransactionId;

        RecentPurchases.Insert(0, new RecentPurchase(result.TransactionId, studentName, total, DateTimeOffset.Now));
        while (RecentPurchases.Count > 10) RecentPurchases.RemoveAt(RecentPurchases.Count - 1);

        Clear();
        ShowSuccess($"{studentName} — {total} paid. New balance {result.BalanceAfter}.");
        return true;
    }

    /// <summary>
    /// Undoes the last purchase made from this till, within the café's undo window. It writes
    /// a reversal rather than removing anything, so the mistake and the fix both stay visible.
    /// </summary>
    [RelayCommand]
    private void Undo() => UndoLast();

    public bool UndoLast()
    {
        var last = RecentPurchases.FirstOrDefault();
        if (last is null)
        {
            ShowError("There is nothing to undo.");
            return false;
        }

        var result = _cafe.Ledger.Reverse(last.TransactionId, "Undo at counter",
            _cafe.TillName, _cafe.SessionId, _cafe.Settings.UndoWindow);

        if (!result.Succeeded)
        {
            ShowError(result.Message);
            return false;
        }

        RecentPurchases.Remove(last);
        _cafe.Refresh();
        ShowSuccess(result.Message);
        return true;
    }

    /// <summary>Called after an admin changes prices or items, so the till stops showing stale ones.</summary>
    public void ReloadFromDatabase()
    {
        _cafe.Refresh();
        SearchStudents(StudentQuery);
        RefreshAvailableItems();
        OnPropertyChanged(nameof(CanExecute));
    }

    private void SyncRows()
    {
        Rows.Clear();

        foreach (var line in _basket.Lines)
            Rows.Add(new ItemRowViewModel
            {
                LineNo = line.LineNo,
                Item = line.ItemId is null ? null : _cafe.AvailableItems.FirstOrDefault(i => i.Id == line.ItemId),
                CustomDescription = line.IsCustom ? line.ItemName : null,
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
            });

        NotifyTotals();
    }

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(BalanceNow));
        OnPropertyChanged(nameof(BalanceAfter));
        OnPropertyChanged(nameof(CanSpend));
        OnPropertyChanged(nameof(CanExecute));
        CommitCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Validation));
        OnPropertyChanged(nameof(ExecuteBlockedReason));
        OnPropertyChanged(nameof(StudentIsInDebt));
        OnPropertyChanged(nameof(StudentIsLow));
        OnPropertyChanged(nameof(StudentNote));
        OnPropertyChanged(nameof(NeedsLargePurchaseConfirmation));
    }

    private void ShowSuccess(string text)
    {
        Message = text;
        MessageIsError = false;
    }

    private void ShowError(string text)
    {
        Message = text;
        MessageIsError = true;
    }

    private void ClearMessage()
    {
        Message = string.Empty;
        MessageIsError = false;
    }
}
