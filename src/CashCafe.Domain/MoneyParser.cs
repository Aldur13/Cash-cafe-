using System.Globalization;
using System.Text.RegularExpressions;

namespace CashCafe.Domain;

public sealed record MoneyParseResult(bool Success, Money Value, string? Error)
{
    public static MoneyParseResult Ok(Money value) => new(true, value, null);
    public static MoneyParseResult Fail(string error) => new(false, Money.Zero, error);
}

/// <summary>
/// Reads the amounts people actually type into spreadsheets.
///
/// Swedish sheets are messy in specific, predictable ways — a comma or a dot for decimals,
/// a space or a non-breaking space for thousands, "kr" or ":-" stuck on the end, a negative
/// written with a hyphen, an en dash, a real minus sign, or accounting brackets. All of
/// those are read. What is deliberately *not* read is anything ambiguous: three decimals is
/// an error rather than a silent rounding, because a rounding nobody asked for is exactly
/// the class of quiet mistake this system exists to remove.
/// </summary>
public static partial class MoneyParser
{
    [GeneratedRegex(@"^\s*(?<neg1>[-−–—])?\s*(?<paren>\()?\s*(?<neg2>[-−–—])?\s*(?<digits>[\d\s .,']+?)\s*\)?\s*(?<suffix>kr|sek|kronor|:-|:−)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    public static MoneyParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return MoneyParseResult.Fail("empty");

        var trimmed = text.Trim();
        var match = AmountPattern().Match(trimmed);
        if (!match.Success) return MoneyParseResult.Fail($"'{trimmed}' is not an amount");

        var negative = match.Groups["neg1"].Success || match.Groups["neg2"].Success || match.Groups["paren"].Success;
        var digits = match.Groups["digits"].Value;

        var normalized = NormalizeSeparators(digits);
        if (normalized is null) return MoneyParseResult.Fail($"'{trimmed}' is not an amount");

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return MoneyParseResult.Fail($"'{trimmed}' is not an amount");

        var decimals = normalized.Contains('.') ? normalized.Length - normalized.IndexOf('.') - 1 : 0;
        if (decimals > 2)
            return MoneyParseResult.Fail($"'{trimmed}' has more than two decimals — fix it in the sheet rather than let us guess");

        var money = Money.FromKronor(value);
        return MoneyParseResult.Ok(negative ? money.Negated() : money);
    }

    /// <summary>
    /// Turns the digit part into something invariant parsing understands, by working out
    /// which separator — if any — is the decimal one.
    ///
    /// The rules, in order:
    /// <list type="bullet">
    /// <item>Both a comma and a dot present: the <b>last</b> one is the decimal separator
    /// and the other is thousands. Covers "1.250,50" and "1,250.50" alike.</item>
    /// <item>The same separator more than once: they are all thousands ("1,250,500").</item>
    /// <item>A single dot with exactly three digits after it: thousands ("1.250"), because
    /// that is how a Swedish sheet writes it.</item>
    /// <item>Anything else: it is the decimal separator.</item>
    /// </list>
    ///
    /// The last rule is the important one. A comma is the Swedish decimal separator, so
    /// "50,555" is three decimals — an error — and not fifty thousand kronor. Guessing
    /// thousands there would turn one typo into a balance nobody could explain.
    /// </summary>
    private static string? NormalizeSeparators(string digits)
    {
        var stripped = digits
            .Replace(" ", string.Empty)
            .Replace("\u00A0", string.Empty)
            .Replace("\u202F", string.Empty)
            .Replace("'", string.Empty);

        if (stripped.Length == 0 || !stripped.Any(char.IsDigit)) return null;
        if (stripped.Any(c => !char.IsDigit(c) && c != '.' && c != ',')) return null;

        var commas = stripped.Count(c => c == ',');
        var dots = stripped.Count(c => c == '.');

        if (commas == 0 && dots == 0) return stripped;

        char decimalSeparator;

        if (commas > 0 && dots > 0)
        {
            decimalSeparator = stripped.LastIndexOf(',') > stripped.LastIndexOf('.') ? ',' : '.';
        }
        else
        {
            var separator = commas > 0 ? ',' : '.';
            var occurrences = commas > 0 ? commas : dots;
            var tail = stripped.Length - stripped.LastIndexOf(separator) - 1;

            // Repeated separators are always thousands; a lone dot with three digits after
            // it is thousands; a comma is Sweden's decimal separator whatever follows it.
            var isThousands = occurrences > 1 || (separator == '.' && tail == 3);
            if (isThousands) return stripped.Replace(",", string.Empty).Replace(".", string.Empty);

            decimalSeparator = separator;
        }

        var lastSeparator = stripped.LastIndexOf(decimalSeparator);
        var whole = stripped[..lastSeparator].Replace(",", string.Empty).Replace(".", string.Empty);
        var fraction = stripped[(lastSeparator + 1)..];

        if (fraction.Any(c => !char.IsDigit(c))) return null;

        return $"{whole}.{fraction}";
    }
}
