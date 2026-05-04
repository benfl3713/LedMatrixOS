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
    public override int FrameRate => 1;

    private BdfFont _font = Fonts.Big;
    private Pixel _color = new Pixel(255, 120, 0);

    // Configurable settings
    private string _stationId = "";
    private string _platformFilter = "";
    private int _maxDepartures = 3;

    private string? _appKey;
    private volatile Departure[] _departures = Array.Empty<Departure>();
    private volatile bool _isLoading = true;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private DateTime _lastRefresh = DateTime.MinValue;

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

        [JsonPropertyName("towards")]
        public string Towards { get; set; } = "";
    }

    public IEnumerable<AppSetting> GetSettings()
    {
        return new[]
        {
            new AppSetting("stationId", "Station ID", "TfL Naptan ID for the station (e.g. 940GZZLUBKF)", AppSettingType.String, "", _stationId),
            new AppSetting("platformFilter", "Platform Filter", "Filter by platform name (e.g. 'Eastbound'). Leave empty to show all platforms.", AppSettingType.String, "", _platformFilter),
            new AppSetting("maxDepartures", "Max Departures", "Number of departures to display", AppSettingType.Integer, 2, _maxDepartures, 1, 6),
        };
    }

    public void UpdateSetting(string key, object value)
    {
        switch (key)
        {
            case "stationId":
                _stationId = value.ToString() ?? "";
                _departures = Array.Empty<Departure>();
                _lastRefresh = DateTime.MinValue;
                break;
            case "platformFilter":
                _platformFilter = value.ToString() ?? "";
                _departures = Array.Empty<Departure>();
                _lastRefresh = DateTime.MinValue;
                break;
            case "maxDepartures":
                _maxDepartures = Math.Clamp(Convert.ToInt32(value), 1, 6);
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
            _stationId = configuredStation;
        }

        var configuredPlatform = configuration["TubeDeparturesApp:PlatformFilter"];
        if (!string.IsNullOrEmpty(configuredPlatform))
        {
            _platformFilter = configuredPlatform;
        }

        if (int.TryParse(configuration["TubeDeparturesApp:MaxDepartures"], out var maxDep) && maxDep > 0)
        {
            _maxDepartures = Math.Clamp(maxDep, 1, 6);
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

                _lastRefresh = DateTime.Now;
            }

            _isLoading = false;
        }
        catch
        {
            _isLoading = false;
            throw;
        }
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

        for (int i = 0; i < _departures.Length; i++)
        {
            var dep = _departures[i];
            int minsAway = Math.Max(0, dep.TimeToStation / 60);
            DrawDepartureRow(frame, i + 1, dep.DestinationName, minsAway);
        }
        
        frame.DrawHorizontalLine(14 * 3 + 5, frame.Width, _color);
        frame.DrawText(Fonts.Small, 6, 14 * 4 + 3, new Pixel(0, 160, 180), $"Last update: {_lastRefresh:HH:mm:ss}");
    }

    private void DrawDepartureRow(FrameBuffer frame, int row, string station, int minsAway)
    {
        string suffix = "due ";
        if (minsAway > 0)
        {
            string minsText = minsAway == 1 ? "min " : "mins";
            suffix = $"{minsAway}{minsText}";
        }

        string formatStationLength = $"-{26 - suffix.Length}";
        
        string text = $"{row} {string.Format($"{{0,{formatStationLength}}}", station)}{suffix}";
        frame.DrawText(_font, 0, 14 * row, _color, text);
    }
}
