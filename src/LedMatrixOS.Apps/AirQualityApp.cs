using LedMatrixOS.Apps.AirQuality;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Air quality for a place (Open-Meteo, no key): a big European AQI with its band, PM2.5 and PM10 chips, and a "good to run outside?"
/// verdict along the bottom. A side pager alternates a 24 hour AQI trend with UV and pollen.
/// </summary>
public sealed class AirQualityApp : WidgetApp
{
    private enum State { Loading, Ready, Offline }

    private const int GaugeWidth = 88;
    private const int SideWidth = 88;
    private const int StripHeight = 12;
    private static readonly IReadOnlyList<float> NoValues = [];

    private IAirQualitySource _source;
    private volatile ILiveData<AirQualitySnapshot>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active, _locationFromUser;

    private AirQualitySnapshot? _viewFor;
    private AirQualityView? _view;
    private State _state = State.Loading;
    private bool _stale;

    private AirQualityStyles _styles = null!;
    private Node _content = null!;
    private Label _message = null!, _band = null!, _verdict = null!, _uv = null!, _uvLabel = null!, _pm25Value = null!, _pm10Value = null!;
    private MarqueeLabel _place = null!;
    private RollingNumber _digits = null!;
    private Pill _pm25Pill = null!, _pm10Pill = null!;
    private Sparkline _trend = null!;
    private Label[] _pollenLevels = null!;
    private Pager _pager = null!;

    public override string Id => "air-quality";
    public override string Name => "Air Quality";
    public override int FrameRate => 30;

    [Setting("Location", Description = "Search for a place.", Search = true)]
    public string Location { get; set; } = "London";

    [Setting("Page Seconds", Description = "Seconds each side page stays up.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 7;

    [ActivatorUtilitiesConstructor]
    public AirQualityApp(HttpClient http) : this(new OpenMeteoAirQualitySource(http)) { }

    public AirQualityApp(IAirQualitySource source) => _source = source;

    /// <summary>The latest good reading (kept while offline), or null before the first fetch succeeds.</summary>
    public AirQualitySnapshot? Current => _data?.Value;

    // ---- lifecycle ---------------------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken ct)
    {
        await base.OnActivatedAsync(dimensions, configuration, ct);
        if (string.Equals(configuration["AirQuality:Source"], "Fake", StringComparison.OrdinalIgnoreCase)) _source = new FakeAirQualitySource();
        if (!_locationFromUser)
        {
            if (configuration["AirQuality:Location"] is { Length: > 0 } own) Location = own;
            else if (configuration["Weather:Location"] is { Length: > 0 } weather) Location = weather;
        }

        _active = true;
        StartPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken ct)
    {
        _active = false;
        _pollCts?.Cancel();
        await base.OnDeactivatedAsync(ct);
    }

    protected override void OnSettingChanged(string key)
    {
        if (key == "location") _locationFromUser = true;
        if (_active && key == "location") StartPolling();
        if (key == "pageSeconds" && _pager is not null) _pager.Interval = TimeSpan.FromSeconds(PageSeconds);
    }

    private void StartPolling()
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        var query = new AirQualityQuery(Location);
        _data = Poll(TimeSpan.FromMinutes(15), ct => _source.GetAsync(query, ct), _pollCts.Token);
    }

    /// <summary>Test seam: replaces the polled data with a fixed source (call before the first frame).</summary>
    internal void UseData(ILiveData<AirQualitySnapshot>? data)
    {
        _pollCts?.Cancel();
        _data = data;
    }

    // ---- view --------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _styles = new AirQualityStyles();
        var st = _styles;
        var none = st.Small[AirQualityScale.Neutral];

        _place = new MarqueeLabel(() => PlaceText()) { Style = st.Place, HAlign = Align.Stretch };
        _digits = new RollingNumber(() => _view is { } v ? v.Aqi : 0) { Style = st.Digits[AirQualityStyles.Slots - 1], Spacing = 0 };
        _band = new Label(() => _view?.BandLabel ?? "") { Style = none };

        var gauge = new Stack(Orientation.Vertical, gap: 1)
        {
            Width = GaugeWidth,
            HAlign = Align.Start,
            VAlign = Align.Stretch,
            Padding = new Thickness(6, 2, 2, 0),
            Children =
            {
                _place,
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    CrossAlign = Align.End,
                    Children =
                    {
                        _digits,
                        new Stack(Orientation.Vertical, gap: 0) { Children = { new Label("EU") { Style = st.Muted }, new Label("AQI") { Style = st.Muted } } },
                    },
                },
                _band,
            },
        };

        _pm25Pill = new Pill("PM2.5", AirQualityScale.NeutralColor) { Style = st.PillText, Width = 34, Height = 9 };
        _pm10Pill = new Pill("PM10", AirQualityScale.NeutralColor) { Style = st.PillText, Width = 34, Height = 9 };
        _pm25Value = new Label(() => _view?.Pm25Text ?? "") { Style = none, VAlign = Align.Center };
        _pm10Value = new Label(() => _view?.Pm10Text ?? "") { Style = none, VAlign = Align.Center };
        var middle = new Stack(Orientation.Vertical, gap: 3)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Padding = new Thickness(6, 4, 2, 0),
            Children =
            {
                new Stack(Orientation.Horizontal, gap: 4) { Children = { _pm25Pill, _pm25Value } },
                new Stack(Orientation.Horizontal, gap: 4) { Children = { _pm10Pill, _pm10Value } },
                new Label("ug/m3") { Style = st.Muted },
                new Label(() => _view?.GasText ?? "") { Style = st.Muted },
            },
        };

        _trend = new Sparkline { Source = () => _view?.Hourly ?? NoValues, Min = 0, Max = 40, NaturalHeight = 12, FillBrightness = 0.3f, Grow = 1, ShowLatest = false };
        var trendPage = new Stack(Orientation.Vertical, gap: 2)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Padding = new Thickness(6, 3, 4, 2),
            Children =
            {
                new Label("NEXT 24H") { Style = st.Muted },
                _trend,
                new Label(() => _view?.TrendText ?? "") { Style = st.Muted },
            },
        };

        _uv = new Label(() => _view?.UvText ?? "") { Style = none };
        _uvLabel = new Label(() => _view?.UvLabel ?? "") { Style = st.Tiny[AirQualityScale.Neutral], VAlign = Align.End };
        _pollenLevels = new Label[3];
        var pollenRows = new Stack(Orientation.Vertical, gap: 1);
        string[] names = ["Alder", "Birch", "Grass"];
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            _pollenLevels[i] = new Label(() => _view?.PollenLabels[index] ?? "") { Style = st.Tiny[AirQualityScale.Neutral] };
            pollenRows.Add(new Stack(Orientation.Horizontal, gap: 4)
            {
                Children = { new Label(names[i]) { Style = st.Muted, Width = 30 }, _pollenLevels[i] },
            });
        }

        var uvPage = new Stack(Orientation.Vertical, gap: 2)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Padding = new Thickness(6, 3, 4, 0),
            Children =
            {
                new Stack(Orientation.Horizontal, gap: 3) { Children = { _uv, _uvLabel } },
                new Label("POLLEN") { Style = st.Muted },
                pollenRows,
            },
        };

        _pager = new Pager(1, TimeSpan.FromSeconds(PageSeconds), new SlideTransition(MoveDirection.Up)) { trendPage, uvPage };
        var side = new Panel
        {
            Width = SideWidth,
            HAlign = Align.End,
            VAlign = Align.Stretch,
            Children =
            {
                _pager,
                new Divider(Orientation.Vertical) { Color = new Pixel(255, 255, 255).WithBrightness(0.3f), HAlign = Align.Start },
                new PageDots(_pager) { HAlign = Align.End, VAlign = Align.Start, Margin = new Thickness(0, 4, 5, 0) },
            },
        };

        _verdict = new Label(() => _view?.VerdictText ?? "") { Style = none, VAlign = Align.Center };
        var strip = new Panel
        {
            HAlign = Align.Stretch,
            Height = StripHeight,
            Children =
            {
                new Block(new Pixel(14, 14, 20)) { HAlign = Align.Stretch, VAlign = Align.Stretch },
                new Stack(Orientation.Horizontal) { HAlign = Align.Stretch, VAlign = Align.Stretch, Padding = new Thickness(6, 0, 2, 0), Children = { _verdict } },
            },
        };

        var rest = new Dock { Bottom = strip, Right = side, Fill = middle, HAlign = Align.Stretch, VAlign = Align.Stretch };
        _content = new Dock { Left = gauge, Fill = rest, HAlign = Align.Stretch, VAlign = Align.Stretch };
        _message = new Label(() => MessageText()) { Style = st.Message, HAlign = Align.Center, VAlign = Align.Center };

        // A fresh tree starts on the message; Update swaps to the content (and restyles it) once there is a reading.
        _content.Visible = false;
        _state = State.Loading;
        _viewFor = null;
        _view = null;
        _stale = false;
        return new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _content, _message } };
    }

    private string PlaceText() =>
        _stale ? "OFFLINE" : _view?.Place ?? PlaneSpotter.PlaceGeocoder.DisplayName(Location).ToUpperInvariant();

    private string MessageText() => _state switch
    {
        State.Loading => "Loading air quality",
        _ => _data?.Error is LocationNotFoundException ? "Unknown location" : "Offline, retrying",
    };

    // ---- per frame ---------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        var snap = _data?.Value;
        var state = snap is not null ? State.Ready : _data?.Error is not null ? State.Offline : State.Loading;
        bool stale = snap is not null && _data?.Error is not null;

        if (!ReferenceEquals(snap, _viewFor))
        {
            _viewFor = snap;
            _view = snap is null ? null : new AirQualityView(snap);
            Apply();
        }

        if (stale != _stale)
        {
            _stale = stale;
            _place.Style = stale ? _styles.PlaceStale : _styles.Place;
        }

        if (state != _state)
        {
            _state = state;
            _content.Visible = state == State.Ready;
            _message.Visible = state != State.Ready;
        }

        base.Update(context, cancellationToken);
    }

    /// <summary>Restyles the nodes whose colour follows the reading. Runs once per new reading, not per frame.</summary>
    private void Apply()
    {
        var st = _styles;
        if (_view is not { } v) return;

        int band = (int)v.Band;
        _digits.Style = st.Digits[band];
        _band.Style = st.Small[band];
        _verdict.Style = st.Small[v.VerdictSlot];
        _pm25Pill.Background = AirQualityScale.ColorOf((AqiBand)v.Pm25Slot).WithBrightness(0.5f);
        _pm10Pill.Background = AirQualityScale.ColorOf((AqiBand)v.Pm10Slot).WithBrightness(0.5f);
        _pm25Value.Style = st.Small[v.Pm25Slot];
        _pm10Value.Style = st.Small[v.Pm10Slot];
        _uv.Style = st.Small[v.UvSlot];
        _uvLabel.Style = st.Tiny[v.UvSlot];
        for (int i = 0; i < _pollenLevels.Length; i++) _pollenLevels[i].Style = st.Tiny[v.PollenSlots[i]];
        _trend.Line = AirQualityScale.ColorOf(v.Band);
        _trend.Max = v.ChartMax;
    }
}
