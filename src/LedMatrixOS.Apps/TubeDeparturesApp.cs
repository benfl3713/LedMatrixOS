using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LedMatrixOS.Graphics;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps;

public class TubeDeparturesApp : SettingsAppBase
{
    public override string Id => "tube-departures";
    public override string Name => "Tube Departures";
    public override int FrameRate => 20;

    private BdfFont _font = Fonts.Big;
    private Pixel _color = new Pixel(255, 120, 0);

    private const string NoStationSearchHint = "Type at least 2 chars";

    // Configurable settings (declaration order is the order they appear in the UI; stationSelect is inserted after stationSearch)
    [Setting("Station Search", Description = "Type a station name (e.g. Baker Street).")]
    public string StationSearch { get; set; } = "";

    [Setting("Station ID", Description = "TfL Naptan ID (auto-filled when you select from Station Select).")]
    public string StationId { get; set; } = "";

    [Setting("Platform Filter", Description = "Filter by platform name (e.g. 'Eastbound'). Leave empty to show all platforms.")]
    public string PlatformFilter { get; set; } = "";

    [Setting("Max Departures", Description = "Number of departures to cycle through", Min = 1, Max = 12)]
    public int MaxDepartures { get; set; } = 3;

    [Setting("Colour Departures By Line", Description = "When enabled, each departure row uses the line colour.")]
    public bool ColorDeparturesByLine { get; set; }

    private readonly HttpClient _http;
    private string? _appKey;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _lineStatusRefreshInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _stationNameRefreshInterval = TimeSpan.FromHours(6);

    // Live data for the current station; replaced (and the old polls stopped) whenever the station changes
    private volatile ILiveData<ArrivalData[]>? _arrivals;
    private volatile ILiveData<StationLineStatus[]>? _lineStatuses;
    private volatile ILiveData<string>? _stationNameData;
    private CancellationTokenSource? _stationPollCts;
    private DateTimeOffset? _arrivalsSeenAt;

    private volatile string[] _stationSearchOptions = new[] { NoStationSearchHint };
    private string _stationSearchLastQuery = "";
    private readonly TimeSpan _departurePageInterval = TimeSpan.FromSeconds(8);
    private DateTime _lastDeparturePageSwitch = DateTime.MinValue;
    private int _departurePage;
    private bool _isPageTransitioning;
    private int _transitionFromPage;
    private int _transitionToPage;
    private bool _transitionScrollUp = true;
    private DateTime _transitionStartedAt = DateTime.MinValue;
    private readonly TimeSpan _pageTransitionDuration = TimeSpan.FromMilliseconds(850);

    private const int VisibleDepartureRows = 3;
    private const int DeparturesClipTopY = 0;
    private const int DeparturesClipBottomY = 46;

    public TubeDeparturesApp(HttpClient httpClient)
    {
        _http = httpClient;
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    private class Departure
    {
        public string DestinationName { get; set; } = "";
        public int TimeToStation { get; set; }
        public string PlatformName { get; set; } = "";
        public string LineName { get; set; } = "";
        public string LineId { get; set; } = "";
    }

    private class ArrivalData
    {
        [JsonPropertyName("destinationName")]
        public string DestinationName { get; set; } = "";

        [JsonPropertyName("timeToStation")]
        public int TimeToStation { get; set; }

        [JsonPropertyName("platformName")]
        public string PlatformName { get; set; } = "";

        [JsonPropertyName("lineName")]
        public string LineName { get; set; } = "";

        [JsonPropertyName("lineId")]
        public string LineId { get; set; } = "";

        [JsonPropertyName("towards")]
        public string Towards { get; set; } = "";
    }

    private class TubeLineStatusResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("lineStatuses")]
        public TubeLineStatusDetail[]? LineStatuses { get; set; }
    }

    private class TubeLineStatusDetail
    {
        [JsonPropertyName("statusSeverity")]
        public int StatusSeverity { get; set; }

        [JsonPropertyName("statusSeverityDescription")]
        public string StatusSeverityDescription { get; set; } = "";
    }

    private record StationLineStatus(string LineId, string Name, int Severity, string Description);

    private class StopPointNameData
    {
        [JsonPropertyName("commonName")]
        public string CommonName { get; set; } = "";
    }

    private class StopPointSearchResponse
    {
        [JsonPropertyName("matches")]
        public StopPointSearchMatch[]? Matches { get; set; }
    }

    private class StopPointSearchMatch
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("modes")]
        public string[]? Modes { get; set; }
    }

    private static readonly Dictionary<string, string> LineAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bakerloo",         "BL" },
        { "central",          "CE" },
        { "circle",           "CI" },
        { "district",         "DI" },
        { "hammersmith-city", "HC" },
        { "jubilee",          "JU" },
        { "metropolitan",     "ME" },
        { "northern",         "NO" },
        { "piccadilly",       "PI" },
        { "victoria",         "VI" },
        { "waterloo-city",    "WC" },
        { "dlr",              "DL" },
        { "elizabeth",        "EL" },
        { "overground",       "OV" },
    };

    // stationSelect has options that change as the user types, so it is not a [Setting] property.
    public override IEnumerable<AppSetting> GetSettings()
    {
        foreach (var setting in base.GetSettings())
        {
            yield return setting;
            if (setting.Key == "stationSearch")
            {
                yield return new AppSetting("stationSelect", "Station Select", "Choose a result to set the station automatically.", AppSettingType.Select, "", "", Options: _stationSearchOptions);
            }
        }
    }

    public override void UpdateSetting(string key, object value)
    {
        if (key != "stationSelect")
        {
            base.UpdateSetting(key, value);
            return;
        }

        var selected = value.ToString() ?? "";
        var splitIndex = selected.IndexOf(" | ", StringComparison.Ordinal);
        if (splitIndex > 0)
        {
            var selectedStationId = selected[..splitIndex].Trim();
            if (!string.IsNullOrWhiteSpace(selectedStationId))
            {
                ApplyStationId(selectedStationId);
            }
        }
    }

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "stationSearch":
                SearchStations();
                break;
            case "stationId":
                ApplyStationId(StationId);
                break;
            case "platformFilter":
            case "maxDepartures":
                ResetDeparturePaging();
                break;
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _appKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_appKey))
        {
            Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");
        }

        var configuredStation = configuration["TubeDeparturesApp:StationId"];
        if (!string.IsNullOrEmpty(configuredStation))
        {
            StationId = configuredStation;
        }

        var configuredPlatform = configuration["TubeDeparturesApp:PlatformFilter"];
        if (!string.IsNullOrEmpty(configuredPlatform))
        {
            PlatformFilter = configuredPlatform;
        }

        if (int.TryParse(configuration["TubeDeparturesApp:MaxDepartures"], out var maxDep) && maxDep > 0)
        {
            MaxDepartures = Math.Clamp(maxDep, 1, 12);
        }

        if (bool.TryParse(configuration["TubeDeparturesApp:ColorDeparturesByLine"], out var colorByLine))
        {
            ColorDeparturesByLine = colorByLine;
        }

        RestartStationPolling();

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    /// <summary>
    /// (Re)starts the polls that depend on the current station, stopping any previous ones.
    /// </summary>
    private void RestartStationPolling()
    {
        _stationPollCts?.Cancel();
        _arrivals = null;
        _lineStatuses = null;
        _stationNameData = null;

        var stationId = StationId;
        if (string.IsNullOrWhiteSpace(stationId))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _stationPollCts = cts;

        var arrivals = Poll(_refreshInterval, ct => FetchArrivalsAsync(stationId, ct), cts.Token);
        _arrivals = arrivals;
        _lineStatuses = Poll(_lineStatusRefreshInterval, ct => FetchLineStatusesAsync(arrivals, ct), cts.Token);
        _stationNameData = Poll(_stationNameRefreshInterval, ct => FetchStationNameAsync(stationId, ct), cts.Token);
    }

    private string WithAppKey(string url)
    {
        return string.IsNullOrEmpty(_appKey) ? url : $"{url}?app_key={Uri.EscapeDataString(_appKey)}";
    }

    private async Task<ArrivalData[]> FetchArrivalsAsync(string stationId, CancellationToken cancellationToken)
    {
        var apiUrl = WithAppKey($"https://api.tfl.gov.uk/StopPoint/{Uri.EscapeDataString(stationId)}/Arrivals");
        return await _http.GetFromJsonAsync<ArrivalData[]>(apiUrl, cancellationToken) ?? Array.Empty<ArrivalData>();
    }

    private async Task<StationLineStatus[]> FetchLineStatusesAsync(ILiveData<ArrivalData[]> arrivals, CancellationToken cancellationToken)
    {
        // Which lines serve the station is only known once arrivals have loaded
        string[] lineIds;
        while ((lineIds = LineIdsOf(arrivals.Value)).Length == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        var joinedIds = string.Join(",", lineIds.Select(Uri.EscapeDataString));
        var apiUrl = WithAppKey($"https://api.tfl.gov.uk/Line/{joinedIds}/Status");
        var lines = await _http.GetFromJsonAsync<TubeLineStatusResponse[]>(apiUrl, cancellationToken) ?? Array.Empty<TubeLineStatusResponse>();

        return lines
            .Select(l =>
            {
                var status = l.LineStatuses?.FirstOrDefault();
                return new StationLineStatus(
                    l.Id,
                    l.Name,
                    status?.StatusSeverity ?? 0,
                    status?.StatusSeverityDescription ?? "Unknown"
                );
            })
            .OrderBy(s => s.LineId)
            .ToArray();
    }

    // All unique line IDs that serve this station (from unfiltered arrivals)
    private static string[] LineIdsOf(ArrivalData[]? arrivals)
    {
        return (arrivals ?? Array.Empty<ArrivalData>())
            .Where(a => !string.IsNullOrWhiteSpace(a.LineId))
            .Select(a => a.LineId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id)
            .ToArray();
    }

    private async Task<string> FetchStationNameAsync(string stationId, CancellationToken cancellationToken)
    {
        var apiUrl = WithAppKey($"https://api.tfl.gov.uk/StopPoint/{Uri.EscapeDataString(stationId)}");
        var data = await _http.GetFromJsonAsync<StopPointNameData>(apiUrl, cancellationToken);
        return string.IsNullOrWhiteSpace(data?.CommonName) ? "" : StripStationSuffix(data.CommonName);
    }

    private void SearchStations()
    {
        var query = StationSearch.Trim();
        if (query.Length < 2)
        {
            _stationSearchOptions = new[] { NoStationSearchHint };
            return;
        }

        RunInBackground(ct => FetchStationSearchAsync(query, ct));
    }

    private async Task FetchStationSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(query, _stationSearchLastQuery, StringComparison.OrdinalIgnoreCase) && _stationSearchOptions.Length > 0)
            {
                return;
            }

            var apiUrl = $"https://api.tfl.gov.uk/StopPoint/Search/{Uri.EscapeDataString(query)}";
            var queryArgs = new List<string>
            {
                "modes=tube,dlr,overground,elizabeth-line,tram"
            };

            if (!string.IsNullOrEmpty(_appKey))
            {
                queryArgs.Add($"app_key={Uri.EscapeDataString(_appKey)}");
            }

            apiUrl += "?" + string.Join("&", queryArgs);

            string[] options;
            var response = await _http.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                options = new[] { "No matches" };
            }
            else
            {
                var data = await response.Content.ReadFromJsonAsync<StopPointSearchResponse>(cancellationToken);
                var matches = (data?.Matches ?? Array.Empty<StopPointSearchMatch>())
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
                    .Where(IsLikelyRailStop)
                    .DistinctBy(m => m.Id)
                    .Take(12)
                    .Select(m => $"{m.Id} | {StripStationSuffix(m.Name)}")
                    .ToArray();
                options = matches.Length > 0 ? matches : new[] { "No matches" };
                _stationSearchLastQuery = query;
            }

            // The user may have kept typing while this was in flight; only the latest query may publish its results
            if (string.Equals(query, StationSearch.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _stationSearchOptions = options;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            _stationSearchOptions = new[] { "Search failed" };
        }
    }

    private static bool IsLikelyRailStop(StopPointSearchMatch match)
    {
        if (match.Id.StartsWith("940GZZ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var modes = match.Modes ?? Array.Empty<string>();
        return modes.Any(m =>
            string.Equals(m, "tube", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m, "dlr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m, "overground", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m, "elizabeth-line", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m, "tram", StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyStationId(string stationId)
    {
        StationId = stationId;
        ResetDeparturePaging();
        RestartStationPolling();
    }

    // Departures to show: platform filter and max count are applied here so changing them takes effect immediately
    private Departure[] GetDepartures()
    {
        var arrivals = _arrivals?.Value;
        if (arrivals == null)
        {
            return Array.Empty<Departure>();
        }

        IEnumerable<ArrivalData> filtered = arrivals;

        var platformFilter = PlatformFilter;
        if (!string.IsNullOrWhiteSpace(platformFilter))
        {
            filtered = filtered.Where(a =>
                a.PlatformName.Contains(platformFilter, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderBy(a => a.TimeToStation)
            .Take(MaxDepartures)
            .Select(a => new Departure
            {
                DestinationName = StripStationSuffix(string.IsNullOrWhiteSpace(a.Towards) ? a.DestinationName : a.Towards),
                TimeToStation = a.TimeToStation,
                PlatformName = a.PlatformName,
                LineName = a.LineName,
                LineId = a.LineId,
            })
            .ToArray();
    }

    private void ResetDeparturePaging()
    {
        _departurePage = 0;
        _lastDeparturePageSwitch = DateTime.MinValue;
        _isPageTransitioning = false;
        _transitionFromPage = 0;
        _transitionToPage = 0;
        _transitionScrollUp = true;
        _transitionStartedAt = DateTime.MinValue;
    }

    private static string StripStationSuffix(string name)
    {
        return name
            .Replace(" Underground Station", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Rail Station", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Station", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        // Fresh data restarts paging from the first page
        var updatedAt = _arrivals?.LastUpdated;
        if (updatedAt != _arrivalsSeenAt)
        {
            _arrivalsSeenAt = updatedAt;
            ResetDeparturePaging();
        }

        var maxCount = Math.Min(MaxDepartures, GetDepartures().Length);
        var totalPages = Math.Max(1, (int)Math.Ceiling(maxCount / (double)VisibleDepartureRows));

        if (totalPages <= 1)
        {
            ResetDeparturePaging();
            return;
        }

        if (_isPageTransitioning)
        {
            if (DateTime.Now - _transitionStartedAt >= _pageTransitionDuration)
            {
                _isPageTransitioning = false;
                _departurePage = _transitionToPage;
            }
            return;
        }

        if (_lastDeparturePageSwitch == DateTime.MinValue)
        {
            _lastDeparturePageSwitch = DateTime.Now;
            return;
        }

        if (DateTime.Now - _lastDeparturePageSwitch >= _departurePageInterval)
        {
            _transitionFromPage = _departurePage;
            _transitionToPage = (_departurePage + 1) % totalPages;
            // Normal paging: next page enters from below. Wrap to page 1: enters from above.
            _transitionScrollUp = _transitionToPage > _transitionFromPage;
            _isPageTransitioning = true;
            _transitionStartedAt = DateTime.Now;
            _lastDeparturePageSwitch = DateTime.Now;
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(StationId))
        {
            frame.DrawText(_font, 0, _font.BoundingBox.Y, _color, "No station");
            frame.DrawText(_font, 0, _font.BoundingBox.Y * 2, _color, "configured");
            return;
        }

        var departures = GetDepartures();
        var arrivals = _arrivals;

        if (departures.Length == 0 && (arrivals == null || (arrivals.Value == null && arrivals.Error == null)))
        {
            frame.DrawText(_font, 0, _font.BoundingBox.Y, _color, "Loading...");
            return;
        }

        if (departures.Length == 0)
        {
            frame.DrawText(_font, 0, _font.BoundingBox.Y, _color, "No departures");
            return;
        }

        var maxCount = Math.Min(MaxDepartures, departures.Length);
        var totalPages = Math.Max(1, (int)Math.Ceiling(maxCount / (double)VisibleDepartureRows));

        if (_isPageTransitioning)
        {
            var elapsed = DateTime.Now - _transitionStartedAt;
            var t = Math.Clamp(elapsed.TotalMilliseconds / _pageTransitionDuration.TotalMilliseconds, 0.0, 1.0);

            // Cubic bezier easing gives a smooth acceleration/deceleration profile.
            var eased = EvaluateBezierProgress(t, 0.22, 1.0, 0.36, 1.0);
            var pageHeight = 14 * VisibleDepartureRows;
            var fromOffset = _transitionScrollUp
                ? (int)Math.Round(-eased * pageHeight)
                : (int)Math.Round(eased * pageHeight);
            var toOffset = _transitionScrollUp
                ? fromOffset + pageHeight
                : fromOffset - pageHeight;

            DrawDeparturePage(frame, departures, _transitionFromPage, maxCount, fromOffset);
            DrawDeparturePage(frame, departures, _transitionToPage, maxCount, toOffset);
        }
        else
        {
            var activePage = Math.Clamp(_departurePage, 0, totalPages - 1);
            DrawDeparturePage(frame, departures, activePage, maxCount, 0);
        }

        frame.DrawHorizontalLine(14 * 3 + 5, frame.Width, _color);

        var statusPage = _isPageTransitioning
            ? Math.Clamp(_transitionToPage, 0, totalPages - 1)
            : Math.Clamp(_departurePage, 0, totalPages - 1);

        DrawLineStatusBar(frame, statusPage + 1, totalPages);
    }

    private void DrawDeparturePage(FrameBuffer frame, Departure[] departures, int page, int maxCount, int yOffset)
    {
        if (page < 0)
        {
            return;
        }

        var visibleDepartures = departures
            .Take(maxCount)
            .Skip(page * VisibleDepartureRows)
            .Take(VisibleDepartureRows)
            .ToArray();

        for (int i = 0; i < visibleDepartures.Length; i++)
        {
            var dep = visibleDepartures[i];
            int minsAway = Math.Max(0, dep.TimeToStation / 60);
            var departureNumber = page * VisibleDepartureRows + i + 1;
            var rowColor = GetDepartureRowColor(dep.LineId);
            DrawDepartureRow(frame, i + 1, departureNumber, dep.DestinationName, minsAway, rowColor, yOffset);
        }
    }

    private Pixel GetDepartureRowColor(string? lineId)
    {
        if (!ColorDeparturesByLine)
        {
            return _color;
        }

        var normalized = NormalizeLineId(lineId);
        if (TubeColors.TryGet(normalized, out var lineColor))
        {
            return lineColor;
        }

        return _color;
    }

    private static string NormalizeLineId(string? lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId))
        {
            return string.Empty;
        }

        var normalized = lineId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "london-overground" => "overground",
            "elizabeth-line" => "elizabeth",
            _ => normalized
        };
    }

    private static double EvaluateBezierProgress(double t, double p1x, double p1y, double p2x, double p2y)
    {
        // Solve x(u)=t with binary search, then return y(u) for a CSS-style cubic bezier.
        var low = 0.0;
        var high = 1.0;
        var u = t;

        for (var i = 0; i < 12; i++)
        {
            u = (low + high) * 0.5;
            var x = CubicBezier(u, p1x, p2x);
            if (x < t)
                low = u;
            else
                high = u;
        }

        return CubicBezier(u, p1y, p2y);
    }

    private static double CubicBezier(double t, double p1, double p2)
    {
        var inv = 1.0 - t;
        return 3.0 * inv * inv * t * p1
             + 3.0 * inv * t * t * p2
             + t * t * t;
    }

    private void DrawLineStatusBar(FrameBuffer frame, int currentPage, int totalPages)
    {
        // Bottom bar: y=49 to y=61 (13px), line-status tiles on the left
        const int tileStartY = 49;
        const int tileHeight = 13;
        const int tileWidth = 11;
        const int tileGap = 1;
        const int startX = 1;
        const int pad = 3;

        // QuiteSmall (5x7, offsetY=-1): charY = line + (y - 7 + 1) = line + (y - 6)
        // At y=61: renders from y=55 to y=61 — fits neatly inside the 13px tile.
        // ExtraSmall (4x6, offsetY=-1): renders from y=56 to y=61 — same baseline.
        const int textBaseline = tileStartY + tileHeight - 1; // = 61

        var statuses = _lineStatuses?.Value ?? Array.Empty<StationLineStatus>();
        int tileCount = statuses.Length;

        // ── Line-status tiles ─────────────────────────────────────────────────
        for (int i = 0; i < tileCount; i++)
        {
            var status = statuses[i];
            bool goodService = status.Severity >= 9;

            var baseColor = TubeColors.TryGet(status.LineId, out var c) ? c : new Pixel(100, 100, 100);

            // Bright half-dim for good service, heavily dimmed for disruptions
            Pixel tileColor = goodService ? baseColor / 2 : baseColor / 5;

            int x = startX + i * (tileWidth + tileGap);

            // Tile background
            frame.FillRect(new Rectangle(x, tileStartY, tileWidth, tileHeight), tileColor);

            // 2-letter abbreviation centred in tile
            var abbrev = LineAbbreviations.TryGetValue(status.LineId, out var a)
                ? a
                : status.LineId[..Math.Min(2, status.LineId.Length)].ToUpperInvariant();

            Pixel textColor = goodService ? new Pixel(255, 255, 255) : new Pixel(255, 140, 0);
            frame.DrawText(Fonts.ExtraSmall, x + 1, textBaseline, textColor, abbrev);

            // Red disruption pip at top-right corner
            if (!goodService)
            {
                frame.SetPixel(x + tileWidth - 1, tileStartY,     new Pixel(255, 0, 0));
                frame.SetPixel(x + tileWidth - 2, tileStartY,     new Pixel(255, 0, 0));
                frame.SetPixel(x + tileWidth - 1, tileStartY + 1, new Pixel(255, 0, 0));
            }
        }

        // ── Station name + live clock ─────────────────────────────────────────
        int tilesEndX = startX + tileCount * (tileWidth + tileGap);

        // Right-side clock: "14:32" — always shown, right-aligned
        var timeText = DateTime.Now.ToString("HH:mm");
        int charW = Fonts.QuiteSmall.BoundingBox.X; // 5px per char
        int timeTextWidth = timeText.Length * charW;
        int timeX = frame.Width - timeTextWidth;
        frame.DrawText(Fonts.QuiteSmall, timeX, textBaseline, new Pixel(0, 160, 180), timeText);

        if (totalPages > 1)
        {
            var pageText = $"{currentPage}/{totalPages}";
            int pageWidth = pageText.Length * charW;
            int pageX = timeX - pageWidth - 2;
            frame.DrawText(Fonts.QuiteSmall, pageX, textBaseline, new Pixel(200, 160, 0), pageText);
        }

        // Station name: left of clock, right of tiles
        var stationName = _stationNameData?.Value ?? "";
        if (!string.IsNullOrWhiteSpace(stationName))
        {
            int nameStartX = tilesEndX + pad;
            int maxNameWidth = timeX - nameStartX - pad;
            int maxChars = maxNameWidth / charW;

            if (maxChars > 0)
            {
                var display = stationName.ToUpperInvariant();
                if (display.Length > maxChars)
                    display = display[..(maxChars - 1)] + "~";

                frame.DrawText(Fonts.QuiteSmall, nameStartX, textBaseline, new Pixel(200, 200, 200), display);
            }
        }
    }

    private void DrawDepartureRow(FrameBuffer frame, int rowPosition, int departureNumber, string station, int minsAway, Pixel textColor, int yOffset = 0)
    {
        string suffix = "due ";
        if (minsAway > 0)
        {
            string minsText = minsAway == 1 ? "min " : "mins";
            suffix = $"{minsAway}{minsText}";
        }

        string formatStationLength = $"-{26 - suffix.Length}";

        string text = $"{departureNumber} {string.Format($"{{0,{formatStationLength}}}", station)}{suffix}";

        // Rows scroll between pages, so clip them to the departures area (inclusive of the bottom row)
        frame.PushClip(new Rectangle(0, DeparturesClipTopY, frame.Width, DeparturesClipBottomY - DeparturesClipTopY + 1));
        frame.DrawText(_font, 0, 14 * rowPosition + yOffset, textColor, text);
        frame.PopClip();
    }
}
