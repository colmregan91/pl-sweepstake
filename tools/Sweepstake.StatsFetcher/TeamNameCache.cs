using System.Collections.Concurrent;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// Resolves team id to club display name, fetching each club at most once. The registry only
/// spans 17 clubs, so lazily dereferencing the ids we actually encounter costs fewer requests
/// than pulling the whole 20-team list and expanding every $ref.
/// </summary>
internal sealed class TeamNameCache(EspnClient espn)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _cache = new(StringComparer.Ordinal);

    public Task<string?> GetDisplayNameAsync(string teamId, CancellationToken ct) =>
        _cache.GetOrAdd(
            teamId,
            id => new Lazy<Task<string?>>(
                () => FetchAsync(id, ct),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private async Task<string?> FetchAsync(string teamId, CancellationToken ct)
    {
        var team = await espn.GetOrNullAsync(espn.TeamUrl(teamId), EspnJsonContext.Default.EspnTeam, ct);
        return team?.DisplayName;
    }
}
