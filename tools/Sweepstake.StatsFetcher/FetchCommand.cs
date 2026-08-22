using System.Text.Json;
using Sweepstake.Core;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// Refreshes wwwroot/data/stats.json. Run by CI on a schedule.
/// <para>
/// Totals are summed from live per-fixture data rather than read from ESPN's season rollup.
/// The rollup lags: verified on 2026-08-22, when six matches had finished but types/0/leaders,
/// types/1/leaders and the per-athlete statistics/0 endpoint all still reported only the
/// previous day's fixture. The rollup is kept as an advisory cross-check.
/// </para>
/// <para>
/// The contract is unchanged: a run either writes a complete, plausible file or writes nothing
/// and exits non-zero. A partial file must never overwrite a good one.
/// </para>
/// </summary>
internal static class FetchCommand
{
    private const string StatsSource = "espn-core-api";

    public static async Task<int> RunAsync(RepoPaths paths, bool rebuildEverything, CancellationToken ct)
    {
        var picks = SweepstakeJson.ReadPicks(await File.ReadAllTextAsync(paths.PicksJson, ct));
        PicksIntegrity.ThrowIfInvalid(picks);

        Console.WriteLine($"fetch - season {picks.Season} per-fixture results -> {paths.Relative(paths.StatsJson)}");
        if (rebuildEverything)
        {
            Console.WriteLine("  --rebuild: re-reading every fixture, ignoring the settled-fixture cache");
        }

        using var espn = new EspnClient(picks.Season, Console.Out);
        var now = DateTimeOffset.UtcNow;

        var cache = await MatchStatsCacheFile.ReadAsync(paths.MatchStatsJson, picks.Season, Console.Out, ct);
        var before = cache.Fixtures.Count;

        var reader = new EventLogReader(espn, Console.Out);
        cache = await reader.RefreshAsync(picks, cache, now, rebuildEverything, ct);

        Console.WriteLine(
            $"  fixtures: {cache.Fixtures.Count} known ({cache.Fixtures.Count - before} new), " +
            $"{reader.FixturesRead} player-fixtures read, {reader.FixturesSkipped} already cached" +
            (reader.Swept ? string.Empty : " (no sweep needed)"));

        if (reader.PlayersWithoutAnEventLog > 0)
        {
            Console.WriteLine(
                $"  {reader.PlayersWithoutAnEventLog} registry player(s) have no fixtures yet and score 0 " +
                "(ESPN creates an event log on a player's first appearance)");
        }

        var players = BuildPlayers(picks, cache.SeasonTotals());
        var stats = new StatsFile(now, picks.Season, StatsSource, players);

        var previous = await ReadPreviousAsync(paths.StatsJson, ct);
        GuardAgainstEmptyResult(cache, previous);
        GuardAgainstRegression(previous, stats);

        // Only worth a 230 KB request when the numbers could have moved. On an idle run the
        // answer cannot have changed since the last sweep.
        if (reader.Swept)
        {
            await CrossCheckAgainstRollupAsync(espn, picks, players, ct);
        }

        var changed = HasPlayerDataChanged(previous, stats);

        await MatchStatsCacheFile.WriteAsync(paths.MatchStatsJson, cache, ct);
        await WriteAtomicallyAsync(paths.StatsJson, SweepstakeJson.WriteStats(stats), ct);

        Report(paths, players, changed, espn.RequestCount);
        await PublishActionsOutputAsync(changed, ct);
        return 0;
    }

    private static Dictionary<string, PlayerStat> BuildPlayers(
        PicksFile picks,
        IReadOnlyDictionary<string, (int Goals, int Assists)> totals)
    {
        var players = new Dictionary<string, PlayerStat>(picks.Players.Count, StringComparer.Ordinal);

        foreach (var (id, player) in picks.Players)
        {
            var total = totals.GetValueOrDefault(id);

            // The display name comes from our curated registry, not from ESPN. ESPN spells some
            // names without their diacritics, and letting that in would produce a pointless
            // commit every time it changed its mind.
            players[id] = new PlayerStat(player.Name, total.Goals, total.Assists);
        }

        return players;
    }

    /// <summary>
    /// If we previously knew about fixtures and now know about none, something went wrong
    /// upstream. Writing zeros over a good file is the one outcome worth failing for.
    /// </summary>
    private static void GuardAgainstEmptyResult(MatchStatsCache cache, StatsFile? previous)
    {
        if (cache.Fixtures.Count > 0)
        {
            return;
        }

        var previouslyScored = previous?.Players.Values.Sum(p => p.Goals + p.Assists) ?? 0;
        if (previouslyScored > 0)
        {
            throw new InvalidDataException(
                "No fixtures could be read for any registry player, but the existing stats.json records " +
                $"{previouslyScored} goals+assists. Treating this as a failed fetch; stats.json was not written.");
        }

        Console.WriteLine("  no fixtures played yet this season - every player scores 0");
    }

    /// <summary>Season totals only ever go up within a season.</summary>
    internal static void GuardAgainstRegression(StatsFile? previous, StatsFile next)
    {
        if (previous is null || !string.Equals(previous.Season, next.Season, StringComparison.Ordinal))
        {
            return;
        }

        var before = previous.Players.Values.Sum(p => p.Goals + p.Assists);
        var after = next.Players.Values.Sum(p => p.Goals + p.Assists);

        if (before > 0 && after == 0)
        {
            throw new InvalidDataException(
                $"The existing stats.json records {before} goals+assists for season {next.Season}, but this " +
                "fetch produced 0 for every player. Season totals do not go down. Treating this as a failed " +
                "fetch; stats.json was not written.");
        }
    }

    /// <summary>
    /// Advisory only. Compares our summed totals against ESPN's own season rollup. The rollup
    /// lags by design, so us being ahead is expected and harmless. The rollup being ahead of us
    /// is not: that would mean our summing has dropped a fixture, which is the bug worth
    /// catching. Never fails the run.
    /// </summary>
    private static async Task CrossCheckAgainstRollupAsync(
        EspnClient espn,
        PicksFile picks,
        IReadOnlyDictionary<string, PlayerStat> players,
        CancellationToken ct)
    {
        EspnLeadersResponse payload;
        try
        {
            payload = await espn.GetAsync(espn.LeadersUrl(), EspnJsonContext.Default.EspnLeadersResponse, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
        {
            Console.WriteLine($"  cross-check skipped: {ex.Message}");
            return;
        }

        var goals = LeadersReader.Optional(payload, LeadersReader.Goals);
        var assists = LeadersReader.Optional(payload, LeadersReader.Assists);

        if (goals is null || assists is null)
        {
            Console.WriteLine("  cross-check skipped: the rollup has no goals/assists category");
            return;
        }

        var ahead = 0;
        var behind = 0;

        foreach (var (id, stat) in players)
        {
            var rollupGoals = goals.GetValueOrDefault(id);
            var rollupAssists = assists.GetValueOrDefault(id);

            if (stat.Goals > rollupGoals || stat.Assists > rollupAssists)
            {
                ahead++;
            }
            else if (stat.Goals < rollupGoals || stat.Assists < rollupAssists)
            {
                behind++;
                Console.WriteLine(
                    $"  ! {picks.Players[id].Name}: we have {stat.Goals}g/{stat.Assists}a but the rollup says " +
                    $"{rollupGoals}g/{rollupAssists}a. The rollup should not be ahead of us - check the summing.");
            }
        }

        Console.WriteLine(
            $"  cross-check vs season rollup: ahead on {ahead} player(s) (expected - the rollup lags), " +
            $"behind on {behind} (unexpected)");
    }

    private static async Task<StatsFile?> ReadPreviousAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return SweepstakeJson.ReadStats(await File.ReadAllTextAsync(path, ct));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            Console.WriteLine($"  ! the existing stats.json could not be read ({ex.Message}); treating it as absent");
            return null;
        }
    }

    /// <summary>
    /// True when any player's goals or assists differ. Deliberately ignores generatedUtc, which
    /// changes on every run -- CI uses this to avoid a commit per quarter-hour.
    /// </summary>
    internal static bool HasPlayerDataChanged(StatsFile? previous, StatsFile next)
    {
        if (previous is null || previous.Players.Count != next.Players.Count)
        {
            return true;
        }

        foreach (var (id, stat) in next.Players)
        {
            if (!previous.Players.TryGetValue(id, out var before)
                || before.Goals != stat.Goals
                || before.Assists != stat.Assists
                || !string.Equals(before.Name, stat.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteAtomicallyAsync(string path, string json, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write beside the target and move into place, so a crash mid-write cannot leave a
        // half-file where the good one was.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, json + Environment.NewLine, ct);
        File.Move(temp, path, overwrite: true);
    }

    private static void Report(
        RepoPaths paths,
        IReadOnlyDictionary<string, PlayerStat> players,
        bool changed,
        int requestCount)
    {
        Console.WriteLine();
        Console.WriteLine($"wrote {players.Count} players to {paths.Relative(paths.StatsJson)} in {requestCount} requests");
        Console.WriteLine($"  {players.Values.Count(p => p.Goals > 0)} with at least one goal ({players.Values.Sum(p => p.Goals)} total)");
        Console.WriteLine($"  {players.Values.Count(p => p.Assists > 0)} with at least one assist ({players.Values.Sum(p => p.Assists)} total)");
        Console.WriteLine($"  player data changed since the previous file: {(changed ? "yes" : "no")}");

        if (!changed)
        {
            Console.WriteLine("  (only generatedUtc moved -- CI should skip the commit)");
        }
    }

    /// <summary>Hands the changed flag to the workflow, so it need not diff the JSON itself.</summary>
    private static async Task PublishActionsOutputAsync(bool changed, CancellationToken ct)
    {
        var outputPath = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        await File.AppendAllTextAsync(
            outputPath,
            $"changed={(changed ? "true" : "false")}{Environment.NewLine}",
            ct);
    }
}
