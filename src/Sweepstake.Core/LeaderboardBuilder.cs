namespace Sweepstake.Core;

/// <summary>
/// Joins picks to stats and ranks the entrants. Pure: data in, data out, no I/O.
/// <para>
/// The join is integer-keyed id to id. There is no string comparison of player names anywhere
/// in here, and there must never be -- see the rules in CLAUDE.md.
/// </para>
/// </summary>
public static class LeaderboardBuilder
{
    public static IReadOnlyList<ScoredEntrant> Build(PicksFile picks, StatsFile stats, Board board)
    {
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(stats);

        var scored = new List<ScoredEntrant>(picks.Entrants.Count);

        foreach (var entrant in picks.Entrants)
        {
            var selections = board == Board.Goals ? entrant.Goalscorers : entrant.Assisters;
            var scoredPicks = new List<ScoredPick>(selections.Count);
            var total = 0;
            var oddsTotal = 0;

            foreach (var pick in selections)
            {
                // A pick referencing an id the registry does not know about is a bad hand-edit,
                // not a data gap. Fail loudly.
                if (!picks.Players.TryGetValue(pick.EspnId, out var player))
                {
                    throw new KeyNotFoundException(
                        $"Entrant \"{entrant.Name}\" has a {board} pick with ESPN id \"{pick.EspnId}\", " +
                        "which is not present in the players registry of picks.json.");
                }

                // Absent from stats.json just means nobody has recorded one yet -- injured, or
                // simply yet to score. Zero, not an error.
                var value = stats.Players.TryGetValue(pick.EspnId, out var stat)
                    ? board == Board.Goals ? stat.Goals : stat.Assists
                    : 0;

                total += value;
                oddsTotal += ParseOdds(entrant, board, pick);
                scoredPicks.Add(new ScoredPick(pick.EspnId, player, pick.Odds, value));
            }

            scored.Add(new ScoredEntrant(entrant.Name, scoredPicks, total, oddsTotal, Rank: 0));
        }

        // Highest total first, entrant name ascending as a deterministic tiebreak. Ordinal so
        // the order does not shift with the machine's culture.
        scored.Sort(static (a, b) =>
        {
            var byTotal = b.Total.CompareTo(a.Total);
            return byTotal != 0 ? byTotal : string.CompareOrdinal(a.Name, b.Name);
        });

        return AssignCompetitionRanks(scored);
    }

    /// <summary>
    /// Competition ranking: equal totals share a rank and the next distinct total skips the
    /// ranks the tie consumed, giving 1, 2, 2, 4 rather than 1, 2, 3, 4.
    /// </summary>
    private static List<ScoredEntrant> AssignCompetitionRanks(List<ScoredEntrant> sorted)
    {
        var ranked = new List<ScoredEntrant>(sorted.Count);
        var rank = 0;
        int? previousTotal = null;

        for (var i = 0; i < sorted.Count; i++)
        {
            if (previousTotal is null || sorted[i].Total != previousTotal)
            {
                rank = i + 1;
            }

            previousTotal = sorted[i].Total;
            ranked.Add(sorted[i] with { Rank = rank });
        }

        return ranked;
    }

    private static int ParseOdds(Entrant entrant, Board board, Pick pick)
    {
        try
        {
            return Odds.ParseNumerator(pick.Odds);
        }
        catch (FormatException ex)
        {
            throw new FormatException(
                $"Entrant \"{entrant.Name}\" has a malformed {board} odds value " +
                $"for ESPN id \"{pick.EspnId}\". {ex.Message}",
                ex);
        }
    }
}
