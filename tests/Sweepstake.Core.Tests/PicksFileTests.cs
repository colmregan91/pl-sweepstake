using Sweepstake.Core;

namespace Sweepstake.Core.Tests;

/// <summary>
/// Asserts against the real data/picks.json. It is hand-maintained, so these checks exist to
/// turn a fat-fingered edit into a red build rather than a quietly wrong scoreboard.
/// </summary>
public class PicksFileTests
{
    private const int ExpectedEntrants = 17;
    private const int ExpectedRegistryEntries = 38;

    /// <summary>
    /// Odds totals read off the source spreadsheet (docs/design-reference.jpeg). These are the
    /// oracle -- the app must derive the same numbers rather than being told them.
    /// </summary>
    private static readonly (string Name, int Goals, int Assists)[] SpreadsheetOddsTotals =
    [
        ("Eanan", 128, 128),
        ("Joey", 136, 131),
        ("Johnny", 168, 135),
        ("Tarig", 125, 126),
        ("Ste D", 174, 135),
        ("Mossy", 130, 130),
        ("Liamo", 166, 150),
        ("Demo", 130, 128),
        ("Tunney", 128, 128),
        ("Shero", 133, 146),
        ("Raleigh", 135, 174),
        ("Cormo", 128, 126),
        ("Jimmy H", 150, 149),
        ("Skurto", 125, 128),
        ("Deco", 128, 137),
        ("Brophy", 140, 164),
        ("Ciano", 126, 145),
    ];

    private static readonly PicksFile Picks = LoadPicks();

    [Fact]
    public void Deserializes_via_the_source_generated_context()
    {
        Assert.Equal("2026", Picks.Season);
        Assert.Equal("2026/27", Picks.SeasonLabel);
        Assert.NotEmpty(Picks.Players);
        Assert.NotEmpty(Picks.Entrants);
    }

    [Fact]
    public void Has_seventeen_entrants() =>
        Assert.Equal(ExpectedEntrants, Picks.Entrants.Count);

    [Fact]
    public void Has_thirty_eight_registry_entries() =>
        Assert.Equal(ExpectedRegistryEntries, Picks.Players.Count);

    [Fact]
    public void Passes_every_integrity_check()
    {
        // Covers: 3 picks per list, every referenced id in the registry, no unused registry
        // entry, no duplicate id within one list, and every odds value parseable.
        var problems = PicksIntegrity.Validate(Picks);
        Assert.Empty(problems);
    }

    [Fact]
    public void Every_entrant_has_three_picks_on_each_board() =>
        Assert.All(Picks.Entrants, e =>
        {
            Assert.Equal(3, e.Goalscorers.Count);
            Assert.Equal(3, e.Assisters.Count);
        });

    [Fact]
    public void Every_registry_entry_has_a_name_and_a_club() =>
        Assert.All(Picks.Players, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.Name));
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.Club));
        });

    [Fact]
    public void Registry_keys_are_numeric_espn_ids() =>
        Assert.All(Picks.Players.Keys, id => Assert.True(
            id.All(char.IsAsciiDigit),
            $"Registry key \"{id}\" is not a numeric ESPN athlete id."));

    [Fact]
    public void Accented_names_survive_the_round_trip()
    {
        // If this fails, something has re-encoded the file or stripped the diacritics.
        Assert.Equal("Benjamin Šeško", Picks.Players["289155"].Name);
        Assert.Equal("Martin Ødegaard", Picks.Players["203669"].Name);
        Assert.Equal("Pascal Groß", Picks.Players["132659"].Name);
        Assert.Equal("Viktor Gyökeres", Picks.Players["258906"].Name);
        Assert.Equal("Bruno Guimarães", Picks.Players["218522"].Name);
        Assert.Equal("Daniel Muñoz", Picks.Players["146679"].Name);
        Assert.Equal("João Pedro", Picks.Players["284960"].Name);
        Assert.Equal("Jérémy Doku", Picks.Players["283672"].Name);
    }

    [Fact]
    public void The_one_sheet_name_override_is_present()
    {
        // ESPN says "Igor Thiago", the spreadsheet said "Thiago". Recorded, not matched on.
        var thiago = Picks.Players["301894"];
        Assert.Equal("Igor Thiago", thiago.Name);
        Assert.Equal("Thiago", thiago.SheetName);
    }

    [Theory]
    [MemberData(nameof(OddsTotalCases))]
    public void Odds_totals_match_the_spreadsheet(string entrant, Board board, int expected)
    {
        var scored = LeaderboardBuilder.Build(Picks, EmptyStats(), board)
            .Single(e => e.Name == entrant);

        Assert.Equal(expected, scored.OddsTotal);
    }

    public static TheoryData<string, Board, int> OddsTotalCases()
    {
        var data = new TheoryData<string, Board, int>();
        foreach (var (name, goals, assists) in SpreadsheetOddsTotals)
        {
            data.Add(name, Board.Goals, goals);
            data.Add(name, Board.Assists, assists);
        }

        return data;
    }

    [Fact]
    public void All_thirty_four_odds_totals_are_covered() =>
        Assert.Equal(ExpectedEntrants * 2, OddsTotalCases().Count);

    [Fact]
    public void The_spreadsheet_oracle_names_the_same_entrants_as_the_file()
    {
        Assert.Equal(
            Picks.Entrants.Select(e => e.Name).Order(StringComparer.Ordinal),
            SpreadsheetOddsTotals.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Both_boards_rank_all_seventeen_entrants_with_no_stats_yet()
    {
        foreach (var board in (Board[])[Board.Goals, Board.Assists])
        {
            var result = LeaderboardBuilder.Build(Picks, EmptyStats(), board);

            Assert.Equal(ExpectedEntrants, result.Count);
            Assert.All(result, e => Assert.Equal(0, e.Total));

            // Nobody has scored, so everybody is joint first.
            Assert.All(result, e => Assert.Equal(1, e.Rank));
        }
    }

    private static StatsFile EmptyStats() =>
        new(DateTimeOffset.UnixEpoch, "2026", "test", new Dictionary<string, PlayerStat>(StringComparer.Ordinal));

    private static PicksFile LoadPicks()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "picks.json");
        return SweepstakeJson.ReadPicks(File.ReadAllText(path));
    }
}
