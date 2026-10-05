using LedMatrixOS.Apps.Rss;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// News headlines from RSS/Atom feeds. Four rows show the current headlines, each with a colour chip for the feed it came from and the
/// text scrolling when it is too long; the page of four changes every few seconds. Feed URLs come from the Feeds setting and, for
/// private ones, from config (<c>Rss:Feeds</c>, comma separated), which is never readable back through the API.
/// </summary>
public sealed class RssTickerApp : WidgetApp
{
    private enum State { Loading, Ready, NoFeeds, Offline }

    public const int Rows = 4;
    public const int MaxFeeds = 6;
    private const int ChipWidth = 24;

    public static readonly Pixel[] FeedColors =
    [
        new(220, 60, 60), new(40, 140, 230), new(60, 180, 90), new(230, 160, 30), new(170, 90, 220), new(40, 190, 190),
    ];

    private IRssSource _source;
    private volatile ILiveData<RssBoard>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active;
    private string _configFeeds = "";
    private bool? _hasFeeds;

    private RssBoard? _boardFor;
    private int _pageApplied = -1;
    private State _state = State.Loading;

    private readonly string[] _rowText = new string[Rows];
    private Pill[] _chips = null!;
    private MarqueeLabel[] _headlines = null!;
    private Node _content = null!;
    private Label _message = null!;
    private TextStyle _headlineStyle = null!;

    public override string Id => "rss-ticker";
    public override string Name => "RSS Ticker";
    public override int FrameRate => 30;

    [Setting("Feeds", Description = "RSS or Atom feed URLs, separated by commas (up to 6).")]
    public string Feeds { get; set; } = "";

    [Setting("Per Feed", Description = "Headlines taken from each feed.", Min = 1, Max = 10)]
    public int PerFeed { get; set; } = 5;

    [Setting("Page Seconds", Description = "Seconds each set of four headlines stays up.", Min = 4, Max = 60)]
    public int PageSeconds { get; set; } = 12;

    [Setting("Scroll Speed", Description = "Pixels per second for long headlines.", Min = 5, Max = 80)]
    public int ScrollSpeed { get; set; } = 28;

    [ActivatorUtilitiesConstructor]
    public RssTickerApp(HttpClient http) : this(new HttpRssSource(http)) { }

    public RssTickerApp(IRssSource source) => _source = source;

    /// <summary>The feed urls in use: the Feeds setting plus the config-only ones, distinct, at most <see cref="MaxFeeds"/>.</summary>
    public IReadOnlyList<string> FeedUrls => (Feeds + "," + _configFeeds)
        .Split([',', '\n', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct()
        .Take(MaxFeeds)
        .ToArray();

    public RssBoard? Current => _data?.Value;

    // ---- lifecycle ---------------------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken ct)
    {
        await base.OnActivatedAsync(dimensions, configuration, ct);
        if (string.Equals(configuration["Rss:Source"], "Fake", StringComparison.OrdinalIgnoreCase)) _source = new FakeRssSource();
        _configFeeds = configuration["Rss:Feeds"] ?? "";
        _hasFeeds = null;
        _active = true;
        StartPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken ct)
    {
        _active = false;
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(ct);
    }

    protected override void OnSettingChanged(string key)
    {
        _hasFeeds = null;
        if (_active && key is "feeds" or "perFeed") StartPolling();
        if (key == "scrollSpeed") ApplySpeed();
        _pageApplied = -1;
    }

    private void StartPolling()
    {
        var urls = FeedUrls;
        int perFeed = PerFeed;
        _boardFor = null;
        _data = urls.Count == 0 ? null : RestartPoll(ref _pollCts, TimeSpan.FromMinutes(10), ct => FetchAsync(urls, perFeed, ct));
        if (urls.Count == 0) CancelPoll(ref _pollCts);
    }

    private async Task<RssBoard> FetchAsync(IReadOnlyList<string> urls, int perFeed, CancellationToken ct)
    {
        var tasks = urls.Select(async url =>
        {
            try { return await _source.GetAsync(url, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }).ToArray();
        var feeds = await Task.WhenAll(tasks);
        if (feeds.All(f => f is null)) throw new HttpRequestException("No feed could be read");
        return RssBoard.Merge(feeds, perFeed);
    }

    /// <summary>Test seam: replaces the polled data with a fixed source (call before the first frame).</summary>
    internal void UseData(ILiveData<RssBoard>? data)
    {
        CancelPoll(ref _pollCts);
        _data = data;
    }

    // ---- view --------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _headlineStyle = new TextStyle(Fonts.Small, new Pixel(235, 235, 240), Shadow: false);
        var chipStyle = new TextStyle(Fonts.QuiteSmall, Pixel.White, Shadow: false);

        _chips = new Pill[Rows];
        _headlines = new MarqueeLabel[Rows];
        var column = new Stack(Orientation.Vertical, gap: 4)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Center,
            Padding = new Thickness(4, 0, 4, 0),
        };

        for (int i = 0; i < Rows; i++)
        {
            int row = i;
            _rowText[i] = "";
            _chips[i] = new Pill("", FeedColors[0], pulse: false) { Style = chipStyle, Width = ChipWidth, Height = 12, VAlign = Align.Center };
            _headlines[i] = new MarqueeLabel(() => _rowText[row])
            {
                Style = _headlineStyle,
                HAlign = Align.Stretch,
                VAlign = Align.Center,
                PauseDuration = TimeSpan.FromSeconds(1.5),
                Grow = 1,
            };
            column.Add(new Stack(Orientation.Horizontal, gap: 5) { HAlign = Align.Stretch, Children = { _chips[i], _headlines[i] } });
        }

        _content = column;
        _message = new Label(() => MessageText()) { Style = new TextStyle(Fonts.Small, new Pixel(255, 176, 0), Shadow: false), HAlign = Align.Center, VAlign = Align.Center };
        _content.Visible = false;
        _state = State.Loading;
        _boardFor = null;
        _pageApplied = -1;
        ApplySpeed();
        return new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _content, _message } };
    }

    private string MessageText() => _state switch
    {
        State.NoFeeds => "No RSS feeds set",
        State.Offline => "Offline, retrying",
        _ => "Loading headlines",
    };

    private void ApplySpeed()
    {
        if (_headlines is null) return;
        foreach (var h in _headlines) h.Speed = ScrollSpeed;
    }

    // ---- per frame ---------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        var data = _data;
        var board = data?.Value;
        var state = !(_hasFeeds ??= FeedUrls.Count > 0) && board is null ? State.NoFeeds
            : board is not null ? State.Ready
            : data?.Error is not null ? State.Offline
            : State.Loading;

        if (!ReferenceEquals(board, _boardFor))
        {
            _boardFor = board;
            _pageApplied = -1;
        }

        int page = 0;
        if (board is { Headlines.Count: > 0 })
        {
            int pages = (board.Headlines.Count + Rows - 1) / Rows;
            page = (int)(context.Time.TotalSeconds / Math.Max(1, PageSeconds)) % pages;
        }

        if (page != _pageApplied)
        {
            _pageApplied = page;
            ApplyPage(board, page);
        }

        if (state != _state)
        {
            _state = state;
            _content.Visible = state == State.Ready;
            _message.Visible = state != State.Ready;
        }

        base.Update(context, cancellationToken);
    }

    /// <summary>Fills the four rows from the page. Runs when the page or the board changes, not per frame.</summary>
    private void ApplyPage(RssBoard? board, int page)
    {
        for (int i = 0; i < Rows; i++)
        {
            int index = page * Rows + i;
            bool has = board is not null && index < board.Headlines.Count;
            _chips[i].Visible = has;
            _headlines[i].Visible = has;
            if (!has)
            {
                _rowText[i] = "";
                continue;
            }

            var headline = board!.Headlines[index];
            _rowText[i] = headline.Title;
            _chips[i].Text = headline.Tag;
            _chips[i].Background = FeedColors[headline.FeedIndex % FeedColors.Length].WithBrightness(0.7f);
        }
    }
}
