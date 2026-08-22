using System.Globalization;
using Sweepstake.Core;

namespace Sweepstake.Core.Tests;

/// <summary>
/// Small builders so the tests below read as the scenario they describe rather than as
/// object construction. Everything here is synthetic -- no file, no network.
/// </summary>
internal static class Fixtures
{
    private static readonly DateTimeOffset Stamp =
        DateTimeOffset.Parse("2026-08-22T18:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>A registry covering exactly the ids given, with throwaway display names.</summary>
    public static Dictionary<string, Player> Registry(params string[] ids) =>
        ids.ToDictionary(id => id, id => new Player($"Player {id}", "Some FC"), StringComparer.Ordinal);

    public static PicksFile Picks(IReadOnlyDictionary<string, Player> registry, params Entrant[] entrants) =>
        new("2026", "2026/27", registry, entrants);

    public static Entrant Entrant(
        string name,
        (string Id, string Odds)[] goalscorers,
        (string Id, string Odds)[] assisters) =>
        new(
            name,
            goalscorers.Select(p => new Pick(p.Id, p.Odds)).ToArray(),
            assisters.Select(p => new Pick(p.Id, p.Odds)).ToArray());

    /// <summary>Stats for exactly the ids given. Anything omitted is absent, which scores 0.</summary>
    public static StatsFile Stats(params (string Id, int Goals, int Assists)[] rows) =>
        new(
            Stamp,
            "2026",
            "test",
            rows.ToDictionary(
                r => r.Id,
                r => new PlayerStat($"Player {r.Id}", r.Goals, r.Assists),
                StringComparer.Ordinal));
}
