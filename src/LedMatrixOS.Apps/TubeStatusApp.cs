using System.Numerics;
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
/// The all-lines status board. Every line is a tall tile in its own colour across the top: calm and tick-marked when running well, bright,
/// breathing and framed when not. Underneath, a pager slides through each disruption (line, status, reason) or settles on a green all-clear.
/// </summary>
public class TubeStatusApp : WidgetApp
{
    public override string Id => "tube-status";
    public override string Name => "Tube Status";
    public override int FrameRate => 30;

    private const int TileHeight = 40, MaxSingleRow = 14, CompactRowHeight = 20;
    private static readonly IReadOnlyList<LineStatus> NoStatuses = [];

    [Setting("Lines", Description = "Which services to show.", Options = ["Tube", "All London rail"])]
    public string Lines { get; set; } = "Tube";

    [Setting("Page Seconds", Description = "How long each disruption stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    private readonly TflApi _api;
    private volatile ILiveData<LineStatus[]>? _data;
    private CancellationTokenSource? _pollCts;
    private LineStatus[]? _seen;
    private IReadOnlyList<StatusCard> _cards = [StatusCard.AllGood];
    private LineStatus[] _row1 = [], _row2 = [];
    private int _width1 = 22, _width2 = 22, _rowHeight = TileHeight;
    private Pager? _pager;
    private ListView<LineStatus>? _rowA, _rowB;
    private Node? _tiles, _cardsArea;
    private StateScreen? _state;
    private bool _entered, _tilesShown;

    public TubeStatusApp() : this(new HttpClient()) { }

    public TubeStatusApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _api = new TflApi(httpClient);
    }

    protected override Node Build()
    {
        var styles = new BoardStyles();

        _rowA = TileRow(() => _row1, () => _width1);
        _rowB = TileRow(() => _row2, () => _width2);
        _rowB.Visible = false;
        var tiles = new Stack(Orientation.Vertical, gap: 1) { HAlign = Align.Stretch, VAlign = Align.Start, Children = { _rowA, _rowB } };

        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Up) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1 }
            .Bind(() => _cards, card => StatusCards.Build(card, styles));

        _tiles = tiles;
        _cardsArea = _pager;
        _state = new StateScreen(Fonts.Big, Fonts.Small);
        var board = new Stack(Orientation.Vertical, gap: 1) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { tiles, _pager } };
        return new Panel { board, _state };
    }

    // One row of tiles (a second row appears above MaxSingleRow lines); width and height follow the layout computed in Update.
    private ListView<LineStatus> TileRow(Func<IReadOnlyList<LineStatus>?> source, Func<int> width) =>
        new(source, s => new StatusTile(s, Fonts.QuiteSmall, width(), _rowHeight), s => s.LineId)
        {
            Orientation = Orientation.Horizontal, Gap = 1, HAlign = Align.Stretch, Height = _rowHeight, CrossAlign = Align.Start, EnterOffset = 0,
            ItemChanged = (n, s) => { var t = (StatusTile)n; t.Apply(s); t.Width = width(); t.Height = _rowHeight; },
        };

    /// <summary>Tile width so that <paramref name="count"/> tiles and their 1px gaps never exceed <paramref name="total"/> pixels.</summary>
    internal static int TileWidth(int count, int total = 256) => Math.Clamp((total - (count - 1)) / Math.Max(1, count), 3, 30);

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;
        var data = _data;
        var statuses = data?.Value;
        if (!ReferenceEquals(statuses, _seen))
        {
            _seen = statuses;
            var all = statuses ?? [];
            int split = all.Length > MaxSingleRow ? (all.Length + 1) / 2 : all.Length;
            _row1 = all.Take(split).ToArray();
            _row2 = all.Skip(split).ToArray();
            bool two = _row2.Length > 0;
            _rowHeight = two ? CompactRowHeight : TileHeight;
            _width1 = TileWidth(_row1.Length);
            _width2 = TileWidth(_row2.Length);
            _rowA!.Height = _rowB!.Height = _rowHeight;
            _rowB.Visible = two;
            var cards = (statuses ?? []).Where(s => s.Health != Health.Good).OrderByDescending(s => s.Health).Select(s => new StatusCard(s)).ToList();
            _cards = cards.Count > 0 ? cards : [StatusCard.AllGood];
        }

        bool ready = statuses is not null;
        _state!.State = data is { Value: null, Error: not null } ? BoardState.Offline : BoardState.Loading;
        _state.Detail = "Fetching line status";
        _state.Visible = !ready;
        _tiles!.Visible = _cardsArea!.Visible = ready;

        base.Update(context, cancellationToken);

        if (ready && !_tilesShown) DropTilesIn();
        _tilesShown = ready;
        if (!_entered)
        {
            _entered = true;
            Motion.SlideIn(_state, Animator, new Vector2(-120, 0), TimeSpan.Zero, 500.Ms(), Easing.OutBack);
        }
    }

    // The tiles fall in one after another, each landing with a little overshoot.
    private void DropTilesIn()
    {
        int count = 0;
        foreach (var row in new[] { _rowA!, _rowB! })
        {
            var children = row.Children;
            for (int i = 0; i < children.Count; i++, count++)
                Motion.SlideIn(children[i], Animator, new Vector2(0, -TileHeight - 4), TimeSpan.FromMilliseconds(40 * count), 420.Ms(), Easing.OutBack);
        }
        Motion.SlideIn(_cardsArea!, Animator, new Vector2(0, 30), TimeSpan.FromMilliseconds(40 * count + 60), 400.Ms());
    }

    protected override void OnSettingChanged(string key)
    {
        if (key == "pageSeconds" && _pager is not null) _pager.Interval = PageSeconds.Seconds();
        if (key == "lines") RestartPolling();
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _api.AppKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_api.AppKey)) Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");
        _entered = _tilesShown = false;
        _seen = null;
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    private void RestartPolling()
    {
        var modes = Lines == "Tube" ? "tube" : "tube,dlr,overground,elizabeth-line";
        _data = RestartPoll(ref _pollCts, TimeSpan.FromMinutes(5), ct => _api.GetModeStatusesAsync(modes, ct));
    }

    internal Pager? CardPager => _pager;
    internal IEnumerable<Node> TileNodes => _rowA is null ? [] : _rowA.Children.Concat(_rowB!.Children);
    internal int CardAreaHeight => _cardsArea?.Bounds.Height ?? 0;

    /// <summary>Test seam: replaces the polled data (call before the first frame).</summary>
    internal void UseData(ILiveData<LineStatus[]>? data) => _data = data;
}

