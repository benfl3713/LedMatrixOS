using System.Globalization;
using LedMatrixOS.Apps.PlaneSpotter;
using LedMatrixOS.Apps.Tube;
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
/// Aircraft overhead, from the keyless OpenSky Network API: the nearest aircraft as the hero (callsign, airline, altitude with a
/// climb/descend arrow, speed, distance and a compass chip for its track), a small radar with a sweep on the right, and the other aircraft
/// in range listed and paged. A toast announces an aircraft that newly enters range (at most one a minute).
/// </summary>
/// <remarks>
/// The home location is the Location setting (searched by place name) when set; otherwise <c>PlaneSpotter:Latitude</c>/<c>PlaneSpotter:Longitude</c>
/// from configuration, falling back to <c>Weather:Location</c> (a place name, geocoded, or "lat,lon").
/// </remarks>
public sealed class PlaneSpotterApp : WidgetApp
{
    private enum State { NoLocation, Loading, Busy, Offline, Clear, Ready }

    public override string Id => "plane-spotter";
    public override string Name => "Plane Spotter";
    public override int FrameRate => 30;

    public const int MinRadiusKm = 5, MaxRadiusKm = 100;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan AlertGap = TimeSpan.FromSeconds(60);

    [Setting("Location", Description = "Search for the place to watch. Empty uses the home location from the configuration.", Search = true)]
    public string Location { get; set; } = "";

    [Setting("Radius", Description = "How far from home to look, in km.", Min = MinRadiusKm, Max = MaxRadiusKm)]
    public int Radius { get; set; } = 25;

    [Setting("Alerts", Description = "Show a toast when a new aircraft comes into range (at most one a minute).")]
    public bool Alerts { get; set; } = true;

    [Setting("Units", Description = "Altitude units.", Options = ["Feet", "Metres"])]
    public string Units { get; set; } = "Feet";

    [Setting("Page Seconds", Description = "How long each page of further aircraft stays before sliding to the next.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    private readonly IPlaneSource _source;
    private readonly PlaceGeocoder? _geocoder;
    private readonly PlaneBoardModel _model = new();
    private CancellationTokenSource? _pollCts;
    private volatile ILiveData<PlaneSnapshot>? _data;
    private (double Lat, double Lon)? _home;
    private string? _placeText;
    private bool _hasLocation, _active;
    private IConfiguration? _configuration;

    private State _state = State.Loading;
    private Pager? _pager;
    private Node? _board, _radar, _clear, _messageBox;
    private Label? _title, _hint;
    private int _appliedRadius = -1;
    private bool _appliedFeet = true;
    private string _unitLabel = "ft", _radiusText = "25km", _clearHint = "";
    private DateTimeOffset _lastToast = DateTimeOffset.MinValue;

    [ActivatorUtilitiesConstructor]
    public PlaneSpotterApp(HttpClient http) : this(new OpenSkyPlaneSource(http), new PlaceGeocoder(http)) { }

    public PlaneSpotterApp(IPlaneSource source, PlaceGeocoder? geocoder = null)
    {
        _source = source;
        _geocoder = geocoder;
    }

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = new PlaneStyles();

        var hero = new Stack(Orientation.Vertical, gap: 1)
        {
            Width = 104,
            HAlign = Align.Start,
            VAlign = Align.Stretch,
            Padding = new Thickness(3, 2, 0, 0),
            Children =
            {
                new Stack(Orientation.Horizontal)
                {
                    Height = 18,
                    HAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Children =
                    {
                        new Label(() => _model.Hero?.Callsign ?? "") { Style = styles.Callsign, Grow = 1 },
                        new HeadingChip(() => _model.Hero) { Width = 18, Height = 18 },
                    },
                },
                new MarqueeLabel(() => _model.Hero?.Operator ?? "") { Style = styles.Operator, Height = 7, HAlign = Align.Stretch },
                new Stack(Orientation.Horizontal, gap: 2)
                {
                    Height = 13,
                    HAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Children =
                    {
                        new RollingNumber(() => _model.Hero?.Altitude ?? 0) { Style = styles.Altitude },
                        new Label(() => _unitLabel) { Style = styles.Muted },
                        new ClimbArrow(() => _model.Hero?.Climb ?? 0) { Width = 7, Height = 9 },
                    },
                },
                new Stack(Orientation.Horizontal, gap: 5)
                {
                    Height = 8,
                    HAlign = Align.Stretch,
                    Children =
                    {
                        new Label(() => _model.Hero?.SpeedText ?? "") { Style = styles.Detail },
                        new Label(() => _model.Hero?.DistText ?? "") { Style = styles.Operator },
                    },
                },
            },
        };

        _pager = new Pager(pageSize: 1, interval: PageSeconds.Seconds(), transition: new SlideTransition(MoveDirection.Left) { Duration = 500.Ms() }, easing: Easing.InOutCubic)
            { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch }
            .Bind(() => _model.Pages, token => BuildPage(token, styles));

        _board = new Stack(Orientation.Horizontal)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children =
            {
                hero,
                new Block(new Pixel(30, 36, 56), width: 1) { Margin = new Thickness(0, 3) },
                new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Padding = new Thickness(4, 3, 0, 0), Children = { _pager } },
            },
        };

        _title = new Label(() => TitleText()) { Style = styles.Message, HAlign = Align.Center };
        _hint = new Label(() => HintText()) { Style = styles.Muted, HAlign = Align.Center };
        _messageBox = new Stack(Orientation.Vertical, gap: 3)
        {
            HAlign = Align.Center,
            VAlign = Align.Center,
            Visible = false,
            Children = { _title, _hint },
        };

        _clear = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Visible = false,
            Children =
            {
                new SkyNode { HAlign = Align.Stretch, VAlign = Align.Stretch },
                new Stack(Orientation.Vertical, gap: 3)
                {
                    HAlign = Align.Center,
                    VAlign = Align.Center,
                    Children =
                    {
                        new Label("CLEAR SKIES") { Style = styles.MessageBig, HAlign = Align.Center },
                        new Label(() => _clearHint) { Style = styles.Operator, HAlign = Align.Center },
                    },
                },
            },
        };

        var left = new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _board, _clear, _messageBox } };
        _radar = new RadarNode(_model) { Width = 56, Height = 54, VAlign = Align.Center, HAlign = Align.Center };

        var strip = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.End,
            Height = 10,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 3)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(3, 0),
                    Children =
                    {
                        new Clock("HH:mm", Time) { Style = styles.Clock },
                        new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { new Label(() => _model.CountText) { Style = styles.Strip, HAlign = Align.Center, VAlign = Align.Center } } },
                        new Label(() => _radiusText) { Style = styles.Muted },
                    },
                },
            },
        };

        var content = new Stack(Orientation.Horizontal)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children = { left, _radar },
        };
        return new Stack(Orientation.Vertical) { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { content } }, strip } };
    }

    private Node BuildPage(PlanePageToken token, PlaneStyles styles) => new ListView<PlaneRow>(
        () => _model.Page(token.Index), r => new ScrollSlot(new PlaneRowNode(r, styles), PlaneRowNode.RowHeight), r => r.Key)
        { HAlign = Align.Stretch, EnterOffset = 0, EnterDuration = 300.Ms(), ExitDuration = 300.Ms() };

    private string TitleText() => _state switch
    {
        State.NoLocation => "Set a location",
        State.Busy => "OpenSky is busy",
        State.Offline => "No data, retrying",
        _ => "Looking up",
    };

    private string HintText() => _state switch
    {
        State.NoLocation => "Set PlaneSpotter lat/lon in config",
        State.Busy => "Backing off, will retry",
        State.Offline => "Cannot reach OpenSky",
        _ => "Scanning the skies",
    };

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        int radius = Math.Clamp(Radius, MinRadiusKm, MaxRadiusKm);
        bool feet = !string.Equals(Units, "Metres", StringComparison.OrdinalIgnoreCase);
        if (radius != _appliedRadius || feet != _appliedFeet) ApplyDisplaySettings(radius, feet);

        var snapshot = _data?.Value;
        _model.Refresh(snapshot, radius, feet);

        _state = !_hasLocation ? State.NoLocation
            : snapshot is null ? State.Loading
            : snapshot.Status == PlaneStatus.Busy ? State.Busy
            : snapshot.Status == PlaneStatus.Offline ? State.Offline
            : _model.Count == 0 ? State.Clear
            : State.Ready;

        _board!.Visible = _state == State.Ready;
        _clear!.Visible = _state == State.Clear;
        _messageBox!.Visible = _state is State.NoLocation or State.Loading or State.Busy or State.Offline;
        _radar!.Visible = _state is State.Ready or State.Clear or State.Loading;

        if (Alerts && _model.NewRows.Count > 0)
        {
            var now = Time.GetUtcNow();
            if (now - _lastToast >= AlertGap &&
                ShowToast(_model.NewRows[0].Callsign + " inbound", Pixel.Black, PlaneStyles.Amber, TimeSpan.FromSeconds(6)) is not null)
                _lastToast = now;
        }

        base.Update(context, cancellationToken);
    }

    private void ApplyDisplaySettings(int radius, bool feet)
    {
        _appliedRadius = radius;
        _appliedFeet = feet;
        _unitLabel = feet ? "ft" : "m";
        _radiusText = radius.ToString(CultureInfo.InvariantCulture) + "km";
        _clearHint = "Nothing within " + radius.ToString(CultureInfo.InvariantCulture) + " km";
    }

    // ---- settings ---------------------------------------------------------------------------------------------------------------

    protected override void OnSettingChanged(string key)
    {
        if (key == "pageSeconds" && _pager is not null) _pager.Interval = PageSeconds.Seconds();
        if (key == "radius" && _pollCts is not null) RestartPolling();
        if (key == "location" && _active && _configuration is not null)
        {
            ReadLocation(_configuration);
            RestartPolling();
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _model.Reset();
        _lastToast = DateTimeOffset.MinValue;
        _appliedRadius = -1;
        _active = true;
        _configuration = configuration;
        ReadLocation(configuration);
        RestartPolling();
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        CancelPoll(ref _pollCts);
        await base.OnDeactivatedAsync(cancellationToken);
    }

    /// <summary>Reads the home location: explicit coordinates win, otherwise the Weather location (coordinates or a place name to geocode).</summary>
    internal void ReadLocation(IConfiguration configuration)
    {
        _home = null;
        _placeText = null;
        if (!string.IsNullOrWhiteSpace(Location))
        {
            var place = Location.Trim();
            if (PlaceGeocoder.TryParseEncoded(place, out _, out var picked)) _home = picked;
            else if (PlaceGeocoder.TryParseCoordinates(place, out var c)) _home = c;
            else if (_geocoder is not null) _placeText = place;
        }
        else if (double.TryParse(configuration["PlaneSpotter:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(configuration["PlaneSpotter:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
            PlaneMath.IsValidCoordinate(lat, lon))
            _home = (lat, lon);
        else if (configuration["Weather:Location"] is { Length: > 0 } place)
        {
            if (PlaceGeocoder.TryParseCoordinates(place, out var c)) _home = c;
            else if (_geocoder is not null) _placeText = place;
        }

        _hasLocation = _home is not null || _placeText is not null;
    }

    // ---- data -------------------------------------------------------------------------------------------------------------------

    private void RestartPolling()
    {
        CancelPoll(ref _pollCts);
        if (!_hasLocation)
        {
            _data = null;
            return;
        }

        double radius = Math.Clamp(Radius, MinRadiusKm, MaxRadiusKm);
        _data = RestartPoll(ref _pollCts, RefreshInterval, ct => FetchAsync(radius, ct));
    }

    private async Task<PlaneSnapshot> FetchAsync(double radius, CancellationToken ct)
    {
        try
        {
            var home = _home;
            if (home is null && _placeText is not null && _geocoder is not null)
                home = _home = await _geocoder.ResolveAsync(_placeText, ct).ConfigureAwait(false);
            if (home is not { } h) return new PlaneSnapshot(PlaneStatus.Offline, [], 0, 0);
            return await _source.GetAsync(new PlaneQuery(h.Lat, h.Lon, radius), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new PlaneSnapshot(PlaneStatus.Offline, [], 0, 0);
        }
    }

    internal bool HasLocation => _hasLocation;
    internal (double Lat, double Lon)? Home => _home;
    internal Pager? ListPager => _pager;
    internal PlaneBoardModel Model => _model;

    /// <summary>Test seam: replaces the polled data (call before the first frame).</summary>
    internal void UseData(ILiveData<PlaneSnapshot>? data, bool hasLocation = true)
    {
        CancelPoll(ref _pollCts);
        _data = data;
        _hasLocation = hasLocation;
    }
}
