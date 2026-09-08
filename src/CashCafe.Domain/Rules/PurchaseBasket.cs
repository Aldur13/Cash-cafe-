namespace CashCafe.Domain.Rules;

/// <summary>One item row on the till while the purchase is being built.</summary>
public sealed record BasketLine
{
    public int LineNo { get; init; }
    public long? ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public Money UnitPrice { get; init; }
    public int Quantity { get; init; } = 1;
    public bool IsCustom { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(ItemName);
    public Money LineTotal => UnitPrice * Quantity;

    public static BasketLine Empty(int lineNo) => new() { LineNo = lineNo };

    public static BasketLine For(int lineNo, Item item, int quantity = 1) => new()
    {
        LineNo = lineNo,
        ItemId = item.Id,
        ItemName = item.Name,
        UnitPrice = item.Price,
        Quantity = quantity,
    };

    public static BasketLine Custom(int lineNo, string description, Money amount, int quantity = 1) => new()
    {
        LineNo = lineNo,
        ItemName = description,
        UnitPrice = amount,
        Quantity = quantity,
        IsCustom = true,
    };
}

public enum BasketProblem
{
    None,
    NoStudent,
    NoItems,
    InvalidQuantity,
    StudentInactive,
    NotEnoughMoney,
    TooManyLines,
}

/// <summary>The result of validating the whole till screen. Drives the Execute button.</summary>
public sealed record BasketValidation(
    bool CanExecute,
    BasketProblem Problem,
    string Message,
    Money Total,
    PurchaseCheck? Check);

/// <summary>
/// The purchase being built at the counter: a student and up to
/// <see cref="CafeSettings.MaxLinesPerPurchase"/> item rows.
///
/// A new empty row appears as soon as the one above it is filled, which is the
/// "opens another menu if the student wants to buy more items" behaviour, up to the limit.
/// </summary>
public sealed class PurchaseBasket
{
    private readonly List<BasketLine> _lines = new();
    private readonly CafeSettings _settings;

    public PurchaseBasket(CafeSettings settings)
    {
        _settings = settings;
        _lines.Add(BasketLine.Empty(1));
    }

    public Student? Student { get; private set; }
    public IReadOnlyList<BasketLine> Lines => _lines;
    public IEnumerable<BasketLine> FilledLines => _lines.Where(l => !l.IsEmpty);
    public Money Total => Money.Sum(FilledLines.Select(l => l.LineTotal));
    public int ItemCount => FilledLines.Sum(l => l.Quantity);

    /// <summary>True once every allowed row is filled, so no further row can open.</summary>
    public bool IsFull => _lines.Count >= _settings.MaxLinesPerPurchase && _lines.All(l => !l.IsEmpty);

    public void SelectStudent(Student? student) => Student = student;

    public void SetLine(int lineNo, BasketLine line)
    {
        var index = _lines.FindIndex(l => l.LineNo == lineNo);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(lineNo));

        _lines[index] = line with { LineNo = lineNo };
        OpenNextRowIfNeeded();
    }

    public void RemoveLine(int lineNo)
    {
        _lines.RemoveAll(l => l.LineNo == lineNo);
        Renumber();
        if (_lines.Count == 0) _lines.Add(BasketLine.Empty(1));
        OpenNextRowIfNeeded();
    }

    public void Clear()
    {
        Student = null;
        _lines.Clear();
        _lines.Add(BasketLine.Empty(1));
    }

    /// <summary>
    /// Adds a fresh empty row below the last filled one, unless the maximum is reached.
    /// This is what makes the next "menu" appear by itself at the counter.
    /// </summary>
    private void OpenNextRowIfNeeded()
    {
        if (_lines.Count >= _settings.MaxLinesPerPurchase) return;
        if (_lines.Any(l => l.IsEmpty)) return;

        _lines.Add(BasketLine.Empty(_lines.Count + 1));
    }

    private void Renumber()
    {
        for (var i = 0; i < _lines.Count; i++) _lines[i] = _lines[i] with { LineNo = i + 1 };
    }

    /// <summary>Everything the Execute button needs to know, recalculated on every change.</summary>
    public BasketValidation Validate()
    {
        var total = Total;

        if (Student is null)
            return new BasketValidation(false, BasketProblem.NoStudent, "Choose a student", total, null);

        if (!Student.IsActive)
            return new BasketValidation(false, BasketProblem.StudentInactive, "This student is deactivated", total, null);

        if (!FilledLines.Any())
            return new BasketValidation(false, BasketProblem.NoItems, "Add at least one item", total, null);

        if (FilledLines.Any(l => l.Quantity < 1 || l.Quantity > _settings.MaxQuantityPerLine))
            return new BasketValidation(false, BasketProblem.InvalidQuantity,
                $"Quantity must be between 1 and {_settings.MaxQuantityPerLine}", total, null);

        if (FilledLines.Count() > _settings.MaxLinesPerPurchase)
            return new BasketValidation(false, BasketProblem.TooManyLines,
                $"Maximum {_settings.MaxLinesPerPurchase} items per purchase", total, null);

        var floor = BalanceRules.EffectiveFloor(Student.CreditLimit, _settings);
        var check = BalanceRules.CheckPurchase(Student.Balance, floor, total);

        if (!check.IsAllowed)
        {
            var message = BalanceRules.RefusalMessage(Student.DisplayName, Student.Balance, floor, total);
            return new BasketValidation(false, BasketProblem.NotEnoughMoney, message, total, check);
        }

        return new BasketValidation(true, BasketProblem.None, string.Empty, total, check);
    }
}
