using System.Globalization;
using System.Text;

namespace CashCafe.Domain;

/// <summary>
/// Turns a name into the form used for searching and matching: lower case, Swedish
/// letters folded (å/ä→a, ö→o, é→e), punctuation removed, spaces collapsed.
/// </summary>
public static class SearchNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lowered = text.Trim().ToLowerInvariant();
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var ch in decomposed)
        {
            // Drop the accents that decomposition separated out.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && builder.Length > 0)
            {
                // Any punctuation or whitespace becomes a single separator.
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string[] Words(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>"Carl Jacobs" → "cj". Used so typing initials finds a student.</summary>
    public static string Initials(string? text)
    {
        var words = Words(text);
        return string.Concat(words.Select(w => w[0]));
    }
}
