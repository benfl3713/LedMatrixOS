using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// One Tube line, full focus: the line's colour is the identity (a bold banner and glowing track), the service status is a badge that
/// pulses when something is wrong, live trains glide along the stations, and the bottom strip tells you where the line runs, which train
/// is next, or what the disruption is.
/// </summary>
public sealed class TubeLineApp : WidgetApp
{
    private static readonly string[] SupportedLineIds =
    [
        "bakerloo", "central", "circle", "district", "hammersmith-city", "jubilee", "metropolitan", "northern", "piccadilly", "victoria", "waterloo-city",
        "dlr", "elizabeth", "london-overground", "liberty", "lioness", "mildmay", "suffragette", "weaver", "windrush", "tram",
    ];

    private readonly record struct Caption(string Text, Pixel Color);

    private readonly TubeLineClient _client;
    private volatile ILiveData<LineSnapshot>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active, _lineFromUser;

    private readonly Tween<Pixel> _lineColor = new(new Pixel(30, 60, 220));
    private string _shownLine = "";
    private Label _title = null!;
    private List<Caption> _captions = new();
    private LineSnapshot? _captionsFor;
    private ServiceHealth _captionsHealth;
    private bool _captionsLoading;
    private LineSnapshot? _badgeFor;
    private string _badgeText = "";

    public override string Id => "tube-line";
    public override string Name => "Tube Line";
    public override int FrameRate => 30;

    [Setting("Line", Description = "TfL line ID to render.", Options = ["bakerloo", "central", "circle", "district", "hammersmith-city", "jubilee", "metropolitan", "northern", "piccadilly", "victoria", "waterloo-city", "dlr", "elizabeth", "london-overground", "liberty", "lioness", "mildmay", "suffragette", "weaver", "windrush", "tram"])]
    public string LineId { get; set; } = "jubilee";

    [Setting("Branch Mode", Description = "Auto picks the best branch path, Pinned keeps a specific branch route.", Options = ["Auto", "Pinned"])]
    public string BranchMode { get; set; } = "Auto";

    [Setting("Branch Route", Description = "Route path to use when Branch Mode is Pinned.")]
    public string BranchRoute { get; set; } = "auto";

    [ActivatorUtilitiesConstructor]
    public TubeLineApp(HttpClient http) => _client = new TubeLineClient(http);

    /// <summary>The latest line data, or null before the first fetch succeeds.</summary>
    public LineSnapshot? Current => _data?.Value;

    public bool HasError => _data?.Error is not null;

    public override IEnumerable<AppSetting> GetSettings()
    {
        foreach (var s in base.GetSettings())
            yield return s.Key == "branchRoute"
                ? s with { Type = AppSettingType.Select, Options = (_data?.Value?.RouteOptions ?? ["auto"]).ToArray() }
                : s;
    }

    public override void UpdateSetting(string key, object value)
    {
        if (key.Equals("lineId", StringComparison.OrdinalIgnoreCase)) value = NormalizeLineId(SettingsBinder.CoerceString(value, LineId));
        else if (key.Equals("branchRoute", StringComparison.OrdinalIgnoreCase))
        {
            var route = SettingsBinder.CoerceString(value, "auto");
            var options = _data?.Value?.RouteOptions ?? ["auto"];
            value = options.Contains(route, StringComparer.OrdinalIgnoreCase) ? route : "auto";
        }
        base.UpdateSetting(key, value);
    }

    protected override void OnSettingChanged(string key)
    {
        if (key == "lineId")
        {
            _lineFromUser = true;
            BranchRoute = "auto";
        }
        if (_active && key is "lineId" or "branchMode" or "branchRoute") StartPolling();
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken ct)
    {
        await base.OnActivatedAsync(dimensions, configuration, ct);
        _client.AppKey = configuration["TFL:AppKey"];
        if (!_lineFromUser && configuration["TubeLineApp:LineId"] is { Length: > 0 } configured) LineId = NormalizeLineId(configured);
        _active = true;
        _shownLine = "";
        StartPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken ct)
    {
        _active = false;
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(ct);
    }

    private void StartPolling()
    {
        string line = LineId, route = BranchRoute;
        bool pinned = BranchMode == "Pinned";
        _data = RestartPoll(ref _pollCts, TimeSpan.FromSeconds(15), ct => _client.FetchAsync(line, pinned, route, ct));
    }

    public static string NormalizeLineId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "jubilee";
        var n = raw.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        if (n.EndsWith("-line", StringComparison.Ordinal)) n = n[..^5];
        if (n is "overground") n = "london-overground";
        return SupportedLineIds.Contains(n) ? n : "jubilee";
    }

    // ---- view ------------------------------------------------------------------------------------------------------------------

    private LineSnapshot? Snap() => _data?.Value is { } s && s.LineId == LineId ? s : null;

    private (ServiceHealth, string) Badge()
    {
        if (Snap() is { } s)
        {
            if (!ReferenceEquals(s, _badgeFor)) { _badgeFor = s; _badgeText = s.Status.Health == ServiceHealth.Good ? "GOOD SERVICE" : s.Status.Description.ToUpperInvariant(); }
            return (s.Status.Health, _badgeText);
        }
        return _data?.Error is not null ? (ServiceHealth.Unknown, "NO DATA") : (ServiceHealth.Unknown, "LOADING");
    }

    protected override Node Build()
    {
        _title = new Label(new Memo<string>(() => LineId, LineColors.DisplayName).Get) { VAlign = Align.Center, Grow = 1, Style = new TextStyle(WeatherFonts.Bold, Pixel.White) };
        _shownLine = "";
        var band = new Panel
        {
            Height = 20,
            Children =
            {
                new LineBand(() => _lineColor.Value),
                new Stack(Orientation.Horizontal, gap: 4)
                    { Padding = new Thickness(5, 1, 3, 1), HAlign = Align.Stretch, VAlign = Align.Stretch, CrossAlign = Align.Center, Children = { _title, new StatusBadge(Badge) } },
            },
        };

        var strip = new Pager(1, TimeSpan.FromSeconds(5), new SlideTransition(MoveDirection.Up))
        {
            Height = 12,
            Margin = new Thickness(4, 0),
        }.Bind(() => _captions, c => new MarqueeLabel(c.Text) { Style = new TextStyle(Fonts.Small, c.Color), Speed = 40f, PauseDuration = TimeSpan.FromSeconds(1) });

        return new Dock
        {
            Top = band,
            Bottom = strip,
            Fill = new TrackMap(Snap, () => _lineColor.Value, () => Snap()?.Status.Health ?? ServiceHealth.Unknown),
        };
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        base.Update(context, cancellationToken);
        if (_title is null) return;

        if (_shownLine != LineId)
        {
            var target = LineColors.Of(LineId);
            if (_shownLine == "") _lineColor.Set(target);
            else Animator.Animate(_lineColor, target, TimeSpan.FromMilliseconds(700), Easing.InOutCubic);
            _title.Style = new TextStyle(LineColors.DisplayName(LineId).Length > 14 ? Fonts.Small : WeatherFonts.Bold, LineColors.TextOn(target), Shadow: LineColors.TextOn(target) == Pixel.White);
            _shownLine = LineId;
        }

        var snap = Snap();
        bool loading = snap is null;
        var health = snap?.Status.Health ?? ServiceHealth.Unknown;
        if (!ReferenceEquals(snap, _captionsFor) || health != _captionsHealth || loading != _captionsLoading)
        {
            _captionsFor = snap;
            _captionsHealth = health;
            _captionsLoading = loading;
            _captions = BuildCaptions(snap);
        }
    }

    private List<Caption> BuildCaptions(LineSnapshot? s)
    {
        var list = new List<Caption>();
        var dim = new Pixel(215, 225, 245);
        if (s is null)
        {
            list.Add(new Caption(_data?.Error is not null ? "Cannot reach TfL, retrying..." : "Fetching live trains...", dim));
            return list;
        }

        if (s.Status.Health is ServiceHealth.Minor or ServiceHealth.Severe)
        {
            var reason = s.Status.Reason ?? s.Status.Description;
            list.Add(new Caption(reason.Trim(), LineColors.HealthColor(s.Status.Health)));
            return list;
        }

        if (s.Stops.Count >= 2)
            list.Add(new Caption($"{TubeLineClient.ShortName(s.Stops[0].Name)}  >>  {TubeLineClient.ShortName(s.Stops[^1].Name)}   {s.Trains.Count} trains", dim));
        if (s.Trains.Count > 0)
        {
            var next = s.Trains.OrderBy(t => t.Seconds).First();
            int m = (next.Seconds + 30) / 60;
            list.Add(new Caption($"Next: {next.NextStation} {(m == 0 ? "now" : m + " min")}", new Pixel(255, 255, 255)));
        }
        return list;
    }
}
