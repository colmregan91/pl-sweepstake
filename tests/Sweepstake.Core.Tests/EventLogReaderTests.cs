using Sweepstake.StatsFetcher;

namespace Sweepstake.Core.Tests;

/// <summary>
/// The throttling that keeps the live per-fixture design affordable. Getting these wrong is
/// either a board that lags (too lazy) or thousands of pointless requests a day (too eager),
/// so the boundaries are pinned down here rather than discovered in production.
/// </summary>
public class EventLogSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 20, 0, 0, TimeSpan.Zero);

    private static MatchStatsCache Cache(DateTimeOffset lastSweep, params DateTimeOffset[] fixtureFirstSeen) =>
        new(
            "2026",
            Now,
            lastSweep,
            fixtureFirstSeen
                .Select((seen, i) => (Key: $"evt{i}", Value: new CachedFixture(seen, seen, EmptyPlayers())))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static Dictionary<string, CachedPlayerMatch> EmptyPlayers() => new(StringComparer.Ordinal);

    [Fact]
    public void An_empty_cache_always_sweeps() =>
        Assert.True(EventLogReader.ShouldSweep(Cache(Now), Now));

    [Fact]
    public void A_fixture_still_inside_the_hot_window_forces_a_sweep()
    {
        // Kicked off two hours ago: it may still be in play, so poll it every run.
        var cache = Cache(Now, Now.AddHours(-2));

        Assert.True(EventLogReader.ShouldSweep(cache, Now));
    }

    [Fact]
    public void Nothing_hot_and_swept_recently_means_no_requests_at_all()
    {
        // The quiet-Tuesday case: last match two days ago, swept ten minutes ago.
        var cache = Cache(Now.AddMinutes(-10), Now.AddDays(-2));

        Assert.False(EventLogReader.ShouldSweep(cache, Now));
    }

    [Fact]
    public void An_hour_since_the_last_sweep_forces_one_even_when_quiet()
    {
        // So a fixture nobody told us about still gets discovered.
        var cache = Cache(Now.AddHours(-1), Now.AddDays(-2));

        Assert.True(EventLogReader.ShouldSweep(cache, Now));
    }

    [Theory]
    [InlineData(0, false)]      // just swept, nothing hot - stay quiet
    [InlineData(59, false)]     // still inside the hour
    [InlineData(60, true)]      // exactly on the hour
    [InlineData(61, true)]      // past it
    public void The_idle_sweep_interval_is_an_hour(int minutesSinceSweep, bool expectSweep)
    {
        var cache = Cache(Now.AddMinutes(-minutesSinceSweep), Now.AddDays(-2));

        Assert.Equal(expectSweep, EventLogReader.ShouldSweep(cache, Now));
    }
}

public class EventLogUrlTests
{
    /// <summary>
    /// The event log paginates at 25. Without an explicit limit a 38-fixture season silently
    /// returns 25 of them and every total comes out low: measured against completed 2025/26,
    /// Haaland summed to 21 goals instead of 27 and Bruno Fernandes to 12 assists instead of
    /// 21. Plausible numbers, quietly wrong. Do not remove the limit.
    /// </summary>
    [Fact]
    public void The_event_log_url_asks_for_more_than_one_page()
    {
        using var espn = new EspnClient("2026", TextWriter.Null);

        var url = espn.AthleteEventLogUrl("253989");

        Assert.Contains($"limit={EspnClient.EventLogLimit}", url, StringComparison.Ordinal);
        Assert.True(EspnClient.EventLogLimit >= 38, "a Premier League season is 38 fixtures");
    }
}

public class FixtureRereadTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 20, 0, 0, TimeSpan.Zero);

    private static bool Reread(TimeSpan age, TimeSpan sinceRead, bool have = true, bool rebuild = false) =>
        EventLogReader.ShouldRereadFixture(Now - age, Now - sinceRead, have, Now, rebuild);

    [Fact]
    public void A_player_we_have_never_read_for_this_fixture_is_always_read() =>
        Assert.True(Reread(TimeSpan.FromDays(30), TimeSpan.Zero, have: false));

    [Fact]
    public void Rebuild_forces_a_read_even_for_a_settled_fixture() =>
        Assert.True(Reread(TimeSpan.FromDays(30), TimeSpan.Zero, rebuild: true));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void A_hot_fixture_is_re_read_every_run(int hoursOld) =>
        Assert.True(Reread(TimeSpan.FromHours(hoursOld), TimeSpan.Zero));

    [Fact]
    public void Just_past_the_hot_window_it_falls_back_to_hourly()
    {
        // Seven hours old and read a minute ago: leave it alone.
        Assert.False(Reread(TimeSpan.FromHours(7), TimeSpan.FromMinutes(1)));

        // Seven hours old and not read for an hour: check for a correction.
        Assert.True(Reread(TimeSpan.FromHours(7), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Inside_the_correction_window_it_keeps_being_checked_hourly() =>
        Assert.True(Reread(TimeSpan.FromDays(6), TimeSpan.FromHours(2)));

    [Fact]
    public void Past_the_correction_window_it_is_settled_and_never_read_again()
    {
        // Eight days old, not read for a month: still no. This is what keeps the request
        // count flat instead of growing with the season.
        Assert.False(Reread(TimeSpan.FromDays(8), TimeSpan.FromDays(30)));
    }

    [Fact]
    public void The_correction_window_boundary_is_seven_days()
    {
        Assert.True(Reread(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1), TimeSpan.FromHours(2)));
        Assert.False(Reread(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)));
    }
}

public class SeasonTotalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 20, 0, 0, TimeSpan.Zero);

    private static CachedFixture Fixture(params (string Id, int Goals, int Assists)[] lines) =>
        new(Now, Now, lines.ToDictionary(l => l.Id, l => new CachedPlayerMatch(l.Goals, l.Assists), StringComparer.Ordinal));

    private static MatchStatsCache Cache(params CachedFixture[] fixtures) =>
        new("2026", Now, Now,
            fixtures.Select((f, i) => (Key: $"evt{i}", Value: f)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    [Fact]
    public void Totals_sum_a_player_across_their_fixtures()
    {
        // This is the whole point of the redesign: a season total is the sum of matches.
        var totals = Cache(
            Fixture(("1", 1, 0), ("2", 0, 1)),
            Fixture(("1", 2, 1)),
            Fixture(("1", 0, 0), ("2", 3, 0))).SeasonTotals();

        Assert.Equal((3, 1), totals["1"]);
        Assert.Equal((3, 1), totals["2"]);
    }

    [Fact]
    public void A_player_in_no_fixtures_is_simply_absent() =>
        Assert.False(Cache(Fixture(("1", 1, 0))).SeasonTotals().ContainsKey("999"));

    [Fact]
    public void An_empty_cache_totals_nothing() =>
        Assert.Empty(Cache().SeasonTotals());

    [Fact]
    public void A_goalless_appearance_still_counts_as_zero_not_missing()
    {
        var totals = Cache(Fixture(("1", 0, 0))).SeasonTotals();

        Assert.True(totals.ContainsKey("1"));
        Assert.Equal((0, 0), totals["1"]);
    }
}
