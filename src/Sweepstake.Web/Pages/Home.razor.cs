using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Sweepstake.Core;
using Sweepstake.Web.Components;

namespace Sweepstake.Web.Pages;

public sealed partial class Home : IAsyncDisposable
{
    /// <summary>How often to look for a new stats.json while the tab is in the foreground.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    /// <summary>How long a changed value stays highlighted.</summary>
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(8);

    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<string> _changedGoals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedAssists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _goalRankMoves = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _assistRankMoves = new(StringComparer.Ordinal);

    private PicksFile? _picks;
    private StatsFile? _stats;
    private IReadOnlyList<ScoredEntrant> _goals = [];
    private IReadOnlyList<ScoredEntrant> _assists = [];

    private bool _loaded;
    private bool _haveRealStats;
    private string? _loadError;
    private bool _tabVisible = true;
    private int _highlightGeneration;

    private IJSObjectReference? _visibility;
    private DotNetObjectReference<Home>? _self;

    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    private string SeasonLabel => _picks?.SeasonLabel ?? string.Empty;

    private string UpdatedText => _stats is null || !_haveRealStats
        ? "Awaiting the first stats update."
        : $"Last updated {_stats.GeneratedUtc.ToLocalTime().ToString("HH:mm 'on' d MMM yyyy", CultureInfo.CurrentCulture)}.";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _picks = SweepstakeJson.ReadPicks(await Http.GetStringAsync("data/picks.json", _shutdown.Token));
        }
        catch (Exception ex)
        {
            // Without picks there is no board at all, so this one is worth surfacing.
            _loadError = ex.Message;
            return;
        }

        // A missing stats.json is survivable: render everyone on zero and say so.
        var stats = await TryLoadStatsAsync();
        _haveRealStats = stats is not null;
        Rebuild(stats ?? EmptyStats(_picks.Season), highlightChanges: false);
        _loaded = true;

        _ = PollAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            // No CancellationToken on these: the overload that takes one is easy to bind to by
            // accident, and the token then gets serialized as a JS argument and blows up.
            _visibility = await Js.InvokeAsync<IJSObjectReference>("import", "./js/visibility.js");
            _self = DotNetObjectReference.Create(this);
            _tabVisible = await _visibility.InvokeAsync<bool>("subscribe", _self);
        }
        catch (Exception ex) when (ex is JSException or TaskCanceledException or OperationCanceledException)
        {
            // Without the hook the timer simply polls regardless. Not worth failing over.
            Console.Error.WriteLine($"visibility hook unavailable: {ex.Message}");
        }
    }

    /// <summary>Called from JS when the tab is shown or hidden.</summary>
    [JSInvokable]
    public async Task OnVisibilityChanged(bool visible)
    {
        _tabVisible = visible;

        // Coming back to a tab that has been parked for a while: catch up straight away.
        if (visible)
        {
            await RefreshAsync();
        }
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
            {
                // No point polling a background tab; OnVisibilityChanged catches it up.
                if (_tabVisible)
                {
                    await RefreshAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Page is going away.
        }
    }

    private async Task RefreshAsync()
    {
        var stats = await TryLoadStatsAsync();

        // A failed refresh keeps the last good board on screen and tries again next tick.
        if (stats is null)
        {
            return;
        }

        // Nothing new upstream: do not recompute and do not re-render.
        if (_stats is not null && stats.GeneratedUtc == _stats.GeneratedUtc)
        {
            return;
        }

        _haveRealStats = true;
        Rebuild(stats, highlightChanges: true);
        await InvokeAsync(StateHasChanged);
        _ = ClearHighlightsLaterAsync();
    }

    private async Task<StatsFile?> TryLoadStatsAsync()
    {
        try
        {
            // GitHub Pages caches hard; without the query the browser serves the same bytes back.
            var url = $"data/stats.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            return SweepstakeJson.ReadStats(await Http.GetStringAsync(url, _shutdown.Token));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"stats refresh failed, keeping the last good board: {ex.Message}");
            return null;
        }
    }

    private void Rebuild(StatsFile stats, bool highlightChanges)
    {
        if (_picks is null)
        {
            return;
        }

        var goals = LeaderboardBuilder.Build(_picks, stats, Board.Goals);
        var assists = LeaderboardBuilder.Build(_picks, stats, Board.Assists);

        if (highlightChanges)
        {
            Diff(_goals, goals, _changedGoals, _goalRankMoves);
            Diff(_assists, assists, _changedAssists, _assistRankMoves);
        }

        _goals = goals;
        _assists = assists;
        _stats = stats;
    }

    /// <summary>Records which values moved and which entrants changed rank, for the animations.</summary>
    private static void Diff(
        IReadOnlyList<ScoredEntrant> before,
        IReadOnlyList<ScoredEntrant> after,
        HashSet<string> changedValues,
        Dictionary<string, int> rankMoves)
    {
        changedValues.Clear();
        rankMoves.Clear();

        if (before.Count == 0)
        {
            return;
        }

        var previous = before.ToDictionary(e => e.Name, StringComparer.Ordinal);

        foreach (var entrant in after)
        {
            if (!previous.TryGetValue(entrant.Name, out var was))
            {
                continue;
            }

            if (was.Rank != entrant.Rank)
            {
                rankMoves[entrant.Name] = entrant.Rank - was.Rank;
            }

            var wasByPick = was.Picks.ToDictionary(p => p.EspnId, p => p.Value, StringComparer.Ordinal);

            foreach (var pick in entrant.Picks)
            {
                if (wasByPick.TryGetValue(pick.EspnId, out var oldValue) && oldValue != pick.Value)
                {
                    changedValues.Add(BoardPanel.ValueKey(entrant.Name, pick.EspnId));
                }
            }
        }
    }

    private async Task ClearHighlightsLaterAsync()
    {
        var generation = ++_highlightGeneration;

        try
        {
            await Task.Delay(HighlightDuration, _shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A newer refresh has already re-armed the highlights; leave them alone.
        if (generation != _highlightGeneration)
        {
            return;
        }

        _changedGoals.Clear();
        _changedAssists.Clear();
        _goalRankMoves.Clear();
        _assistRankMoves.Clear();
        await InvokeAsync(StateHasChanged);
    }

    private static StatsFile EmptyStats(string season) =>
        new(DateTimeOffset.UnixEpoch, season, "none", new Dictionary<string, PlayerStat>(StringComparer.Ordinal));

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();

        if (_visibility is not null)
        {
            try
            {
                await _visibility.InvokeVoidAsync("unsubscribe");
                await _visibility.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or OperationCanceledException)
            {
                // The page is unloading; the handler goes with it.
            }
        }

        _self?.Dispose();
        _shutdown.Dispose();
    }
}
