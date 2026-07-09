using LedMatrixOS.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using BdfFontParser;
using LedMatrixOS.Graphics.Text;

namespace LedMatrixOS.Apps;

public class TubeLineApp : MatrixAppBase, IConfigurableApp
{
    public override string Id => "tube-line";
    public override string Name => "Tube Line";
    public override int FrameRate => 20;

    private volatile StopPoint[] _lineStops = Array.Empty<StopPoint>();
    private volatile TrainPosition[] _trainPositions = Array.Empty<TrainPosition>();
    private volatile bool _isLoading = true;
    private volatile bool _hasLoadedInitialData;
    private volatile string _selectedLineId = "jubilee"; // Default to Jubilee line

    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);
    private DateTime _lastRefresh = DateTime.MinValue;
    private DateTime _lastSuccessfulUpdateUtc = DateTime.MinValue;
    private string? _appKey;
    private Dictionary<string, int> _stopIndexById = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _stopIndexByName = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _lastUpdateTime = TimeSpan.Zero;
    private volatile string _branchMode = "Auto";
    private volatile string _branchRoute = "auto";
    private volatile string[] _branchRouteOptions = new[] { "auto" };

    // TfL-inspired palette (used for ImageSharp geometry only)
    private static readonly Color TflBlue = Color.FromRgb(0, 25, 168);

    private static readonly Dictionary<string, Color> TubeLineColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bakerloo"] = Color.FromRgb(178, 99, 0),
        ["central"] = Color.FromRgb(220, 36, 31),
        ["circle"] = Color.FromRgb(255, 211, 41),
        ["district"] = Color.FromRgb(0, 125, 50),
        ["hammersmith-city"] = Color.FromRgb(244, 169, 190),
        ["jubilee"] = Color.FromRgb(161, 165, 167),
        ["metropolitan"] = Color.FromRgb(155, 0, 88),
        ["northern"] = Color.FromRgb(100, 100, 100),
        ["piccadilly"] = Color.FromRgb(0, 15, 159),
        ["victoria"] = Color.FromRgb(0, 152, 216),
        ["waterloo-city"] = Color.FromRgb(147, 206, 186),
        ["dlr"] = Color.FromRgb(0, 175, 173),
        ["elizabeth"] = Color.FromRgb(126, 91, 198),
        ["london-overground"] = Color.FromRgb(232, 106, 16),
        ["liberty"] = Color.FromRgb(124, 127, 130),
        ["lioness"] = Color.FromRgb(255, 201, 47),
        ["mildmay"] = Color.FromRgb(0, 102, 177),
        ["suffragette"] = Color.FromRgb(0, 156, 73),
        ["weaver"] = Color.FromRgb(149, 67, 103),
        ["windrush"] = Color.FromRgb(220, 36, 31),
        ["tram"] = Color.FromRgb(132, 189, 0)
    };

    private static readonly string[] SupportedLineIds =
    {
        "bakerloo",
        "central",
        "circle",
        "district",
        "hammersmith-city",
        "jubilee",
        "metropolitan",
        "northern",
        "piccadilly",
        "victoria",
        "waterloo-city",
        "dlr",
        "elizabeth",
        "london-overground",
        "liberty",
        "lioness",
        "mildmay",
        "suffragette",
        "weaver",
        "windrush",
        "tram"
    };

    private class StopPoint
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lon")]
        public double Longitude { get; set; }
    }

    private class TrainPosition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("currentLocation")]
        public string CurrentLocation { get; set; } = "";

        public string Direction { get; set; } = "";
        public string DestinationName { get; set; } = "";
        public int TimeToStation { get; set; }

        // 0-100% along the line
        public double TargetPositionPercent { get; set; }
        public double DisplayPositionPercent { get; set; }
    }

    private class LineRouteData
    {
        [JsonPropertyName("stations")]
        public StopPointData[]? Stations { get; set; }

        [JsonPropertyName("orderedLineRoutes")]
        public OrderedLineRouteData[]? OrderedLineRoutes { get; set; }

        [JsonPropertyName("stopPointSequences")]
        public StopPointSequenceData[]? StopPointSequences { get; set; }
    }

    private class OrderedLineRouteData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("naptanIds")]
        public string[]? NaptanIds { get; set; }

        [JsonPropertyName("serviceType")]
        public string ServiceType { get; set; } = "";
    }

    private class StopPointSequenceData
    {
        [JsonPropertyName("stopPoint")]
        public StopPointData[]? StopPoint { get; set; }

        [JsonPropertyName("serviceType")]
        public string ServiceType { get; set; } = "";
    }

    private class StopPointData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("stationId")]
        public string StationId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("order")]
        public int Order { get; set; }

        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lon")]
        public double Longitude { get; set; }
    }

    private sealed record RouteVariant(string Label, string[] StopIds, bool IsRegular);

    public IEnumerable<AppSetting> GetSettings()
    {
        return
        [
            new AppSetting(
                "lineId",
                "Line",
                "TfL line ID to render.",
                AppSettingType.Select,
                "jubilee",
                _selectedLineId,
                Options: SupportedLineIds),
            new AppSetting(
                "branchMode",
                "Branch Mode",
                "Auto picks the best branch path, Pinned keeps a specific branch route.",
                AppSettingType.Select,
                "Auto",
                _branchMode,
                Options: new[] { "Auto", "Pinned" }),
            new AppSetting(
                "branchRoute",
                "Branch Route",
                "Route path to use when Branch Mode is Pinned.",
                AppSettingType.Select,
                "auto",
                _branchRoute,
                Options: _branchRouteOptions)
        ];
    }

    public void UpdateSetting(string key, object value)
    {
        if (!string.Equals(key, "lineId", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(key, "branchMode", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedMode = NormalizeBranchMode(value?.ToString());
                if (!string.Equals(normalizedMode, _branchMode, StringComparison.OrdinalIgnoreCase))
                {
                    _branchMode = normalizedMode;
                    ResetLineDataState();
                }
            }
            else if (string.Equals(key, "branchRoute", StringComparison.OrdinalIgnoreCase))
            {
                var route = value?.ToString() ?? "auto";
                if (!_branchRouteOptions.Contains(route, StringComparer.OrdinalIgnoreCase))
                {
                    route = "auto";
                }

                if (!string.Equals(route, _branchRoute, StringComparison.OrdinalIgnoreCase))
                {
                    _branchRoute = route;
                    ResetLineDataState();
                }
            }

            return;
        }

        var normalized = NormalizeLineId(value?.ToString());
        if (string.Equals(normalized, _selectedLineId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedLineId = normalized;
        _branchRoute = "auto";
        _branchRouteOptions = new[] { "auto" };
        ResetLineDataState();
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        // Read TFL API key and selected line from configuration
        _appKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_appKey))
        {
            Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");
        }

        var configuredLine = configuration["TubeLineApp:LineId"];
        if (!string.IsNullOrEmpty(configuredLine))
        {
            _selectedLineId = NormalizeLineId(configuredLine);
        }

        // Start background data loading
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
                        if (DateTime.Now - _lastRefresh >= _refreshInterval || _lineStops.Length == 0)
                        {
                            // Fetch line route and train positions
                            await FetchLineDataAsync(httpClient, ct);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
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

    private async Task FetchLineDataAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (!_hasLoadedInitialData)
        {
            _isLoading = true;
        }

        try
        {
            var routeData = await FetchRouteDataAsync(httpClient, cancellationToken);
            if (routeData != null)
            {
                var routeVariants = BuildRouteVariants(routeData);
                _branchRouteOptions = new[] { "auto" }
                    .Concat(routeVariants.Select(v => v.Label).Distinct(StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                if (!_branchRouteOptions.Contains(_branchRoute, StringComparer.OrdinalIgnoreCase))
                {
                    _branchRoute = "auto";
                }

                var selectedVariant = SelectRouteVariant(routeVariants);
                _lineStops = BuildOrderedStops(routeData, selectedVariant?.StopIds);

                var stopIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var stopIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < _lineStops.Length; i++)
                {
                    foreach (var key in GetStopIdCandidates(_lineStops[i].Id))
                    {
                        if (!stopIndexById.ContainsKey(key))
                        {
                            stopIndexById[key] = i;
                        }
                    }

                    var normalizedName = NormalizeStationName(_lineStops[i].Name);
                    if (!string.IsNullOrWhiteSpace(normalizedName) && !stopIndexByName.ContainsKey(normalizedName))
                    {
                        stopIndexByName[normalizedName] = i;
                    }
                }

                _stopIndexById = stopIndexById;
                _stopIndexByName = stopIndexByName;

                // Fetch train positions for this line
                await FetchTrainPositionsAsync(httpClient, cancellationToken);

                if (_lineStops.Length > 0)
                {
                    _hasLoadedInitialData = true;
                    _isLoading = false;
                }
            }

            _lastRefresh = DateTime.Now;
        }
        catch
        {
            if (!_hasLoadedInitialData)
            {
                _isLoading = false;
            }
            throw;
        }
    }

    private async Task<LineRouteData?> FetchRouteDataAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var direction in new[] { "all", "inbound", "outbound" })
        {
            var routeUrl = $"https://api.tfl.gov.uk/Line/{_selectedLineId}/Route/Sequence/{direction}";
            if (!string.IsNullOrEmpty(_appKey))
            {
                routeUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";
            }

            var routeResponse = await httpClient.GetAsync(routeUrl, cancellationToken);
            if (!routeResponse.IsSuccessStatusCode)
            {
                continue;
            }

            var content = await routeResponse.Content.ReadAsStringAsync(cancellationToken);
            var routeData = JsonSerializer.Deserialize<LineRouteData>(content, options);
            if (routeData?.Stations is { Length: > 0 } || routeData?.StopPointSequences is { Length: > 0 })
            {
                return routeData;
            }
        }

        return null;
    }

    private static RouteVariant[] BuildRouteVariants(LineRouteData routeData)
    {
        var variants = new List<RouteVariant>();

        if (routeData.OrderedLineRoutes != null)
        {
            foreach (var route in routeData.OrderedLineRoutes)
            {
                if (route.NaptanIds is not { Length: > 1 })
                {
                    continue;
                }

                var ids = route.NaptanIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
                if (ids.Length < 2)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(route.Name)
                    ? $"{ids[0]} -> {ids[^1]}"
                    : route.Name;

                variants.Add(new RouteVariant(
                    Label: label,
                    StopIds: ids,
                    IsRegular: string.Equals(route.ServiceType, "Regular", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return variants
            .GroupBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.StopIds.Length).First())
            .ToArray();
    }

    private RouteVariant? SelectRouteVariant(RouteVariant[] variants)
    {
        if (variants.Length == 0)
        {
            return null;
        }

        if (string.Equals(_branchMode, "Pinned", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_branchRoute, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var pinned = variants.FirstOrDefault(v => string.Equals(v.Label, _branchRoute, StringComparison.OrdinalIgnoreCase));
            if (pinned != null)
            {
                return pinned;
            }
        }

        return variants
            .OrderByDescending(v => v.IsRegular)
            .ThenByDescending(v => v.StopIds.Length)
            .First();
    }

    private static string NormalizeLineId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "jubilee";
        }

        var normalized = raw.Trim().ToLowerInvariant();
        normalized = normalized.Replace(" ", "-").Replace("_", "-");

        if (normalized.EndsWith("-line", StringComparison.Ordinal))
        {
            normalized = normalized[..^5];
        }

        // Backwards-compatible aliases for Overground naming.
        if (normalized is "overground")
        {
            normalized = "london-overground";
        }
        else if (normalized is "mildmay-line" or "mildmayline")
        {
            normalized = "mildmay";
        }
        else if (normalized is "windrush-line" or "windrushline")
        {
            normalized = "windrush";
        }
        else if (normalized is "weaver-line" or "weaverline")
        {
            normalized = "weaver";
        }
        else if (normalized is "suffragette-line" or "suffragetteline")
        {
            normalized = "suffragette";
        }
        else if (normalized is "lioness-line" or "lionessline")
        {
            normalized = "lioness";
        }
        else if (normalized is "liberty-line" or "libertyline")
        {
            normalized = "liberty";
        }

        return SupportedLineIds.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : "jubilee";
    }

    private static string NormalizeBranchMode(string? raw)
    {
        if (string.Equals(raw, "Pinned", StringComparison.OrdinalIgnoreCase))
        {
            return "Pinned";
        }

        return "Auto";
    }

    private void ResetLineDataState()
    {
        _lineStops = Array.Empty<StopPoint>();
        _trainPositions = Array.Empty<TrainPosition>();
        _stopIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _stopIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _hasLoadedInitialData = false;
        _isLoading = true;
        _lastRefresh = DateTime.MinValue;
        _lastSuccessfulUpdateUtc = DateTime.MinValue;
    }

    private static StopPoint[] BuildOrderedStops(LineRouteData routeData, string[]? preferredOrderedIds = null)
    {
        var stopById = new Dictionary<string, StopPoint>(StringComparer.OrdinalIgnoreCase);

        if (routeData.Stations != null)
        {
            foreach (var station in routeData.Stations)
            {
                var id = GetStopId(station);
                if (string.IsNullOrWhiteSpace(id)) continue;

                stopById[id] = new StopPoint
                {
                    Id = id,
                    Name = station.Name,
                    Latitude = station.Latitude,
                    Longitude = station.Longitude
                };
            }
        }

        if (routeData.StopPointSequences != null)
        {
            foreach (var sequence in routeData.StopPointSequences)
            {
                if (sequence.StopPoint == null) continue;

                foreach (var station in sequence.StopPoint)
                {
                    var id = GetStopId(station);
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    if (!stopById.ContainsKey(id))
                    {
                        stopById[id] = new StopPoint
                        {
                            Id = id,
                            Name = station.Name,
                            Latitude = station.Latitude,
                            Longitude = station.Longitude
                        };
                    }
                }
            }
        }

        var orderedIds = new List<string>();

        if (preferredOrderedIds is { Length: > 1 })
        {
            orderedIds.AddRange(preferredOrderedIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        // Primary: orderedLineRoutes gives canonical route order.
        var orderedRoute = routeData.OrderedLineRoutes?
            .Where(r => r.NaptanIds is { Length: > 0 })
            .OrderByDescending(r => string.Equals(r.ServiceType, "Regular", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.NaptanIds!.Length)
            .FirstOrDefault();

        if (orderedIds.Count == 0 && orderedRoute?.NaptanIds != null)
        {
            orderedIds.AddRange(orderedRoute.NaptanIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        // Fallback: stopPointSequences stopPoint order.
        if (orderedIds.Count == 0)
        {
            var sequence = routeData.StopPointSequences?
                .Where(s => s.StopPoint is { Length: > 0 })
                .OrderByDescending(s => string.Equals(s.ServiceType, "Regular", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => s.StopPoint!.Length)
                .FirstOrDefault();

            if (sequence?.StopPoint != null)
            {
                orderedIds.AddRange(sequence.StopPoint
                    .Select(GetStopId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))!);
            }
        }

        // Last fallback: stations as provided.
        if (orderedIds.Count == 0 && routeData.Stations != null)
        {
            orderedIds.AddRange(routeData.Stations
                .Select(GetStopId)
                .Where(id => !string.IsNullOrWhiteSpace(id))!);
        }

        var result = new List<StopPoint>();
        foreach (var id in orderedIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (stopById.TryGetValue(id, out var stop))
            {
                result.Add(stop);
            }
            else
            {
                result.Add(new StopPoint { Id = id, Name = id });
            }
        }

        // Keep map left-to-right geographically (west to east) for easier reading on a horizontal display.
        if (result.Count >= 2 && result[0].Longitude > result[^1].Longitude)
        {
            result.Reverse();
        }

        return result.ToArray();
    }

    private static string GetStopId(StopPointData station)
    {
        if (!string.IsNullOrWhiteSpace(station.StationId)) return station.StationId;
        return station.Id;
    }

    private async Task FetchTrainPositionsAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            // Get all vehicles on this line
            var vehiclesUrl = $"https://api.tfl.gov.uk/Line/{_selectedLineId}/Arrivals";
            if (!string.IsNullOrEmpty(_appKey))
            {
                vehiclesUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";
            }

            var response = await httpClient.GetAsync(vehiclesUrl, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var arrivalsData = JsonSerializer.Deserialize<ArrivalData[]>(content, options);

                if (arrivalsData != null && arrivalsData.Length > 0)
                {
                    // Group by vehicle ID and get the current position
                    // Sort by expected arrival time to get the current/next arriving train status
                    var trainData = arrivalsData
                        .Where(a => !string.IsNullOrEmpty(a.VehicleId) && !string.IsNullOrEmpty(a.NaptanId))
                        .GroupBy(a => a.VehicleId)
                        .Select(g =>
                        {
                            // Get the earliest expected arrival (most immediate for this vehicle)
                            var arrival = g.OrderBy(a => a.TimeToStation).First();

                            if (!TryCalculateTrainPosition(arrival.NaptanId, arrival.StationName, out var positionPercent))
                            {
                                return null;
                            }

                            return new TrainPosition
                            {
                                Id = g.Key,
                                CurrentLocation = arrival.StationName,
                                Direction = arrival.Direction,
                                DestinationName = arrival.DestinationName,
                                TimeToStation = arrival.TimeToStation,
                                TargetPositionPercent = positionPercent,
                                DisplayPositionPercent = GetPreviousDisplayPosition(g.Key)
                            };
                        })
                        .Where(t => t != null)
                        .Select(t => t!)
                        .ToArray();

                    // Initialize new trains at their target to avoid jump-in from zero.
                    foreach (var train in trainData)
                    {
                        if (train.DisplayPositionPercent <= 0)
                        {
                            train.DisplayPositionPercent = train.TargetPositionPercent;
                        }
                    }

                    _trainPositions = trainData;
                    _lastSuccessfulUpdateUtc = DateTime.UtcNow;
                }
            }
        }
        catch
        {
            // If we can't get vehicle data, just show the line
            _trainPositions = Array.Empty<TrainPosition>();
        }
    }

    private double GetPreviousDisplayPosition(string vehicleId)
    {
        var existing = _trainPositions.FirstOrDefault(t => t.Id == vehicleId);
        return existing?.DisplayPositionPercent ?? -1;
    }

    private bool TryCalculateTrainPosition(string currentStopId, string stationName, out double positionPercent)
    {
        positionPercent = 0;
        if (string.IsNullOrWhiteSpace(currentStopId) || _lineStops.Length == 0)
            return false;

        var stopIndex = -1;
        foreach (var key in GetStopIdCandidates(currentStopId))
        {
            if (_stopIndexById.TryGetValue(key, out stopIndex))
            {
                break;
            }
        }

        // Fallback by normalized station name when IDs don't align.
        if (stopIndex < 0)
        {
            var normalizedName = NormalizeStationName(stationName);
            if (!string.IsNullOrWhiteSpace(normalizedName) && _stopIndexByName.TryGetValue(normalizedName, out var nameIndex))
            {
                stopIndex = nameIndex;
            }
        }

        if (stopIndex < 0)
        {
            return false;
        }

        // Return percentage along the line
        positionPercent = (stopIndex / Math.Max(1.0, _lineStops.Length - 1)) * 100;
        return true;
    }

    private static string NormalizeStationName(string? stationName)
    {
        if (string.IsNullOrWhiteSpace(stationName))
        {
            return string.Empty;
        }

        return stationName
            .Trim()
            .Replace(" Underground Station", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Rail Station", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Station", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" & ", " and ", StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();
    }

    private static IEnumerable<string> GetStopIdCandidates(string? stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            yield break;
        }

        var raw = stationId.Trim();
        var normalized = raw.ToUpperInvariant();
        yield return normalized;

        // e.g. "1000013/940GZZLUWLO" -> "940GZZLUWLO"
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
        {
            var afterSlash = normalized[(slashIndex + 1)..];
            yield return afterSlash;
            normalized = afterSlash;
        }

        // e.g. "WLO" -> "940GZZLUWLO"
        if (normalized.Length == 3 && normalized.All(char.IsLetterOrDigit))
        {
            yield return $"940GZZLU{normalized}";
        }

        // e.g. "940GZZLUWLO" -> "WLO"
        if (normalized.StartsWith("940GZZLU", StringComparison.Ordinal) && normalized.Length > 8)
        {
            yield return normalized[8..];
        }
    }

    private class ArrivalData
    {
        [JsonPropertyName("vehicleId")]
        public string VehicleId { get; set; } = "";

        [JsonPropertyName("stationName")]
        public string StationName { get; set; } = "";

        [JsonPropertyName("naptanId")]
        public string NaptanId { get; set; } = "";

        [JsonPropertyName("timeToStation")]
        public int TimeToStation { get; set; }

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = "";

        [JsonPropertyName("destinationName")]
        public string DestinationName { get; set; } = "";
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        var dt = (deltaTime - _lastUpdateTime).TotalSeconds;
        _lastUpdateTime = deltaTime;

        if (dt <= 0 || _trainPositions.Length == 0)
        {
            return;
        }

        // Exponential smoothing for smooth motion between refreshes.
        var smoothing = 1.0 - Math.Exp(-dt * 3.0);
        foreach (var train in _trainPositions)
        {
            train.DisplayPositionPercent += (train.TargetPositionPercent - train.DisplayPositionPercent) * smoothing;
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_isLoading)
        {
            frame.Clear();
            DrawCentredText(frame, Fonts.Small, "Loading...", new Pixel(245, 245, 245));
            return;
        }

        if (_lineStops.Length == 0)
        {
            frame.Clear();
            DrawCentredText(frame, Fonts.Small, "No data", new Pixel(220, 36, 31));
            return;
        }

        // Pass 1 – geometric elements via ImageSharp (track, ticks, trains)
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        var labels = new List<StationLabel>();
        image.Mutate(ctx => DrawTubeLine(ctx, frame.Width, frame.Height, labels));
        frame.RenderImage(image);

        // Pass 2 – crisp BDF text directly on the framebuffer
        foreach (var label in labels)
        {
            int textW = label.Text.Length * Fonts.QuiteSmall.BoundingBox.X;
            frame.DrawText(Fonts.QuiteSmall, label.PixelX - textW / 2, label.YParam, new Pixel(245, 245, 245), label.Text);
        }

        DrawHudOnFrame(frame);
    }

    // Centre a single line of text vertically and horizontally on the frame.
    private static void DrawCentredText(FrameBuffer frame, BdfFont font, string text, Pixel color)
    {
        int x = Math.Max(0, (frame.Width - text.Length * font.BoundingBox.X) / 2);
        // y positions the BDF baseline so the glyph block sits mid-frame
        int y = frame.Height / 2 + font.BoundingBox.Y / 2;
        frame.DrawText(font, x, y, color, text);
    }

    // BDF-font HUD: line name (left, line colour) + train count (right, muted)
    private void DrawHudOnFrame(FrameBuffer frame)
    {
        var lineColor  = GetLineColorPixel(_selectedLineId);
        var lineName   = _selectedLineId.Replace("-", " ").ToUpperInvariant() + " LINE";
        var trainText  = $"{_trainPositions.Length} trains";
        int charW      = Fonts.QuiteSmall.BoundingBox.X; // 5px per glyph
        int y          = frame.Height - 1;               // baseline at very bottom

        frame.DrawText(Fonts.QuiteSmall, 3, y, lineColor, lineName);

        int trainW = trainText.Length * charW;
        frame.DrawText(Fonts.QuiteSmall, frame.Width - trainW - 2, y, new Pixel(170, 180, 200), trainText);
    }

    // Convert the ImageSharp line Color to a Pixel for BDF rendering.
    private static Pixel GetLineColorPixel(string? lineId)
    {
        if (!string.IsNullOrWhiteSpace(lineId) && TubeLineColors.TryGetValue(lineId, out var color))
        {
            var rgb = color.ToPixel<Rgb24>();
            return new Pixel(rgb.R, rgb.G, rgb.B);
        }
        return new Pixel(0, 25, 168);
    }

    // Label data accumulated during the geometry pass and consumed by Pass 2.
    private readonly record struct StationLabel(int PixelX, int YParam, string Text);

    private void DrawTubeLine(IImageProcessingContext ctx, int width, int height, List<StationLabel> labels)
    {
        var lineColor = GetLineColor(_selectedLineId);
        var stops = _lineStops;
        int numStops = stops.Length;

        // ── Geometry ──────────────────────────────────────────────────────────
        const int leftPad   = 8;
        const int rightPad  = 8;
        const int lineThick = 5;
        int lineY    = height >= 64 ? 33 : height / 2;
        int tickLen  = height >= 64 ? 9  : 5;
        int trackLeft  = leftPad;
        int trackRight = width - rightPad;
        int trackWidth = trackRight - trackLeft;

        // ── Track ─────────────────────────────────────────────────────────────
        ctx.Fill(lineColor, new RectangleF(trackLeft, lineY - lineThick / 2f, trackWidth, lineThick));

        // Square end-caps (terminus bumpers)
        ctx.Fill(Color.White, new RectangleF(trackLeft  - 1.5f, lineY - lineThick / 2f - 1, lineThick + 3, lineThick + 2));
        ctx.Fill(Color.White, new RectangleF(trackRight - lineThick / 2f - 1, lineY - lineThick / 2f - 1, lineThick + 3, lineThick + 2));
        ctx.Fill(lineColor, new RectangleF(trackLeft  - 0.5f, lineY - lineThick / 2f, lineThick + 1, lineThick));
        ctx.Fill(lineColor, new RectangleF(trackRight - lineThick / 2f, lineY - lineThick / 2f, lineThick + 1, lineThick));

        if (numStops < 2)
            return;

        // ── Station x-positions ───────────────────────────────────────────────
        float[] xs = new float[numStops];
        for (int i = 0; i < numStops; i++)
            xs[i] = trackLeft + (float)(i / (double)(numStops - 1) * trackWidth);

        double stationSpacing = trackWidth / (double)(numStops - 1);
        int labelInterval = Math.Max(1, (int)Math.Ceiling(30.0 / stationSpacing));

        // ── Pass 1 – tick marks ───────────────────────────────────────────────
        for (int i = 0; i < numStops; i++)
        {
            float x       = xs[i];
            bool terminus = i == 0 || i == numStops - 1;
            if (terminus) continue;

            bool doLabel  = i % labelInterval == 0;
            bool above    = doLabel && ((i / labelInterval) % 2 == 0);
            float tickTop = above
                ? lineY - lineThick / 2f - tickLen
                : lineY + lineThick / 2f;

            ctx.Fill(Color.White, new RectangleF(x - 0.75f, tickTop, 1.5f, tickLen));
        }

        // ── Pass 2 – trains ───────────────────────────────────────────────────
        foreach (var train in _trainPositions)
        {
            float x          = trackLeft + (float)(train.DisplayPositionPercent / 100.0) * trackWidth;
            var   trainColor = GetTrainColor(train.Direction);
            bool  goingRight = !string.Equals(train.Direction, "inbound", StringComparison.OrdinalIgnoreCase);

            // Slim vertical bar — narrow enough that the track and ticks show through
            const float coreW = 2f;
            const float coreH = 7f;
            ctx.Fill(trainColor, new RectangleF(x - coreW / 2f, lineY - coreH / 2f, coreW, coreH));

            // Tiny direction pip on the leading edge
            float pipX = goingRight ? x + coreW / 2f : x - coreW / 2f - 1.5f;
            ctx.Fill(Color.White, new RectangleF(pipX, lineY - 1.5f, 1.5f, 3f));
        }

        // ── Pass 3 – collect station labels for BDF rendering ─────────────────
        // QuiteSmall (5x7, offsetY=-1): charY = line + (y - 6).
        //   • Above labels: BDF y = label bottom  → use ImageSharp labelY directly.
        //   • Below labels: BDF y = label top + 6 → BoundingBox.Y - 1 = 6.
        int bdfH = Fonts.QuiteSmall.BoundingBox.Y - 1; // = 6

        for (int i = 0; i < numStops; i++)
        {
            bool terminus = i == 0 || i == numStops - 1;
            bool doLabel  = terminus || i % labelInterval == 0;
            if (!doLabel) continue;

            float x    = xs[i];
            bool above = terminus ? i == 0 : (i / labelInterval) % 2 == 0;

            float imagesharpLabelY = above
                ? lineY - lineThick / 2f - tickLen - 2
                : lineY + lineThick / 2f + tickLen + 2;

            int bdfY = above
                ? (int)imagesharpLabelY            // bottom of text = ImageSharp bottom
                : (int)imagesharpLabelY + bdfH;    // top of text → shift down by font height-1

            var name = ShortenStationName(stops[i].Name);
            labels.Add(new StationLabel((int)x, bdfY, name));
        }
    }

    // ── Station name abbreviation ─────────────────────────────────────────────

    private static string ShortenStationName(string rawName)
    {
        // Strip "Underground Station" etc. and upper-case (NormalizeStationName does this)
        var name = NormalizeStationName(rawName);

        // Well-known long names
        name = name
            .Replace("KING'S CROSS ST. PANCRAS", "KING'S X")
            .Replace("TOTTENHAM COURT ROAD",      "TOT CT RD")
            .Replace("OXFORD CIRCUS",              "OXF CIRC")
            .Replace("WESTMINSTER",                "WESTMNSTR")
            .Replace("LONDON BRIDGE",              "LDN BRDG")
            .Replace("CANARY WHARF",               "CNRY WRF")
            .Replace("NORTH GREENWICH",            "N GRNWCH")
            .Replace("CANADA WATER",               "CAN WTR")
            .Replace("WEST HAMPSTEAD",             "W HMPSTD")
            .Replace("FINCHLEY ROAD",              "FNCHLY RD")
            .Replace("SWISS COTTAGE",              "SWISS CT")
            .Replace("ST. JOHN'S WOOD",            "ST JOHNS")
            .Replace("GREEN PARK",                 "GRN PK")
            .Replace("BAKER STREET",               "BAKER ST")
            .Replace("BOND STREET",                "BOND ST")
            .Replace("WEMBLEY PARK",               "WMBLY PK")
            .Replace("CANNING TOWN",               "CANNNG T")
            .Replace("WILLESDEN GREEN",            "WLSDN GN")
            .Replace("HAMMERSMITH",                "HMRSTH")
            .Replace("SHEPHERD'S BUSH",            "SHEP BSH");

        // Generic word abbreviations (longer patterns first to avoid partial matches)
        name = name
            .Replace(" JUNCTION", " JCT")
            .Replace(" GARDENS",  " GDNS")
            .Replace(" BRIDGE",   " BRDG")
            .Replace(" STREET",   " ST")
            .Replace(" ROAD",     " RD")
            .Replace(" WHARF",    " WRF")
            .Replace(" SQUARE",   " SQ")
            .Replace(" PARK",     " PK")
            .Replace("NORTH ",    "N ")
            .Replace("SOUTH ",    "S ")
            .Replace("EAST ",     "E ")
            .Replace("WEST ",     "W ")
            .Replace(" NORTH",    " N")
            .Replace(" SOUTH",    " S")
            .Replace(" EAST",     " E")
            .Replace(" WEST",     " W");

        name = name.Trim();
        return name.Length > 9 ? name[..9].TrimEnd() : name;
    }

    private static Color GetLineColor(string? lineId)
    {
        if (!string.IsNullOrWhiteSpace(lineId) && TubeLineColors.TryGetValue(lineId, out var color))
        {
            return color;
        }

        return TflBlue;
    }

    private static Color GetTrainColor(string? direction) =>
        direction?.Trim().ToLowerInvariant() switch
        {
            "inbound"  => Color.FromRgb(0,   190, 255),
            "outbound" => Color.FromRgb(255,  80,  20),
            _          => Color.FromRgb(255, 220,  30),
        };
}
