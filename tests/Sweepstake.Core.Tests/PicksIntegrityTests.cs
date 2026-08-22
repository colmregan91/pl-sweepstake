using Sweepstake.Core;

namespace Sweepstake.Core.Tests;

public class PicksIntegrityTests
{
    [Fact]
    public void A_well_formed_file_reports_no_problems()
    {
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")])));

        Assert.Empty(problems);
    }

    [Fact]
    public void A_pick_outside_the_registry_is_reported()
    {
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("999", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")])));

        Assert.Contains(problems, p => p.Contains("999", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unpicked_registry_entry_is_reported()
    {
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2", "3", "4"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")])));

        Assert.Contains(problems, p => p.Contains("\"4\"", StringComparison.Ordinal)
                                    && p.Contains("not picked", StringComparison.Ordinal));
    }

    [Fact]
    public void The_wrong_number_of_picks_is_reported()
    {
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1")],
                assisters: [("1", "10/1"), ("2", "18/1")])));

        Assert.Equal(2, problems.Count(p => p.Contains("expected 3", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_duplicate_within_one_list_is_reported()
    {
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("1", "8/1"), ("2", "40/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("2", "18/1")])));

        Assert.Equal(2, problems.Count(p => p.Contains("twice", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_same_player_on_both_boards_is_fine()
    {
        // Cole Palmer is picked as both a scorer and an assister by several entrants.
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")])));

        Assert.Empty(problems);
    }

    [Fact]
    public void Malformed_odds_are_reported()
    {
        var problems = PicksIntegrity.Validate(Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8 to 1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")])));

        Assert.Contains(problems, p => p.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowIfInvalid_lists_every_problem_at_once()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "9"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("999", "80/1")],
                assisters: [("1", "10/1"), ("1", "10/1")]));

        var ex = Assert.Throws<InvalidDataException>(() => PicksIntegrity.ThrowIfInvalid(picks));

        Assert.Contains("999", ex.Message, StringComparison.Ordinal);   // unknown id
        Assert.Contains("twice", ex.Message, StringComparison.Ordinal); // duplicate
        Assert.Contains("expected 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not picked", ex.Message, StringComparison.Ordinal); // id 9 unused
    }

    [Fact]
    public void ThrowIfInvalid_is_silent_on_a_clean_file()
    {
        var picks = Fixtures.Picks(
            Fixtures.Registry("1", "2", "3"),
            Fixtures.Entrant("Solo",
                goalscorers: [("1", "8/1"), ("2", "40/1"), ("3", "80/1")],
                assisters: [("1", "10/1"), ("2", "18/1"), ("3", "100/1")]));

        PicksIntegrity.ThrowIfInvalid(picks);
    }
}
