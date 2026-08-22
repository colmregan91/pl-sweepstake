using Sweepstake.Core;

namespace Sweepstake.StatsFetcher;

/// <summary>
/// Maintenance command. Confirms that the athlete ids committed in picks.json still point at
/// the players we think they do.
/// <para>
/// This is the one place in the codebase that compares player names, and it is not a match: it
/// is a diff, printed for a human to read. Nothing here feeds the scoreboard and nothing here
/// edits picks.json. Run it after a transfer window, not in the deploy path.
/// </para>
/// </summary>
internal static class VerifyCommand
{
    public static async Task<int> RunAsync(RepoPaths paths, CancellationToken ct)
    {
        var picks = SweepstakeJson.ReadPicks(await File.ReadAllTextAsync(paths.PicksJson, ct));
        PicksIntegrity.ThrowIfInvalid(picks);

        Console.WriteLine($"verify - {picks.Players.Count} registry ids against ESPN season {picks.Season}");
        Console.WriteLine($"  registry: {paths.Relative(paths.PicksJson)}");
        Console.WriteLine();

        using var espn = new EspnClient(picks.Season, Console.Out);
        var teams = new TeamNameCache(espn);

        var ordered = picks.Players
            .OrderBy(kv => kv.Value.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToArray();

        var results = await Task.WhenAll(ordered.Select(kv => CheckAsync(espn, teams, kv.Key, kv.Value, ct)));

        foreach (var result in results)
        {
            Console.WriteLine(Format(result));
        }

        return Summarise(results, espn.RequestCount);
    }

    private static async Task<CheckResult> CheckAsync(
        EspnClient espn,
        TeamNameCache teams,
        string id,
        Player registry,
        CancellationToken ct)
    {
        var athlete = await espn.GetOrNullAsync(espn.AthleteUrl(id), EspnJsonContext.Default.EspnAthlete, ct);

        if (athlete is null)
        {
            // A 404 means the player has left the league or ESPN has retired the id. Either
            // way the committed id is now wrong and somebody has to look at it.
            return new CheckResult(id, registry, EspnName: null, EspnClub: null, Active: null, Missing: true);
        }

        var club = athlete.Team is { } team
            ? await teams.GetDisplayNameAsync(team.Id, ct)
            : null;

        return new CheckResult(id, registry, athlete.DisplayName, club, athlete.Active, Missing: false);
    }

    private static string Format(CheckResult r)
    {
        var note = r.Missing
            ? "404 - id no longer resolves for this season"
            : string.Join("; ", Notes(r));

        return $"  {r.Status,-9} {r.Id,-7}  {Pad(r.Registry.Name, 24)}  {Pad(r.EspnClub ?? "-", 24)}  {note}";
    }

    private static IEnumerable<string> Notes(CheckResult r)
    {
        if (r.NameIsTransliteration)
        {
            yield return $"espn spells this \"{r.EspnName}\"";
        }
        else if (r.NameConflicts)
        {
            yield return $"espn name \"{r.EspnName ?? "(none)"}\" != registry \"{r.Registry.Name}\"";
        }

        if (r.ClubDiffers)
        {
            yield return $"espn club \"{r.EspnClub ?? "(none)"}\" != registry \"{r.Registry.Club}\"";
        }

        if (r.Active == false)
        {
            yield return "espn marks this athlete inactive";
        }
    }

    private static int Summarise(IReadOnlyList<CheckResult> results, int requestCount)
    {
        var missing = results.Count(r => r.Missing);
        var nameConflicts = results.Count(r => r.NameConflicts);
        var spelling = results.Count(r => r.NameIsTransliteration);
        var clubDiffs = results.Count(r => r.ClubDiffers);
        var inactive = results.Count(r => r is { Missing: false, Active: false });
        var ok = results.Count(r => r.Ok);

        Console.WriteLine();
        Console.WriteLine($"{results.Count} checked in {requestCount} requests");
        Console.WriteLine($"  {ok,3} ok");
        Console.WriteLine($"  {spelling,3} transliteration only (ESPN drops the diacritics)");
        Console.WriteLine($"  {nameConflicts,3} name disagreement(s)");
        Console.WriteLine($"  {clubDiffs,3} club disagreement(s)");
        Console.WriteLine($"  {inactive,3} marked inactive by ESPN");
        Console.WriteLine($"  {missing,3} missing (404)");
        Console.WriteLine();

        if (missing > 0)
        {
            Console.Error.WriteLine(
                $"FAIL: {missing} id(s) no longer resolve. Those players have left the league or ESPN " +
                "has retired the id. Resolve the correct ids by hand and update data/picks.json.");
            return 1;
        }

        if (nameConflicts + clubDiffs + inactive > 0)
        {
            Console.WriteLine(
                "Disagreements are not automatically errors - a club change is just a transfer. Review " +
                "the rows above and edit data/picks.json by hand if any of them is genuinely wrong.");
        }
        else
        {
            Console.WriteLine("All ids resolve to the expected player at the expected club.");
        }

        if (spelling > 0)
        {
            Console.WriteLine(
                $"The {spelling} \"spelling\" row(s) are expected and need no action: ESPN stores those " +
                "names without their diacritics, and picks.json keeps them deliberately.");
        }

        Console.WriteLine("Nothing was written. verify never edits picks.json.");
        return 0;
    }

    /// <summary>Pads to a fixed column, counting text elements so accents do not skew the width.</summary>
    private static string Pad(string value, int width)
    {
        var text = value.Length <= width ? value : string.Concat(value.AsSpan(0, width - 1), "…");
        return text.PadRight(width);
    }

    private sealed record CheckResult(
        string Id,
        Player Registry,
        string? EspnName,
        string? EspnClub,
        bool? Active,
        bool Missing)
    {
        private bool NameDiffers =>
            !Missing && !string.Equals(EspnName, Registry.Name, StringComparison.Ordinal);

        /// <summary>Same player, different transliteration. Expected, and not actionable.</summary>
        public bool NameIsTransliteration =>
            NameDiffers && NameFold.SameApartFromSpelling(EspnName, Registry.Name);

        /// <summary>A different name entirely. Somebody needs to look at this.</summary>
        public bool NameConflicts => NameDiffers && !NameIsTransliteration;

        public bool ClubDiffers =>
            !Missing && !string.Equals(EspnClub, Registry.Club, StringComparison.Ordinal);

        public bool Ok => !Missing && !NameDiffers && !ClubDiffers && Active != false;

        public string Status => this switch
        {
            { Missing: true } => "MISSING",
            { NameConflicts: true, ClubDiffers: true } => "NAME+CLUB",
            { NameConflicts: true } => "NAME",
            { ClubDiffers: true } => "CLUB",
            { Active: false } => "INACTIVE",
            { NameIsTransliteration: true } => "spelling",
            _ => "ok",
        };
    }
}
