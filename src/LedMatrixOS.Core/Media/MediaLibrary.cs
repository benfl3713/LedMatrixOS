using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Core.Media;

/// <summary>
/// The uploaded pictures, GIFs and videos. Everything lives under one root directory, one folder per item named by a generated GUID
/// (client file names never reach the file system), plus <c>media.json</c>, a small index that is replaced atomically.
/// <para>
/// A picture or GIF keeps its (validated) original as <c>source.bin</c> and is decoded and scaled for the display when it is played
/// (<see cref="OpenClip"/>). A video is transcoded once, at upload, into the capped raw cache <c>video.raw</c>. Each item also has
/// <c>thumb.png</c>.
/// </para>
/// </summary>
public sealed class MediaLibrary
{
    private const string IndexName = "media.json";
    private const string SourceName = "source.bin";
    private const string VideoName = "video.raw";
    private const string ThumbName = "thumb.png";
    private const int ThumbWidth = 128;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record Entry(MediaItem Item, long Bytes, DateTime CreatedUtc);

    private readonly object _gate = new();
    private readonly string _root;
    private readonly IVideoTranscoder _transcoder;
    private List<Entry> _entries = [];
    private MediaItem[] _snapshot = [];
    private int _version;

    public MediaLibrary(string root, MediaConfig config, IVideoTranscoder? transcoder = null)
    {
        _root = Path.GetFullPath(root);
        Config = config;
        _transcoder = transcoder ?? new NoVideoTranscoder();
        Directory.CreateDirectory(_root);
        Load();
    }

    public MediaConfig Config { get; }

    /// <summary>Counts every change to the library, so a player can tell cheaply that its list is stale.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>The items, oldest first. A new array on every change, so reading it never allocates.</summary>
    public IReadOnlyList<MediaItem> Items => Volatile.Read(ref _snapshot);

    public MediaCapabilities Capabilities => new(_transcoder.IsAvailable, Config.MaxBytes);

    public long TotalBytes
    {
        get { lock (_gate) return _entries.Sum(e => e.Bytes); }
    }

    public MediaItem? Get(string id)
    {
        if (!TryNormalizeId(id, out var key)) return null;
        lock (_gate) return _entries.FirstOrDefault(e => e.Item.Id == key)?.Item;
    }

    /// <summary>The ids are GUIDs we generated; anything else (including path fragments) is not an id.</summary>
    public static bool TryNormalizeId(string? id, out string normalized)
    {
        normalized = "";
        if (id is null || id.Length != 32 || !Guid.TryParseExact(id, "N", out var guid)) return false;
        normalized = guid.ToString("N");
        return true;
    }

    private string ItemDir(string id) => Path.Combine(_root, id);

    // ---- adding ----------------------------------------------------------------------------------------------------

    public async Task<MediaItem> AddAsync(Stream upload, string? name, CancellationToken ct = default)
    {
        string temp = Path.Combine(_root, ".upload-" + Guid.NewGuid().ToString("N"));
        string id = Guid.NewGuid().ToString("N");
        string dir = ItemDir(id);
        try
        {
            long length = await CopyLimitedAsync(upload, temp, Config.MaxBytes, ct).ConfigureAwait(false);
            if (length == 0) throw new MediaException(MediaError.Unsupported, "The file is empty.");
            lock (_gate)
            {
                if (_entries.Sum(e => e.Bytes) + length > Config.MaxLibraryBytes)
                    throw new MediaException(MediaError.LibraryFull, "The media library is full. Delete something first.");
            }

            var kind = Sniff(temp, out var imageFormat) ?? throw new MediaException(MediaError.Unsupported, "Not a supported picture, GIF or video.");
            Directory.CreateDirectory(dir);

            MediaItem item;
            if (kind == MediaKind.Video)
            {
                if (!_transcoder.IsAvailable) throw new MediaException(MediaError.VideoUnavailable, "Video needs ffmpeg, which is not available on this device.");
                item = await AddVideoAsync(temp, id, dir, DisplayName(name), ct).ConfigureAwait(false);
            }
            else
            {
                item = AddPicture(temp, id, dir, DisplayName(name), imageFormat);
            }

            long bytes = DirectorySize(dir);
            lock (_gate)
            {
                if (_entries.Sum(e => e.Bytes) + bytes > Config.MaxLibraryBytes)
                    throw new MediaException(MediaError.LibraryFull, "The media library is full. Delete something first.");
                _entries.Add(new Entry(item, bytes, DateTime.UtcNow));
                Publish();
                SaveIndex();
            }

            return item;
        }
        catch
        {
            TryDeleteDirectory(dir);
            throw;
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private static async Task<long> CopyLimitedAsync(Stream input, string path, long limit, CancellationToken ct)
    {
        long total = 0;
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit) throw new MediaException(MediaError.TooLarge, $"The file is larger than {limit / (1024 * 1024)} MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return total;
    }

    /// <summary>Content based type detection: image format sniffing first, then container magic bytes for video. The extension is never used.</summary>
    public static MediaKind? Sniff(string path, out string? imageFormat)
    {
        imageFormat = null;
        try
        {
            var format = Image.DetectFormat(path);
            if (format.Name is "PNG" or "JPEG" or "GIF" or "BMP" or "WEBP")
            {
                imageFormat = format.Name;
                return MediaKind.Image;   // refined to Gif when the picture turns out to be animated
            }
            return null;   // TGA, TIFF, PBM...: decodable but not something we want to store
        }
        catch (UnknownImageFormatException) { }
        catch (InvalidImageContentException) { }

        Span<byte> head = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        int n = stream.Read(head);
        if (n < 8) return null;
        if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return MediaKind.Video;                              // webm, mkv (EBML)
        var box = Encoding.ASCII.GetString(head.Slice(4, 4));
        if (box == "ftyp")                                                                                                                // mp4, mov, m4v
        {
            var brand = n >= 12 ? Encoding.ASCII.GetString(head.Slice(8, 4)) : "";
            return brand is "avif" or "avis" or "heic" or "heix" or "mif1" or "msf1" ? null : MediaKind.Video;   // HEIF pictures are not video
        }
        return box is "moov" or "mdat" or "wide" or "free" or "skip" ? MediaKind.Video : null;                                              // older QuickTime
    }

    private MediaItem AddPicture(string temp, string id, string dir, string name, string? format)
    {
        ImageInfo info;
        try { info = Image.Identify(temp); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw Corrupt(ex); }

        int frames = info.FrameMetadataCollection.Count;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > Config.MaxDimension || info.Height > Config.MaxDimension)
            throw new MediaException(MediaError.Unsupported, $"Pictures larger than {Config.MaxDimension}x{Config.MaxDimension} are not supported.");
        if (frames > Config.MaxFrames)
            throw new MediaException(MediaError.Unsupported, $"Animations longer than {Config.MaxFrames} frames are not supported.");
        if ((long)info.Width * info.Height * frames > Config.MaxDecodePixels)
            throw new MediaException(MediaError.Unsupported, "The picture is too large to decode safely on this device.");

        // Decode the first frame now: it proves the file is not corrupt and gives the thumbnail.
        try
        {
            using var first = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, temp);
            WriteThumb(first, Path.Combine(dir, ThumbName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not MediaException) { throw Corrupt(ex); }

        long duration = 0;
        if (frames > 1)
            for (int i = 0; i < frames; i++) duration += MediaFramePreparer.FrameDelayMs(info.FrameMetadataCollection[i]);

        File.Move(temp, Path.Combine(dir, SourceName));
        return new MediaItem(id, name, frames > 1 ? MediaKind.Gif : MediaKind.Image, info.Width, info.Height, frames, duration);
    }

    private async Task<MediaItem> AddVideoAsync(string temp, string id, string dir, string name, CancellationToken ct)
    {
        string raw = Path.Combine(dir, VideoName);
        VideoTranscodeResult result;
        try
        {
            result = await _transcoder.TranscodeAsync(temp, raw, Config.DisplayWidth, Config.DisplayHeight, Config.VideoMaxSeconds, Config.VideoMaxFps, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not MediaException) { throw Corrupt(ex); }

        using var check = File.OpenRead(raw);
        if (!RawVideoFile.TryReadHeader(check, out int w, out int h, out int frames, out int fps))
            throw new MediaException(MediaError.Unsupported, "The video could not be converted.");
        check.Position = RawVideoFile.HeaderSize;
        var first = new byte[w * h * 3];
        check.ReadExactly(first);
        using var image = Image.LoadPixelData<Rgb24>(first, w, h);
        WriteThumb(image, Path.Combine(dir, ThumbName));
        return new MediaItem(id, name, MediaKind.Video, w, h, frames, frames * 1000L / Math.Max(1, fps));
    }

    private static void WriteThumb<TPixel>(Image<TPixel> source, string path) where TPixel : unmanaged, IPixel<TPixel>
    {
        using var thumb = source.Clone(c =>
        {
            if (source.Width > ThumbWidth)
                c.Resize(ThumbWidth, Math.Max(1, (int)Math.Round(source.Height * (double)ThumbWidth / source.Width)), KnownResamplers.Lanczos3);
        });
        thumb.Save(path, new PngEncoder());
    }

    private static MediaException Corrupt(Exception ex) => new(MediaError.Unsupported, "The file is corrupt or not a supported picture or video (" + ex.GetType().Name + ").");

    /// <summary>Display name: a client supplied name or file name reduced to a plain, bounded label. It is only ever shown, never used as a path.</summary>
    internal static string DisplayName(string? name)
    {
        var text = new StringBuilder();
        foreach (char c in name ?? "")
            if (!char.IsControl(c)) text.Append(c);
        var clean = text.ToString().Trim();
        clean = clean.Replace('\\', '/');
        clean = clean[(clean.LastIndexOf('/') + 1)..];
        if (clean.Length > 80) clean = clean[..80];
        return clean.Length == 0 ? "Untitled" : clean;
    }

    // ---- reading ---------------------------------------------------------------------------------------------------

    public byte[]? GetThumb(string id)
    {
        if (Get(id) is not { } item) return null;
        try { return File.ReadAllBytes(Path.Combine(ItemDir(item.Id), ThumbName)); }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Decodes (picture, GIF) or opens (video) an item, prepared for <paramref name="request"/>. Slow for a big GIF: call off the
    /// render thread. Throws <see cref="MediaException"/> (NotFound) for an unknown id, and other exceptions for damaged files.
    /// </summary>
    public MediaClip OpenClip(string id, ClipRequest request)
    {
        var item = Get(id) ?? throw new MediaException(MediaError.NotFound, "Unknown media item.");
        string dir = ItemDir(item.Id);
        return item.Kind == MediaKind.Video
            ? new VideoClip(Path.Combine(dir, VideoName), request.BrightnessPercent)
            : MediaFramePreparer.Prepare(Path.Combine(dir, SourceName), request, Config.MaxFrames);
    }

    // ---- deleting --------------------------------------------------------------------------------------------------

    public bool Delete(string id)
    {
        if (!TryNormalizeId(id, out var key)) return false;
        lock (_gate)
        {
            int index = _entries.FindIndex(e => e.Item.Id == key);
            if (index < 0) return false;
            _entries.RemoveAt(index);
            Publish();
            SaveIndex();
        }
        TryDeleteDirectory(ItemDir(key));
        return true;
    }

    // ---- index -----------------------------------------------------------------------------------------------------

    private void Load()
    {
        var path = Path.Combine(_root, IndexName);
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path), Json) ?? [];
                _entries = loaded.Where(e => TryNormalizeId(e.Item?.Id, out _) && Directory.Exists(ItemDir(e.Item!.Id))).ToList();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _entries = [];   // a damaged index must not stop the app starting
            }
        }

        foreach (var leftover in Directory.EnumerateFileSystemEntries(_root, ".upload-*")) TryDeleteFile(leftover);
        Publish();
    }

    private void Publish()
    {
        Volatile.Write(ref _snapshot, _entries.Select(e => e.Item).ToArray());
        Interlocked.Increment(ref _version);
    }

    private void SaveIndex()
    {
        var path = Path.Combine(_root, IndexName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));
        File.Move(temp, path, overwrite: true);
    }

    private static long DirectorySize(string dir) => Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length);

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
