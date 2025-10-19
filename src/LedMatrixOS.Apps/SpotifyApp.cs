using LedMatrixOS.Apps.Common;
using LedMatrixOS.Apps.Services;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace LedMatrixOS.Apps;

public sealed class SpotifyApp : MatrixAppBase
{
    public override string Id => "spotify";
    public override string Name => "Spotify";
    public override int FrameRate { get; } = 10;

    // Mock scrolling text - you'll want to replace this with your actual ScrollOverflowText
    private ScrollOverflowTextForFrameBuffer? _songNameText;
    private ScrollOverflowTextForFrameBuffer? _artistNameText;
    private ScrollOverflowTextForFrameBuffer? _nextTrackText;
    
    // Party mode - replace with actual logic
    private bool PartyModeEnabled => false;
    private readonly Random _partyRandom = new Random();

    // Image context for SetPixel simulation
    private FrameBuffer _matrix;
    private int _matrixWidth;
    private int _matrixHeight;
    
    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _matrixWidth = dimensions.width;
        _matrixHeight = dimensions.height;
        
        System.Console.WriteLine("Initializing Spotify Data");
        var clientId = configuration["Spotify:ClientId"];
        var clientSecret = configuration["Spotify:ClientSecret"];
        var dataService = new SpotifyDataService(new HttpClient(), clientId, clientSecret);

        RunInBackground(dataService.ExecuteAsync);

        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
    }
    
    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        _matrix = frame;
        DrawFrame();
    }

    private void DrawFrame()
    {
        //_matrix.Clear();
        if (string.IsNullOrEmpty(SpotifyDataStore.Value.SongName))
        {
            return;
        }
        DrawAnimatedBackgroundWithAlbumColors();
        DrawArtwork();
        DrawProgress();

        if (SpotifyDataStore.Value.IsSavedSong == true)
            DrawHeart(180, 44, 6, new Pixel(0, 180, 0));

        _songNameText ??= new ScrollOverflowTextForFrameBuffer(74, 27, 15, Fonts.Big);
        _artistNameText ??= new ScrollOverflowTextForFrameBuffer(74, 47, 20, Fonts.Small);

        _songNameText.Draw(_matrix, SpotifyDataStore.Value.SongName);
        if (!string.IsNullOrEmpty(SpotifyDataStore.Value.ArtistName))
            _artistNameText.Draw(_matrix, "by " + SpotifyDataStore.Value.ArtistName);
        
        if (SpotifyDataStore.Value.TrackLength > 0)
        {
            var progress = TimeSpan.FromMilliseconds(SpotifyDataStore.Value.Progress);
            var total = TimeSpan.FromMilliseconds(SpotifyDataStore.Value.TrackLength);
            _matrix.DrawText(Fonts.ExtraSmall, 128 - (6 * Fonts.ExtraSmall.BoundingBox.X), 62, new Pixel(150, 150, 150), @$"{progress:mm\:ss} / {total:mm\:ss}");
        }
        
        if (SpotifyDataStore.Value.NextTrackName != null)
        {
            DrawNextTrackArtwork();
            _nextTrackText ??= new ScrollOverflowTextForFrameBuffer(192, 62, 11, Fonts.QuiteSmall);
            _nextTrackText.Draw(_matrix, "Next: " + SpotifyDataStore.Value.NextTrackName);
        }

        if (SpotifyDataStore.Value.IsPlaying)
            DrawEqualizer();
        else
            DrawPaused();

        if (PartyModeEnabled)
            DrawPartyMode();
    }

    private void DrawPaused()
    {
        // Draw a pause icon in the center of the matrix
        int centerX = _matrixWidth - (_matrixWidth / 4 / 2);
        int centerY = _matrixHeight - (_matrixHeight / 2);

        // Draw two vertical bars for the pause icon
        int barWidth = 4;
        int barHeight = 16;

        for (int x = -barWidth; x <= barWidth; x++)
        {
            for (int y = -barHeight / 2; y <= barHeight / 2; y++)
            {
                if (x < -barWidth / 2 || x > barWidth / 2)
                {
                    _matrix.SetPixel(centerX + x, centerY + y, new Pixel(150, 150, 150));
                }
            }
        }
    }

    private void DrawArtwork()
    {
        if (SpotifyDataStore.Value.Artwork == null)
            return;

        using var image = Image.Load<Rgb24>(SpotifyDataStore.Value.Artwork);
        image.Mutate(i => i.Resize(_matrixWidth / 4, _matrixHeight));
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);

                // pixelRow.Length has the same value as accessor.Width,
                // but using pixelRow.Length allows the JIT to optimize away bounds checks:
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    // Get a reference to the pixel at position x
                    ref Rgb24 pixel = ref pixelRow[x];
                    _matrix.SetPixel(x, y, new Pixel(pixel.R, pixel.G, pixel.B) / 2);
                }
            }
        });
    }
    
    private void DrawNextTrackArtwork()
    {
        if (SpotifyDataStore.Value.NextTrackArtwork == null)
            return;

        var startX = 180;
        var startY = 53;

        using var image = Image.Load<Rgb24>(SpotifyDataStore.Value.NextTrackArtwork);
        image.Mutate(i => i.Resize(10, 10));
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);

                // pixelRow.Length has the same value as accessor.Width,
                // but using pixelRow.Length allows the JIT to optimize away bounds checks:
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    // Get a reference to the pixel at position x
                    ref Rgb24 pixel = ref pixelRow[x];
                    _matrix.SetPixel(x + startX, y + startY, new Pixel(pixel.R, pixel.G, pixel.B) / 2);
                }
            }
        });
    }

    // --- Playful Progress Bar ---
    private double _progressBarWavePhase = 0;
    // Gradient colors: green -> yellow -> red
    private readonly Pixel[] _progressGradient = new Pixel[] {
        new Pixel(0, 255, 0), // Green
        new Pixel(255, 255, 0), // Yellow
        new Pixel(255, 0, 0) // Red
    };
    
    private Pixel GetProgressGradientColor(double t)
    {
        if (!PartyModeEnabled)
            return new Pixel(0, 150, 0);
        
        // t: 0.0 (start) to 1.0 (end)
        if (t < 0.5)
        {
            // Green to Yellow
            double localT = t / 0.5;
            return new Pixel(
                (byte)(0 + localT * (255 - 0)),
                255,
                0
            );
        }
        else
        {
            // Yellow to Red
            double localT = (t - 0.5) / 0.5;
            return new Pixel(
                255,
                (byte)(255 - localT * 255),
                0
            );
        }
    }
    
    private void DrawProgress()
    {
        if (SpotifyDataStore.Value.TrackLength == 0)
            return;

        var progress = (double)SpotifyDataStore.Value.Progress / SpotifyDataStore.Value.TrackLength;
        var progressBarLength = (int)(_matrixWidth * progress);
        int y = _matrixHeight - (PartyModeEnabled ? 3 : 1);
        _progressBarWavePhase += 0.25;

        for (int i = 0; i < _matrixWidth; i++)
        {
            double t = (double)i / (_matrixWidth - 1);
            var baseColor = GetProgressGradientColor(t);

            int waveOffset = 0;
            if (PartyModeEnabled)
            {
                waveOffset = (int)(Math.Sin((i / 4.0) + _progressBarWavePhase) * 2);
            }
            if (i < progressBarLength)
            {
                _matrix.SetPixel(i, y + waveOffset, baseColor);
            }
            if (i == progressBarLength - 1 && PartyModeEnabled)
            {
                DrawMusicNote(i, y + waveOffset - 2, baseColor);
            }
        }
    }

    // Simple pixel music note icon for the runner
    private void DrawMusicNote(int x, int y, Pixel color)
    {
        _matrix.SetPixel(x, y, color); // Head
        if (y > 0) _matrix.SetPixel(x, y-1, color); // Stem
        if (x > 0 && y > 0) _matrix.SetPixel(x-1, y-1, color); // Flag
    }

    private readonly Random _random = new Random();
    private int[] _previousBarHeights;
    private Pixel _currentBackgroundColor = new Pixel(0, 0, 0);

    // --- Fun Animated Equalizer ---
    private double _equalizerPhase = 0;
    private readonly Pixel[] _eqColors = new Pixel[] {
        new Pixel(0,255,255), new Pixel(0,128,255), new Pixel(0,255,128),
        new Pixel(255,255,0), new Pixel(255,128,0), new Pixel(255,0,128), new Pixel(128,0,255)
    };
    private readonly double[] _eqBarOffsets = new double[32]; // up to 32 bars
    private readonly double[] _eqBarSpeeds = new double[32];
    private bool _eqBarInit = false;
    
    private void DrawEqualizer()
    {
        int barWidth = 8;
        int spacing = 4;
        int padding = 10;
        int numBars = (_matrixWidth / 4 - 2 * padding) / (barWidth + spacing);
        if (_previousBarHeights == null || _previousBarHeights.Length != numBars)
            _previousBarHeights = new int[numBars];

        for (int i = 0; i < numBars; i++)
        {
            // Generate a new target height (classic random bounce)
            int targetHeight = _random.Next(1, _matrixHeight - 2 * padding);

            // Smoothly transition the height
            int barHeight = (int)(_previousBarHeights[i] + 0.2 * (targetHeight - _previousBarHeights[i]));
            _previousBarHeights[i] = barHeight;

            int xStart = _matrixWidth - (i + 1) * (barWidth + spacing) - padding;

            // Use album art colors if available, otherwise fallback to palette
            Pixel barColor;
            if (SpotifyDataStore.Value.AlbumColors != null && SpotifyDataStore.Value.AlbumColors.Count > 0)
            {
                barColor = SpotifyDataStore.Value.AlbumColors[i % SpotifyDataStore.Value.AlbumColors.Count];
            }
            else if (SpotifyDataStore.Value.AlbumColor.HasValue)
            {
                var baseColor = SpotifyDataStore.Value.AlbumColor.Value;
                byte r = (byte)Math.Min(255, Math.Max(0, baseColor.R + _random.Next(-30, 31)));
                byte g = (byte)Math.Min(255, Math.Max(0, baseColor.G + _random.Next(-30, 31)));
                byte b = (byte)Math.Min(255, Math.Max(0, baseColor.B + _random.Next(-30, 31)));
                barColor = new Pixel(r, g, b);
            }
            else
            {
                barColor = _eqColors[i % _eqColors.Length];
            }
            for (int x = xStart; x < xStart + barWidth; x++)
            {
                for (int y = 0; y < barHeight; y++)
                {
                    _matrix.SetPixel(x, _matrixHeight - 1 - y - padding, barColor);
                    _matrix.SetPixel(x + 1, _matrixHeight - y - padding, Pixel.Black);
                }
            }
        }
    }

    private void DrawHeart(int centerX, int centerY, int diameter, Pixel color)
    {
        int radius = diameter / 2;
        int triangleHeight = (int)(1.5 * radius);

        // Draw the left circle
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    _matrix.SetPixel(centerX - radius + x, centerY - radius + y, color);
                }
            }
        }

        // Draw the right circle
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    _matrix.SetPixel(centerX + radius + x, centerY - radius + y, color);
                }
            }
        }

        // Draw the triangle (bottom part of the heart)
        for (int y = 0; y <= triangleHeight; y++)
        {
            int width = (int)((1.0 - (double)y / triangleHeight) * diameter);
            int xStart = centerX - width / 2;
            int xEnd = centerX + width / 2;

            for (int x = xStart; x <= xEnd; x++)
            {
                _matrix.SetPixel(x, centerY + y, color);
            }
        }
    }

    // --- Animated Background State ---
    private double _backgroundWavePhase = 0;
    
    private void DrawAnimatedBackground()
    {
        // Animated color wave background
        _backgroundWavePhase += 0.05; // Controls speed
        for (int x = 0; x < _matrixWidth; x++)
        {
            for (int y = 0; y < _matrixHeight; y++)
            {
                // Wave pattern: color varies with x, y, and phase
                double v = Math.Sin((x + _backgroundWavePhase * 20) / 12.0) + Math.Cos((y + _backgroundWavePhase * 10) / 8.0);
                byte r = (byte)(80 + 60 * Math.Sin(v + _backgroundWavePhase));
                byte g = (byte)(80 + 60 * Math.Sin(v + _backgroundWavePhase + 2));
                byte b = (byte)(80 + 60 * Math.Sin(v + _backgroundWavePhase + 4));
                _matrix.SetPixel(x, y, new Pixel(r, g, b) / 4);
            }
        }
    }
    
    private void DrawAnimatedBackgroundWithAlbumColors()
    {
        // Use album art colors for the animated background
        _backgroundWavePhase += 0.05; // Controls speed

        var albumColors = SpotifyDataStore.Value.AlbumColors;
        int colorCount = albumColors != null ? albumColors.Count : 0;

        for (int x = 0; x < _matrixWidth; x++)
        {
            for (int y = 0; y < _matrixHeight; y++)
            {
                if (colorCount > 0)
                {
// Use a smooth wave to interpolate between album colors across the whole matrix
                    double wave = Math.Sin((x + _backgroundWavePhase * 20) / 12.0) + Math.Cos((y + _backgroundWavePhase * 10) / 8.0);
                    double colorPos = ((wave + 2) / 4) * colorCount; // Normalize wave to [0, colorCount)
                    int colorIndex = (int)Math.Floor(colorPos) % colorCount;
                    int nextColorIndex = (colorIndex + 1) % colorCount;
                    var baseColor = albumColors[colorIndex];
                    var nextColor = albumColors[nextColorIndex];
                    double t = colorPos - Math.Floor(colorPos); // Fractional part for interpolation
                    byte r = (byte)(baseColor.R * (1 - t) + nextColor.R * t);
                    byte g = (byte)(baseColor.G * (1 - t) + nextColor.G * t);
                    byte b = (byte)(baseColor.B * (1 - t) + nextColor.B * t);
                    _matrix.SetPixel(x, y, new Pixel(r, g, b) / 4);
                }
                else
                {
                    // Fallback to default animated background
                    double v = Math.Sin((x + _backgroundWavePhase * 20) / 12.0) + Math.Cos((y + _backgroundWavePhase * 10) / 8.0);
                    byte r = (byte)(80 + 60 * Math.Sin(v + _backgroundWavePhase));
                    byte g = (byte)(80 + 60 * Math.Sin(v + _backgroundWavePhase + 2));
                    byte b = (byte)(80 + 60 * Math.Sin(v + _backgroundWavePhase + 4));
                    _matrix.SetPixel(x, y, new Pixel(r, g, b) / 4);
                }
            }
        }
    }

    private void DrawPartyMode()
    {
        // Subtle party mode: animate a few sparkles per frame
        int sparkles = 10; // Number of sparkles per frame
        for (int i = 0; i < sparkles; i++)
        {
            int x = _partyRandom.Next(_matrixWidth);
            int y = _partyRandom.Next(_matrixHeight);
            var color = new Pixel(
                (byte)_partyRandom.Next(256),
                (byte)_partyRandom.Next(256),
                (byte)_partyRandom.Next(256)
            );
            _matrix.SetPixel(x, y, color);
        }
        // Optionally, add a gentle color wave background here for extra fun
    }
}
