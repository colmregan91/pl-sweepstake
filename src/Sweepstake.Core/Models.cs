using System.Text.Json.Serialization;

namespace Sweepstake.Core;

/// <summary>Which of the two leaderboards is being scored.</summary>
public enum Board
{
    Goals,
    Assists,
}

/// <summary>
/// A registry entry from picks.json, keyed by ESPN athlete id. <paramref name="Club"/> and
/// <paramref name="SheetName"/> are display-only and must never be used for matching.
/// </summary>
public sealed record Player(string Name, string Club, string? SheetName = null);

/// <summary>
/// One of an entrant's three selections. Deliberately carries no player name -- a pick is an
/// id, and the id is the only thing joined on.
/// </summary>
public sealed record Pick(string EspnId, string Odds);

public sealed record Entrant(
    string Name,
    IReadOnlyList<Pick> Goalscorers,
    IReadOnlyList<Pick> Assisters);

/// <summary>The hand-maintained picks.json. Fixed for the season.</summary>
public sealed record PicksFile(
    string Season,
    string SeasonLabel,
    IReadOnlyDictionary<string, Player> Players,
    IReadOnlyList<Entrant> Entrants);

public sealed record PlayerStat(string Name, int Goals, int Assists);

/// <summary>
/// The generated stats.json. Property order here is the property order in the file -- keep it
/// matching the shape documented in CLAUDE.md.
/// </summary>
public sealed record StatsFile(
    [property: JsonConverter(typeof(UtcTimestampConverter))] DateTimeOffset GeneratedUtc,
    string Season,
    string Source,
    IReadOnlyDictionary<string, PlayerStat> Players);

/// <summary>A pick joined to its registry entry and its score on one board.</summary>
public sealed record ScoredPick(string EspnId, Player Player, string Odds, int Value);

public sealed record ScoredEntrant(
    string Name,
    IReadOnlyList<ScoredPick> Picks,
    int Total,
    int OddsTotal,
    int Rank);
