using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

public class TubeStatusApp : MatrixAppBase
{
    public override string Id => "tube-status";
    public override string Name => "Tube Status";
    public override int FrameRate => 1;

    private Font? _squareFont;

    private volatile TubeLineStatus[] _lineStatuses = Array.Empty<TubeLineStatus>();
    private volatile bool _isLoading = true;
    private volatile int _currentPage;

    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastRefresh = DateTime.MinValue;
    private string? _appKey;

    private class TubeLineStatus
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("lineStatuses")]
        public LineStatusDetail[]? LineStatusDetails { get; set; }
    }

    private class LineStatusDetail
    {
        [JsonPropertyName("statusSeverity")]
        public int StatusSeverity { get; set; }

        [JsonPropertyName("statusSeverityDescription")]
        public string StatusSeverityDescription { get; set; } = "";
    }

    // Tube line colors (official London Underground colors)
    private static readonly Dictionary<string, (byte R, byte G, byte B)> LineColors = new()
    {
        { "bakerloo", (156, 105, 56) },      // Brown
        { "central", (220, 36, 35) },         // Red
        { "circle", (255, 206, 0) },          // Yellow
        { "district", (0, 114, 41) },         // Green
        { "hammersmith-city", (215, 153, 175) }, // Pink
        { "jubilee", (168, 99, 170) },        // Purple
        { "metropolitan", (155, 0, 88) },     // Magenta
        { "northern", (0, 0, 0) },            // Black
        { "piccadilly", (0, 24, 168) },       // Blue
        { "victoria", (0, 160, 226) },        // Light Blue
        { "waterloo-city", (100, 200, 150) }, // Teal
        { "dlr", (0, 175, 173) },             // Teal Green
        { "tflrail", (126, 91, 198) },        // Purple
    };

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            _squareFont = SystemFonts.CreateFont("Nimbus Sans", 10, FontStyle.Bold);
        }
        catch
        {
            _squareFont = SystemFonts.CreateFont("Arial", 10, FontStyle.Bold);
        }

        // Read TFL API key from configuration
        _appKey = configuration["TFL:AppKey"];
        if (string.IsNullOrEmpty(_appKey))
        {
            Console.WriteLine("Warning: TFL:AppKey not configured. TFL API calls may fail.");
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
                        if (DateTime.Now - _lastRefresh >= _refreshInterval || _lineStatuses.Length == 0)
                        {
                            await FetchTubeStatusAsync(httpClient, ct);
                        }

                        // Scroll through pages every 10 seconds for better pacing
                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                        if (_lineStatuses.Length > 0)
                        {
                            // Calculate squares per page
                            int squaresPerPage = (_lineStatuses.Length > 4) ? 4 : _lineStatuses.Length;
                            int totalPages = Math.Max(1, (int)Math.Ceiling(_lineStatuses.Length / (double)squaresPerPage));
                            _currentPage = (_currentPage + 1) % totalPages;
                        }
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

    private async Task FetchTubeStatusAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        _isLoading = true;

        try
        {
            var apiUrl = "https://api.tfl.gov.uk/Line/Mode/tube/Status";
            if (!string.IsNullOrEmpty(_appKey))
            {
                apiUrl += $"?app_key={Uri.EscapeDataString(_appKey)}";
            }

            var response = await httpClient.GetAsync(apiUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var lines = JsonSerializer.Deserialize<TubeLineStatus[]>(content, options) ?? Array.Empty<TubeLineStatus>();

                _lineStatuses = lines.OrderBy(l => l.Name).ToArray();
                _lastRefresh = DateTime.Now;
                _currentPage = 0;
                _isLoading = false;
            }
            else
            {
                _isLoading = false;
            }
        }
        catch
        {
            _isLoading = false;
            throw;
        }
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        // Update happens in background
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            ctx.Fill(Color.Black);

            if (_isLoading)
            {
                DrawLoadingState(ctx, frame.Width, frame.Height);
            }
            else if (_lineStatuses.Length > 0)
            {
                DrawLineStatuses(ctx, frame.Width, frame.Height);
            }
            else
            {
                DrawErrorState(ctx, frame.Width, frame.Height);
            }
        });

        frame.RenderImage(image);
    }

    private void DrawLoadingState(IImageProcessingContext ctx, int width, int height)
    {
        var color = Color.Cyan;
        var textOptions = new RichTextOptions(_squareFont!)
        {
            Origin = new PointF(width / 2.0f - 20, height / 2.0f - 5)
        };
        ctx.DrawText(textOptions, "Loading...", color);
    }

    private void DrawErrorState(IImageProcessingContext ctx, int width, int height)
    {
        var color = Color.Red;
        var textOptions = new RichTextOptions(_squareFont!)
        {
            Origin = new PointF(width / 2.0f - 15, height / 2.0f)
        };
        ctx.DrawText(textOptions, "No Data", color);
    }

    private void DrawLineStatuses(IImageProcessingContext ctx, int width, int height)
    {
        // Calculate square size and grid layout
        int squareSize = Math.Min(width, height);
        int squaresPerRow = Math.Max(1, width / squareSize);
        int squaresPerColumn = Math.Max(1, height / squareSize);
        int squaresPerPage = squaresPerRow * squaresPerColumn;
        
        int startIdx = _currentPage * squaresPerPage;
        int endIdx = Math.Min(startIdx + squaresPerPage, _lineStatuses.Length);
        var visibleLines = _lineStatuses.Skip(startIdx).Take(endIdx - startIdx).ToArray();

        int displayIdx = 0;
        for (int row = 0; row < squaresPerColumn; row++)
        {
            for (int col = 0; col < squaresPerRow; col++)
            {
                if (displayIdx >= visibleLines.Length) break;

                var line = visibleLines[displayIdx];
                int x = col * squareSize;
                int y = row * squareSize;

                // Get line color
                var lineId = line.Id.ToLower();
                var lineColorTuple = LineColors.TryGetValue(lineId, out var color) ? color : (100, 100, 100);
                var lineColor = Color.FromRgba((byte)(lineColorTuple.Item1 / 2), (byte)(lineColorTuple.Item2 / 2), (byte)(lineColorTuple.Item3 / 2), 255);

                // Get status
                var status = line.LineStatusDetails?.FirstOrDefault();
                var statusDescription = status?.StatusSeverityDescription ?? "Unknown";

                // Draw the colored square
                var square = new RectangleF(x, y, squareSize, squareSize);
                ctx.Fill(lineColor, square);

                // Draw status text in the middle
                DrawStatusText(ctx, x, y, squareSize, statusDescription);

                displayIdx++;
            }
        }
    }

    private void DrawStatusText(IImageProcessingContext ctx, int squareX, int squareY, int squareSize, string statusDescription)
    {
        // Determine status text and color based on description
        var (statusText, textColor) = statusDescription.ToLower() switch
        {
            "good service" => ("Good", Color.White),
            "minor delays" => ("Minor", Color.Black),
            "major delays" => ("Major", Color.White),
            "suspended" => ("Closed", Color.White),
            "part suspended" => ("Partial", Color.White),
            "planned closure" => ("Planned", Color.White),
            "severe delays" => ("Severe", Color.White),
            "bus service" => ("Bus", Color.White),
            _ => (statusDescription.Length > 6 ? statusDescription.Substring(0, 6) : statusDescription, Color.White)
        };

        // Draw status text centered in the square
        int centerX = squareX + squareSize / 2;
        int centerY = squareY + squareSize / 2;

        var textOptions = new RichTextOptions(_squareFont!)
        {
            Origin = new PointF(centerX - 15, centerY - 5)
        };
        ctx.DrawText(textOptions, statusText, textColor);
    }

}
