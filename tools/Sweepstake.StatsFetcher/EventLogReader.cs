using Sweepstake.Core;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// Builds season totals from live per-fixture data.
/// <para>
/// Every season-level ESPN endpoint lags a matchday by hours -- verified on 2026-08-22, when
/// six matches had finished but types/0/leaders, types/1/leaders and the per-athlete
/// statistics/0 all still reported only the previous day's fixture. The per-fixture numbers
/// under an athlete's eventlog were already correct. So totals are summed from fixtures.
/// </para>
/// <para>
/// Three timers keep that affordable, because the naive version would re-read every fixture
/// of every player 96 times a day:
/// <list type="bullet">
/// <item>a fixture is <b>settled</b> a week after we first saw it, and never read again;</item>
/// <item>a fixture seen in the last few hours is <b>hot</b> and re-read every run, so a goal
/// lands on the board within one cron tick;</item>
/// <item>anything in between is re-read hourly, purely to absorb Opta corrections.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class EventLogReader(EspnClient espn, TextWriter log)
{
    /// <summary>How long a fixture stays eligible for re-reading, to absorb Opta corrections.</summary>
    public static readonly TimeSpan CorrectionWindow = TimeSpan.FromDays(7);

    /// <summary>A fixture this new may still be in play, so it is re-read on every run.</summary>
    public static readonly TimeSpan HotWindow = TimeSpan.FromHours(6);

    /// <summary>How often a finished-but-unsettled fixture is re-checked for corrections.</summary>
    public static readonly TimeSpan CorrectionRecheckInterval = TimeSpan.FromHours(1);

    /// <summary>How often to re-list every player's fixtures when nothing is going on.</summary>
    public static readonly TimeSpan IdleSweepInterval = TimeSpan.FromHours(1);

    private const string OffensiveCategory = "offensive";
    private const string GoalsStat = "totalGoals";

    // Not shotAssists, secondAssists or gameWinningAssists, all of which sit alongside it.
    private const string AssistsStat = "goalAssists";

    public int FixturesRead { get; private set; }

    public int FixturesSkipped { get; private set; }

    public int PlayersWithoutAnEventLog { get; private set; }

    public bool Swept { get; private set; }

    public async Task<MatchStatsCache> RefreshAsync(
        PicksFile picks,
        MatchStatsCache cache,
        DateTimeOffset now,
        bool rebuildEverything,
        CancellationToken ct)
    {
        var fixtures = cache.Fixtures.ToDictionary(
            kv => kv.Key,
            kv => new MutableFixture(kv.Value),
            StringComparer.Ordinal);

        Swept = rebuildEverything || ShouldSweep(cache, now);

        if (Swept)
        {
            await SweepEventLogsAsync(picks, fixtures, now, rebuildEverything, ct);
        }
        else
        {
            // ShouldSweep already fires whenever anything could have moved, so there is
            // genuinely nothing to do. This is the cheap path on a quiet afternoon: zero
            // requests, and stats.json is rewritten with the same numbers.
            log.WriteLine("  nothing in play and swept within the hour - no requests needed");
        }

        return new MatchStatsCache(
            picks.Season,
            now,
            Swept ? now : cache.LastSweepUtc,
            fixtures.ToDictionary(kv => kv.Key, kv => kv.Value.ToCached(), StringComparer.Ordinal));
    }

    /// <summary>
    /// Sweep when anything might have changed: no cache yet, a fixture is still hot, or it has
    /// been an hour since we last looked. Otherwise the 38 event-log reads are pure waste.
    /// </summary>
    internal static bool ShouldSweep(MatchStatsCache cache, DateTimeOffset now) =>
        cache.Fixtures.Count == 0
        || cache.Fixtures.Values.Any(f => now - f.FirstSeenUtc < HotWindow)
        || now - cache.LastSweepUtc >= IdleSweepInterval;

    /// <summary>
    /// Whether one player's line in one fixture is worth re-reading. Pure, so the throttling
    /// can be tested without touching the network.
    /// </summary>
    internal static bool ShouldRereadFixture(
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastReadUtc,
        bool alreadyHaveThisPlayer,
        DateTimeOffset now,
        bool rebuildEverything)
    {
        if (rebuildEverything || !alreadyHaveThisPlayer)
        {
            return true;
        }

        var age = now - firstSeenUtc;

        // Settled: a week old, corrections have long since landed.
        if (age > CorrectionWindow)
        {
            return false;
        }

        // Hot: possibly still in play, so track it every run.
        if (age < HotWindow)
        {
            return true;
        }

        return now - lastReadUtc >= CorrectionRecheckInterval;
    }

    private async Task SweepEventLogsAsync(
        PicksFile picks,
        Dictionary<string, MutableFixture> fixtures,
        DateTimeOffset now,
        bool rebuildEverything,
        CancellationToken ct)
    {
        var logs = await Task.WhenAll(picks.Players.Keys.Select(id => LoadEventLogAsync(id, ct)));

        foreach (var (athleteId, entries) in logs)
        {
            foreach (var entry in entries)
            {
                if (!entry.Played || entry.Event is not { } evt || entry.Statistics is not { } statsRef)
                {
                    continue;
                }

                var eventId = evt.Id;
                var known = fixtures.TryGetValue(eventId, out var fixture);

                if (known && !ShouldRereadFixture(
                        fixture!.FirstSeenUtc, fixture.LastReadUtc, fixture.Players.ContainsKey(athleteId),
                        now, rebuildEverything))
                {
                    FixturesSkipped++;
                    continue;
                }

                var line = await ReadFixtureAsync(statsRef.Ref, ct);
                if (line is null)
                {
                    continue;
                }

                FixturesRead++;

                if (!known)
                {
                    fixture = new MutableFixture(now);
                    fixtures[eventId] = fixture;
                }

                fixture!.Players[athleteId] = line;
                fixture.LastReadUtc = now;
            }
        }
    }

    private async Task<(string AthleteId, IReadOnlyList<EspnEventLogEntry> Entries)> LoadEventLogAsync(
        string athleteId,
        CancellationToken ct)
    {
        var eventLog = await espn.GetOrNullAsync(
            espn.AthleteEventLogUrl(athleteId), EspnJsonContext.Default.EspnEventLog, ct);

        var items = eventLog?.Events?.Items;

        // ESPN only creates an event log once a player has featured. Nothing to read yet is
        // normal in August and simply means zero, not an error.
        if (items is null || items.Count == 0)
        {
            PlayersWithoutAnEventLog++;
            return (athleteId, []);
        }

        return (athleteId, items);
    }

    private async Task<CachedPlayerMatch?> ReadFixtureAsync(string statisticsUrl, CancellationToken ct)
    {
        var stats = await espn.GetOrNullAsync(
            statisticsUrl, EspnJsonContext.Default.EspnStatisticsResponse, ct);

        return stats is null
            ? null
            : new CachedPlayerMatch(
                AsInt(stats.Find(OffensiveCategory, GoalsStat)),
                AsInt(stats.Find(OffensiveCategory, AssistsStat)));
    }

    private static int AsInt(double? value) =>
        value is null ? 0 : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);

    private sealed class MutableFixture
    {
        public MutableFixture(DateTimeOffset firstSeenUtc)
        {
            FirstSeenUtc = firstSeenUtc;
            LastReadUtc = DateTimeOffset.UnixEpoch;
            Players = new Dictionary<string, CachedPlayerMatch>(StringComparer.Ordinal);
        }

        public MutableFixture(CachedFixture cached)
        {
            FirstSeenUtc = cached.FirstSeenUtc;
            LastReadUtc = cached.LastReadUtc;
            Players = cached.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }

        public DateTimeOffset FirstSeenUtc { get; }

        public DateTimeOffset LastReadUtc { get; set; }

        public Dictionary<string, CachedPlayerMatch> Players { get; }

        public CachedFixture ToCached() => new(FirstSeenUtc, LastReadUtc, Players);
    }
}
