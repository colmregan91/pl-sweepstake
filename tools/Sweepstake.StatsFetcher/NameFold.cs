using System.Globalization;
using System.Text;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// Folds a display name down to bare lowercase ASCII, so that <c>verify</c> can tell a
/// transliteration difference ("Šeško" vs "Sesko") from a genuinely different player.
/// <para>
/// READ THIS BEFORE REUSING IT: this exists only to classify a line in a human-read report.
/// It is never used to match a player to a stat. Picks join to stats by ESPN athlete id and
/// nothing else -- see the "never match players by name" rule in CLAUDE.md. A fold loose
/// enough to be useful here is exactly the kind of fuzzy match that eventually pairs the wrong
/// two Igor Jesuses.
/// </para>
/// </summary>
internal static class NameFold
{
    public static string Simplify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            // FormD splits "ö" into "o" + combining diaeresis; drop the mark, keep the letter.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // These do not decompose, so they need spelling out by hand.
            switch (ch)
            {
                case 'ß': builder.Append("ss"); break;
                case 'Ø' or 'ø': builder.Append('o'); break;
                case 'Đ' or 'đ' or 'Ð' or 'ð': builder.Append('d'); break;
                case 'Æ' or 'æ': builder.Append("ae"); break;
                case 'Œ' or 'œ': builder.Append("oe"); break;
                case 'Ł' or 'ł': builder.Append('l'); break;
                case 'Þ' or 'þ': builder.Append("th"); break;
                default:
                    if (char.IsLetterOrDigit(ch))
                    {
                        builder.Append(char.ToLowerInvariant(ch));
                    }
                    else if (char.IsWhiteSpace(ch) || ch is '-' or '\'' or '.')
                    {
                        builder.Append(' ');
                    }

                    break;
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>True when two names differ only in diacritics, punctuation or spacing.</summary>
    public static bool SameApartFromSpelling(string? a, string? b) =>
        Simplify(a).Length > 0 && string.Equals(Simplify(a), Simplify(b), StringComparison.Ordinal);
}
