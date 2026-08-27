namespace Sweepstake.Web;

/// <summary>
/// Deliberate, temporary hold on the boards.
///
/// While <see cref="StallOnLoad"/> is true the page renders its shell and then sits on
/// "Loading the boards…" forever — the shape a genuinely stalled data feed has, since the
/// static assets come from Pages and the numbers do not.
///
/// Nothing else is touched. picks.json and stats.json are unchanged and the update-stats and
/// matchday workflows keep refreshing them, so setting this back to false and pushing brings
/// the real board back with current numbers, roughly two minutes later. This flag is
/// intentionally the only thing that has to change.
///
/// Not a const: a compile-time constant makes the code after the early return unreachable,
/// which is CS0162, which is an error under warnings-as-errors in CI.
/// </summary>
internal static class AppMode
{
    internal static readonly bool StallOnLoad = true;
}
