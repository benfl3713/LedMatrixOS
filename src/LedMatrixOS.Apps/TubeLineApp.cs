using LedMatrixOS.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace LedMatrixOS.Apps;

public class TubeLineApp : MatrixAppBase
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
    private Font? _infoFont;

    // TfL-inspired palette
    private static readonly Color TflBlue = Color.FromRgb(0, 25, 168);
    private static readonly Color TflRed = Color.FromRgb(220, 36, 31);
    private static readonly Color TflBg = Color.FromRgb(8, 12, 20);
    private static readonly Color TflLight = Color.FromRgb(245, 245, 245);
    private static readonly Color TflHudMuted = Color.FromRgb(170, 180, 200);

    private static readonly Dictionary<string, Color> TubeLineColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bakerloo"] = Color.FromRgb(178, 99, 0),
        ["central"] = Color.FromRgb(220, 36, 31),
        ["circle"] = Color.FromRgb(255, 211, 41),
        ["district"] = Color.FromRgb(0, 125, 50),
        ["hammersmith-city"] = Color.FromRgb(244, 169, 190),
        ["jubilee"] = Color.FromRgb(161, 165, 167),
        ["metropolitan"] = Color.FromRgb(155, 0, 88),
        ["northern"] = Color.FromRgb(35, 31, 32),
        ["piccadilly"] = Color.FromRgb(0, 15, 159),
        ["victoria"] = Color.FromRgb(0, 152, 216),
        ["waterloo-city"] = Color.FromRgb(147, 206, 186)
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

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            _infoFont = SystemFonts.CreateFont("Nimbus Sans", dimensions.height >= 64 ? 10 : 8, FontStyle.Bold);
        }
        catch
        {
            _infoFont = SystemFonts.CreateFont("Arial", dimensions.height >= 64 ? 10 : 8, FontStyle.Bold);
        }

        // Read TFL API key and selected line from configuration
        _appKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_appKey))
        {
            Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");
        }

        var configuredLine = configuration["TubeLineApp:LineId"];
        if (!string.IsNullOrEmpty(configuredLine))
        {
            _selectedLineId = configuredLine.ToLower();
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
            // Fetch route with stops
            var routeUrl = $"https://api.tfl.gov.uk/Line/{_selectedLineId}/Route/Sequence/Inbound";
            if (!string.IsNullOrEmpty(_appKey))
            {
                routeUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";
            }

            var routeResponse = await httpClient.GetAsync(routeUrl, cancellationToken);
            
            if (routeResponse.IsSuccessStatusCode)
            {
                var content = await routeResponse.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var routeData = JsonSerializer.Deserialize<LineRouteData>(content, options);

                if (routeData != null)
                {
                    _lineStops = BuildOrderedStops(routeData);

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

    private static StopPoint[] BuildOrderedStops(LineRouteData routeData)
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

        // Primary: orderedLineRoutes gives canonical route order.
        var orderedRoute = routeData.OrderedLineRoutes?
            .Where(r => r.NaptanIds is { Length: > 0 })
            .OrderByDescending(r => string.Equals(r.ServiceType, "Regular", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.NaptanIds!.Length)
            .FirstOrDefault();

        if (orderedRoute?.NaptanIds != null)
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
        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            ctx.Fill(TflBg);

            if (_isLoading)
            {
                DrawLoading(ctx, frame.Width, frame.Height);
            }
            else if (_lineStops.Length > 0)
            {
                DrawTubeLine(ctx, frame.Width, frame.Height);
            }
            else
            {
                DrawError(ctx, frame.Width, frame.Height);
            }
        });

        frame.RenderImage(image);
    }

    private void DrawLoading(IImageProcessingContext ctx, int width, int height)
    {
        if (_infoFont == null) return;
        ctx.DrawText(new RichTextOptions(_infoFont) { Origin = new PointF(4, Math.Max(2, height / 2f - 8)) }, "Loading...", TflLight);
    }

    private void DrawError(IImageProcessingContext ctx, int width, int height)
    {
        if (_infoFont == null) return;
        ctx.DrawText(new RichTextOptions(_infoFont) { Origin = new PointF(4, Math.Max(2, height / 2f - 8)) }, "No data", TflRed);
    }

    private void DrawTubeLine(IImageProcessingContext ctx, int width, int height)
    {
        var lineColor = GetLineColor(_selectedLineId);
        var topHud = height >= 64 ? 8 : 6;
        var bottomHud = height >= 64 ? 8 : 6;
        var leftPadding = 5;
        var rightPadding = 5;

        var rows = height >= 64 ? 4 : (height >= 40 ? 3 : 2);
        var snake = BuildSnakePath(width, height, topHud, bottomHud, leftPadding, rightPadding, rows);

        // Draw wrapped track using thicker segments for visibility.
        foreach (var segment in snake.Segments)
        {
            DrawTrackSegment(ctx, segment, 3, lineColor);
        }

        // Draw station markers along the wrapped path.
        int numStops = Math.Min(_lineStops.Length, 30); // Keep readable marker density
        if (numStops > 1)
        {
            for (int i = 0; i < numStops; i++)
            {
                var stopPercent = i / (float)(numStops - 1);
                var point = GetPointOnSnake(snake, stopPercent);
                var stationCircle = new RectangleF(point.X - 2.5f, point.Y - 2.5f, 5, 5);
                ctx.Fill(TflLight, stationCircle);
                ctx.Draw(TflBlue, 1, stationCircle);
            }
        }

        // Draw trains along the same wrapped path.
        foreach (var train in _trainPositions)
        {
            var point = GetPointOnSnake(snake, (float)(train.DisplayPositionPercent / 100.0));

            // Slightly bigger marker so movement is easier to see on low-res displays.
            var trainMarker = new RectangleF(point.X - 1.0f, point.Y - 4.0f, 2.0f, 8.0f);
            ctx.Fill(GetTrainColor(train.Direction), trainMarker);
        }

        DrawHud(ctx, width, height);
    }

    private sealed class SnakePath
    {
        public List<SnakeSegment> Segments { get; } = new();
        public float TotalLength { get; set; }
    }

    private readonly record struct SnakeSegment(float X1, float Y1, float X2, float Y2, float Length);

    private static SnakePath BuildSnakePath(int width, int height, int topHud, int bottomHud, int leftPadding, int rightPadding, int rows)
    {
        var path = new SnakePath();

        float xLeft = leftPadding;
        float xRight = Math.Max(xLeft + 4, width - rightPadding);
        float yTop = topHud;
        float yBottom = Math.Max(yTop + 1, height - bottomHud);
        float rowSpacing = rows > 1 ? (yBottom - yTop) / (rows - 1) : 0;

        float total = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = yTop + row * rowSpacing;
            bool leftToRight = row % 2 == 0;
            float x1 = leftToRight ? xLeft : xRight;
            float x2 = leftToRight ? xRight : xLeft;
            float hLength = Math.Abs(x2 - x1);
            path.Segments.Add(new SnakeSegment(x1, y, x2, y, hLength));
            total += hLength;

            if (row < rows - 1)
            {
                float nextY = yTop + (row + 1) * rowSpacing;
                float vx = x2;
                float vLength = Math.Abs(nextY - y);
                path.Segments.Add(new SnakeSegment(vx, y, vx, nextY, vLength));
                total += vLength;
            }
        }

        path.TotalLength = Math.Max(1, total);
        return path;
    }

    private static PointF GetPointOnSnake(SnakePath path, float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        float targetDistance = path.TotalLength * progress;
        float traveled = 0;

        foreach (var segment in path.Segments)
        {
            if (targetDistance <= traveled + segment.Length)
            {
                float segProgress = segment.Length <= 0 ? 0 : (targetDistance - traveled) / segment.Length;
                float x = segment.X1 + (segment.X2 - segment.X1) * segProgress;
                float y = segment.Y1 + (segment.Y2 - segment.Y1) * segProgress;
                return new PointF(x, y);
            }

            traveled += segment.Length;
        }

        var last = path.Segments[^1];
        return new PointF(last.X2, last.Y2);
    }

    private static void DrawTrackSegment(IImageProcessingContext ctx, SnakeSegment segment, float thickness, Color color)
    {
        if (Math.Abs(segment.Y1 - segment.Y2) < 0.001f)
        {
            // Horizontal
            float x = Math.Min(segment.X1, segment.X2);
            float w = Math.Abs(segment.X2 - segment.X1);
            ctx.Fill(color, new RectangleF(x, segment.Y1 - thickness / 2f, w, thickness));
        }
        else
        {
            // Vertical connector
            float y = Math.Min(segment.Y1, segment.Y2);
            float h = Math.Abs(segment.Y2 - segment.Y1);
            ctx.Fill(color, new RectangleF(segment.X1 - thickness / 2f, y, thickness, h));
        }
    }

    private void DrawHud(IImageProcessingContext ctx, int width, int height)
    {
        if (_infoFont == null)
        {
            return;
        }

        var lineText = _selectedLineId.ToUpperInvariant();
        var countText = $"{_trainPositions.Length} trains";
        var age = _lastSuccessfulUpdateUtc == DateTime.MinValue
            ? "--"
            : ((int)Math.Max(0, (DateTime.UtcNow - _lastSuccessfulUpdateUtc).TotalSeconds)).ToString();

        var primaryDirection = _trainPositions
            .Where(t => !string.IsNullOrWhiteSpace(t.Direction))
            .GroupBy(t => t.Direction, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "inbound";

        ctx.DrawText(new RichTextOptions(_infoFont) { Origin = new PointF(2, 1) }, lineText, GetLineColor(_selectedLineId));
        ctx.DrawText(new RichTextOptions(_infoFont) { Origin = new PointF(Math.Max(2, width - 90), 1) }, countText, TflBlue);

        // var bottomText = $"{primaryDirection}  {age}s";
        // ctx.DrawText(new RichTextOptions(_infoFont) { Origin = new PointF(2, Math.Max(1, height - 11)) }, bottomText, TflHudMuted);
    }

    private static Color GetLineColor(string? lineId)
    {
        if (!string.IsNullOrWhiteSpace(lineId) && TubeLineColors.TryGetValue(lineId, out var color))
        {
            return color;
        }

        return TflBlue;
    }

    private static Color GetTrainColor(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return TflLight;
        }

        return direction.Trim().ToLowerInvariant() switch
        {
            "inbound" => TflBlue,
            "outbound" => TflRed,
            _ => TflLight
        };
    }
}
