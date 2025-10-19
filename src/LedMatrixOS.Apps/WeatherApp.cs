using LedMatrixOS.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

public sealed class WeatherApp : MatrixAppBase
{
    public override string Id => "weather";
    public override string Name => "Weather";

    private Font? _tempFont;
    private Font? _locationFont;
    
    // Thread-safe data storage
    private volatile WeatherData? _currentWeather;
    private volatile bool _isLoading = true;
    private volatile string _loadingStatus = "Loading...";
    
    // Background loading control
    private CancellationTokenSource? _backgroundCts;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(10);
    private DateTime _lastRefresh = DateTime.MinValue;

    private class WeatherData
    {
        public string Location { get; set; } = "";
        public int Temperature { get; set; }
        public string Condition { get; set; } = "";
        public string Icon { get; set; } = "";
        public DateTime LastUpdated { get; set; }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            _tempFont = SystemFonts.CreateFont("Nimbus Sans", 16, FontStyle.Bold);
            _locationFont = SystemFonts.CreateFont("Nimbus Sans", 8);
        }
        catch
        {
            _tempFont = SystemFonts.CreateFont("Arial", 16);
            _locationFont = SystemFonts.CreateFont("Arial", 8);
        }

        // Start background data loading
        StartBackgroundDataLoading();

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        // Clean up background tasks
        _backgroundCts?.Cancel();
        _backgroundCts?.Dispose();
        return base.OnDeactivatedAsync(cancellationToken);
    }

    private void StartBackgroundDataLoading()
    {
        _backgroundCts = new CancellationTokenSource();
        
        RunInBackground(async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Check if we need to refresh data
                    if (DateTime.Now - _lastRefresh >= _refreshInterval || _currentWeather == null)
                    {
                        await LoadWeatherDataAsync(ct);
                    }
                    
                    // Wait before checking again (check every 30 seconds)
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    // Log error but don't crash the background task
                    _loadingStatus = $"Error: {ex.Message}";
                    _isLoading = false;
                    
                    // Wait before retrying
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                }
            }
        });
    }

    private async Task LoadWeatherDataAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        _loadingStatus = "Fetching weather...";

        try
        {
            // Simulate API call with realistic delay
            await Task.Delay(2000, cancellationToken);
            
            // In a real implementation, you'd call a weather API here
            // For demo purposes, we'll simulate weather data
            var weather = await SimulateWeatherApiCall(cancellationToken);
            
            // Atomically update the weather data
            _currentWeather = weather;
            _lastRefresh = DateTime.Now;
            _isLoading = false;
        }
        catch (OperationCanceledException)
        {
            throw; // Let this bubble up
        }
        catch (Exception ex)
        {
            _loadingStatus = "Failed to load weather";
            _isLoading = false;
            throw; // Let the caller handle this
        }
    }

    private async Task<WeatherData> SimulateWeatherApiCall(CancellationToken cancellationToken)
    {
        // Simulate network delay
        await Task.Delay(1000, cancellationToken);
        
        // Simulate some changing weather data
        var random = new Random();
        var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Snowy", "Stormy" };
        var locations = new[] { "New York", "London", "Tokyo", "Sydney" };
        
        return new WeatherData
        {
            Location = locations[random.Next(locations.Length)],
            Temperature = random.Next(-10, 35),
            Condition = conditions[random.Next(conditions.Length)],
            Icon = "☀️", // In real app, this would come from API
            LastUpdated = DateTime.Now
        };
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        // Update method stays lightweight - no heavy operations here
        // All data loading happens in background
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_tempFont == null || _locationFont == null) return;

        using var image = new Image<Rgb24>(frame.Width, frame.Height);

        image.Mutate(ctx =>
        {
            if (_isLoading)
            {
                // Show loading state
                DrawLoadingState(ctx, frame.Width, frame.Height);
            }
            else if (_currentWeather != null)
            {
                // Show weather data
                DrawWeatherData(ctx, frame.Width, frame.Height, _currentWeather);
            }
            else
            {
                // Show error state
                DrawErrorState(ctx, frame.Width, frame.Height);
            }
        });

        frame.RenderImage(image);
    }

    private void DrawLoadingState(IImageProcessingContext ctx, int width, int height)
    {
        var color = ImageSharpExtensions.FromHsv(200, 0.8f, 1.0f);
        var textOptions = new RichTextOptions(_locationFont!)
        {
            Origin = new PointF(width / 2 - 20, height / 2)
        };
        
        ctx.DrawText(textOptions, _loadingStatus, color);
        
        // Add a simple loading animation
        var dots = new string('.', ((int)(DateTime.Now.Millisecond / 333) % 4));
        var dotsOptions = new RichTextOptions(_locationFont!)
        {
            Origin = new PointF(width / 2 + 10, height / 2)
        };
        ctx.DrawText(dotsOptions, dots, color);
    }

    private void DrawWeatherData(IImageProcessingContext ctx, int width, int height, WeatherData weather)
    {
        var tempColor = ImageSharpExtensions.FromHsv(weather.Temperature > 20 ? 0 : 240, 0.8f, 1.0f);
        var textColor = ImageSharpExtensions.FromHsv(200, 0.6f, 1.0f);

        // Temperature
        var tempText = $"{weather.Temperature}°C";
        var tempOptions = new RichTextOptions(_tempFont!)
        {
            Origin = new PointF(width / 2 - 15, height / 2 - 10)
        };
        ctx.DrawText(tempOptions, tempText, tempColor);

        // Location and condition
        var locationText = $"{weather.Location}";
        var locationOptions = new RichTextOptions(_locationFont!)
        {
            Origin = new PointF(width / 2 - locationText.Length * 2, height / 2 + 8)
        };
        ctx.DrawText(locationOptions, locationText, textColor);

        var conditionText = weather.Condition;
        var conditionOptions = new RichTextOptions(_locationFont!)
        {
            Origin = new PointF(width / 2 - conditionText.Length * 2, height / 2 + 16)
        };
        ctx.DrawText(conditionOptions, conditionText, textColor);

        // Age indicator (show how fresh the data is)
        var age = DateTime.Now - weather.LastUpdated;
        if (age.TotalMinutes > 15)
        {
            var staleColor = ImageSharpExtensions.FromHsv(30, 0.8f, 0.6f); // Orange
            ctx.DrawText(new RichTextOptions(_locationFont!) { Origin = new PointF(2, 2) }, "!", staleColor);
        }
    }

    private void DrawErrorState(IImageProcessingContext ctx, int width, int height)
    {
        var errorColor = ImageSharpExtensions.FromHsv(0, 0.8f, 1.0f);
        var textOptions = new RichTextOptions(_locationFont!)
        {
            Origin = new PointF(width / 2 - 15, height / 2)
        };
        
        ctx.DrawText(textOptions, "No Data", errorColor);
    }
}
