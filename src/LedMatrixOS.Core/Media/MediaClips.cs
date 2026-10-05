using System.IO.MemoryMappedFiles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LedMatrixOS.Core.Media;

/// <summary>How a picture is placed on the display.</summary>
public enum MediaFit
{
    /// <summary>Whole picture visible, aspect kept, bars in the background colour.</summary>
    Fit,
    /// <summary>Fills the display, aspect kept, the overflow is cropped.</summary>
    Fill,
    /// <summary>Fills the display, distorting the aspect.</summary>
    Stretch,
    /// <summary>No scaling: one picture pixel per LED, centred (cropped when bigger).</summary>
    Center,
    /// <summary>Scaled to the display height; a wider picture scrolls sideways.</summary>
    Scroll,
}

/// <summary>What a clip is prepared for.</summary>
/// <param name="BrightnessPercent">Brightness trim, 1-100: applied once while preparing so playback is a plain copy.</param>
public readonly record struct ClipRequest(int Width, int Height, MediaFit Fit, Pixel Background, int BrightnessPercent = 100);

/// <summary>
/// A prepared, playable frame set. Nothing here allocates while playing: <see cref="FrameAt"/> and <see cref="Draw"/> only index
/// pre-built frames (or read from a memory mapped file into a reused buffer).
/// </summary>
public abstract class MediaClip : IDisposable
{
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract int FrameCount { get; }
    /// <summary>One loop of the animation in milliseconds (0 for a still).</summary>
    public abstract long TotalMs { get; }
    public bool Animated => FrameCount > 1;

    /// <summary>The frame to show <paramref name="ms"/> into one loop (clamped to the loop).</summary>
    public abstract int FrameAt(long ms);

    /// <summary>Copies a frame onto <paramref name="target"/> with its top left at (dx, dy); anything outside is clipped.</summary>
    public abstract void Draw(FrameBuffer target, int frame, int dx, int dy);

    public virtual void Dispose() { }

    public static byte[] BrightnessTable(int percent)
    {
        percent = Math.Clamp(percent, 1, 100);
        var table = new byte[256];
        for (int i = 0; i < 256; i++) table[i] = (byte)(i * percent / 100);
        return table;
    }
}

/// <summary>Pre-scaled frames held as <see cref="FrameBuffer"/>s (pictures and GIFs).</summary>
public sealed class BufferClip(FrameBuffer[] frames, int[] frameEndMs) : MediaClip
{
    public override int Width => frames[0].Width;
    public override int Height => frames[0].Height;
    public override int FrameCount => frames.Length;
    public override long TotalMs => frameEndMs.Length > 1 ? frameEndMs[^1] : 0;

    public override int FrameAt(long ms)
    {
        if (frames.Length == 1) return 0;
        if (ms < 0) return 0;
        // First frame whose end is beyond ms.
        int lo = 0, hi = frameEndMs.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (frameEndMs[mid] > ms) hi = mid; else lo = mid + 1;
        }
        return lo;
    }

    public override void Draw(FrameBuffer target, int frame, int dx, int dy) =>
        target.CopyFrom(frames[(uint)frame < (uint)frames.Length ? frame : 0], dx, dy);

    /// <summary>The frame as stored (for tests).</summary>
    public FrameBuffer GetFrame(int index) => frames[index];
}

/// <summary>Frames streamed from the raw video cache through a memory mapped view.</summary>
public sealed class VideoClip : MediaClip
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte[] _buffer;
    private readonly byte[] _lut;
    private readonly int _width, _height, _frames, _fps;
    private readonly long _frameBytes;

    public VideoClip(string path, int brightnessPercent)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (!RawVideoFile.TryReadHeader(stream, out _width, out _height, out _frames, out _fps))
                throw new InvalidDataException("Not a valid video cache file");
            _file = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _frameBytes = (long)_width * _height * 3;
        _buffer = new byte[_frameBytes];
        _lut = BrightnessTable(brightnessPercent);
    }

    public override int Width => _width;
    public override int Height => _height;
    public override int FrameCount => _frames;
    public override long TotalMs => _frames > 1 ? _frames * 1000L / _fps : 0;

    public override int FrameAt(long ms) => (int)Math.Clamp(ms * _fps / 1000, 0, _frames - 1);

    public override void Draw(FrameBuffer target, int frame, int dx, int dy)
    {
        frame = Math.Clamp(frame, 0, _frames - 1);
        _view.ReadArray(RawVideoFile.HeaderSize + frame * _frameBytes, _buffer, 0, _buffer.Length);
        var lut = _lut;
        int i = 0;
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++, i += 3)
                target.SetPixel(dx + x, dy + y, new Pixel(lut[_buffer[i]], lut[_buffer[i + 1]], lut[_buffer[i + 2]]));
    }

    public override void Dispose()
    {
        _view.Dispose();
        _file.Dispose();
    }
}

/// <summary>Decodes a stored picture or GIF and scales every frame for the display.</summary>
public static class MediaFramePreparer
{
    public const int MinFrameDelayMs = 20;
    public const int DefaultFrameDelayMs = 100;

    /// <summary>The delay of one decoded frame in milliseconds, never below <see cref="MinFrameDelayMs"/>.</summary>
    public static int FrameDelayMs(ImageFrameMetadata metadata)
    {
        int ms = 0;
        if (metadata.TryGetGifMetadata(out var gif)) ms = gif.FrameDelay * 10;
        else if (metadata.TryGetWebpFrameMetadata(out var webp)) ms = (int)Math.Min(webp.FrameDelay, 60_000u);
        return ms <= 0 ? DefaultFrameDelayMs : Math.Max(ms, MinFrameDelayMs);
    }

    public static BufferClip Prepare(string path, ClipRequest request, int maxFrames)
    {
        using var image = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = (uint)Math.Max(1, maxFrames) }, path);
        int count = image.Frames.Count;
        var lut = MediaClip.BrightnessTable(request.BrightnessPercent);
        var fit = request.Fit;
        if (fit == MediaFit.Scroll && count > 1) fit = MediaFit.Fit;   // sideways scrolling is for stills

        var frames = new FrameBuffer[count];
        var ends = new int[count];
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            total += count == 1 ? 0 : FrameDelayMs(image.Frames[i].Metadata);
            ends[i] = total;
            frames[i] = Compose(image.Frames.CloneFrame(i), request, fit, lut, dispose: true);
        }

        return new BufferClip(frames, ends);
    }

    internal static FrameBuffer Compose(Image<Rgba32> source, ClipRequest request, MediaFit fit, byte[] lut, bool dispose)
    {
        try
        {
            int W = request.Width, H = request.Height;
            int w = source.Width, h = source.Height;
            int outW = W, outH = H;
            int tw, th;
            switch (fit)
            {
                case MediaFit.Fill:
                {
                    double s = Math.Max((double)W / w, (double)H / h);
                    tw = Math.Max(W, (int)Math.Round(w * s));
                    th = Math.Max(H, (int)Math.Round(h * s));
                    break;
                }
                case MediaFit.Stretch:
                    tw = W; th = H;
                    break;
                case MediaFit.Center:
                    tw = w; th = h;
                    break;
                case MediaFit.Scroll when (long)w * H / h > W:
                    th = H;
                    tw = (int)Math.Min(8192, (long)w * H / h);
                    outW = tw;
                    break;
                default:   // Fit (and Scroll for a picture that is not wider than the display)
                {
                    double s = Math.Min((double)W / w, (double)H / h);
                    tw = Math.Max(1, (int)Math.Round(w * s));
                    th = Math.Max(1, (int)Math.Round(h * s));
                    break;
                }
            }

            if (tw != w || th != h)
            {
                var sampler = tw > w || th > h ? KnownResamplers.NearestNeighbor : KnownResamplers.Lanczos3;
                source.Mutate(c => c.Resize(new ResizeOptions { Size = new Size(tw, th), Sampler = sampler, Mode = ResizeMode.Stretch }));
            }

            var bg = new Pixel(lut[request.Background.R], lut[request.Background.G], lut[request.Background.B]);
            var frame = new FrameBuffer(outW, outH);
            frame.Clear(bg);
            int offX = (outW - tw) / 2, offY = (outH - th) / 2;   // negative when cropping
            source.ProcessPixelRows(accessor =>
            {
                for (int sy = 0; sy < accessor.Height; sy++)
                {
                    int y = sy + offY;
                    if ((uint)y >= (uint)outH) continue;
                    var row = accessor.GetRowSpan(sy);
                    for (int sx = 0; sx < row.Length; sx++)
                    {
                        int x = sx + offX;
                        if ((uint)x >= (uint)outW) continue;
                        var p = row[sx];
                        if (p.A == 0) continue;
                        var px = new Pixel(lut[p.R], lut[p.G], lut[p.B]);
                        frame.SetPixel(x, y, p.A == 255 ? px : bg.Blend(px, p.A / 255f));
                    }
                }
            });
            return frame;
        }
        finally
        {
            if (dispose) source.Dispose();
        }
    }
}
