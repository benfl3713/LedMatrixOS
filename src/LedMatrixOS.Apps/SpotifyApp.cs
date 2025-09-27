using LedMatrixOS.Core;
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

    // Original fields - restored from your original design
    private double _progressBarWavePhase = 0;
    private double _equalizerPhase = 0;
    private double _backgroundWavePhase = 0;
    private int[] _previousBarHeights = Array.Empty<int>();
    private readonly Random _random = new();
    private readonly Random _partyRandom = new();
    private Color _currentBackgroundColor = ColorExtensions.FromHsv(0, 0, 0);
    
    // Fonts
    private Font? _bigFont;
    private Font? _smallFont;
    private Font? _extraSmallFont;
    private Font? _quiteSmallFont;
    
    // Mock scrolling text - you'll want to replace this with your actual ScrollOverflowText
    private ScrollOverflowText? _songNameText;
    private ScrollOverflowText? _artistNameText;
    private ScrollOverflowText? _nextTrackText;
    
    // Mock data store - replace with your actual Spotify data
    private SpotifyDataStore _spotifyDataStore = new();
    
    // Party mode - replace with actual logic
    private bool PartyModeEnabled => false;

    // Image context for SetPixel simulation
    private IImageProcessingContext? _ctx;
    private int _matrixWidth;
    private int _matrixHeight;

    // Original gradient colors
    private readonly Color[] _progressGradient = new Color[] {
        ColorExtensions.FromHsv(120, 1.0f, 1.0f), // Green
        ColorExtensions.FromHsv(60, 1.0f, 1.0f),  // Yellow
        ColorExtensions.FromHsv(0, 1.0f, 1.0f)    // Red
    };

    // Original equalizer colors
    private readonly Color[] _eqColors = new Color[] {
        ColorExtensions.FromHsv(180, 1.0f, 1.0f),
        ColorExtensions.FromHsv(200, 0.8f, 1.0f),
        ColorExtensions.FromHsv(220, 1.0f, 0.8f),
        ColorExtensions.FromHsv(60, 1.0f, 1.0f),
        ColorExtensions.FromHsv(30, 1.0f, 0.8f),
        ColorExtensions.FromHsv(300, 1.0f, 0.8f),
        ColorExtensions.FromHsv(270, 0.8f, 1.0f)
    };
    
    private readonly double[] _eqBarOffsets = new double[32];
    private readonly double[] _eqBarSpeeds = new double[32];
    private bool _eqBarInit = false;

    private class SpotifyDataStore
    {
        public SpotifyData Value { get; set; } = new();
    }
    
    private class SpotifyData
    {
        public string SongName { get; set; } = "Really Long Demo Song Name That Scrolls";
        public string ArtistName { get; set; } = "Demo Artist";
        public bool IsPlaying { get; set; } = true;
        public int Progress { get; set; } = 45000; // ms
        public int TrackLength { get; set; } = 180000; // ms
        public bool? IsSavedSong { get; set; } = true;
        public string? NextTrackName { get; set; } = "Next Track Name";
        public byte[]? Artwork { get; set; }
        public byte[]? NextTrackArtwork { get; set; }
        public List<Color>? AlbumColors { get; set; }
        public Color? AlbumColor { get; set; }
    }
    
    private class ScrollOverflowText
    {
        private readonly int _x, _y, _width;
        private readonly Font _font;
        private double _scrollOffset = 0;
        
        public ScrollOverflowText(int x, int y, int width, Font font)
        {
            _x = x;
            _y = y;
            _width = width;
            _font = font;
        }
        
        public void Draw(SpotifyApp app, string text)
        {
            if (app._ctx == null || string.IsNullOrEmpty(text)) return;
            
            // Simple scrolling implementation
            _scrollOffset += 1.0;
            var textOptions = new RichTextOptions(_font)
            {
                Origin = new PointF(_x - (float)(_scrollOffset % 100), _y)
            };
            app._ctx.DrawText(textOptions, text, Color.White);
        }
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, CancellationToken cancellationToken)
    {
        _matrixWidth = dimensions.width;
        _matrixHeight = dimensions.height;
        
        try
        {
            _bigFont = SystemFonts.CreateFont("Nimbus Sans", 12, FontStyle.Bold);
            _smallFont = SystemFonts.CreateFont("Nimbus Sans", 8);
            _extraSmallFont = SystemFonts.CreateFont("Nimbus Sans", 6);
            _quiteSmallFont = SystemFonts.CreateFont("Nimbus Sans", 7);
        }
        catch
        {
            _bigFont = SystemFonts.CreateFont("Arial", 12);
            _smallFont = SystemFonts.CreateFont("Arial", 8);
            _extraSmallFont = SystemFonts.CreateFont("Arial", 6);
            _quiteSmallFont = SystemFonts.CreateFont("Arial", 7);
        }

        // Initialize mock album colors
        _spotifyDataStore.Value.AlbumColors = new List<Color>
        {
            ColorExtensions.FromHsv(220, 0.8f, 1.0f),
            ColorExtensions.FromHsv(240, 0.6f, 0.8f),
            ColorExtensions.FromHsv(200, 0.9f, 0.9f)
        };
        
        _spotifyDataStore.Value.AlbumColor = ColorExtensions.FromHsv(210, 0.7f, 0.9f);

        return base.OnActivatedAsync(dimensions, cancellationToken);
    }

    public override void Update(TimeSpan deltaTime, CancellationToken cancellationToken)
    {
        // Simulate changing progress
        if (_spotifyDataStore.Value.IsPlaying)
        {
            _spotifyDataStore.Value.Progress += (int)(deltaTime.TotalMilliseconds);
            if (_spotifyDataStore.Value.Progress >= _spotifyDataStore.Value.TrackLength)
            {
                _spotifyDataStore.Value.Progress = 0;
            }
        }
    }

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgb24>(frame.Width, frame.Height);
        
        image.Mutate(ctx =>
        {
            _ctx = ctx; // Store context for helper methods
            DrawFrame();
            _ctx = null;
        });

        frame.RenderImage(image);
    }

    // Helper method to simulate _matrix.SetPixel()
    private void SetPixel(int x, int y, Color color)
    {
        if (_ctx == null || x < 0 || y < 0 || x >= _matrixWidth || y >= _matrixHeight) return;
        _ctx.Fill(color, new RectangularPolygon(x, y, 1, 1));
    }

    // Original DrawFrame method with SetPixel converted
    private void DrawFrame()
    {
        if (string.IsNullOrEmpty(_spotifyDataStore.Value.SongName))
        {
            return;
        }
        DrawAnimatedBackgroundWithAlbumColors();
        DrawArtwork();
        DrawProgress();

        if (_spotifyDataStore.Value.IsSavedSong == true)
            DrawHeart(180, 44, 6, ColorExtensions.FromHsv(120, 1.0f, 0.7f)); // Green

        _songNameText ??= new ScrollOverflowText(74, 27, 15, _bigFont!);
        _artistNameText ??= new ScrollOverflowText(74, 47, 20, _smallFont!);

        _songNameText.Draw(this, _spotifyDataStore.Value.SongName);
        if (!string.IsNullOrEmpty(_spotifyDataStore.Value.ArtistName))
            _artistNameText.Draw(this, "by " + _spotifyDataStore.Value.ArtistName);
        
        if (_spotifyDataStore.Value.TrackLength > 0)
        {
            var progress = TimeSpan.FromMilliseconds(_spotifyDataStore.Value.Progress);
            var total = TimeSpan.FromMilliseconds(_spotifyDataStore.Value.TrackLength);
            var timeText = $"{progress:mm\\:ss} / {total:mm\\:ss}";
            var timeX = 128 - (timeText.Length * 3); // Approximate positioning
            DrawText(_extraSmallFont!, timeX, 62, ColorExtensions.FromHsv(0, 0, 0.6f), timeText);
        }
        
        if (_spotifyDataStore.Value.NextTrackName != null)
        {
            DrawNextTrackArtwork();
            _nextTrackText ??= new ScrollOverflowText(192, 62, 11, _quiteSmallFont!);
            _nextTrackText.Draw(this, "Next: " + _spotifyDataStore.Value.NextTrackName);
        }

        if (_spotifyDataStore.Value.IsPlaying)
            DrawEqualizer();
        else
            DrawPaused();

        if (PartyModeEnabled)
            DrawPartyMode();
        try
        {
            _bigFont = SystemFonts.CreateFont("Nimbus Sans", 18, FontStyle.Bold);
            _smallFont = SystemFonts.CreateFont("Nimbus Sans", 12);
            _extraSmallFont = SystemFonts.CreateFont("Nimbus Sans", 6);
        }
        catch
        {
            _bigFont = SystemFonts.CreateFont("Arial", 18);
            _smallFont = SystemFonts.CreateFont("Arial", 8);
            _extraSmallFont = SystemFonts.CreateFont("Arial", 6);
        }

        // Initialize mock album colors
        _spotifyDataStore.Value.AlbumColors = new List<Color>
        {
            ColorExtensions.FromHsv(220, 0.8f, 1.0f),
            ColorExtensions.FromHsv(240, 0.6f, 0.8f),
            ColorExtensions.FromHsv(200, 0.9f, 0.9f)
        };
    }

    // Helper method for text drawing
    private void DrawText(Font font, int x, int y, Color color, string text)
    {
        if (_ctx == null) return;
        var textOptions = new RichTextOptions(font) { Origin = new PointF(x, y) };
        _ctx.DrawText(textOptions, text, color);
    }

    private void DrawPaused()
    {
        // Draw a pause icon in the center of the matrix
        int centerX = _matrixWidth - (_matrixHeight / 2);
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
                    SetPixel(centerX + x, centerY + y, ColorExtensions.FromHsv(0, 0, 0.6f));
                }
            }
        }
    }

    private void DrawArtwork()
    {
        if (_spotifyDataStore.Value.Artwork == null)
            return;

        using var image = Image.Load<Rgb24>(_spotifyDataStore.Value.Artwork);
        image.Mutate(i => i.Resize(_matrixHeight, _matrixHeight));
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    ref Rgb24 pixel = ref pixelRow[x];
                    var color = Color.FromRgb((byte)(pixel.R / 2), (byte)(pixel.G / 2), (byte)(pixel.B / 2));
                    SetPixel(x, y, color);
                }
            }
        });
    }
    
    private void DrawNextTrackArtwork()
    {
        if (_spotifyDataStore.Value.NextTrackArtwork == null)
            return;

        var startX = 180;
        var startY = 53;

        using var image = Image.Load<Rgb24>(_spotifyDataStore.Value.NextTrackArtwork);
        image.Mutate(i => i.Resize(10, 10));
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    ref Rgb24 pixel = ref pixelRow[x];
                    var color = Color.FromRgb((byte)(pixel.R / 2), (byte)(pixel.G / 2), (byte)(pixel.B / 2));
                    SetPixel(x + startX, y + startY, color);
                }
            }
        });
    }

    private Color GetProgressGradientColor(double t)
    {
        if (!PartyModeEnabled)
            return ColorExtensions.FromHsv(120, 1.0f, 0.6f);
        
        // t: 0.0 (start) to 1.0 (end)
        if (t < 0.5)
        {
            // Green to Yellow
            double localT = t / 0.5;
            return Color.FromRgb(
                (byte)(0 + localT * 255),
                255,
                0
            );
        }
        else
        {
            // Yellow to Red
            double localT = (t - 0.5) / 0.5;
            return Color.FromRgb(
                255,
                (byte)(255 - localT * 255),
                0
            );
        }
    }
    
    private void DrawProgress()
    {
        if (_spotifyDataStore.Value.TrackLength == 0)
            return;

        var progress = (double)_spotifyDataStore.Value.Progress / _spotifyDataStore.Value.TrackLength;
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
                SetPixel(i, y + waveOffset, baseColor);
            }
            if (i == progressBarLength - 1 && PartyModeEnabled)
            {
                DrawMusicNote(i, y + waveOffset - 2, baseColor);
            }
        }
    }

    // Simple pixel music note icon for the runner
    private void DrawMusicNote(int x, int y, Color color)
    {
        SetPixel(x, y, color); // Head
        if (y > 0) SetPixel(x, y-1, color); // Stem
        if (x > 0 && y > 0) SetPixel(x-1, y-1, color); // Flag
    }

    // --- Fun Animated Equalizer ---
    private void DrawEqualizer()
    {
        int barWidth = 8;
        int spacing = 4;
        int padding = 10;
        int numBars = (_matrixHeight - 2 * padding) / (barWidth + spacing);
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
            Color barColor;
            if (_spotifyDataStore.Value.AlbumColors != null && _spotifyDataStore.Value.AlbumColors.Count > 0)
            {
                barColor = _spotifyDataStore.Value.AlbumColors[i % _spotifyDataStore.Value.AlbumColors.Count];
            }
            else if (_spotifyDataStore.Value.AlbumColor.HasValue)
            {
                var baseColorPixel = _spotifyDataStore.Value.AlbumColor.Value.ToPixel<Rgb24>();
                int r = Math.Min(255, Math.Max(0, baseColorPixel.R + _random.Next(-30, 31)));
                int g = Math.Min(255, Math.Max(0, baseColorPixel.G + _random.Next(-30, 31)));
                int b = Math.Min(255, Math.Max(0, baseColorPixel.B + _random.Next(-30, 31)));
                barColor = Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
            else
            {
                barColor = _eqColors[i % _eqColors.Length];
            }
            for (int x = xStart; x < xStart + barWidth; x++)
            {
                for (int y = 0; y < barHeight; y++)
                {
                    SetPixel(x, _matrixHeight - 1 - y - padding, barColor);
                    SetPixel(x + 1, _matrixHeight - y - padding, ColorExtensions.FromHsv(0, 0, 0));
                }
            }
        }
    }

    private void DrawHeart(int centerX, int centerY, int diameter, Color color)
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
                    SetPixel(centerX - radius + x, centerY - radius + y, color);
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
                    SetPixel(centerX + radius + x, centerY - radius + y, color);
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
                SetPixel(x, centerY + y, color);
            }
        }
    }

    // --- Animated Background State ---
    private void DrawAnimatedBackgroundWithAlbumColors()
    {
        // Use album art colors for the animated background
        _backgroundWavePhase += 0.05; // Controls speed

        var albumColors = _spotifyDataStore.Value.AlbumColors;
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
                    var baseColor = albumColors![colorIndex];
                    var nextColor = albumColors[nextColorIndex];
                    double t = colorPos - Math.Floor(colorPos); // Fractional part for interpolation
                    
                    var basePixel = baseColor.ToPixel<Rgb24>();
                    var nextPixel = nextColor.ToPixel<Rgb24>();
                    
                    int r = (int)(basePixel.R * (1 - t) + nextPixel.R * t);
                    int g = (int)(basePixel.G * (1 - t) + nextPixel.G * t);
                    int b = (int)(basePixel.B * (1 - t) + nextPixel.B * t);
                    SetPixel(x, y, Color.FromRgb((byte)(r / 4), (byte)(g / 4), (byte)(b / 4)));
                }
                else
                {
                    // Fallback to default animated background
                    double v = Math.Sin((x + _backgroundWavePhase * 20) / 12.0) + Math.Cos((y + _backgroundWavePhase * 10) / 8.0);
                    int r = (int)(80 + 60 * Math.Sin(v + _backgroundWavePhase));
                    int g = (int)(80 + 60 * Math.Sin(v + _backgroundWavePhase + 2));
                    int b = (int)(80 + 60 * Math.Sin(v + _backgroundWavePhase + 4));
                    SetPixel(x, y, Color.FromRgb((byte)(r / 4), (byte)(g / 4), (byte)(b / 4)));
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
            int x = _partyRandom.Next(_matrixWidth - _matrixHeight) + _matrixHeight;
            int y = _partyRandom.Next(_matrixHeight);
            var color = Color.FromRgb(
                (byte)_partyRandom.Next(256),
                (byte)_partyRandom.Next(256),
                (byte)_partyRandom.Next(256)
            );
            SetPixel(x, y, color);
        }
    }
}
