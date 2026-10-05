using LedMatrixOS.Core;
using LedMatrixOS.Core.Media;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps;

/// <summary>
/// Plays the pictures, GIFs and videos uploaded to the <see cref="MediaLibrary"/>: the picked items (or all of them) one after
/// another, each placed on the display by the chosen mode. Frames are prepared off the render thread (the next item is prefetched while
/// the current one plays) and drawn by index from the frame clock, so a steady frame allocates nothing.
/// <para>
/// Videos are transcoded at upload to a letterboxed (black bars) display-sized cache, so for a video <c>Fill</c>, <c>Stretch</c>,
/// <c>Center</c> and <c>Scroll</c> all show that same frame, and the background colour only fills any space around it.
/// </para>
/// </summary>
public sealed class MediaApp : SettingsAppBase
{
    public static readonly string[] Modes = ["Fit", "Fill", "Stretch", "Center", "Scroll"];

    private const double ScrollHoldSeconds = 1.0;
    private const double ScrollPixelsPerSecond = 24.0;
    private static readonly Pixel CardBackground = new(10, 12, 26);

    private readonly MediaLibrary _library;
    private int _width = 256, _height = 64;

    // Settings as applied (parsed once when they change, not per frame).
    private volatile bool _playlistDirty = true;
    private volatile bool _clipDirty;
    private MediaFit _fit = MediaFit.Fit;
    private Pixel _background = Pixel.Black;
    private readonly Random _random = new();

    private int _seenVersion = -1;
    private string[] _playlist = [];

    private TimeSpan _now;
    private MediaClip? _clip;
    private string? _clipId;
    private int _index = -1;
    private TimeSpan _itemStart;

    private Task<MediaClip?>? _pending;
    private int _pendingIndex;
    private bool _pendingIsReload;
    private int _failures;
    private TimeSpan _retryAt;

    private readonly TextRun _cardTitle = new(), _cardLine = new();
    private string? _cardShown;

    public MediaApp(MediaLibrary library) => _library = library;

    public override string Id => "media";
    public override string Name => "Media Player";
    public override int FrameRate => 30;

    [Setting("Items", Description = "Pictures, GIFs and videos to play. Leave empty to play everything in the library.", MultiSearch = true, Browse = true)]
    public string Items { get; set; } = "";

    [Setting("Mode", Description = "Fit shows it all with bars, Fill crops to fill, Stretch distorts to fill, Center is pixel for pixel, Scroll pans a wide picture sideways.", Options = ["Fit", "Fill", "Stretch", "Center", "Scroll"])]
    public string Mode { get; set; } = "Fit";

    [Setting("Seconds per item", Description = "How long a picture (or an animation that has no loop count) stays on screen.", Min = 1, Max = 3600)]
    public int Seconds { get; set; } = 10;

    [Setting("Loop count", Description = "Plays a GIF or video this many times before moving on. 0 = until the seconds per item run out.", Min = 0, Max = 100)]
    public int Loops { get; set; }

    [Setting("Speed (%)", Description = "Playback speed in percent: 25 is a quarter speed, 400 is four times.", Min = 25, Max = 400)]
    public int Speed { get; set; } = 100;

    [Setting("Background", Description = "Colour of the bars around a picture, as #RRGGBB.")]
    public string Background { get; set; } = "#000000";

    [Setting("Shuffle", Description = "Play the items in random order.")]
    public bool Shuffle { get; set; }

    [Setting("Brightness trim (%)", Description = "Dims the pictures (10-100) without touching the display brightness.", Min = 10, Max = 100)]
    public int Brightness { get; set; } = 100;

    /// <summary>The id of the item being shown (tests and diagnostics).</summary>
    public string? CurrentId => _clipId;

    /// <summary>The prepare or prefetch in flight, if any (tests wait on it).</summary>
    public Task? PendingLoad => _pending;

    /// <summary>True while the item to show next is being prepared (as opposed to a prefetch waiting for the current item to end).</summary>
    public bool IsPreparing => _pending is not null && _pendingIsReload;

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _height = dimensions.height;
        _width = dimensions.width;
        _clipDirty = true;
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        await base.OnDeactivatedAsync(cancellationToken);
        DiscardPending();
        _clip?.Dispose();
        _clip = null;
        _clipId = null;
        _index = -1;
        _seenVersion = -1;
        _playlistDirty = true;
    }

    protected override void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "items":
                _playlistDirty = true;
                break;
            case "mode":
            case "background":
            case "brightness":
                _clipDirty = true;
                break;
        }
    }

    // ---- per frame -------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _now = context.Time;

        if (_playlistDirty || _library.Version != _seenVersion) RebuildPlaylist();
        if (_playlist.Length == 0) return;
        if (_clipDirty)
        {
            _clipDirty = false;
            ReadLook();
            DiscardPending();
            if (_clip is not null) StartLoad(Math.Max(0, _index), reload: true);
        }

        bool due = false;
        if (_clip is not null)
        {
            double elapsed = (_now - _itemStart).TotalSeconds;
            if (elapsed >= ItemSeconds(_clip))
            {
                if (_playlist.Length <= 1) _itemStart = _now;   // the only item just starts over
                else due = true;
            }
        }

        if (_pending is { IsCompleted: true } finished && (_pendingIsReload || due)) Take(finished);

        if (_pending is null)
        {
            if (_clip is null)
            {
                if (_now >= _retryAt) StartLoad(_index >= 0 && _index < _playlist.Length ? _index : FirstIndex(), reload: true);
            }
            else if (_playlist.Length > 1)
            {
                StartLoad(NextIndex(), reload: false);   // prefetch while the current item plays
            }
        }
    }

    private void Take(Task<MediaClip?> finished)
    {
        _pending = null;
        MediaClip? next = null;
        try { next = finished.IsCompletedSuccessfully ? finished.Result : null; } catch { /* faulted: treated as unplayable */ }

        if (next is null)
        {
            // Unplayable (damaged, deleted meanwhile): skip it; when every item fails, show a card and retry later.
            _index = _pendingIndex;
            if (++_failures >= _playlist.Length) { _failures = 0; _retryAt = _now + TimeSpan.FromSeconds(5); _cardShown = null; }
            else StartLoad(NextIndex(), reload: true);
            return;
        }

        _failures = 0;
        _clip?.Dispose();
        _clip = next;
        _index = _pendingIndex;
        _clipId = _pendingIndex < _playlist.Length ? _playlist[_pendingIndex] : null;
        _itemStart = _now;
    }

    private void StartLoad(int index, bool reload)
    {
        if (_playlist.Length == 0) return;
        index = Math.Clamp(index, 0, _playlist.Length - 1);
        var id = _playlist[index];
        var request = new ClipRequest(_width, _height, _fit, _background, Math.Clamp(Brightness, 10, 100));
        _pendingIndex = index;
        _pendingIsReload = reload;
        _pending = Task.Run(() =>
        {
            try { return (MediaClip?)_library.OpenClip(id, request); }
            catch (Exception) { return null; }
        });
    }

    private void DiscardPending()
    {
        var pending = _pending;
        _pending = null;
        // Whatever it produces is nobody's: dispose it when it lands.
        pending?.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result?.Dispose(); }, TaskScheduler.Default);
    }

    private int FirstIndex() => Shuffle ? _random.Next(_playlist.Length) : 0;

    private int NextIndex()
    {
        int n = _playlist.Length;
        if (n <= 1) return 0;
        if (!Shuffle) return (_index + 1) % n;
        int pick = _random.Next(n - 1);
        return pick >= _index ? pick + 1 : pick;
    }

    private void ReadLook()
    {
        _fit = Enum.TryParse<MediaFit>(Mode, ignoreCase: true, out var fit) ? fit : MediaFit.Fit;
        _background = Pixel.TryParseHex(Background, out var bg) ? bg : Pixel.Black;
    }

    private void RebuildPlaylist()
    {
        _playlistDirty = false;
        _seenVersion = _library.Version;
        ReadLook();

        var items = _library.Items;
        var wanted = SettingsBinder.SplitIds(Items);
        var ids = new List<string>(items.Count);
        if (wanted.Length == 0) foreach (var item in items) ids.Add(item.Id);
        else foreach (var id in wanted) if (_library.Get(id) is { } found) ids.Add(found.Id);
        _playlist = ids.ToArray();
        _cardShown = null;

        DiscardPending();
        if (_playlist.Length == 0)
        {
            _clip?.Dispose();
            _clip = null;
            _clipId = null;
            _index = -1;
            return;
        }

        int at = _clipId is null ? -1 : Array.IndexOf(_playlist, _clipId);
        if (at >= 0) _index = at;                  // still playing something that is in the list: carry on
        else StartLoad(FirstIndex(), reload: true); // the current item left the list (or nothing played yet)
    }

    private double ItemSeconds(MediaClip clip)
    {
        double speed = Math.Clamp(Speed, 25, 400) / 100.0;
        if (clip.Animated && Loops > 0) return Loops * (clip.TotalMs / 1000.0) / speed;
        if (clip.Width > _width) return ScrollCycleSeconds(clip.Width - _width) / speed;
        return Math.Max(1, Seconds);
    }

    private static double ScrollCycleSeconds(int travel) => 2 * ScrollHoldSeconds + travel / ScrollPixelsPerSecond;

    private static int ScrollOffset(double seconds, int travel)
    {
        double t = seconds % ScrollCycleSeconds(travel);
        if (t < ScrollHoldSeconds) return 0;
        double moving = t - ScrollHoldSeconds;
        return Math.Min(travel, (int)(moving * ScrollPixelsPerSecond));
    }

    // ---- drawing ---------------------------------------------------------------------------------------------------

    public override void Render(FrameBuffer frame, CancellationToken cancellationToken)
    {
        if (_playlist.Length == 0)
        {
            DrawCard(frame, "No media yet", "Upload media from the app");
            return;
        }

        if (_clip is not { } clip)
        {
            frame.Fill(new Rectangle(0, 0, frame.Width, frame.Height), _background);
            if (_failures == 0 && _retryAt > TimeSpan.Zero && _now < _retryAt) DrawCard(frame, "Can't play media", "Check the files in the app");
            return;
        }

        double anim = (_now - _itemStart).TotalSeconds * (Math.Clamp(Speed, 25, 400) / 100.0);
        int frameIndex = clip.Animated ? clip.FrameAt((long)(anim * 1000) % Math.Max(1, clip.TotalMs)) : 0;

        int dx, dy = (frame.Height - clip.Height) / 2;
        if (clip.Width > frame.Width) dx = -ScrollOffset(anim, clip.Width - frame.Width);
        else dx = (frame.Width - clip.Width) / 2;

        if (clip.Width < frame.Width || clip.Height < frame.Height)
            frame.Fill(new Rectangle(0, 0, frame.Width, frame.Height), _background);
        clip.Draw(frame, frameIndex, dx, dy);
    }

    private void DrawCard(FrameBuffer frame, string title, string line)
    {
        frame.Fill(new Rectangle(0, 0, frame.Width, frame.Height), CardBackground);
        if (!ReferenceEquals(_cardShown, title))
        {
            _cardTitle.Set(Fonts.Small, title);
            _cardLine.Set(Fonts.QuiteSmall, line);
            _cardShown = title;
        }

        var accent = new Pixel(120, 150, 255);
        var muted = new Pixel(150, 160, 190);
        int cx = frame.Width / 2;
        int top = Math.Max(2, (frame.Height - (16 + 4 + _cardTitle.Height + 3 + _cardLine.Height)) / 2);

        // A little picture icon: frame, sun and hill.
        int ix = cx - 12;
        frame.Fill(new Rectangle(ix, top, 24, 1), accent);
        frame.Fill(new Rectangle(ix, top + 15, 24, 1), accent);
        frame.Fill(new Rectangle(ix, top, 1, 16), accent);
        frame.Fill(new Rectangle(ix + 23, top, 1, 16), accent);
        frame.Fill(new Rectangle(ix + 16, top + 3, 3, 3), new Pixel(255, 210, 80));
        for (int i = 0; i < 8; i++) frame.Fill(new Rectangle(ix + 3 + i, top + 14 - i, Math.Max(1, 14 - 2 * i), 1), accent);

        int y = top + 16 + 4;
        _cardTitle.Draw(frame, cx - _cardTitle.Width / 2, y, Pixel.White);
        _cardLine.Draw(frame, cx - _cardLine.Width / 2, y + _cardTitle.Height + 3, muted);
    }
}

/// <summary>The live picker behind the Items setting: the library, filtered by what is typed (and listed in full when nothing is).</summary>
public sealed class MediaItemOptions(MediaLibrary library) : ISettingOptionsProvider
{
    public bool Browse => true;

    public Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct)
    {
        IReadOnlyList<SettingOption> options = library.Items
            .Where(i => query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(i => new SettingOption(i.Id, i.Name, Describe(i)))
            .ToList();
        return Task.FromResult(options);
    }

    public Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) =>
        Task.FromResult(library.Get(value)?.Name);

    private static string Describe(MediaItem item) => item.Kind switch
    {
        MediaKind.Video => $"Video {item.Width}x{item.Height}",
        MediaKind.Gif => $"GIF {item.Width}x{item.Height}",
        _ => $"Picture {item.Width}x{item.Height}",
    };
}
