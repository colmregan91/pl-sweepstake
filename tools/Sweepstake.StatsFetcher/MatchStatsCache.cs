using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sweepstake.Core;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// What each fixture contributed, per player. Committed so a finished match is fetched once
/// rather than re-read 96 times a day forever.
/// <para>
/// Only the 38 ids in the picks registry are stored -- this is not a general match archive.
/// </para>
/// </summary>
internal sealed record MatchStatsCache(
    string Season,
    [property: JsonConverter(typeof(UtcTimestampConverter))] DateTimeOffset UpdatedUtc,
    [property: JsonConverter(typeof(UtcTimestampConverter))] DateTimeOffset LastSweepUtc,
    IReadOnlyDictionary<string, CachedFixture> Fixtures)
{
    public static MatchStatsCache Empty(string season) =>
        new(season, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            new Dictionary<string, CachedFixture>(StringComparer.Ordinal));

    /// <summary>Season totals per athlete id, summed across every cached fixture.</summary>
    public Dictionary<string, (int Goals, int Assists)> SeasonTotals()
    {
        var totals = new Dictionary<string, (int Goals, int Assists)>(StringComparer.Ordinal);

        foreach (var fixture in Fixtures.Values)
        {
            foreach (var (athleteId, line) in fixture.Players)
            {
                var running = totals.GetValueOrDefault(athleteId);
                totals[athleteId] = (running.Goals + line.Goals, running.Assists + line.Assists);
            }
        }

        return totals;
    }
}

/// <summary>
/// <paramref name="FirstSeenUtc"/> drives the re-read window. Opta reassigns goals and assists
/// after the fact -- a deflection reclassified, an assist withdrawn -- so a fixture is re-read
/// for a week after we first saw it, and only then treated as settled.
/// <para>
/// <paramref name="LastReadUtc"/> throttles that: a fixture still in play is re-read every run,
/// but one that finished hours ago only needs checking hourly for corrections.
/// </para>
/// </summary>
internal sealed record CachedFixture(
    [property: JsonConverter(typeof(UtcTimestampConverter))] DateTimeOffset FirstSeenUtc,
    [property: JsonConverter(typeof(UtcTimestampConverter))] DateTimeOffset LastReadUtc,
    IReadOnlyDictionary<string, CachedPlayerMatch> Players);

internal sealed record CachedPlayerMatch(int Goals, int Assists);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(MatchStatsCache))]
internal sealed partial class MatchStatsCacheJsonContext : JsonSerializerContext;

internal static class MatchStatsCacheFile
{
    private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

    public static async Task<MatchStatsCache> ReadAsync(string path, string season, TextWriter log, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return MatchStatsCache.Empty(season);
        }

        try
        {
            var cache = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(path, ct),
                MatchStatsCacheJsonContext.Default.MatchStatsCache);

            if (cache is null)
            {
                return MatchStatsCache.Empty(season);
            }

            // A season rollover invalidates everything; start again rather than mixing seasons.
            if (!string.Equals(cache.Season, season, StringComparison.Ordinal))
            {
                log.WriteLine($"  cache is for season {cache.Season}, not {season}; starting a fresh one");
                return MatchStatsCache.Empty(season);
            }

            return cache;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            log.WriteLine($"  ! the match cache could not be read ({ex.Message}); rebuilding from scratch");
            return MatchStatsCache.Empty(season);
        }
    }

    public static async Task WriteAsync(string path, MatchStatsCache cache, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var typeInfo = (JsonTypeInfo<MatchStatsCache>)WriteOptions.GetTypeInfo(typeof(MatchStatsCache));
        var json = JsonSerializer.Serialize(cache, typeInfo);

        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, json + Environment.NewLine, ct);
        File.Move(temp, path, overwrite: true);
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        var options = new JsonSerializerOptions(MatchStatsCacheJsonContext.Default.Options)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.MakeReadOnly();
        return options;
    }
}
