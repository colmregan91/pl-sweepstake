using Sweepstake.StatsFetcher;

namespace Sweepstake.Core.Tests;

/// <summary>
/// The core API hands back links rather than objects, so every athlete and team id in the
/// pipeline is parsed out of a URL. Getting this wrong would silently mis-key the whole board.
/// </summary>
public class EspnRefTests
{
    [Theory]
    [InlineData(
        "http://sports.core.api.espn.com/v2/sports/soccer/leagues/eng.1/seasons/2026/teams/364?lang=en&region=us",
        "364")]
    [InlineData(
        "http://sports.core.api.espn.com/v2/sports/soccer/leagues/eng.1/seasons/2026/athletes/235662?lang=en&region=us",
        "235662")]
    [InlineData("https://example.test/athletes/301894", "301894")]
    [InlineData("https://example.test/athletes/301894/", "301894")]
    [InlineData("https://example.test/athletes/301894#frag", "301894")]
    [InlineData("301894", "301894")]
    public void LastPathSegment_pulls_the_id_out_of_a_ref(string url, string expected) =>
        Assert.Equal(expected, EspnRef.LastPathSegment(url));

    [Fact]
    public void Id_reads_the_segment_from_the_ref_property()
    {
        var reference = new EspnRef(
            "http://sports.core.api.espn.com/v2/sports/soccer/leagues/eng.1/seasons/2026/teams/382?lang=en&region=us");

        Assert.Equal("382", reference.Id);
    }
}

public class NameFoldTests
{
    [Theory]
    [InlineData("Benjamin Šeško", "Benjamin Sesko")]
    [InlineData("Pascal Groß", "Pascal Gross")]
    [InlineData("Martin Ødegaard", "Martin Odegaard")]
    [InlineData("Viktor Gyökeres", "Viktor Gyokeres")]
    [InlineData("Bruno Guimarães", "Bruno Guimaraes")]
    [InlineData("Daniel Muñoz", "Daniel Munoz")]
    [InlineData("João Pedro", "Joao Pedro")]
    [InlineData("Jérémy Doku", "Jeremy Doku")]
    [InlineData("Dominic Calvert-Lewin", "Dominic Calvert Lewin")]
    [InlineData("Alexander Isak", "alexander  isak")]
    public void Names_differing_only_in_spelling_are_recognised(string a, string b) =>
        Assert.True(NameFold.SameApartFromSpelling(a, b), $"\"{a}\" should fold to the same as \"{b}\"");

    [Theory]
    [InlineData("Igor Jesus", "Igor Thiago")]
    [InlineData("Bruno Fernandes", "Bruno Guimarães")]
    [InlineData("Cole Palmer", "Cole Palmer Jr")]
    [InlineData("Alexander Isak", "")]
    [InlineData("", "")]
    public void Genuinely_different_names_are_not_folded_together(string a, string b) =>
        Assert.False(NameFold.SameApartFromSpelling(a, b), $"\"{a}\" should not fold to the same as \"{b}\"");

    [Fact]
    public void Simplify_reduces_to_lowercase_ascii_words() =>
        Assert.Equal("benjamin sesko", NameFold.Simplify("  Benjamin   Šeško "));
}
