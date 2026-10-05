using LedMatrixOS.Core;
using LedMatrixOS.Core.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LedMatrixOS.Tests;

public class MediaLibraryTests
{
    private static MediaException Fails(Func<MediaItem> add) => Assert.Throws<MediaException>(add);

    [Fact]
    public void Png_IsStoredWithItsMetadata()
    {
        using var fx = new MediaFixture();
        var item = fx.Add(MediaImages.TestCard(160, 80), "card.png");

        Assert.Equal(MediaKind.Image, item.Kind);
        Assert.Equal(("card.png", 160, 80, 1, 0L), (item.Name, item.Width, item.Height, item.Frames, item.DurationMs));
        Assert.True(MediaLibrary.TryNormalizeId(item.Id, out _));
        Assert.Equal(item, Assert.Single(fx.Library.Items));
        Assert.Equal(item, fx.Library.Get(item.Id));
    }

    [Fact]
    public void Gif_ReportsFramesAndDuration()
    {
        using var fx = new MediaFixture();
        var item = fx.Add(MediaImages.Gif(frames: 3, delayCs: 50));

        Assert.Equal(MediaKind.Gif, item.Kind);
        Assert.Equal(3, item.Frames);
        Assert.Equal(1500, item.DurationMs);
    }

    [Fact]
    public void Gif_FrameDelaysBelowTheMinimumAreClamped()
    {
        using var fx = new MediaFixture();
        var item = fx.Add(MediaImages.Gif(frames: 4, delayCs: 1));   // 10 ms asked for, 20 ms allowed

        Assert.Equal(4 * MediaFramePreparer.MinFrameDelayMs, item.DurationMs);
    }

    [Fact]
    public void Gif_ZeroDelayUsesTheDefault()
    {
        using var fx = new MediaFixture();
        var item = fx.Add(MediaImages.Gif(frames: 2, delayCs: 0));

        Assert.Equal(2 * MediaFramePreparer.DefaultFrameDelayMs, item.DurationMs);
    }

    [Fact]
    public void TypeComesFromTheContent_NotTheFileName()
    {
        using var fx = new MediaFixture();
        Assert.Equal(MediaKind.Image, fx.Add(MediaImages.TestCard(), "holiday.mp4").Kind);
        Assert.Equal(MediaKind.Gif, fx.Add(MediaImages.Gif(), "clip.png").Kind);
    }

    [Theory]
    [InlineData("not an image at all, just some text")]
    [InlineData("")]
    public void Garbage_IsRejectedAsUnsupported(string text)
    {
        using var fx = new MediaFixture();
        var ex = Fails(() => fx.Add(System.Text.Encoding.UTF8.GetBytes(text), "x.png"));
        Assert.Equal(MediaError.Unsupported, ex.Error);
        Assert.Empty(fx.Library.Items);
    }

    [Fact]
    public void TruncatedPicture_IsRejectedAndLeavesNothingBehind()
    {
        using var fx = new MediaFixture();
        var png = MediaImages.TestCard();
        var ex = Fails(() => fx.Add(png[..(png.Length / 2)]));

        Assert.Equal(MediaError.Unsupported, ex.Error);
        Assert.Empty(fx.Library.Items);
        Assert.Empty(Directory.GetDirectories(fx.Root));
        Assert.Empty(Directory.GetFiles(fx.Root, ".upload-*"));
    }

    [Fact]
    public void Oversize_IsRejectedAsTooLarge()
    {
        using var fx = new MediaFixture(configure: c => c.MaxBytes = 100);
        var ex = Fails(() => fx.Add(MediaImages.TestCard()));
        Assert.Equal(MediaError.TooLarge, ex.Error);
        Assert.Empty(fx.Library.Items);
    }

    [Fact]
    public void HugeDimensions_AreRejected()
    {
        using var fx = new MediaFixture();
        var wide = MediaImages.Png(9000, 1, (_, _) => new Rgba32(10, 10, 10));   // tiny file, huge canvas
        var ex = Fails(() => fx.Add(wide));
        Assert.Equal(MediaError.Unsupported, ex.Error);
    }

    [Fact]
    public void DecodeBudget_RejectsAnAnimationThatWouldUseTooMuchMemory()
    {
        using var fx = new MediaFixture(configure: c => c.MaxDecodePixels = 64 * 32);   // one frame of the test GIF, no more
        var ex = Fails(() => fx.Add(MediaImages.Gif(frames: 3)));
        Assert.Equal(MediaError.Unsupported, ex.Error);
    }

    [Fact]
    public void LibraryIsBounded()
    {
        var card = MediaImages.TestCard();
        using var fx = new MediaFixture(configure: c => c.MaxLibraryBytes = card.Length * 5);
        fx.Add(card);
        var ex = Fails(() =>
        {
            for (int i = 0; i < 10; i++) fx.Add(card);   // each item also keeps a thumbnail, so a handful fit
            return default!;
        });
        Assert.Equal(MediaError.LibraryFull, ex.Error);
        Assert.True(fx.Library.TotalBytes <= fx.Config.MaxLibraryBytes);
        Assert.Equal(Directory.GetDirectories(fx.Root).Length, fx.Library.Items.Count);
    }

    [Fact]
    public void Thumbnail_IsAPngNoWiderThan128()
    {
        using var fx = new MediaFixture();
        var wide = fx.Add(MediaImages.Panorama(512, 64));
        var small = fx.Add(MediaImages.TestCard(40, 20));

        using var big = Image.Load(fx.Library.GetThumb(wide.Id)!);
        Assert.Equal(128, big.Width);
        Assert.Equal(16, big.Height);
        using var tiny = Image.Load(fx.Library.GetThumb(small.Id)!);
        Assert.Equal((40, 20), (tiny.Width, tiny.Height));   // never scaled up
        Assert.Equal("PNG", Image.DetectFormat(fx.Library.GetThumb(wide.Id)!).Name);
        Assert.Null(fx.Library.GetThumb(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void Library_SurvivesARestart()
    {
        using var fx = new MediaFixture();
        var a = fx.Add(MediaImages.TestCard(), "a");
        var b = fx.Add(MediaImages.Gif(), "b");

        var reopened = fx.Reopen();

        Assert.Equal([a, b], reopened.Items);
        Assert.NotNull(reopened.GetThumb(b.Id));
        Assert.False(File.Exists(Path.Combine(fx.Root, "media.json.tmp")));   // the index is replaced, not left half written
        Assert.True(File.Exists(Path.Combine(fx.Root, "media.json")));
    }

    [Fact]
    public void DamagedIndex_StartsEmpty()
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard());
        File.WriteAllText(Path.Combine(fx.Root, "media.json"), "{ not json");

        Assert.Empty(fx.Reopen().Items);
    }

    [Fact]
    public void Delete_RemovesTheItemAndItsFiles()
    {
        using var fx = new MediaFixture();
        var item = fx.Add(MediaImages.TestCard());
        string dir = Path.Combine(fx.Root, item.Id);
        Assert.True(Directory.Exists(dir));

        Assert.True(fx.Library.Delete(item.Id));

        Assert.False(Directory.Exists(dir));
        Assert.Null(fx.Library.Get(item.Id));
        Assert.Empty(fx.Reopen().Items);
        Assert.False(fx.Library.Delete(item.Id));
    }

    [Fact]
    public void Version_ChangesWhenTheLibraryDoes()
    {
        using var fx = new MediaFixture();
        int v0 = fx.Library.Version;
        var item = fx.Add(MediaImages.TestCard());
        int v1 = fx.Library.Version;
        fx.Library.Delete(item.Id);
        Assert.True(v1 > v0 && fx.Library.Version > v1);
    }

    // ---- path traversal --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("../media.json")]
    [InlineData("..\\..\\secret")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("00000000000000000000000000000000/../..")]
    public void IdsThatAreNotGuidsAreNeverResolved(string id)
    {
        using var fx = new MediaFixture();
        fx.Add(MediaImages.TestCard());
        string outside = Path.Combine(Path.GetDirectoryName(fx.Root)!, "ledmedia-outside-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outside, "keep me");
        try
        {
            Assert.False(MediaLibrary.TryNormalizeId(id, out _));
            Assert.Null(fx.Library.Get(id));
            Assert.Null(fx.Library.GetThumb(id));
            Assert.False(fx.Library.Delete(id));
            Assert.Equal(MediaError.NotFound, Assert.Throws<MediaException>(() => fx.Library.OpenClip(id, new ClipRequest(256, 64, MediaFit.Fit, Pixel.Black))).Error);
            Assert.True(File.Exists(outside));
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public void ClientFileNamesAreOnlyLabels()
    {
        using var fx = new MediaFixture();
        var item = fx.Add(MediaImages.TestCard(), "..\\..\\evil/../../hack.png");

        Assert.Equal("hack.png", item.Name);
        // Everything the upload created is inside its own GUID folder.
        var dir = Assert.Single(Directory.GetDirectories(fx.Root));
        Assert.Equal(item.Id, Path.GetFileName(dir));
        Assert.DoesNotContain(Directory.GetFiles(fx.Root, "*", SearchOption.AllDirectories), f => f.Contains("hack") || f.Contains("evil"));
    }

    [Theory]
    [InlineData(null, "Untitled")]
    [InlineData("   ", "Untitled")]
    [InlineData("a\u0000b\nc.png", "abc.png")]
    public void DisplayName_IsCleaned(string? raw, string expected) => Assert.Equal(expected, MediaLibrary.DisplayName(raw));

    [Fact]
    public void DisplayName_IsBounded() => Assert.Equal(80, MediaLibrary.DisplayName(new string('x', 500)).Length);

    // ---- video -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Video_WithoutFfmpeg_IsUnavailable()
    {
        using var fx = new MediaFixture();
        Assert.False(fx.Library.Capabilities.Video);
        var ex = Fails(() => fx.Add(MediaImages.Mp4Header(), "clip.mp4"));
        Assert.Equal(MediaError.VideoUnavailable, ex.Error);
        Assert.Empty(fx.Library.Items);
        Assert.Empty(Directory.GetDirectories(fx.Root));
    }

    [Fact]
    public void Video_WithAnUnavailableTranscoder_IsUnavailable()
    {
        using var fx = new MediaFixture(new FakeTranscoder(available: false));
        Assert.False(fx.Library.Capabilities.Video);
        Assert.Equal(MediaError.VideoUnavailable, Fails(() => fx.Add(MediaImages.Mp4Header())).Error);
    }

    [Fact]
    public void Video_IsTranscodedToTheCappedCache()
    {
        var transcoder = new FakeTranscoder();
        using var fx = new MediaFixture(transcoder);
        Assert.True(fx.Library.Capabilities.Video);
        Assert.Equal(25L * 1024 * 1024, fx.Library.Capabilities.MaxBytes);

        var item = fx.Add(MediaImages.Mp4Header(), "clip.mp4");

        Assert.Equal(1, transcoder.Calls);
        Assert.Equal((MediaKind.Video, 256, 64, 10, 1000L), (item.Kind, item.Width, item.Height, item.Frames, item.DurationMs));
        Assert.Equal(["thumb.png", "video.raw"], Directory.GetFiles(Path.Combine(fx.Root, item.Id)).Select(Path.GetFileName).Order().ToArray());   // the original is not kept
        Assert.NotNull(fx.Library.GetThumb(item.Id));
    }

    [Theory]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8 })]                                  // webm / mkv
    [InlineData(new byte[] { 0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'q', (byte)'t', (byte)' ', (byte)' ' })]   // mov
    public void VideoContainers_AreRecognisedByMagicBytes(byte[] head)
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        Assert.Equal(MediaKind.Video, fx.Add(head).Kind);
    }

    [Fact]
    public void HeifPicture_IsNotMistakenForVideo()
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        byte[] heic = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0];
        Assert.Equal(MediaError.Unsupported, Fails(() => fx.Add(heic)).Error);
    }

    [Fact]
    public void VideoClip_StreamsFramesFromTheCache()
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        var item = fx.Add(MediaImages.Mp4Header());
        using var clip = fx.Library.OpenClip(item.Id, new ClipRequest(256, 64, MediaFit.Fit, Pixel.Black));

        Assert.Equal((256, 64, 10, 1000L), (clip.Width, clip.Height, clip.FrameCount, clip.TotalMs));
        Assert.Equal(0, clip.FrameAt(0));
        Assert.Equal(5, clip.FrameAt(500));
        Assert.Equal(9, clip.FrameAt(5000));

        var frame = new FrameBuffer(256, 64);
        clip.Draw(frame, 3, 0, 0);
        Assert.Equal(new Pixel(80, 40, 90), frame.GetPixel(10, 10));
        clip.Draw(frame, 9, 0, 0);
        Assert.Equal(new Pixel(200, 40, 90), frame.GetPixel(255, 63));
    }

    [Fact]
    public void VideoClip_BrightnessTrimDimsTheOutput()
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        var item = fx.Add(MediaImages.Mp4Header());
        using var clip = fx.Library.OpenClip(item.Id, new ClipRequest(256, 64, MediaFit.Fit, Pixel.Black, 50));
        var frame = new FrameBuffer(256, 64);
        clip.Draw(frame, 3, 0, 0);
        Assert.Equal(new Pixel(40, 20, 45), frame.GetPixel(0, 0));
    }

    [Fact]
    public void VideoFile_CanBeDeletedWhilePlaying()
    {
        using var fx = new MediaFixture(new FakeTranscoder());
        var item = fx.Add(MediaImages.Mp4Header());
        using var clip = fx.Library.OpenClip(item.Id, new ClipRequest(256, 64, MediaFit.Fit, Pixel.Black));

        Assert.True(fx.Library.Delete(item.Id));
        clip.Draw(new FrameBuffer(256, 64), 0, 0, 0);   // still readable
    }

    [Fact]
    public void RawVideoHeader_RejectsAShortFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ledraw-" + Guid.NewGuid().ToString("N"));
        try
        {
            RawVideoFile.Write(path, 4, 4, 10, [new byte[48], new byte[48]]);
            using (var ok = File.OpenRead(path)) Assert.True(RawVideoFile.TryReadHeader(ok, out _, out _, out int frames, out _) && frames == 2);
            File.WriteAllBytes(path, File.ReadAllBytes(path)[..60]);   // second frame cut off
            using var cut = File.OpenRead(path);
            Assert.False(RawVideoFile.TryReadHeader(cut, out _, out _, out _, out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Ffmpeg_ArgumentsAreAnArgumentListWithTheCaps()
    {
        var args = FfmpegVideoTranscoder.BuildArguments("/tmp/in put; rm -rf /.mp4", 256, 64, 20, 15);

        Assert.Contains("/tmp/in put; rm -rf /.mp4", args);   // one element, never split or interpreted
        Assert.Equal("20", args[args.ToList().IndexOf("-t") + 1]);
        Assert.Contains("fps=15,", args[args.ToList().IndexOf("-vf") + 1]);
        Assert.Contains("scale=256:64", args[args.ToList().IndexOf("-vf") + 1]);
    }

    [Fact]
    public void Ffmpeg_MissingExecutable_IsUnavailable()
    {
        var transcoder = new FfmpegVideoTranscoder(Path.Combine(Path.GetTempPath(), "no-such-ffmpeg-" + Guid.NewGuid().ToString("N")), TimeSpan.FromSeconds(5));
        Assert.False(transcoder.IsAvailable);
        var ex = Assert.ThrowsAsync<MediaException>(() => transcoder.TranscodeAsync("in", "out", 256, 64, 20, 15, CancellationToken.None)).GetAwaiter().GetResult();
        Assert.Equal(MediaError.VideoUnavailable, ex.Error);
    }

    // ---- scaling ---------------------------------------------------------------------------------------------------

    private static BufferClip Clip(byte[] png, MediaFit fit, int brightness = 100, Pixel? background = null)
    {
        using var fx = new MediaFixture();
        var item = fx.Add(png);
        return (BufferClip)fx.Library.OpenClip(item.Id, new ClipRequest(256, 64, fit, background ?? Pixel.Black, brightness));
    }

    [Fact]
    public void Fit_KeepsTheAspectWithBars()
    {
        using var clip = Clip(MediaImages.TestCard(160, 80), MediaFit.Fit, background: new Pixel(0, 0, 90));
        var f = clip.GetFrame(0);

        Assert.Equal((256, 64), (clip.Width, clip.Height));
        Assert.Equal(new Pixel(0, 0, 90), f.GetPixel(10, 32));     // bar on the left (picture is 128 wide, centred)
        Assert.Equal(new Pixel(0, 0, 90), f.GetPixel(250, 32));    // and on the right
        Assert.True(f.GetPixel(100, 10) is { R: > 150, G: < 100 });   // the red quadrant of the picture (128 wide from x = 64)
    }

    [Fact]
    public void Fill_CropsToCoverTheDisplay()
    {
        using var clip = Clip(MediaImages.TestCard(160, 80), MediaFit.Fill);
        var f = clip.GetFrame(0);

        for (int x = 0; x < 256; x += 51) Assert.NotEqual(Pixel.Black, f.GetPixel(x, 0));   // no bars anywhere
        Assert.NotEqual(Pixel.Black, f.GetPixel(255, 63));
    }

    [Fact]
    public void Center_IsPixelForPixel()
    {
        using var clip = Clip(MediaImages.TestCard(40, 20), MediaFit.Center);
        var f = clip.GetFrame(0);

        Assert.Equal(Pixel.White, f.GetPixel(108, 22));   // top left corner of a 40x20 picture centred at (128, 32)
        Assert.Equal(new Pixel(220, 40, 40), f.GetPixel(110, 24));
        Assert.Equal(Pixel.Black, f.GetPixel(100, 10));
    }

    [Fact]
    public void Scroll_ScalesToTheHeightAndKeepsTheWidth()
    {
        using var clip = Clip(MediaImages.Panorama(512, 32), MediaFit.Scroll);
        Assert.Equal((1024, 64), (clip.Width, clip.Height));

        using var narrow = Clip(MediaImages.TestCard(40, 20), MediaFit.Scroll);   // not wider than the display: shown like Fit
        Assert.Equal((256, 64), (narrow.Width, narrow.Height));
    }

    [Fact]
    public void Brightness_TrimsEveryPixel()
    {
        using var full = Clip(MediaImages.TestCard(256, 64), MediaFit.Stretch);
        using var half = Clip(MediaImages.TestCard(256, 64), MediaFit.Stretch, brightness: 50);
        var a = full.GetFrame(0).GetPixel(20, 10);
        var b = half.GetFrame(0).GetPixel(20, 10);
        Assert.Equal(new Pixel((byte)(a.R / 2), (byte)(a.G / 2), (byte)(a.B / 2)), b);
    }

    [Fact]
    public void TransparentPixels_AreBlendedOntoTheBackground()
    {
        using var clip = Clip(MediaImages.Png(256, 64, (x, _) => x < 128 ? new Rgba32(255, 0, 0, 0) : new Rgba32(255, 0, 0, 255)), MediaFit.Stretch, background: new Pixel(0, 0, 200));
        var f = clip.GetFrame(0);
        Assert.Equal(new Pixel(0, 0, 200), f.GetPixel(10, 10));
        Assert.Equal(new Pixel(255, 0, 0), f.GetPixel(200, 10));
    }

    [Fact]
    public void GifClip_FindsTheFrameForATime()
    {
        using var clip = Clip(MediaImages.Gif(frames: 3, delayCs: 50), MediaFit.Fit);

        Assert.Equal((3, 1500L), (clip.FrameCount, clip.TotalMs));
        Assert.Equal(0, clip.FrameAt(0));
        Assert.Equal(0, clip.FrameAt(499));
        Assert.Equal(1, clip.FrameAt(500));
        Assert.Equal(2, clip.FrameAt(1499));
        Assert.Equal(2, clip.FrameAt(99999));   // clamped; the player takes the time modulo the loop first
    }
}
