using System.Globalization;

namespace CashCafe.Domain;

/// <summary>
/// An amount of money, stored as a whole number of öre (1 kr = 100 öre).
///
/// Every amount in the system is an integer. There is no double, no float and no
/// decimal arithmetic anywhere in the money path, which is what guarantees that
/// totals add up exactly and balances cannot drift.
/// </summary>
public readonly record struct Money(long Ore) : IComparable<Money>
{
    public static readonly Money Zero = new(0);

    /// <summary>
    /// The Swedish formatting used everywhere in the UI: "1 250,50 kr".
    ///
    /// Built from sv-SE but with one deliberate change: the negative sign is forced to an
    /// ASCII hyphen. Modern ICU data gives sv-SE the typographic minus U+2212, which looks
    /// right but is not what Excel, CSV importers, or anyone searching a report will parse
    /// as a negative number — and a debt that reads as text rather than a number is exactly
    /// the kind of quiet error this system exists to remove.
    /// </summary>
    public static readonly CultureInfo Culture = CreateCulture();

    private static CultureInfo CreateCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("sv-SE").Clone();
        culture.NumberFormat.NegativeSign = "-";
        return culture;
    }

    public static Money FromKronor(decimal kronor) =>
        new((long)decimal.Round(kronor * 100m, 0, MidpointRounding.ToEven));

    public static Money FromKronor(int kronor) => new(kronor * 100L);

    public decimal Kronor => Ore / 100m;

    public bool IsNegative => Ore < 0;
    public bool IsZero => Ore == 0;

    public Money Abs() => new(Math.Abs(Ore));
    public Money Negated() => new(-Ore);

    public static Money operator +(Money a, Money b) => new(a.Ore + b.Ore);
    public static Money operator -(Money a, Money b) => new(a.Ore - b.Ore);
    public static Money operator -(Money a) => new(-a.Ore);
    public static Money operator *(Money a, int quantity) => new(a.Ore * quantity);
    public static Money operator *(int quantity, Money a) => new(a.Ore * quantity);

    public static bool operator <(Money a, Money b) => a.Ore < b.Ore;
    public static bool operator >(Money a, Money b) => a.Ore > b.Ore;
    public static bool operator <=(Money a, Money b) => a.Ore <= b.Ore;
    public static bool operator >=(Money a, Money b) => a.Ore >= b.Ore;

    public int CompareTo(Money other) => Ore.CompareTo(other.Ore);

    public static Money Sum(IEnumerable<Money> amounts)
    {
        long total = 0;
        foreach (var amount in amounts) total += amount.Ore;
        return new Money(total);
    }

    /// <summary>"40,00 kr" — the form shown in the UI and in exports.</summary>
    public override string ToString() => ToString(withCurrency: true);

    public string ToString(bool withCurrency) =>
        Kronor.ToString("#,##0.00", Culture) + (withCurrency ? " kr" : string.Empty);
}
