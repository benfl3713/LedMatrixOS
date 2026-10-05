using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// TfL road disruptions, worst first. Each page lists four disruptions (severity pill, road, and where/what as a marquee); the pager slides
/// through the pages, or the board settles on a green all-clear. Watch the whole network or pick corridors, and hide the minor ones with
/// the minimum severity. TfL polling lives here, HTTP in <see cref="TflApi"/>.
/// </summary>
public class RoadDisruptionsApp : WidgetApp
{
    public override string Id => "road-disruptions";
    public override string Name => "Road Disruptions";
    public override int FrameRate => 30;

    public const int MaxCorridors = 8, RowsPerPage = 4, MaxDisruptions = 24;

    [Setting("Corridors", Description = "Roads to watch, up to 8. Leave empty to watch every TfL road.", MultiSearch = true, Browse = true, Max = MaxCorridors)]
    public string Corridors { get; set; } = "";

    [Setting("Minimum Severity", Description = "Hide disruptions below this level.", Options = ["Minimal", "Moderate", "Serious", "Severe"])]
    public string MinSeverity { get; set; } = "Serious";

    [Setting("Page Seconds", Description = "How long each page of disruptions stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 8;

    [Setting("Show Description", Description = "Show where and what the disruption is, as well as the road.")]
    public bool ShowDescription { get; set; } = true;

    private readonly TflApi _api;
    private volatile ILiveData<RoadDisruption[]>? _data;
    private CancellationTokenSource? _pollCts;

    private RoadDisruption[]? _seen;
    private RoadSeverity _seenMin;
    private IReadOnlyList<RoadDisruption> _view = [];
    private IReadOnlyList<RoadToken> _rows = [], _heroView = [];
    private bool _shownDescription = true;
    private Pager? _heroPager;
    private string _countText = "", _clearDetail = "";
    private Pager? _pager;
    private Node? _board, _allClear;
    private StateScreen? _state;

    public RoadDisruptionsApp() : this(new HttpClient()) { }

    public RoadDisruptionsApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _api = new TflApi(httpClient);
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = new BoardStyles();

        _pager = new Pager(pageSize: RowsPerPage, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Up) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1 }
            .Bind(() => _rows, t => new RoadRow(t.Disruption, styles, t.ShowDescription));

        // A lone disruption gets the whole panel.
        _heroPager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Up) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1 }
            .Bind(() => _heroView, t => new RoadHero(t.Disruption, styles, t.ShowDescription));

        var header = new Panel
        {
            HAlign = Align.Stretch,
            Height = 12,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(3, 1),
                    Children =
                    {
                        new Pill("ROADS", new Pixel(0, 90, 200)) { Style = new TextStyle(Fonts.ExtraSmall, Pixel.White, Shadow: false), Height = 9 },
                        new Label(() => _countText) { Style = styles.Strip, Grow = 1 },
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                    },
                },
            },
        };

        var list = new Stack(Orientation.Vertical) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { header, _pager, _heroPager } };
        _board = list;

        _allClear = new Stack(Orientation.Horizontal, gap: 8)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            CrossAlign = Align.Center,
            Padding = new Thickness(8, 0),
            Visible = false,
            Children =
            {
                new TickBadge(24),
                new Stack(Orientation.Vertical, gap: 3)
                {
                    VAlign = Align.Center,
                    Grow = 1,
                    Children =
                    {
                        new Label("ALL CLEAR") { Style = new TextStyle(Fonts.Big, LineHealth.GoodColor) },
                        new Label(() => _clearDetail) { Style = styles.Strip },
                    },
                },
            },
        };

        _state = new StateScreen(Fonts.Big, Fonts.Small);
        return new Panel { list, _allClear, _state };
    }

    // ---- per frame --------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        var data = _data;
        var disruptions = data?.Value;
        var min = ParseMin(MinSeverity);
        if (!ReferenceEquals(disruptions, _seen) || min != _seenMin || ShowDescription != _shownDescription)
        {
            _shownDescription = ShowDescription;
            _seen = disruptions;
            _seenMin = min;
            _view = Filter(disruptions ?? [], min);
            var tokens = _view.Select(d => new RoadToken(d, ShowDescription)).ToArray();
            _rows = tokens.Length == 1 ? [] : tokens;
            _heroView = tokens.Length == 1 ? tokens : [];
            _pager!.Visible = _rows.Count > 0;
            _heroPager!.Visible = _heroView.Count > 0;
            _countText = _view.Count == 0 ? "" : _view.Count == 1 ? "1 disruption" : $"{_view.Count} disruptions";
            _clearDetail = ClearDetail(min);
        }

        bool ready = disruptions is not null;
        bool any = ready && _view.Count > 0;
        _state!.State = data is { Value: null, Error: not null } ? BoardState.Offline : BoardState.Loading;
        _state.Detail = "Fetching road status";
        _state.Visible = !ready;
        _board!.Visible = any;
        _allClear!.Visible = ready && !any;

        base.Update(context, cancellationToken);
    }

    internal static RoadSeverity ParseMin(string? text) =>
        Enum.TryParse<RoadSeverity>(text?.Trim(), ignoreCase: true, out var s) && Enum.IsDefined(s) ? s : RoadSeverity.Serious;

    /// <summary>The disruptions at or above <paramref name="min"/>, worst first, at most <see cref="MaxDisruptions"/>.</summary>
    internal static RoadDisruption[] Filter(RoadDisruption[] all, RoadSeverity min) => all
        .Where(d => d.Severity >= min)
        .OrderByDescending(d => d.Severity)
        .ThenBy(d => d.Corridor, StringComparer.OrdinalIgnoreCase)
        .ThenBy(d => d.Id, StringComparer.Ordinal)
        .Take(MaxDisruptions)
        .ToArray();

    private string ClearDetail(RoadSeverity min)
    {
        var scope = ParseCorridors(Corridors).Length > 0 ? "on your roads" : "on TfL roads";
        return min == RoadSeverity.Minimal ? $"No disruptions {scope}" : $"Nothing {min.ToString().ToLowerInvariant()} or worse {scope}";
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        if (key == "corridors") RestartPolling();
        else if (key == "pageSeconds" && _pager is not null) _pager.Interval = PageSeconds.Seconds();
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _api.AppKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_api.AppKey)) Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");
        _seen = null;
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    /// <summary>The configured corridor ids: trimmed, lower case, distinct, at most <see cref="MaxCorridors"/>.</summary>
    internal static string[] ParseCorridors(string? ids) => (ids ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(id => id.ToLowerInvariant())
        .Distinct()
        .Take(MaxCorridors)
        .ToArray();

    private void RestartPolling()
    {
        var corridors = ParseCorridors(Corridors);
        _seen = null;
        _data = RestartPoll(ref _pollCts, TimeSpan.FromMinutes(5), ct => _api.GetRoadDisruptionsAsync(corridors, ct));
    }

    internal Pager? DisruptionPager => _pager;
    internal IReadOnlyList<RoadDisruption> Visible => _view;

    /// <summary>Test seam: replaces the polled data (call before the first frame).</summary>
    internal void UseData(ILiveData<RoadDisruption[]>? data)
    {
        CancelPoll(ref _pollCts);
        _data = data;
    }
}

/// <summary>A disruption plus how it is shown, so toggling the description rebuilds the pages.</summary>
internal readonly record struct RoadToken(RoadDisruption Disruption, bool ShowDescription);
