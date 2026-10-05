using System.Globalization;
using LedMatrixOS.Apps.Services;
using LedMatrixOS.Apps.Spotify;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Now-playing display for Spotify: album art on the left, title / artist / next track and a progress bar on the right, tinted with the
/// album's palette over a slow animated backdrop, plus a small equaliser. Data comes from <see cref="SpotifyDataService"/> through
/// <see cref="SpotifyFeed"/>; art is decoded once per track there, and progress is interpolated from the app clock between polls.
/// </summary>
public sealed class SpotifyApp : WidgetApp
{
    public override string Id => "spotify";
    public override string Name => "Spotify";
    public override int FrameRate => 30;

    [Setting("Show Next Track", Description = "Show the next track in the queue under the artist.")]
    public bool ShowNextTrack { get; set; } = true;

    [Setting("Show Visualiser", Description = "Show the small equaliser (reacts to live audio when it is streaming).")]
    public bool ShowVisualiser { get; set; } = true;

    [Setting("Background", Description = "Backdrop behind the text, coloured from the album art.", Options = ["Waves", "Gradient", "Off"])]
    public string Background { get; set; } = "Waves";

    [Setting("Text Size", Description = "Normal shows title, artist and the next track; Large enlarges the title and artist and drops the next line.", Options = ["Normal", "Large"])]
    public string TextSize { get; set; } = "Normal";

    [Setting("Album Art", Description = "Hide the album art to give the text the full width.", Options = ["Show", "Hide"])]
    public string AlbumArt { get; set; } = "Show";

    [Setting("Show Progress", Description = "Show the progress bar along the bottom.")]
    public bool ShowProgress { get; set; } = true;

    [Setting("Scroll Speed", Description = "How fast long titles scroll, in pixels per second.", Min = 10, Max = 100)]
    public int ScrollSpeed { get; set; } = 30;

    [Setting("Dim When Paused", Description = "How much the display dims while playback is paused (0-100 percent).", Min = 0, Max = 100)]
    public int DimWhenPaused { get; set; } = 40;

    private readonly HttpClient _http;
    private readonly AudioDataService? _audio;
    private readonly SpotifyFeed _feed = new();
    private volatile ILiveData<NowPlaying>? _data;
    private bool _authMissing;

    // View
    private BackdropNode? _backdrop;
    private Node? _playing, _scene, _artPanel;
    private StateCard? _card;
    private Clock? _idleClock;
    private Icon? _art;
    private ArtPlaceholder? _placeholder;
    private PauseBadge? _pauseBadge;
    private MarqueeLabel? _title, _artist, _next;
    private Label? _timeLabel;
    private HeartNode? _heart;
    private VisualiserNode? _visualiser;
    private ProgressBar? _bar;
    private Node? _bottomRow;

    // Track state, rebuilt on a track change only
    private string _trackKey = "";
    private string _titleText = "", _artistText = "", _nextText = "";
    private SpotifyColors _colors = SpotifyColors.Default;
    private string _timeText = "";
    private int _shownSecond = -1;
    private int _durationMs;
    private string _appliedSize = "", _appliedArt = "";
    private float _appliedOpacity = -1f;
    private TimeSpan _trackStart;
    private bool _artLoading;

    // Progress interpolation: the position at an anchor time, advanced by the app clock while playing
    private NowPlaying? _lastSnapshot;
    private int _anchorMs;
    private TimeSpan _anchorTime;
    private bool _isPlaying;
    private TimeSpan _now;

    public SpotifyApp(HttpClient httpClient)
    {
        _http = httpClient;
    }

    [ActivatorUtilitiesConstructor]
    public SpotifyApp(HttpClient httpClient, AudioDataService audioService)
    {
        _http = httpClient;
        _audio = audioService;
    }

    /// <summary>The state currently shown (tests and diagnostics).</summary>
    internal SpotifyState State { get; private set; } = SpotifyState.Loading;

    /// <summary>Playback position, interpolated from the app clock.</summary>
    internal int PositionMs => !_isPlaying ? _anchorMs : Math.Min(_durationMs > 0 ? _durationMs : int.MaxValue, _anchorMs + (int)(_now - _anchorTime).TotalMilliseconds);

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        _backdrop = new BackdropNode { StartX = NowPlaying.ArtSize };

        _art = new Icon();
        _placeholder = new ArtPlaceholder();
        _pauseBadge = new PauseBadge { HAlign = Align.End, VAlign = Align.End, Margin = new Thickness(0, 0, 3, 3), Visible = false };
        var artPanel = new Panel { Width = NowPlaying.ArtSize, Height = NowPlaying.ArtSize, Children = { _placeholder, _art, _pauseBadge } };
        _artPanel = artPanel;

        _title = new MarqueeLabel(() => _titleText) { Style = new TextStyle(Fonts.Big, Pixel.White) };
        _artist = new MarqueeLabel(() => _artistText) { Style = Muted(SpotifyColors.SpotifyGreen) };
        _next = new MarqueeLabel(() => _nextText) { Style = new TextStyle(Fonts.QuiteSmall, new Pixel(205, 205, 215), Shadow: false), Margin = new Thickness(0, 1, 0, 0) };

        _timeLabel = new Label(() => _timeText) { Style = new TextStyle(Fonts.QuiteSmall, new Pixel(232, 232, 236), Shadow: false), VAlign = Align.Center };
        _heart = new HeartNode { VAlign = Align.Center, Margin = new Thickness(5, 0, 0, 0) };
        _visualiser = new VisualiserNode { Audio = _audio, VAlign = Align.End };
        _bottomRow = new Stack(Orientation.Horizontal)
        {
            CrossAlign = Align.Center,
            Children = { _timeLabel, _heart, new Panel { Grow = 1 }, _visualiser },
        };

        _bar = new ProgressBar(() => Progress01()) { Thickness = 4, Background = new Pixel(64, 64, 74) };

        var info = new Stack(Orientation.Vertical)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            CrossAlign = Align.Stretch,
            Padding = new Thickness(6, 2, 5, 3),
            Children = { _title, _artist, _next, new Panel { Grow = 1 }, _bottomRow, new Panel { Height = 2 }, _bar },
        };

        _playing = new Dock { HAlign = Align.Stretch, VAlign = Align.Stretch, Left = artPanel, Fill = info };

        _card = new StateCard();
        _idleClock = new Clock("HH:mm", Time)
        {
            Style = new TextStyle(Fonts.Small, new Pixel(130, 130, 140), Shadow: false),
            HAlign = Align.End,
            VAlign = Align.Start,
            Margin = new Thickness(0, 4, 6, 0),
        };

        // The backdrop and the track view share one group so pausing can dim them together.
        _scene = new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _backdrop, _playing } };
        _appliedSize = _appliedArt = "";
        _appliedOpacity = -1f;
        return new Panel { _scene, _card, _idleClock };
    }

    private TextStyle Muted(Pixel accent) => new(Large ? Fonts.Big : Fonts.Small, Pixel.Lerp(accent, Pixel.White, 0.35f), Shadow: false);

    private float Progress01() => _durationMs > 0 ? Math.Clamp(PositionMs / (float)_durationMs, 0f, 1f) : 0f;

    // ---- per frame --------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        _now = context.Time;

        var data = _data;
        var np = data?.Value;
        State = StateFor(data, np);
        if (State == SpotifyState.Playing) Follow(np!);

        bool playing = State == SpotifyState.Playing;
        ApplyLayoutSettings();
        _playing!.Visible = playing;
        _card!.Visible = !playing;
        _card.State = State;
        _idleClock!.Visible = State == SpotifyState.Idle;

        if (playing)
        {
            _pauseBadge!.Visible = !_isPlaying && ShowAlbum;
            _next!.Visible = ShowNextTrack && _nextText.Length > 0 && !Large;
            _bar!.Visible = ShowProgress;
            _placeholder!.Loading = _artLoading && _lastSnapshot!.Art is null && _now - _trackStart < ArtLoadingGrace;
            float opacity = _isPlaying ? 1f : 1f - Math.Clamp(DimWhenPaused, 0, 100) / 100f;
            if (opacity != _appliedOpacity) { _appliedOpacity = opacity; _scene!.Opacity = opacity; }
            _heart!.Visible = _lastSnapshot!.IsSaved;
            _visualiser!.Visible = ShowVisualiser;
            _visualiser.Active = _isPlaying;
            _backdrop!.Colors = _colors;
            UpdateTime();
        }
        _backdrop!.Style = Background;

        base.Update(context, cancellationToken);
    }

    private static readonly TimeSpan ArtLoadingGrace = TimeSpan.FromSeconds(6);

    private bool Large => TextSize == "Large";
    private bool ShowAlbum => AlbumArt != "Hide";

    /// <summary>Applies Text Size, Album Art and Scroll Speed; the heavier parts only when the setting actually changed.</summary>
    private void ApplyLayoutSettings()
    {
        if (_title is null) return;
        int speed = Math.Clamp(ScrollSpeed, 10, 100);
        _title.Speed = speed;
        _artist!.Speed = speed;
        _next!.Speed = speed;

        if (!string.Equals(_appliedSize, TextSize, StringComparison.Ordinal))
        {
            _appliedSize = TextSize;
            _title.Style = new TextStyle(Large ? SpotifyFonts.Large : Fonts.Big, Pixel.White);
            _artist.Style = Muted(_colors.Accent);
        }

        if (!string.Equals(_appliedArt, AlbumArt, StringComparison.Ordinal))
        {
            _appliedArt = AlbumArt;
            _artPanel!.Visible = ShowAlbum;
            _backdrop!.StartX = ShowAlbum ? NowPlaying.ArtSize : 0;
        }
    }

    private SpotifyState StateFor(ILiveData<NowPlaying>? data, NowPlaying? np)
    {
        if (_authMissing) return SpotifyState.NoLogin;
        if (data is null) return SpotifyState.Loading;
        if (data.Error is not null && np is null) return SpotifyState.Offline;
        if (np is null) return SpotifyState.Loading;
        return np.HasTrack ? SpotifyState.Playing : SpotifyState.Idle;
    }

    /// <summary>Applies a new snapshot: a different track resets the view; otherwise progress is only re-anchored if it drifted.</summary>
    private void Follow(NowPlaying np)
    {
        if (!ReferenceEquals(np, _lastSnapshot))
        {
            bool newTrack = np.Key != _trackKey || _lastSnapshot is null;
            _lastSnapshot = np;

            if (newTrack) ApplyTrack(np);
            _durationMs = np.DurationMs;

            bool resync = newTrack || np.IsPlaying != _isPlaying || Math.Abs(np.ProgressMs - PositionMs) > 1500;
            _isPlaying = np.IsPlaying;
            if (resync)
            {
                _anchorMs = np.ProgressMs;
                _anchorTime = _now;
                _shownSecond = -1;
            }

            _nextText = string.IsNullOrEmpty(np.NextTitle) ? "" : "Next: " + np.NextTitle;
        }
    }

    private void ApplyTrack(NowPlaying np)
    {
        _trackKey = np.Key;
        _titleText = np.Title;
        _artistText = np.Artist;
        _colors = SpotifyColors.From(np.Palette);

        _art!.Sprite = np.Art;
        _trackStart = _now;
        _artLoading = np.ArtLoading;
        _placeholder!.Visible = np.Art is null;
        _placeholder.Colors = _colors;
        _artist!.Style = Muted(_colors.Accent);
        _bar!.Fill = _colors.Accent;
        _heart!.Color = _colors.Accent;
        _visualiser!.Low = Pixel.Lerp(_colors.A, _colors.Accent, 0.35f);
        _visualiser.High = _colors.Accent;
    }

    private void UpdateTime()
    {
        int seconds = PositionMs / 1000;
        if (seconds == _shownSecond) return;
        _shownSecond = seconds;
        _timeText = _durationMs > 0 ? $"{Mmss(seconds)} / {Mmss(_durationMs / 1000)}" : Mmss(seconds);
    }

    private static string Mmss(int totalSeconds) => string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60}:{totalSeconds % 60:00}");

    // ---- lifecycle & data -------------------------------------------------------------------------------------------------------

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);

        var clientId = configuration["Spotify:ClientId"];
        var clientSecret = configuration["Spotify:ClientSecret"];
        _authMissing = string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret);
        _lastSnapshot = null;
        _trackKey = "";
        if (_authMissing) return;

        var service = new SpotifyDataService(_http, clientId!, clientSecret!);
        RunInBackground(ct => _feed.RunServiceAsync(service, ct));
        _data = Poll(TimeSpan.FromSeconds(1), ct => _feed.ReadAsync(ct))!;
    }

    /// <summary>Test seam: replaces the data source (call before the first frame).</summary>
    internal void UseData(ILiveData<NowPlaying>? data, bool authMissing = false)
    {
        _data = data;
        _authMissing = authMissing;
        _lastSnapshot = null;
        _trackKey = "";
    }
}
