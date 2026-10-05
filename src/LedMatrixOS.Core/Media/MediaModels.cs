using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core.Media;

/// <summary>What kind of media an item is. Serialised lower case ("image", "gif", "video").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
public enum MediaKind
{
    [JsonStringEnumMemberName("image")] Image,
    [JsonStringEnumMemberName("gif")] Gif,
    [JsonStringEnumMemberName("video")] Video,
}

/// <summary>
/// One library item as the API shows it. <c>Frames</c> is 1 for a still; <c>DurationMs</c> is the length of one loop of an
/// animation (0 for a still). Width and height are those of the uploaded file.
/// </summary>
public sealed record MediaItem(string Id, string Name, MediaKind Kind, int Width, int Height, int Frames, long DurationMs);

/// <summary>What the server can do with uploads (GET /api/media/capabilities).</summary>
public sealed record MediaCapabilities(bool Video, long MaxBytes);

public enum MediaError
{
    /// <summary>Not an image or video this player understands, or the file is corrupt.</summary>
    Unsupported,
    TooLarge,
    VideoUnavailable,
    LibraryFull,
    NotFound,
}

public sealed class MediaException(MediaError error, string message) : Exception(message)
{
    public MediaError Error { get; } = error;
}

/// <summary>Limits and tools for the media library. Bound from the <c>Media</c> configuration section.</summary>
public sealed class MediaConfig
{
    /// <summary>Size of the display the frame sets are prepared for.</summary>
    public int DisplayWidth { get; set; } = 256;
    public int DisplayHeight { get; set; } = 64;

    /// <summary>Largest single upload.</summary>
    public long MaxBytes { get; set; } = 25L * 1024 * 1024;
    /// <summary>Bound on everything stored in the library.</summary>
    public long MaxLibraryBytes { get; set; } = 500L * 1024 * 1024;
    /// <summary>Largest width or height of an uploaded picture (guards against decompression bombs).</summary>
    public int MaxDimension { get; set; } = 8192;
    /// <summary>Largest width x height x frames that is decoded (4 bytes per pixel while decoding).</summary>
    public long MaxDecodePixels { get; set; } = 40_000_000;
    public int MaxFrames { get; set; } = 500;

    public int VideoMaxSeconds { get; set; } = 20;
    public int VideoMaxFps { get; set; } = 15;
    /// <summary>The ffmpeg executable: a path, or a name looked up on PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int TranscodeTimeoutSeconds { get; set; } = 120;

    public static MediaConfig From(IConfiguration configuration, int displayWidth, int displayHeight)
    {
        var config = new MediaConfig { DisplayWidth = displayWidth, DisplayHeight = displayHeight };
        var section = configuration.GetSection("Media");
        config.MaxBytes = Read(section, "MaxBytes", config.MaxBytes);
        config.MaxLibraryBytes = Read(section, "MaxLibraryBytes", config.MaxLibraryBytes);
        config.MaxDimension = Read(section, "MaxDimension", config.MaxDimension);
        config.MaxDecodePixels = Read(section, "MaxDecodePixels", config.MaxDecodePixels);
        config.MaxFrames = Read(section, "MaxFrames", config.MaxFrames);
        config.VideoMaxSeconds = Read(section, "VideoMaxSeconds", config.VideoMaxSeconds);
        config.VideoMaxFps = Read(section, "VideoMaxFps", config.VideoMaxFps);
        config.TranscodeTimeoutSeconds = Read(section, "TranscodeTimeoutSeconds", config.TranscodeTimeoutSeconds);
        if (section["FfmpegPath"] is { Length: > 0 } ffmpeg) config.FfmpegPath = ffmpeg;
        return config;
    }

    private static T Read<T>(IConfigurationSection section, string key, T fallback) where T : IParsable<T> =>
        T.TryParse(section[key], System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
