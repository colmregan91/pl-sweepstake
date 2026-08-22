namespace Sweepstake.StatsFetcher;

/// <summary>
/// Locates the two data files relative to the repository root, so the tool works the same
/// whether it is launched from the repo root, from its own project directory, or by CI.
/// </summary>
internal sealed record RepoPaths(string Root)
{
    public string PicksJson => Path.Combine(Root, "data", "picks.json");

    public string StatsJson =>
        Path.Combine(Root, "src", "Sweepstake.Web", "wwwroot", "data", "stats.json");

    /// <summary>
    /// Per-fixture results, keyed by event id. Committed but never served to the browser:
    /// it exists so a finished match is fetched once rather than on every run.
    /// </summary>
    public string MatchStatsJson => Path.Combine(Root, "data", "match-stats.json");

    public static RepoPaths Discover()
    {
        // Walk up from the binary rather than trusting the working directory.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "picks.json")))
            {
                return new RepoPaths(dir.FullName);
            }
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root: no data/picks.json found walking up from {AppContext.BaseDirectory}.");
    }

    public string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');
}
