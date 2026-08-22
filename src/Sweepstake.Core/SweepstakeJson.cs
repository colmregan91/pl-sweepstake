using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Sweepstake.Core;

/// <summary>
/// Source-generated JSON metadata. Blazor WASM trims aggressively, so reflection-based
/// serialization is not an option -- everything goes through this context.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(PicksFile))]
[JsonSerializable(typeof(StatsFile))]
internal sealed partial class SweepstakeJsonContext : JsonSerializerContext;

/// <summary>
/// Reads and writes the two data files. Takes and returns strings rather than paths -- Core
/// stays I/O-free, so callers own the file system (the fetcher) or the network (the web app).
/// </summary>
public static class SweepstakeJson
{
    // Player names carry accents (Šeško, Ødegaard, Gyökeres, Groß, Guimarães, Muñoz, João).
    // The default encoder would escape those to \uXXXX, which is valid JSON but makes the
    // committed stats.json unreadable in a diff. Relaxed escaping keeps them as UTF-8.
    private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

    public static PicksFile ReadPicks(string json) =>
        JsonSerializer.Deserialize(json, SweepstakeJsonContext.Default.PicksFile)
        ?? throw new InvalidDataException("picks.json deserialized to null.");

    public static StatsFile ReadStats(string json) =>
        JsonSerializer.Deserialize(json, SweepstakeJsonContext.Default.StatsFile)
        ?? throw new InvalidDataException("stats.json deserialized to null.");

    public static string WriteStats(StatsFile stats)
    {
        var typeInfo = (JsonTypeInfo<StatsFile>)WriteOptions.GetTypeInfo(typeof(StatsFile));
        return JsonSerializer.Serialize(stats, typeInfo);
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        var options = new JsonSerializerOptions(SweepstakeJsonContext.Default.Options)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.MakeReadOnly();
        return options;
    }
}
