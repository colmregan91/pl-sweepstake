using Sweepstake.Core;

namespace Sweepstake.Core.Tests;

public class OddsTests
{
    [Theory]
    [InlineData("8/1", 8)]
    [InlineData("150/1", 150)]
    [InlineData("10/1", 10)]
    [InlineData("100/1", 100)]
    [InlineData(" 40/1 ", 40)]
    public void ParseNumerator_reads_the_numerator(string odds, int expected) =>
        Assert.Equal(expected, Odds.ParseNumerator(odds));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8")]          // no separator
    [InlineData("8/")]         // no denominator
    [InlineData("/1")]         // no numerator
    [InlineData("8/1/2")]      // denominator is not a number
    [InlineData("evens")]
    [InlineData("-8/1")]       // negative
    [InlineData("0/1")]        // zero is not a real price
    [InlineData("8/0")]
    [InlineData("8.5/1")]      // not a whole number
    public void ParseNumerator_rejects_malformed_input(string odds)
    {
        var ex = Assert.Throws<FormatException>(() => Odds.ParseNumerator(odds));

        // The message has to name the offending value, otherwise a bad hand-edit in a
        // 34-row file is a hunt.
        Assert.Contains("fractional odds", ex.Message, StringComparison.Ordinal);
    }
}
