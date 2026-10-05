using System.Globalization;
using LedMatrixOS.Apps.ISS;
using LedMatrixOS.Apps.PlaneSpotter;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Where the International Space Station is right now, from the keyless wheretheiss.at API (polled every few seconds): a small world map with the
/// station, its ground track and the night side shaded, plus its speed, altitude, coordinates and the country or ocean it is over.
/// With a Location set it also shows how far away the station is, and lights up (pulse, sweep and a banner) when it is within ~1500 km.
/// </summary>
/// <remarks>
/// The home location is the Location setting (searched by place name) when set; otherwise <c>IssTracker:Latitude</c>/<c>IssTracker:Longitude</c>
/// from configuration, falling back to <c>Weather:Location</c> (a place name, geocoded, or "lat,lon"). Without any, the map still works.
/// </remarks>
public sealed class IssTrackerApp : WidgetApp
{
    public override string Id => "iss-tracker";
    public override string Name => "ISS Tracker";
    public override int FrameRate => 30;

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AlertGap = TimeSpan.FromMinutes(5);

    [Setting("Location", Description = "Search for your place, to see how far away the station is and when it passes near. Empty uses the home location from the configuration.", Search = true)]
    public string Location { get; set; } = "";

    [Setting("Units", Description = "Altitude, speed and distance units.", Options = ["Kilometres", "Miles"])]
    public string Units { get; set; } = "Kilometres";

    [Setting("Alerts", Description = "Show a toast when the station comes within range of your location (at most one every five minutes).")]
    public bool Alerts { get; set; } = true;

    private readonly IIssSource _source;
    private readonly PlaceGeocoder? _geocoder;
    private readonly IssModel _model = new();
    private CancellationTokenSource? _pollCts;
    private volatile ILiveData<IssSnapshot>? _data;
    private volatile HomePoint? _home;
    private string? _placeText;
    private bool _active;
    private IConfiguration? _configuration;
    private bool _loadedOnce;
    private Node? _stats, _messageBox;
    private DateTimeOffset _lastToast = DateTimeOffset.MinValue;
    private string _messageTitle = "Locating the ISS", _messageHint = "Waiting for signal";

    [ActivatorUtilitiesConstructor]
    public IssTrackerApp(HttpClient http) : this(new WhereTheIssSource(http), new PlaceGeocoder(http)) { }

    public IssTrackerApp(IIssSource source, PlaceGeocoder? geocoder = null)
    {
        _source = source;
        _geocoder = geocoder;
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = new IssStyles();

        _stats = new Stack(Orientation.Vertical, gap: 1)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Padding = new Thickness(4, 3, 2, 0),
            Children =
            {
                new MarqueeLabel(() => _model.RegionText) { Style = styles.Region, Height = 8, HAlign = Align.Stretch },
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    Height = 13,
                    HAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Children =
                    {
                        new RollingNumber(() => _model.Speed) { Style = styles.Speed },
                        new Label(() => _model.SpeedUnit) { Style = styles.Muted },
                        new Label("ISS") { Style = styles.Title, Grow = 1, HAlign = Align.Stretch, TextAlignment = TextAlign.Right },
                    },
                },
                new Label(() => _model.AltText) { Style = styles.Altitude, Height = 8, HAlign = Align.Stretch },
                new Label(() => _model.LatLonText) { Style = styles.Coords, Height = 8, HAlign = Align.Stretch },
                new Label(() => _model.DistText) { Style = styles.Distance, Height = 8, HAlign = Align.Stretch },
            },
        };

        _messageBox = new Stack(Orientation.Vertical, gap: 3)
        {
            HAlign = Align.Center,
            VAlign = Align.Center,
            Visible = false,
            Children =
            {
                new Label(() => _messageTitle) { Style = styles.Message, HAlign = Align.Center },
                new Label(() => _messageHint) { Style = styles.Muted, HAlign = Align.Center },
            },
        };

        var band = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.End,
            Height = 9,
            Children =
            {
                new PulseBlock(() => _model.InRange && !_model.Stale, IssStyles.Amber) { HAlign = Align.Stretch, VAlign = Align.Stretch },
                new Label(() => _model.InRange && !_model.Stale ? _model.Headline : "") { Style = styles.Band, HAlign = Align.Center, VAlign = Align.Center },
            },
        };

        var right = new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _stats, _messageBox, band } };
        return new Stack(Orientation.Horizontal)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children =
            {
                new IssMapNode(_model) { Width = WorldMap.Width + 2, VAlign = Align.Stretch },
                new Block(new Pixel(30, 36, 56), width: 1) { Margin = new Thickness(0, 3) },
                right,
            },
        };
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        var snapshot = _data?.Value;
        bool entered = _model.Refresh(snapshot, _home, string.Equals(Units, "Miles", StringComparison.OrdinalIgnoreCase));
        if (snapshot is not null) _loadedOnce = true;

        bool has = _model.HasPosition;
        _stats!.Visible = has;
        _messageBox!.Visible = !has;
        if (!has)
        {
            bool offline = _loadedOnce;
            _messageTitle = offline ? "No data, retrying" : "Locating the ISS";
            _messageHint = offline ? "Feed unreachable" : "Waiting for signal";
        }

        if (Alerts && entered)
        {
            var now = Time.GetUtcNow();
            if (now - _lastToast >= AlertGap &&
                ShowToast(_model.Overhead ? "ISS overhead now" : "ISS nearby", Pixel.Black, IssStyles.Amber, TimeSpan.FromSeconds(6)) is not null)
                _lastToast = now;
        }

        base.Update(context, cancellationToken);
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        if (key == "location" && _active && _configuration is not null) ReadLocation(_configuration);
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _lastToast = DateTimeOffset.MinValue;
        _active = true;
        _configuration = configuration;
        ReadLocation(configuration);
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        _pollCts?.Cancel();
        _pollCts = null;
        await base.OnDeactivatedAsync(cancellationToken);
    }

    /// <summary>Reads the home location: the setting wins, otherwise explicit coordinates, otherwise the Weather location (coordinates or a place name to geocode).</summary>
    internal void ReadLocation(IConfiguration configuration)
    {
        _home = null;
        _placeText = null;
        if (!string.IsNullOrWhiteSpace(Location))
        {
            Resolve(Location.Trim());
        }
        else if (double.TryParse(configuration["IssTracker:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(configuration["IssTracker:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
            PlaneMath.IsValidCoordinate(lat, lon))
        {
            _home = new HomePoint(lat, lon, "Home");
        }
        else if (configuration["Weather:Location"] is { Length: > 0 } place)
        {
            Resolve(place);
        }
    }

    private void Resolve(string place)
    {
        if (PlaceGeocoder.TryParseEncoded(place, out var name, out var picked)) _home = new HomePoint(picked.Lat, picked.Lon, name);
        else if (PlaceGeocoder.TryParseCoordinates(place, out var c)) _home = new HomePoint(c.Lat, c.Lon, "Home");
        else if (_geocoder is not null) _placeText = place;
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    private void RestartPolling()
    {
        _pollCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pollCts = cts;
        _data = Poll(RefreshInterval, FetchAsync, cts.Token);
    }

    private async Task<IssSnapshot> FetchAsync(CancellationToken ct)
    {
        if (_home is null && _placeText is { } text && _geocoder is not null)
        {
            try
            {
                if (await _geocoder.ResolveAsync(text, ct).ConfigureAwait(false) is { } h) _home = new HomePoint(h.Lat, h.Lon, PlaceGeocoder.DisplayName(text));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // the map still works without a home
            }
        }

        try
        {
            return await _source.GetAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return IssSnapshot.Offline;
        }
    }

    internal IssModel Model => _model;

    /// <summary>Test seam: replaces the polled data and the home point (call before the first frame).</summary>
    internal void UseData(ILiveData<IssSnapshot>? data, (double Lat, double Lon)? home = null)
    {
        _pollCts?.Cancel();
        _pollCts = null;
        _data = data;
        _home = home is { } h ? new HomePoint(h.Lat, h.Lon, "Home") : null;
    }
}
