using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;
using LedMatrixOS.Graphics;

namespace LedMatrixOS.Apps;

public class TubeDeparturesApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "tube-departures";
    public override string Name => "Tube Departures";
    public override int FrameRate => 20;

    private BdfFont _font = Fonts.Big;
    private Pixel _color = new Pixel(255, 120, 0);

    // Configurable settings
    private string _stationId = "";
    private string _stationSearch = "";
    private string _platformFilter = "";
    private int _maxDepartures = 3;

    private string? _appKey;
    private volatile Departure[] _departures = Array.Empty<Departure>();
    private volatile bool _isLoading = true;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private DateTime _lastRefresh = DateTime.MinValue;
    private volatile StationLineStatus[] _stationLineStatuses = Array.Empty<StationLineStatus>();
    private volatile string[] _stationLineIds = Array.Empty<string>();
    private readonly TimeSpan _lineStatusRefreshInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastLineStatusRefresh = DateTime.MinValue;
    private volatile string _stationName = "";
    private bool _stationNameFetched = false;
    private volatile string[] _stationSearchOptions = new[] { "Type at least 2 chars" };
    private volatile bool _stationSearchDirty;
    private string _stationSearchLastQuery = "";
    private readonly TimeSpan _departurePageInterval = TimeSpan.FromSeconds(4);
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

    private class Departure
    {
        public string DestinationName { get; set; } = "";
        public int TimeToStation { get; set; }
        public string PlatformName { get; set; } = "";
        public string LineName { get; set; } = "";
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

    private static readonly Dictionary<string, Pixel> TubeLineColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bakerloo",         new Pixel(156, 105, 56)  },
        { "central",          new Pixel(220, 36,  35)  },
        { "circle",           new Pixel(255, 206, 0)   },
        { "district",         new Pixel(0,   114, 41)  },
        { "hammersmith-city", new Pixel(215, 153, 175) },
        { "jubilee",          new Pixel(161, 165, 167) },
        { "metropolitan",     new Pixel(155, 0,   88)  },
        { "northern",         new Pixel(90,  90,  90)  },
        { "piccadilly",       new Pixel(0,   24,  168) },
        { "victoria",         new Pixel(0,   160, 226) },
        { "waterloo-city",    new Pixel(100, 200, 150) },
        { "dlr",              new Pixel(0,   175, 173) },
        { "elizabeth",        new Pixel(126, 91,  198) },
        { "overground",       new Pixel(232, 106, 16)  },
    };

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

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("stationSearch", "Station Search", "Type a station name (e.g. Baker Street).", AppSettingType.String, "", _stationSearch),
            new AppSetting("stationSelect", "Station Select", "Choose a result to set the station automatically.", AppSettingType.Select, "", "", Options: _stationSearchOptions),
            new AppSetting("stationId", "Station ID", "TfL Naptan ID (auto-filled when you select from Station Select).", AppSettingType.String, "", _stationId),
            new AppSetting("platformFilter", "Platform Filter", "Filter by platform name (e.g. 'Eastbound'). Leave empty to show all platforms.", AppSettingType.String, "", _platformFilter),
            new AppSetting("maxDepartures", "Max Departures", "Number of departures to cycle through", AppSettingType.Integer, 3, _maxDepartures, 1, 12),
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "stationSearch":
                _stationSearch = value.ToString() ?? "";
                _stationSearchDirty = true;
                if (_stationSearch.Trim().Length < 2)
                {
                    _stationSearchOptions = new[] { "Type at least 2 chars" };
                }
                break;
            case "stationSelect":
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
                break;
            case "stationId":
                ApplyStationId(value.ToString() ?? "");
                break;
            case "platformFilter":
                _platformFilter = value.ToString() ?? "";
                _departures = Array.Empty<Departure>();
                _lastRefresh = DateTime.MinValue;
                break;
            case "maxDepartures":
                _maxDepartures = Math.Clamp(CoerceIntSetting(value, _maxDepartures), 1, 12);
                ResetDeparturePaging();
                break;
        }
    }

    private static int CoerceIntSetting(object value, int fallback)
    {
        try
        {
            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var n))
                {
                    return n;
                }

                if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var parsed))
                {
                    return parsed;
                }

                return fallback;
            }

            if (value is int i)
            {
                return i;
            }

            if (value is long l)
            {
                return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            }

            if (value is string s && int.TryParse(s, out var fromString))
            {
                return fromString;
            }

            return Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
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
            _stationId = configuredStation;
        }

        var configuredPlatform = configuration["TubeDeparturesApp:PlatformFilter"];
        if (!string.IsNullOrEmpty(configuredPlatform))
        {
            _platformFilter = configuredPlatform;
        }

        if (int.TryParse(configuration["TubeDeparturesApp:MaxDepartures"], out var maxDep) && maxDep > 0)
        {
            _maxDepartures = Math.Clamp(maxDep, 1, 12);
        }

        StartBackgroundDataLoading();

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    private void StartBackgroundDataLoading()
    {
        RunInBackground(async ct =>
        {
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(_stationId) &&
                            (DateTime.Now - _lastRefresh >= _refreshInterval || _departures.Length == 0))
                        {
                            await FetchDeparturesAsync(httpClient, ct);
                        }

                        var lineIds = _stationLineIds;
                        if (lineIds.Length > 0 &&
                            (DateTime.Now - _lastLineStatusRefresh >= _lineStatusRefreshInterval || _stationLineStatuses.Length == 0))
                        {
                            await FetchLineStatusesAsync(httpClient, lineIds, ct);
                        }

                        if (_stationSearchDirty)
                        {
                            await FetchStationSearchAsync(httpClient, ct);
                        }

                        if (!_stationNameFetched && !string.IsNullOrWhiteSpace(_stationId))
                        {
                            await FetchStationNameAsync(httpClient, ct);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        _isLoading = false;
                        await Task.Delay(TimeSpan.FromMinutes(1), ct);
                    }
                }
            }
            finally
            {
                httpClient.Dispose();
            }
        });
    }

    private async Task FetchDeparturesAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        _isLoading = true;

        try
        {
            var apiUrl = $"https://api.tfl.gov.uk/StopPoint/{Uri.EscapeDataString(_stationId)}/Arrivals";
            if (!string.IsNullOrEmpty(_appKey))
            {
                apiUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";
            }

            var response = await httpClient.GetAsync(apiUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var arrivals = JsonSerializer.Deserialize<ArrivalData[]>(content, options) ?? Array.Empty<ArrivalData>();

                IEnumerable<ArrivalData> filtered = arrivals;

                if (!string.IsNullOrWhiteSpace(_platformFilter))
                {
                    filtered = filtered.Where(a =>
                        a.PlatformName.Contains(_platformFilter, StringComparison.OrdinalIgnoreCase));
                }

                _departures = filtered
                    .OrderBy(a => a.TimeToStation)
                    .Take(_maxDepartures)
                    .Select(a => new Departure
                    {
                        DestinationName = StripStationSuffix(string.IsNullOrWhiteSpace(a.Towards) ? a.DestinationName : a.Towards),
                        TimeToStation = a.TimeToStation,
                        PlatformName = a.PlatformName,
                        LineName = a.LineName,
                    })
                    .ToArray();

                // Collect all unique line IDs that serve this station (from unfiltered arrivals)
                _stationLineIds = arrivals
                    .Where(a => !string.IsNullOrWhiteSpace(a.LineId))
                    .Select(a => a.LineId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id)
                    .ToArray();

                _lastRefresh = DateTime.Now;
                ResetDeparturePaging();
            }

            _isLoading = false;
        }
        catch
        {
            _isLoading = false;
            throw;
        }
    }

    private async Task FetchLineStatusesAsync(HttpClient httpClient, string[] lineIds, CancellationToken cancellationToken)
    {
        try
        {
            var joinedIds = string.Join(",", lineIds.Select(Uri.EscapeDataString));
            var apiUrl = $"https://api.tfl.gov.uk/Line/{joinedIds}/Status";
            if (!string.IsNullOrEmpty(_appKey))
            {
                apiUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";
            }

            var response = await httpClient.GetAsync(apiUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var lines = JsonSerializer.Deserialize<TubeLineStatusResponse[]>(content, options) ?? Array.Empty<TubeLineStatusResponse>();

                _stationLineStatuses = lines
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

                _lastLineStatusRefresh = DateTime.Now;
            }
        }
        catch
        {
            // If line status fetch fails, keep existing data
        }
    }

    private async Task FetchStationNameAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            var apiUrl = $"https://api.tfl.gov.uk/StopPoint/{Uri.EscapeDataString(_stationId)}";
            if (!string.IsNullOrEmpty(_appKey))
                apiUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";

            var response = await httpClient.GetAsync(apiUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<StopPointNameData>(content, options);
                if (!string.IsNullOrWhiteSpace(data?.CommonName))
                    _stationName = StripStationSuffix(data.CommonName);
            }
        }
        catch
        {
            // Silently fall back to empty — station name is decorative
        }
        finally
        {
            _stationNameFetched = true;
        }
    }

    private async Task FetchStationSearchAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            var query = _stationSearch.Trim();
            _stationSearchDirty = false;

            if (query.Length < 2)
            {
                _stationSearchOptions = new[] { "Type at least 2 chars" };
                return;
            }

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

            var response = await httpClient.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _stationSearchOptions = new[] { "No matches" };
                return;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<StopPointSearchResponse>(content, options);

            var matches = (data?.Matches ?? Array.Empty<StopPointSearchMatch>())
                .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
                .Where(IsLikelyRailStop)
                .DistinctBy(m => m.Id)
                .Take(12)
                .Select(m => $"{m.Id} | {StripStationSuffix(m.Name)}")
                .ToArray();

            _stationSearchOptions = matches.Length > 0 ? matches : new[] { "No matches" };
            _stationSearchLastQuery = query;
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
        _stationId = stationId;
        _departures = Array.Empty<Departure>();
        _lastRefresh = DateTime.MinValue;
        ResetDeparturePaging();
        _stationName = "";
        _stationNameFetched = false;
        _stationLineIds = Array.Empty<string>();
        _stationLineStatuses = Array.Empty<StationLineStatus>();
        _lastLineStatusRefresh = DateTime.MinValue;
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
        var maxCount = Math.Min(_maxDepartures, _departures.Length);
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
        if (string.IsNullOrWhiteSpace(_stationId))
        {
            frame.DrawText(_font, 0, _font.BoundingBox.Y, _color, "No station");
            frame.DrawText(_font, 0, _font.BoundingBox.Y * 2, _color, "configured");
            return;
        }

        if (_isLoading && _departures.Length == 0)
        {
            frame.DrawText(_font, 0, _font.BoundingBox.Y, _color, "Loading...");
            return;
        }

        if (_departures.Length == 0)
        {
            frame.DrawText(_font, 0, _font.BoundingBox.Y, _color, "No departures");
            return;
        }

        var maxCount = Math.Min(_maxDepartures, _departures.Length);
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

            DrawDeparturePage(frame, _transitionFromPage, maxCount, fromOffset);
            DrawDeparturePage(frame, _transitionToPage, maxCount, toOffset);
        }
        else
        {
            var activePage = Math.Clamp(_departurePage, 0, totalPages - 1);
            DrawDeparturePage(frame, activePage, maxCount, 0);
        }
        
        frame.DrawHorizontalLine(14 * 3 + 5, frame.Width, _color);

        var statusPage = _isPageTransitioning
            ? Math.Clamp(_transitionToPage, 0, totalPages - 1)
            : Math.Clamp(_departurePage, 0, totalPages - 1);

        DrawLineStatusBar(frame, statusPage + 1, totalPages);
    }

    private void DrawDeparturePage(FrameBuffer frame, int page, int maxCount, int yOffset)
    {
        if (page < 0)
        {
            return;
        }

        var visibleDepartures = _departures
            .Take(maxCount)
            .Skip(page * VisibleDepartureRows)
            .Take(VisibleDepartureRows)
            .ToArray();

        for (int i = 0; i < visibleDepartures.Length; i++)
        {
            var dep = visibleDepartures[i];
            int minsAway = Math.Max(0, dep.TimeToStation / 60);
            var departureNumber = page * VisibleDepartureRows + i + 1;
            DrawDepartureRow(frame, i + 1, departureNumber, dep.DestinationName, minsAway, yOffset);
        }
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

        var statuses = _stationLineStatuses;
        int tileCount = statuses.Length;

        // ── Line-status tiles ─────────────────────────────────────────────────
        for (int i = 0; i < tileCount; i++)
        {
            var status = statuses[i];
            bool goodService = status.Severity >= 9;

            var baseColor = TubeLineColors.TryGetValue(status.LineId, out var c) ? c : new Pixel(100, 100, 100);

            // Bright half-dim for good service, heavily dimmed for disruptions
            Pixel tileColor = goodService
                ? new Pixel((byte)(baseColor.R / 2), (byte)(baseColor.G / 2), (byte)(baseColor.B / 2))
                : new Pixel((byte)(baseColor.R / 5), (byte)(baseColor.G / 5), (byte)(baseColor.B / 5));

            int x = startX + i * (tileWidth + tileGap);

            // Tile background
            for (int dx = 0; dx < tileWidth; dx++)
                for (int dy = 0; dy < tileHeight; dy++)
                    frame.SetPixel(x + dx, tileStartY + dy, tileColor);

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
        var stationName = _stationName;
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

    private void DrawDepartureRow(FrameBuffer frame, int rowPosition, int departureNumber, string station, int minsAway, int yOffset = 0)
    {
        string suffix = "due ";
        if (minsAway > 0)
        {
            string minsText = minsAway == 1 ? "min " : "mins";
            suffix = $"{minsAway}{minsText}";
        }

        string formatStationLength = $"-{26 - suffix.Length}";
        
        string text = $"{departureNumber} {string.Format($"{{0,{formatStationLength}}}", station)}{suffix}";
        DrawClippedText(frame, _font, 0, 14 * rowPosition + yOffset, _color, text, DeparturesClipTopY, DeparturesClipBottomY);
    }

    private static void DrawClippedText(FrameBuffer frame, BdfFont font, int x, int y, Pixel color, string text, int clipTopY, int clipBottomY)
    {
        // TextExtensions maps font bitmap line -> framebuffer Y using this baseline transform.
        var baselineOffset = y - font.BoundingBox.Y - font.BoundingBox.OffsetY;

        var startLine = Math.Max(0, clipTopY - baselineOffset);
        var endLineExclusive = clipBottomY - baselineOffset + 1;

        if (endLineExclusive <= startLine)
        {
            return;
        }

        frame.DrawText(font, x, y, color, text, startLine, endLineExclusive);
    }
}
