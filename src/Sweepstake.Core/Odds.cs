using System.Globalization;

namespace Sweepstake.Core;

/// <summary>
/// Fractional odds exactly as written on the sheet ("8/1", "150/1").
/// <para>
/// Only the numerator is ever used. The odds "Totals" column in the source spreadsheet is the
/// sum of the three numerators -- verified against the sheet: 8 + 40 + 80 = 128.
/// </para>
/// </summary>
public static class Odds
{
    /// <summary>Parses "8/1" to 8. Throws <see cref="FormatException"/> on anything else.</summary>
    public static int ParseNumerator(string odds)
    {
        if (string.IsNullOrWhiteSpace(odds))
        {
            throw new FormatException(
                "Odds value is empty. Expected fractional odds such as \"8/1\".");
        }

        var text = odds.AsSpan().Trim();
        var slash = text.IndexOf('/');
        if (slash < 0)
        {
            throw new FormatException(
                $"Odds value \"{odds}\" contains no '/'. Expected fractional odds such as \"8/1\".");
        }

        var numeratorText = text[..slash].Trim();
        var denominatorText = text[(slash + 1)..].Trim();

        // NumberStyles.None rejects signs, decimals and embedded whitespace, so "-8/1" and
        // "8.5/1" fail here rather than silently truncating.
        if (!int.TryParse(numeratorText, NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
            || numerator <= 0)
        {
            throw new FormatException(
                $"Odds value \"{odds}\" has a numerator that is not a positive whole number. " +
                "Expected fractional odds such as \"8/1\".");
        }

        // The denominator is not summed, but validating it catches "8/" and "8/1/2", which
        // would otherwise pass as a well-formed 8.
        if (!int.TryParse(denominatorText, NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
            || denominator <= 0)
        {
            throw new FormatException(
                $"Odds value \"{odds}\" has a denominator that is not a positive whole number. " +
                "Expected fractional odds such as \"8/1\".");
        }

        return numerator;
    }
}
