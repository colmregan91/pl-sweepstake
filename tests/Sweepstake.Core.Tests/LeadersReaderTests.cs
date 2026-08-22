using Sweepstake.Core;
using Sweepstake.StatsFetcher;

namespace Sweepstake.Core.Tests;

public class LeadersReaderTests
{
    private const string AthleteRef =
        "http://sports.core.api.espn.com/v2/sports/soccer/leagues/eng.1/seasons/2026/athletes/";

    /// <summary>
    /// The real payload lists goalsLeaders and assistsLeaders BEFORE goals and assists, and all
    /// four share a displayName in pairs. This fixture reproduces that ordering and gives the
    /// pairs deliberately different values, so selecting the wrong one cannot pass.
    /// </summary>
    private static EspnLeadersResponse RealisticPayload() => new(
    [
        Category("goalsLeaders", "Goals", (231182, 99), (203669, 98)),
        Category("assistsLeaders", "Assists", (231182, 97), (203669, 96)),
        Category("goals", "Goals", (231182, 1), (203669, 2)),
        Category("assists", "Assists", (231182, 3), (203669, 4)),
        Category("shotsOnTarget", "Shots On Target", (231182, 7)),
    ]);

    [Fact]
    public void Goals_is_selected_by_name_not_by_display_name()
    {
        var goals = LeadersReader.Require(RealisticPayload(), LeadersReader.Goals);

        // 99 would mean goalsLeaders won, which is what matching on displayName would do.
        Assert.Equal(1, goals["231182"]);
        Assert.Equal(2, goals["203669"]);
    }

    [Fact]
    public void Assists_is_selected_by_name_not_by_display_name()
    {
        var assists = LeadersReader.Require(RealisticPayload(), LeadersReader.Assists);

        Assert.Equal(3, assists["231182"]);
        Assert.Equal(4, assists["203669"]);
    }

    [Fact]
    public void The_cross_check_categories_are_reachable_separately()
    {
        var payload = RealisticPayload();

        Assert.Equal(99, LeadersReader.Optional(payload, LeadersReader.GoalsCrossCheck)!["231182"]);
        Assert.Equal(97, LeadersReader.Optional(payload, LeadersReader.AssistsCrossCheck)!["231182"]);
    }

    [Fact]
    public void A_missing_category_throws_rather_than_defaulting_to_empty()
    {
        // Defaulting to empty would silently zero every board.
        var payload = new EspnLeadersResponse([Category("assists", "Assists", (1, 1))]);

        var ex = Assert.Throws<InvalidDataException>(
            () => LeadersReader.Require(payload, LeadersReader.Goals));

        Assert.Contains("goals", ex.Message, StringComparison.Ordinal);
        Assert.Contains("assists", ex.Message, StringComparison.Ordinal); // lists what it did offer
        Assert.Contains("not written", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_categories_array_throws()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => LeadersReader.Require(new EspnLeadersResponse(null), LeadersReader.Goals));

        Assert.Contains("goals", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_returns_null_for_an_absent_category() =>
        Assert.Null(LeadersReader.Optional(new EspnLeadersResponse([]), LeadersReader.GoalsCrossCheck));

    [Fact]
    public void An_empty_category_reads_as_no_data_not_an_error()
    {
        // Nobody has scored yet is a real state, distinct from the category being missing.
        var payload = new EspnLeadersResponse([new EspnLeaderCategory("goals", "Goals", [])]);

        Assert.Empty(LeadersReader.Require(payload, LeadersReader.Goals));
    }

    [Fact]
    public void Values_arrive_as_doubles_and_are_rounded_to_int()
    {
        var payload = new EspnLeadersResponse(
        [
            new EspnLeaderCategory("goals", "Goals",
            [
                new EspnLeader(1.0, new EspnRef($"{AthleteRef}1")),
                new EspnLeader(27.0, new EspnRef($"{AthleteRef}2")),
            ]),
        ]);

        var goals = LeadersReader.Require(payload, LeadersReader.Goals);

        Assert.Equal(1, goals["1"]);
        Assert.Equal(27, goals["2"]);
    }

    [Fact]
    public void Entries_without_an_athlete_ref_are_skipped()
    {
        var payload = new EspnLeadersResponse(
        [
            new EspnLeaderCategory("goals", "Goals",
            [
                new EspnLeader(5.0, null),
                new EspnLeader(3.0, new EspnRef($"{AthleteRef}7")),
            ]),
        ]);

        var goals = LeadersReader.Require(payload, LeadersReader.Goals);

        Assert.Equal(3, Assert.Single(goals).Value);
    }

    [Fact]
    public void Athlete_ids_are_parsed_out_of_the_ref_url()
    {
        var goals = LeadersReader.Require(RealisticPayload(), LeadersReader.Goals);

        Assert.Equal(["203669", "231182"], goals.Keys.Order(StringComparer.Ordinal));
    }

    private static EspnLeaderCategory Category(
        string name,
        string displayName,
        params (int Id, int Value)[] leaders) =>
        new(
            name,
            displayName,
            [.. leaders.Select(l => new EspnLeader(l.Value, new EspnRef($"{AthleteRef}{l.Id}?lang=en&region=us")))]);
}

public class FetchGuardTests
{
    private static StatsFile Stats(string season, params (string Id, int Goals, int Assists)[] rows) =>
        new(
            DateTimeOffset.UnixEpoch,
            season,
            "test",
            rows.ToDictionary(r => r.Id, r => new PlayerStat($"P{r.Id}", r.Goals, r.Assists), StringComparer.Ordinal));

    [Fact]
    public void A_season_total_collapsing_to_zero_is_treated_as_a_failed_fetch()
    {
        var previous = Stats("2026", ("1", 12, 4), ("2", 3, 9));
        var next = Stats("2026", ("1", 0, 0), ("2", 0, 0));

        var ex = Assert.Throws<InvalidDataException>(() => FetchCommand.GuardAgainstRegression(previous, next));

        Assert.Contains("do not go down", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not written", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_season_legitimately_starts_at_zero()
    {
        var previous = Stats("2025", ("1", 27, 4));
        var next = Stats("2026", ("1", 0, 0));

        FetchCommand.GuardAgainstRegression(previous, next);
    }

    [Fact]
    public void A_first_run_with_no_previous_file_is_allowed()
    {
        FetchCommand.GuardAgainstRegression(null, Stats("2026", ("1", 0, 0)));
    }

    [Fact]
    public void Zero_to_zero_pre_season_is_allowed()
    {
        FetchCommand.GuardAgainstRegression(Stats("2026", ("1", 0, 0)), Stats("2026", ("1", 0, 0)));
    }

    [Fact]
    public void Change_detection_ignores_the_timestamp()
    {
        var previous = Stats("2026", ("1", 1, 0)) with { GeneratedUtc = DateTimeOffset.UnixEpoch };
        var next = Stats("2026", ("1", 1, 0)) with { GeneratedUtc = DateTimeOffset.UtcNow };

        Assert.False(FetchCommand.HasPlayerDataChanged(previous, next));
    }

    [Theory]
    [InlineData(2, 0)] // a goal arrived
    [InlineData(1, 1)] // an assist arrived
    public void Change_detection_notices_a_moved_number(int goals, int assists) =>
        Assert.True(FetchCommand.HasPlayerDataChanged(
            Stats("2026", ("1", 1, 0)),
            Stats("2026", ("1", goals, assists))));

    [Fact]
    public void Change_detection_notices_a_player_being_added_or_removed()
    {
        Assert.True(FetchCommand.HasPlayerDataChanged(
            Stats("2026", ("1", 1, 0)),
            Stats("2026", ("1", 1, 0), ("2", 0, 0))));

        Assert.True(FetchCommand.HasPlayerDataChanged(
            Stats("2026", ("1", 1, 0), ("2", 0, 0)),
            Stats("2026", ("1", 1, 0))));
    }

    [Fact]
    public void Change_detection_notices_a_swapped_id_at_the_same_count()
    {
        // Same number of players, same totals, different ids -- a registry edit.
        Assert.True(FetchCommand.HasPlayerDataChanged(
            Stats("2026", ("1", 1, 0)),
            Stats("2026", ("2", 1, 0))));
    }

    [Fact]
    public void Change_detection_treats_no_previous_file_as_a_change() =>
        Assert.True(FetchCommand.HasPlayerDataChanged(null, Stats("2026", ("1", 0, 0))));
}
