using System.Text.Json.Serialization;

namespace Sweepstake.StatsFetcher;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EspnAthlete))]
[JsonSerializable(typeof(EspnTeam))]
[JsonSerializable(typeof(EspnLeadersResponse))]
[JsonSerializable(typeof(EspnStatisticsResponse))]
[JsonSerializable(typeof(EspnEventLog))]
internal sealed partial class EspnJsonContext : JsonSerializerContext;
