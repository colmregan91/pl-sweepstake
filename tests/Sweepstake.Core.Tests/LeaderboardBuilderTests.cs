using Sweepstake.Core;

namespace Sweepstake.Core.Tests;

public class LeaderboardBuilderTests
{
    [Fact]
    public void Totals_sum_the_three_picks()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")]));

        var stats = Fixtures.Stats(("1", 7, 2), ("2", 4, 5), ("3", 1, 3));

        var goals = LeaderboardBuilder.Build(picks, stats, Board.Goals).Single();
        var assists = LeaderboardBuilder.Build(picks, stats, Board.Assists).Single();

        Assert.Equal(12, goals.Total);
        Assert.Equal(10, assists.Total);

        // And each row carries its own value, so the board can be checked by eye.
        Assert.Equal([7, 4, 1], goals.Picks.Select(p => p.Value));
        Assert.Equal([2, 5, 3], assists.Picks.Select(p => p.Value));
    }

    [Fact]
    public void Boards_score_independently()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "8/1"), ("3", "8/1")],
                assisters: [("1", "8/1"), ("2", "8/1"), ("3", "8/1")]));

        // Same three players on both lists, but a goal is not an assist.
        var stats = Fixtures.Stats(("1", 10, 0), ("2", 10, 0), ("3", 10, 1));

        Assert.Equal(30, LeaderboardBuilder.Build(picks, stats, Board.Goals).Single().Total);
        Assert.Equal(1, LeaderboardBuilder.Build(picks, stats, Board.Assists).Single().Total);
    }

    [Fact]
    public void A_player_missing_from_stats_scores_zero_rather_than_throwing()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")]));

        // Only id 1 has scored. Ids 2 and 3 are simply absent from the leaders payload.
        var stats = Fixtures.Stats(("1", 5, 5));

        var result = LeaderboardBuilder.Build(picks, stats, Board.Goals).Single();

        Assert.Equal(5, result.Total);
        Assert.Equal([5, 0, 0], result.Picks.Select(p => p.Value));
    }

    [Fact]
    public void An_empty_stats_file_scores_everyone_zero()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")]));

        // Pre-season: nobody has scored. This is the state the design reference was captured in.
        var result = LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Goals).Single();

        Assert.Equal(0, result.Total);
        Assert.Equal(128, result.OddsTotal);
        Assert.Equal(1, result.Rank);
    }

    [Fact]
    public void A_pick_missing_from_the_registry_throws()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("999", "80/1")],
                assisters: [("1", "8/1"), ("2", "40/1"), ("1", "80/1")]));

        var ex = Assert.Throws<KeyNotFoundException>(
            () => LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Goals));

        Assert.Contains("999", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Solo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_odds_throw_naming_the_entrant_and_the_id()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "forty to one"), ("3", "80/1")],
                assisters: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")]));

        var ex = Assert.Throws<FormatException>(
            () => LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Goals));

        Assert.Contains("Solo", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\"2\"", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Highest_total_sorts_first()
    {
        var board = LeaderboardBuilder.Build(ThreeWayPicks(), ThreeWayStats(), Board.Goals);

        Assert.Equal(["Bee", "Cee", "Ay"], board.Select(e => e.Name));
        Assert.Equal([9, 5, 1], board.Select(e => e.Total));
    }

    [Fact]
    public void Equal_totals_are_broken_by_name_ascending()
    {
        var registry = Fixtures.Registry("1");
        var picks = Fixtures.Picks(
            registry,
            Fixtures.Entrant("Zoe", [("1", "8/1")], [("1", "8/1")]),
            Fixtures.Entrant("Adam", [("1", "8/1")], [("1", "8/1")]),
            Fixtures.Entrant("Mia", [("1", "8/1")], [("1", "8/1")]));

        var board = LeaderboardBuilder.Build(picks, Fixtures.Stats(("1", 3, 0)), Board.Goals);

        Assert.Equal(["Adam", "Mia", "Zoe"], board.Select(e => e.Name));
        Assert.All(board, e => Assert.Equal(3, e.Total));
    }

    [Fact]
    public void Ties_share_a_rank_and_the_next_rank_skips()
    {
        var registry = Fixtures.Registry("1", "2", "3", "4");
        var picks = Fixtures.Picks(
            registry,
            Fixtures.Entrant("Ay", [("1", "8/1")], [("1", "8/1")]),     // 10
            Fixtures.Entrant("Bee", [("2", "8/1")], [("2", "8/1")]),    //  5  tied
            Fixtures.Entrant("Cee", [("3", "8/1")], [("3", "8/1")]),    //  5  tied
            Fixtures.Entrant("Dee", [("4", "8/1")], [("4", "8/1")]));   //  1

        var stats = Fixtures.Stats(("1", 10, 0), ("2", 5, 0), ("3", 5, 0), ("4", 1, 0));

        var board = LeaderboardBuilder.Build(picks, stats, Board.Goals);

        Assert.Equal(["Ay", "Bee", "Cee", "Dee"], board.Select(e => e.Name));
        Assert.Equal([10, 5, 5, 1], board.Select(e => e.Total));

        // Competition ranking: 1, 2, 2, 4 -- rank 3 is consumed by the tie.
        Assert.Equal([1, 2, 2, 4], board.Select(e => e.Rank));
    }

    [Fact]
    public void Everyone_tied_on_zero_shares_rank_one()
    {
        var registry = Fixtures.Registry("1");
        var picks = Fixtures.Picks(
            registry,
            Fixtures.Entrant("Ay", [("1", "8/1")], [("1", "8/1")]),
            Fixtures.Entrant("Bee", [("1", "8/1")], [("1", "8/1")]),
            Fixtures.Entrant("Cee", [("1", "8/1")], [("1", "8/1")]));

        var board = LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Goals);

        Assert.All(board, e => Assert.Equal(1, e.Rank));
    }

    [Fact]
    public void Odds_totals_sum_the_three_numerators()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")]));

        Assert.Equal(128, LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Goals).Single().OddsTotal);
        Assert.Equal(128, LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Assists).Single().OddsTotal);
    }

    [Fact]
    public void Scored_picks_keep_their_registry_entry_and_odds_for_display()
    {
        var registry = new Dictionary<string, Player>(StringComparer.Ordinal)
        {
            ["301894"] = new("Igor Thiago", "Brentford", SheetName: "Thiago"),
        };

        var picks = Fixtures.Picks(
            registry,
            Fixtures.Entrant("Solo", [("301894", "10/1")], [("301894", "10/1")]));

        var pick = LeaderboardBuilder.Build(picks, Fixtures.Stats(("301894", 2, 0)), Board.Goals)
            .Single().Picks.Single();

        Assert.Equal("301894", pick.EspnId);
        Assert.Equal("Igor Thiago", pick.Player.Name);
        Assert.Equal("Brentford", pick.Player.Club);
        Assert.Equal("Thiago", pick.Player.SheetName);
        Assert.Equal("10/1", pick.Odds);
        Assert.Equal(2, pick.Value);
    }

    [Fact]
    public void Picks_keep_their_listed_order_within_an_entrant()
    {
        // The board shows an entrant's three picks in the order they appear in picks.json,
        // matching the sheet. Sorting applies between entrants, not within one.
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("3", "8/1"), ("1", "40/1"), ("2", "80/1")],
                assisters: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")]));

        var order = LeaderboardBuilder.Build(picks, Fixtures.Stats(), Board.Goals)
            .Single().Picks.Select(p => p.EspnId);

        Assert.Equal(["3", "1", "2"], order);
    }

    private static PicksFile ThreeWayPicks() =>
        Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Ay", [("1", "8/1")], [("1", "8/1")]),
            Fixtures.Entrant("Bee", [("2", "8/1")], [("2", "8/1")]),
            Fixtures.Entrant("Cee", [("3", "8/1")], [("3", "8/1")]));

    private static StatsFile ThreeWayStats() =>
        Fixtures.Stats(("1", 1, 0), ("2", 9, 0), ("3", 5, 0));
}
