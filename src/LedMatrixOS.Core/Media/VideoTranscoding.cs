using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LedMatrixOS.Core.Media;

/// <summary>The outcome of a transcode: how many frames were written and at what rate.</summary>
public sealed record VideoTranscodeResult(int Frames, int Fps);

/// <summary>
/// Turns an uploaded video into the capped raw cache file (see <see cref="RawVideoFile"/>): at most
/// <c>maxSeconds</c> long, at most <c>maxFps</c>, every frame exactly <c>width</c> x <c>height</c> (letterboxed in black).
/// </summary>
public interface IVideoTranscoder
{
    /// <summary>False when the tool is missing, in which case video uploads are refused.</summary>
    bool IsAvailable { get; }

    Task<VideoTranscodeResult> TranscodeAsync(string inputPath, string outputPath, int width, int height, int maxSeconds, int maxFps, CancellationToken ct);
}

/// <summary>Used when nothing else is configured: no video support.</summary>
public sealed class NoVideoTranscoder : IVideoTranscoder
{
    public bool IsAvailable => false;
    public Task<VideoTranscodeResult> TranscodeAsync(string inputPath, string outputPath, int width, int height, int maxSeconds, int maxFps, CancellationToken ct) =>
        throw new MediaException(MediaError.VideoUnavailable, "Video needs ffmpeg, which is not available on this device.");
}

/// <summary>
/// The raw video cache: a 20 byte header (magic "LMV1", width, height, frames, fps as little endian int32) followed by the frames,
/// each <c>width * height * 3</c> bytes of RGB. Played by seeking into a memory mapped view, so a clip is never held in managed memory.
/// </summary>
public static class RawVideoFile
{
    public const int HeaderSize = 20;
    private static readonly byte[] Magic = "LMV1"u8.ToArray();

    public static void WriteHeader(Stream stream, int width, int height, int frames, int fps)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BitConverter.TryWriteBytes(header[4..], width);
        BitConverter.TryWriteBytes(header[8..], height);
        BitConverter.TryWriteBytes(header[12..], frames);
        BitConverter.TryWriteBytes(header[16..], fps);
        stream.Write(header);
    }

    public static bool TryReadHeader(Stream stream, out int width, out int height, out int frames, out int fps)
    {
        width = height = frames = fps = 0;
        Span<byte> header = stackalloc byte[HeaderSize];
        if (stream.Read(header) != HeaderSize || !header[..4].SequenceEqual(Magic)) return false;
        width = BitConverter.ToInt32(header[4..]);
        height = BitConverter.ToInt32(header[8..]);
        frames = BitConverter.ToInt32(header[12..]);
        fps = BitConverter.ToInt32(header[16..]);
        return width is > 0 and <= 16384 && height is > 0 and <= 16384 && frames > 0 && fps > 0
            && stream.Length >= HeaderSize + (long)frames * width * height * 3;
    }

    /// <summary>Writes a cache file from frames given as RGB byte arrays (tests and fake transcoders).</summary>
    public static VideoTranscodeResult Write(string path, int width, int height, int fps, IEnumerable<byte[]> frames)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        WriteHeader(file, width, height, 0, fps);
        int count = 0;
        foreach (var frame in frames)
        {
            if (frame.Length != width * height * 3) throw new ArgumentException("Frame has the wrong size");
            file.Write(frame);
            count++;
        }
        file.Position = 0;
        WriteHeader(file, width, height, count, fps);
        return new VideoTranscodeResult(count, fps);
    }
}

/// <summary>
/// Transcodes with the <c>ffmpeg</c> executable. The arguments go through <see cref="ProcessStartInfo.ArgumentList"/> (never a shell
/// string), the process has a timeout and is killed when it is exceeded, and its raw output is streamed to the cache file.
/// </summary>
public sealed class FfmpegVideoTranscoder(string ffmpegPath, TimeSpan timeout) : IVideoTranscoder
{
    private static readonly TimeSpan AvailabilityTtl = TimeSpan.FromSeconds(60);
    private readonly object _gate = new();
    private bool _available;
    private DateTime _checkedAt = DateTime.MinValue;

    public string FfmpegPath { get; } = ffmpegPath;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                if (DateTime.UtcNow - _checkedAt < AvailabilityTtl) return _available;
                _available = Probe();
                _checkedAt = DateTime.UtcNow;
                return _available;
            }
        }
    }

    private bool Probe()
    {
        try
        {
            var info = NewStartInfo();
            info.ArgumentList.Add("-version");
            using var process = Process.Start(info);
            if (process is null) return false;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(5000)) { Kill(process); return false; }
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;   // not installed / not executable
        }
    }

    private ProcessStartInfo NewStartInfo() => new(FfmpegPath)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    public static IReadOnlyList<string> BuildArguments(string input, int width, int height, int maxSeconds, int maxFps) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin",
        "-t", maxSeconds.ToString(CultureInfo.InvariantCulture),
        "-i", input,
        "-an", "-sn", "-dn",
        "-vf", string.Create(CultureInfo.InvariantCulture,
            $"fps={maxFps},scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,format=rgb24"),
        "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1",
    ];

    public async Task<VideoTranscodeResult> TranscodeAsync(string inputPath, string outputPath, int width, int height, int maxSeconds, int maxFps, CancellationToken ct)
    {
        if (!IsAvailable) throw new MediaException(MediaError.VideoUnavailable, "ffmpeg is not available.");

        var info = NewStartInfo();
        foreach (var argument in BuildArguments(inputPath, width, height, maxSeconds, maxFps)) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new MediaException(MediaError.VideoUnavailable, "ffmpeg could not be started.");
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null && stderr.Length < 2000) stderr.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        try { process.StandardInput.Close(); } catch { /* already gone */ }

        long frameBytes = (long)width * height * 3;
        long maxBytes = frameBytes * maxSeconds * maxFps;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        // Killing the process closes its pipe, which is what actually unblocks a read stuck on a hung ffmpeg.
        using var killOnTimeout = timeoutCts.Token.Register(() => Kill(process));

        try
        {
            long written = 0;
            int frames;
            await using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                RawVideoFile.WriteHeader(file, width, height, 0, maxFps);
                var buffer = new byte[(int)Math.Min(frameBytes * 8, 1 << 20)];
                var stream = process.StandardOutput.BaseStream;
                int read;
                while ((read = await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false)) > 0)
                {
                    long take = Math.Min(read, maxBytes - written);
                    if (take <= 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, (int)take), timeoutCts.Token).ConfigureAwait(false);
                    written += take;
                }

                timeoutCts.Token.ThrowIfCancellationRequested();
                frames = (int)(written / frameBytes);
                file.SetLength(RawVideoFile.HeaderSize + frames * frameBytes);   // drop a partial last frame
                file.Position = 0;
                RawVideoFile.WriteHeader(file, width, height, frames, maxFps);
            }

            if (frames == 0) throw new MediaException(MediaError.Unsupported, "The video has no decodable frames.");
            return new VideoTranscodeResult(frames, maxFps);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MediaException(MediaError.Unsupported, "Transcoding the video took too long.");
        }
        finally
        {
            Kill(process);
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already exited */ }
    }
}
