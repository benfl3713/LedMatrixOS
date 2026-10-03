using System.Numerics;
using BdfFontParser;
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
/// Weather as a living scene: sky, sun or moon, clouds, rain, snow and lightning behind a big rolling temperature, with a side panel
/// that pages through today's details, the next hours and the next days. Data is real (Open-Meteo) behind <see cref="IWeatherSource"/>.
/// </summary>
public sealed class WeatherApp : WidgetApp
{
    private enum State { Loading, Ready, Offline }

    private IWeatherSource _source;
    private volatile ILiveData<WeatherSnapshot>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active, _locationFromUser;

    private State _state = State.Loading;
    private bool _everReady;
    private TimeSpan _revealAt = TimeSpan.MaxValue;
    private readonly Tween<Pixel> _tempColor = new(new Pixel(255, 255, 255));
    private Pixel _styledColor;
    private bool _stale;

    private WeatherScene _scene = null!;
    private RollingNumber _digits = null!;
    private Node _readout = null!, _panel = null!, _pager = null!, _skeleton = null!, _minus = null!;
    private Pager _pagerNode = null!;
    private MarqueeLabel _place = null!;
    private static readonly string[] Dots = ["", ".", "..", "..."];

    public override string Id => "weather";
    public override string Name => "Weather";
    public override int FrameRate => 30;

    [Setting("Location", Description = "Place name (e.g. London) or coordinates as 'lat,lon'.")]
    public string Location { get; set; } = "London";

    [Setting("Units", Description = "Temperature and wind units.", Options = ["Celsius", "Fahrenheit"])]
    public string Units { get; set; } = "Celsius";

    [Setting("Page Seconds", Description = "Seconds each forecast page stays up.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    [ActivatorUtilitiesConstructor]
    public WeatherApp(HttpClient http) : this(new OpenMeteoWeatherSource(http)) { }

    public WeatherApp(IWeatherSource source) => _source = source;

    /// <summary>The latest good forecast (kept while offline), or null before the first fetch succeeds.</summary>
    public WeatherSnapshot? Current => _data?.Value;

    public bool HasError => _data?.Error is not null;

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken ct)
    {
        await base.OnActivatedAsync(dimensions, configuration, ct);
        if (string.Equals(configuration["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase)) _source = new FakeWeatherSource();
        if (!_locationFromUser && configuration["Weather:Location"] is { Length: > 0 } loc) Location = loc;
        _active = true;
        _state = State.Loading;
        _everReady = false;
        _revealAt = TimeSpan.MaxValue;
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
        if (_active && key is "location" or "units") StartPolling();
    }

    private void StartPolling()
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        var query = new WeatherQuery(Location, Units == "Fahrenheit");
        _data = Poll(TimeSpan.FromMinutes(10), ct => _source.GetAsync(query, ct), _pollCts.Token);
    }

    // ---- derived state -------------------------------------------------------------------------------------------------------

    private WeatherSnapshot? Snap() => _data?.Value;

    private DateTimeOffset LocalNow() => Time.GetUtcNow().ToOffset(Snap()?.UtcOffset ?? TimeSpan.Zero);

    private SceneMood Mood()
    {
        if (Snap() is not { } s) return new SceneMood(_state == State.Offline ? SceneMode.Offline : SceneMode.Loading, WeatherKind.Clear, true, 0, 1);
        var tod = LocalNow().TimeOfDay;
        bool day = s.Sunrise == s.Sunset ? s.IsDay : tod >= s.Sunrise && tod < s.Sunset;
        double near = Math.Min(Math.Abs((tod - s.Sunrise).TotalMinutes), Math.Abs((tod - s.Sunset).TotalMinutes));
        float glow = MathF.Round(Math.Clamp(1f - (float)near / 45f, 0f, 1f) * 4f) / 4f;
        return new SceneMood(SceneMode.Weather, s.Kind, day, glow, WeatherCodes.IntensityOf(s.Code));
    }

    private (Pixel, Pixel) Sky() => (_scene.SkyTop, _scene.SkyBottom);

    private static readonly (double T, Pixel C)[] stops =
    [
        (-5, new Pixel(150, 200, 255)), (2, new Pixel(130, 215, 255)), (10, new Pixel(120, 240, 200)),
        (18, new Pixel(200, 245, 110)), (24, new Pixel(255, 215, 70)), (30, new Pixel(255, 140, 40)), (36, new Pixel(255, 70, 50)),
    ];

    internal static Pixel TempColor(double temp, bool fahrenheit)
    {
        double c = fahrenheit ? (temp - 32) * 5 / 9 : temp;
        if (c <= stops[0].T) return stops[0].C;
        for (int i = 1; i < stops.Length; i++)
            if (c <= stops[i].T) return Pixel.Lerp(stops[i - 1].C, stops[i].C, (float)((c - stops[i - 1].T) / (stops[i].T - stops[i - 1].T)));
        return stops[^1].C;
    }

    // ---- view ------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var digitStyleFont = WeatherFonts.Digits;
        var caption = new Memo<(State, WeatherSnapshot?, int)>(() => (_state, Snap(), (int)(Frame.Time.TotalSeconds * 2.5) % 4), c => c.Item1 switch
        {
            State.Loading => "Loading" + Dots[c.Item3],
            State.Offline when c.Item2 is null => "Offline",
            _ => c.Item2 is { } s ? WeatherCodes.Describe(s.Code, s.IsDay) : "",
        });
        var place = new Memo<(State, WeatherSnapshot?, bool)>(() => (_state, Snap(), _stale), p =>
            p.Item2 is { } s ? s.Location.ToUpperInvariant() + (p.Item3 ? "  OFFLINE" : "  FEELS " + Math.Round(s.Feels) + "°")
            : p.Item1 == State.Offline ? (_data?.Error is LocationNotFoundException ? "UNKNOWN LOCATION" : "RETRYING...") : Location.ToUpperInvariant());
        var hi = new Memo<WeatherSnapshot?>(Snap, s => s is null ? "" : "▲" + Math.Round(s.High));
        var lo = new Memo<WeatherSnapshot?>(Snap, s => s is null ? "" : "▼" + Math.Round(s.Low));
        var unit = new Memo<WeatherSnapshot?>(Snap, s => s is { Fahrenheit: true } ? "F" : "C");

        _scene = new WeatherScene(Mood);
        Pixel TC() => _tempColor.Value;
        _digits = new RollingNumber(() => _state == State.Ready && Frame.Time >= _revealAt && Snap() is { } s ? (int)Math.Abs(Math.Round(s.Temp)) : 0)
            { Style = new TextStyle(digitStyleFont, new Pixel(255, 255, 255)), Spacing = 0 };
        _minus = new MinusBar(TC) { Visible = false };
        _place = new MarqueeLabel(place.Get) { Style = new TextStyle(Fonts.QuiteSmall, new Pixel(200, 215, 245), Shadow: true), Margin = new Thickness(0, 1, 0, 0) };

        _readout = new Stack(Orientation.Horizontal, gap: 1)
        {
            Margin = new Thickness(64, 2, 0, 0), Visible = false,
            Children =
            {
                _minus, _digits,
                new Stack(Orientation.Vertical, gap: 1) { Children = { new DegreeRing(TC), new Label(unit.Get) { Style = new TextStyle(Fonts.Small, new Pixel(210, 220, 240)) } } },
                new Stack(Orientation.Vertical, gap: 3)
                {
                    Margin = new Thickness(3, 5, 0, 0),
                    Children =
                    {
                        new Label(hi.Get) { Style = new TextStyle(Fonts.Small, new Pixel(255, 165, 70)) },
                        new Label(lo.Get) { Style = new TextStyle(Fonts.Small, new Pixel(110, 195, 255)) },
                    },
                },
            },
        };

        var left = new Panel
        {
            _scene,
            _readout,
            new MarqueeLabel(caption.Get) { Style = new TextStyle(Fonts.Small, new Pixel(235, 242, 255)), Margin = new Thickness(64, 41, 0, 0), Width = 150 - 64 + 2 },
            new Panel { _place }.Also(p => { p.Margin = new Thickness(64, 55, 0, 0); p.Width = 84; }),
        };

        _pagerNode = new Pager(1, TimeSpan.FromSeconds(PageSeconds), new SlideTransition(MoveDirection.Up))
        {
            WeatherPages.Now(Snap, LocalNow, Sky), WeatherPages.Hours(Snap, Sky), WeatherPages.Rain(Snap, Sky), WeatherPages.Days(Snap, Sky),
        };
        _pager = _pagerNode;
        _skeleton = new Skeleton(Sky, () => _state == State.Loading);
        _panel = new Panel
        {
            Width = 104,
            Children = { _skeleton, _pagerNode, new Divider(Orientation.Vertical) { Color = new Pixel(255, 255, 255).WithBrightness(0.35f), HAlign = Align.Start } },
        };
        _pager.Visible = false;
        _panel.Position = new Vector2(0, 0);
        var dots = new PageDots(_pagerNode) { HAlign = Align.End, VAlign = Align.Start, Margin = new Thickness(0, 4, 5, 0) };
        ((Panel)_panel).Add(dots);

        return new Dock { Fill = left, Right = _panel };
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        base.Update(context, cancellationToken);
        if (_scene is null) return;

        var snap = Snap();
        var state = snap is not null ? State.Ready : _data?.Error is not null ? State.Offline : State.Loading;
        bool stale = snap is not null && _data?.Error is not null;
        if (stale != _stale)
        {
            _stale = stale;
            _place.Style = new TextStyle(Fonts.QuiteSmall, stale ? new Pixel(255, 176, 0) : new Pixel(200, 215, 245));
        }

        if (state != _state)
        {
            _state = state;
            bool ready = state == State.Ready;
            _readout.Visible = ready;
            _pager.Visible = ready;
            _skeleton.Visible = !ready;
            if (ready && !_everReady)
            {
                _everReady = true;
                _scene.Pop();
                _revealAt = context.Time + TimeSpan.FromMilliseconds(450);
                _readout.Opacity = 0f;
                _readout.AnimateOpacity(1f, TimeSpan.FromMilliseconds(450), Easing.OutCubic);
                _panel.Position = new Vector2(110, 0);
                _panel.AnimatePosition(Vector2.Zero, TimeSpan.FromMilliseconds(700), Easing.OutCubic);
            }
        }

        if (snap is not null)
        {
            var target = TempColor(snap.Temp, snap.Fahrenheit);
            if (_tempColor.Target != target)
            {
                if (_everReady && _tempColor.Value != new Pixel(255, 255, 255)) Animator.Animate(_tempColor, target, TimeSpan.FromSeconds(1.2), Easing.InOutSine);
                else _tempColor.Set(target);
            }
            if (_styledColor != _tempColor.Value)
            {
                _styledColor = _tempColor.Value;
                _digits.Style = new TextStyle(WeatherFonts.Digits, _styledColor);
            }
            _minus.Visible = snap.Temp <= -0.5;
            _pagerNode.Interval = TimeSpan.FromSeconds(PageSeconds);
        }
    }
}

internal static class WeatherFonts
{
    private static BdfFont? _digits;
    private static BdfFont? _bigSource;

    private static BdfFont? _bold;
    private static BdfFont? _boldFor;

    /// <summary>The bold 9x18 font (falls back to the regular one when the file is missing).</summary>
    public static BdfFont Bold
    {
        get
        {
            if (_bold is null || !ReferenceEquals(_boldFor, Fonts.Big))
            {
                _boldFor = Fonts.Big;
                var path = Path.Combine(Path.GetDirectoryName(typeof(Fonts).Assembly.Location)!, "Text", "Fonts", "9x18B.bdf");
                _bold = File.Exists(path) ? new BdfFont(path) : Fonts.Big;
            }
            return _bold;
        }
    }

    /// <summary>The bold 9x18 font doubled to 18x36: digits you can read from across the room.</summary>
    public static BdfFont Digits
    {
        get
        {
            if (_digits is null || !ReferenceEquals(_bigSource, Fonts.Big))
            {
                _bigSource = Fonts.Big;
                BdfFont source = Fonts.Big;
                var path = Path.Combine(Path.GetDirectoryName(typeof(Fonts).Assembly.Location)!, "Text", "Fonts", "9x18B.bdf");
                if (File.Exists(path)) source = new BdfFont(path);
                _digits = source.Scale(2);
            }
            return _digits;
        }
    }
}

internal static class NodeExtensions
{
    public static T Also<T>(this T node, Action<T> configure)
    {
        configure(node);
        return node;
    }
}
