namespace Sweepstake.StatsFetcher;

/// <summary>
/// Pulls the two categories we care about out of the leaders payload.
/// <para>
/// The payload carries twelve categories and two pairs share a <c>displayName</c>:
/// <c>goals</c>/<c>goalsLeaders</c> both display as "Goals", and <c>assists</c>/
/// <c>assistsLeaders</c> both as "Assists". Worse, the <c>*Leaders</c> variants come first in
/// the array, so matching on <c>displayName</c> would reliably select the wrong one. Selection
/// is by exact <c>name</c>, always.
/// </para>
/// </summary>
internal static class LeadersReader
{
    public const string Goals = "goals";
    public const string Assists = "assists";
    public const string GoalsCrossCheck = "goalsLeaders";
    public const string AssistsCrossCheck = "assistsLeaders";

    /// <summary>Reads one category into an athlete-id keyed map. Throws if it is absent.</summary>
    public static Dictionary<string, int> Require(EspnLeadersResponse payload, string categoryName)
    {
        var category = Find(payload, categoryName)
            ?? throw new InvalidDataException(
                $"The ESPN leaders payload has no category named \"{categoryName}\". " +
                $"It offered: {string.Join(", ", CategoryNames(payload))}. " +
                "The payload shape has changed upstream; stats.json was not written.");

        return ToMap(category);
    }

    /// <summary>Reads one category if present. Used for the cross-check, which is advisory.</summary>
    public static Dictionary<string, int>? Optional(EspnLeadersResponse payload, string categoryName)
    {
        var category = Find(payload, categoryName);
        return category is null ? null : ToMap(category);
    }

    public static IEnumerable<string> CategoryNames(EspnLeadersResponse payload) =>
        payload.Categories?.Select(c => c.Name ?? "(unnamed)") ?? [];

    private static EspnLeaderCategory? Find(EspnLeadersResponse payload, string categoryName) =>
        payload.Categories?.FirstOrDefault(
            c => string.Equals(c.Name, categoryName, StringComparison.Ordinal));

    private static Dictionary<string, int> ToMap(EspnLeaderCategory category)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var leader in category.Leaders ?? [])
        {
            if (leader.Athlete is not { } athlete)
            {
                continue;
            }

            var id = athlete.Id;
            if (id.Length == 0)
            {
                continue;
            }

            // Values arrive as doubles ("1.0"). Goals and assists are whole numbers.
            var value = (int)Math.Round(leader.Value, MidpointRounding.AwayFromZero);

            // A duplicate id would mean the payload lists a player twice; keep the larger.
            map[id] = map.TryGetValue(id, out var existing) ? Math.Max(existing, value) : value;
        }

        return map;
    }
}
