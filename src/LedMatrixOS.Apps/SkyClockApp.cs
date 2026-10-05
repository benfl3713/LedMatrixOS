using LedMatrixOS.Apps.Sky;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// The whole panel is the real sky for the current time at a place: the sun crosses a dotted arc between sunrise and sunset, the moon (with its
/// real phase) crosses it by night among twinkling stars, the gradient warms through dawn and dusk, and the weather there brings clouds, rain or snow.
/// A small clock sits in the corner. An optional alarm time makes the sky brighten like a sunrise in the minutes before it, as a gentle wake-up light.
/// </summary>
/// <remarks>Sun and moon positions come from the location's coordinates and <see cref="WidgetApp.Time"/>; the weather comes from the Open-Meteo source the Weather app uses, and the sky is clear when it is unavailable.</remarks>
public class SkyClockApp : WidgetApp
{
    public override string Id => "sky-clock";
    public override string Name => "Sky Clock";
    public override int FrameRate => 20;

    /// <summary>Used until the location has been looked up (and when it cannot be): London.</summary>
    internal const double FallbackLatitude = 51.5072, FallbackLongitude = -0.1276;

    /// <summary>How long after the alarm time the sunrise glow holds before the real sky takes over again.</summary>
    internal const int AlarmHoldMinutes = 10;

    [Setting("Location", Description = "Search for a place (sets where the sun and moon are, and the weather).", Search = true)]
    public string Location { get; set; } = "London";

    [Setting("Alarm Time", Description = "Optional wake-up time as HH:mm (e.g. 06:45). The sky brightens like a sunrise before it. Leave empty for off.")]
    public string AlarmTime { get; set; } = "";

    [Setting("Alarm Lead Minutes", Description = "How long before the alarm the sunrise glow starts.", Min = 5, Max = 120)]
    public int AlarmLeadMinutes { get; set; } = 30;

    private readonly HttpClient? _http;
    private ISkySource _source;
    private volatile ILiveData<SkyData>? _data;
    private CancellationTokenSource? _pollCts;
    private bool _active, _locationFromUser;

    private SkyNode? _sky;
    private string _alarmText = "\0";
    private TimeSpan? _alarm;

    public SkyClockApp(HttpClient httpClient)
    {
        _http = httpClient;
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _source = new OpenMeteoSkySource(new PlaceResolver(httpClient), new OpenMeteoWeatherSource(httpClient));
    }

    internal SkyClockApp(ISkySource source) => _source = source;

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _sky = new SkyNode();
        var clock = new Clock("HH:mm", Time)
        {
            Style = new TextStyle(Fonts.Small, new Pixel(235, 235, 240), Shadow: true),
            HAlign = Align.End,
            VAlign = Align.End,
            Margin = new Thickness(0, 0, 3, 0),
        };
        return new Panel { _sky, clock };
    }

    // ---- per frame --------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        var sky = _sky!;
        var data = _data?.Value;
        double lat = data?.Latitude ?? FallbackLatitude, lon = data?.Longitude ?? FallbackLongitude;
        var utc = Time.GetUtcNow();

        var sun = SolarMath.SunAt(utc, lat, lon);
        double p = sun.Progress;
        int w = Math.Max(16, Host.Width), hy = sky.HorizonY;
        float arcX = 8 + (float)p * (w - 16), arcY = SkyNode.ArcY((float)p, hy);

        sky.Seconds = context.Time.TotalSeconds;
        sky.Kind = data?.Kind ?? WeatherKind.Clear;
        sky.Altitude = sun.Altitude;
        sky.SunVisible = sun.IsDay;
        sky.SunX = arcX;
        sky.SunY = arcY;
        sky.MoonVisible = !sun.IsDay;
        sky.MoonX = arcX;
        sky.MoonY = arcY;
        sky.MoonAge = SolarMath.MoonAge(utc);

        double glow = AlarmGlow(Time.GetLocalNow().TimeOfDay);
        double virtualAlt = -14 + 20 * glow;
        if (glow > 0 && virtualAlt > sun.Altitude)
        {
            // A sunrise on demand: the sun climbs out of the hills at the left while the sky warms.
            sky.Altitude = virtualAlt;
            sky.SunVisible = glow > 0.2;
            sky.MoonVisible = false;
            sky.SunX = w * 0.2f;
            sky.SunY = hy + 5 - (float)glow * (hy - 22);
        }

        base.Update(context, cancellationToken);
    }

    /// <summary>0 outside the alarm window, rising to 1 at the alarm time and staying there for <see cref="AlarmHoldMinutes"/> minutes.</summary>
    internal double AlarmGlow(TimeSpan timeOfDay)
    {
        if (!string.Equals(AlarmTime, _alarmText, StringComparison.Ordinal))
        {
            _alarmText = AlarmTime;
            _alarm = TimeSpan.TryParse(AlarmTime?.Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;
        }

        if (_alarm is not { } alarm) return 0;
        double until = (alarm - timeOfDay).TotalMinutes;
        until = ((until + AlarmHoldMinutes) % 1440 + 1440) % 1440 - AlarmHoldMinutes;   // wrap into [-hold, 1440 - hold)
        if (until <= 0) return until >= -AlarmHoldMinutes ? 1 : 0;
        int lead = Math.Clamp(AlarmLeadMinutes, 5, 120);
        return until <= lead ? 1 - until / lead : 0;
    }

    // ---- lifecycle & settings ---------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);

        if (_http is not null && string.Equals(configuration["Weather:Source"], "Fake", StringComparison.OrdinalIgnoreCase))
            _source = new OpenMeteoSkySource(new PlaceResolver(_http), new FakeWeatherSource());
        if (!_locationFromUser && configuration["Weather:Location"] is { Length: > 0 } loc) Location = loc;

        _active = true;
        RestartPolling();
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(cancellationToken);
    }

    protected override void OnSettingChanged(string key)
    {
        if (!_active || key != "location") return;
        _locationFromUser = true;
        RestartPolling();
    }

    private void RestartPolling()
    {
        var location = Location;
        _data = string.IsNullOrWhiteSpace(location) ? null : RestartPoll(ref _pollCts, TimeSpan.FromMinutes(10), ct => _source.GetAsync(location, ct));
    }

    internal SkyNode? Sky => _sky;

    /// <summary>Test seam: replaces the polled data with a fixed value (call before the first frame).</summary>
    internal void UseData(ILiveData<SkyData>? data) => _data = data;
}
