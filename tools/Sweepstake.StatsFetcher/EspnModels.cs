using System.Text.Json.Serialization;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// Just enough of ESPN's core API to do the job. The core API is $ref-heavy: list endpoints
/// hand back links rather than objects, and ids have to be parsed back out of the URLs.
/// </summary>
internal sealed record EspnRef([property: JsonPropertyName("$ref")] string Ref)
{
    /// <summary>
    /// Pulls the trailing id out of a $ref such as
    /// ".../seasons/2026/teams/364?lang=en&amp;region=us" -> "364".
    /// </summary>
    public string Id => LastPathSegment(Ref);

    public static string LastPathSegment(string url)
    {
        var path = url.AsSpan();

        var query = path.IndexOfAny('?', '#');
        if (query >= 0)
        {
            path = path[..query];
        }

        path = path.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path.ToString() : path[(slash + 1)..].ToString();
    }
}

internal sealed record EspnAthlete(
    string? Id,
    string? DisplayName,
    string? FullName,
    bool? Active,
    EspnRef? Team);

internal sealed record EspnTeam(string? Id, string? DisplayName);

/// <summary>One category of the season leaders payload.</summary>
internal sealed record EspnLeaderCategory(
    string? Name,
    string? DisplayName,
    IReadOnlyList<EspnLeader>? Leaders);

internal sealed record EspnLeader(double Value, EspnRef? Athlete);

internal sealed record EspnLeadersResponse(IReadOnlyList<EspnLeaderCategory>? Categories);

/// <summary>
/// One athlete's fixture list for the season. This is the live path: the season rollup
/// (types/0) lags a matchday by hours, but each entry here points at per-fixture statistics
/// that appear within minutes of full time.
/// </summary>
internal sealed record EspnEventLog(EspnEventLogEvents? Events);

internal sealed record EspnEventLogEvents(int Count, IReadOnlyList<EspnEventLogEntry>? Items);

internal sealed record EspnEventLogEntry(
    EspnRef? Event,
    EspnRef? Competition,
    EspnRef? Statistics,
    string? TeamId,
    bool Played);

/// <summary>
/// The per-athlete season totals used to spot-check the leaders payload. Stats are selected by
/// exact <c>name</c>: the offensive category also carries shotAssists, secondAssists and
/// gameWinningAssists, so anything looser would pick up the wrong number.
/// </summary>
internal sealed record EspnStatisticsResponse(EspnSplits? Splits)
{
    public double? Find(string categoryName, string statName) =>
        Splits?.Categories?
            .FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal))?
            .Stats?
            .FirstOrDefault(s => string.Equals(s.Name, statName, StringComparison.Ordinal))?
            .Value;
}

internal sealed record EspnSplits(IReadOnlyList<EspnStatCategory>? Categories);

internal sealed record EspnStatCategory(string? Name, IReadOnlyList<EspnStat>? Stats);

internal sealed record EspnStat(string? Name, double Value);
