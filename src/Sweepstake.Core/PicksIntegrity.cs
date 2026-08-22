namespace Sweepstake.Core;

/// <summary>
/// Structural checks on picks.json. The file is hand-maintained, so a fat-fingered edit is the
/// realistic failure mode. These checks are cheap and they turn a silently-wrong scoreboard
/// into a failed build.
/// </summary>
public static class PicksIntegrity
{
    public const int ExpectedPicksPerList = 3;

    /// <summary>Returns a human-readable description of every problem found. Empty means clean.</summary>
    public static IReadOnlyList<string> Validate(PicksFile picks)
    {
        ArgumentNullException.ThrowIfNull(picks);

        var problems = new List<string>();
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entrant in picks.Entrants)
        {
            CheckList(entrant, Board.Goals, entrant.Goalscorers, picks, referenced, problems);
            CheckList(entrant, Board.Assists, entrant.Assisters, picks, referenced, problems);
        }

        // A registry entry nobody picked means either a stale hand-edit or a pick that lost its
        // id. Either way it is worth knowing about, and it keeps the fetcher from pulling stats
        // for players no board will ever show.
        foreach (var id in picks.Players.Keys.Where(id => !referenced.Contains(id)).Order(StringComparer.Ordinal))
        {
            problems.Add(
                $"Registry entry \"{id}\" ({picks.Players[id].Name}) is not picked by any entrant.");
        }

        return problems;
    }

    /// <summary>Throws <see cref="InvalidDataException"/> listing every problem, if there are any.</summary>
    public static void ThrowIfInvalid(PicksFile picks)
    {
        var problems = Validate(picks);
        if (problems.Count == 0)
        {
            return;
        }

        throw new InvalidDataException(
            $"picks.json failed {problems.Count} integrity check(s):{Environment.NewLine}  - " +
            string.Join($"{Environment.NewLine}  - ", problems));
    }

    private static void CheckList(
        Entrant entrant,
        Board board,
        IReadOnlyList<Pick> selections,
        PicksFile picks,
        HashSet<string> referenced,
        List<string> problems)
    {
        if (selections.Count != ExpectedPicksPerList)
        {
            problems.Add(
                $"Entrant \"{entrant.Name}\" has {selections.Count} {board} pick(s), expected {ExpectedPicksPerList}.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pick in selections)
        {
            referenced.Add(pick.EspnId);

            if (!picks.Players.ContainsKey(pick.EspnId))
            {
                problems.Add(
                    $"Entrant \"{entrant.Name}\" has a {board} pick with ESPN id \"{pick.EspnId}\", " +
                    "which is not in the players registry.");
            }

            // The same player twice in one list would double-count. Across the two boards is
            // fine and common -- Cole Palmer is picked for both by several entrants.
            if (!seen.Add(pick.EspnId))
            {
                problems.Add(
                    $"Entrant \"{entrant.Name}\" picks ESPN id \"{pick.EspnId}\" twice in their {board} list.");
            }

            try
            {
                Odds.ParseNumerator(pick.Odds);
            }
            catch (FormatException ex)
            {
                problems.Add(
                    $"Entrant \"{entrant.Name}\" has a malformed {board} odds value " +
                    $"for ESPN id \"{pick.EspnId}\". {ex.Message}");
            }
        }
    }
}
