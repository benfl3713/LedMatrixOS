using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Apps.Spotify;

/// <summary>
/// One snapshot of what Spotify is doing. The album art is already decoded and resized to a <see cref="Sprite"/> (once per track, off the
/// render thread), so the app never touches ImageSharp while drawing. An empty <see cref="Title"/> means nothing is playing.
/// </summary>
public sealed class NowPlaying
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public int ProgressMs { get; init; }
    public int DurationMs { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsSaved { get; init; }
    public string? NextTitle { get; init; }

    /// <summary>Album art at <see cref="ArtSize"/> square, or null if there is none or it failed to load.</summary>
    public Sprite? Art { get; init; }

    /// <summary>Dominant album colours, most prominent first.</summary>
    public IReadOnlyList<Pixel> Palette { get; init; } = [];

    public bool HasTrack => Title.Length > 0;

    /// <summary>Identifies the track, so a change of song can be told from a normal progress update.</summary>
    public string Key => Title + "\u0001" + Artist;

    public const int ArtSize = 64;

    public static readonly NowPlaying Nothing = new();

    /// <summary>Decodes and resizes album art to the display size; returns null (so a placeholder is shown) if the bytes are not an image.</summary>
    public static Sprite? DecodeArt(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var image = Image.Load<Rgba32>(bytes);
            image.Mutate(i => i.Resize(ArtSize, ArtSize));
            return Sprite.FromImage(image);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>The few colours the UI is tinted with, derived once per track from the album palette.</summary>
public readonly record struct SpotifyColors(Pixel Accent, Pixel A, Pixel B)
{
    public static readonly Pixel SpotifyGreen = new(30, 215, 96);

    public static readonly SpotifyColors Default = new(SpotifyGreen, new Pixel(30, 150, 80), new Pixel(40, 90, 150));

    public static SpotifyColors From(IReadOnlyList<Pixel>? palette)
    {
        if (palette is null || palette.Count == 0) return Default;

        // Accent: the most vivid colour, lifted so it reads on a dark background.
        Pixel best = palette[0];
        float bestScore = -1;
        foreach (var c in palette)
        {
            int max = Peak(c), min = Math.Min(c.R, Math.Min(c.G, c.B));
            float score = (max - min) + max * 0.25f;
            if (score > bestScore) { bestScore = score; best = c; }
        }

        var accent = Peak(best) < 25 ? SpotifyGreen : Lift(best, 175);
        var a = Peak(palette[0]) < 25 ? Default.A : Lift(palette[0], 120);
        var b = palette.Count > 1 && Peak(palette[1]) >= 25 ? Lift(palette[1], 120) : Pixel.Lerp(a, accent, 0.6f);
        return new SpotifyColors(accent, a, b);
    }

    private static int Peak(Pixel c) => Math.Max(c.R, Math.Max(c.G, c.B));

    private static Pixel Lift(Pixel c, int minPeak)
    {
        int peak = Peak(c);
        if (peak >= minPeak || peak == 0) return c;
        float k = minPeak / (float)peak;
        return new Pixel((byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k));
    }
}
