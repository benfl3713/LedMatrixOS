using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Apps.Services;
using LedMatrixOS.Apps.Spotify;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core.Scheduling;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps.Attention;

/// <summary>
/// <c>line_disrupted:&lt;lineId&gt;</c>: true while TfL reports the line with minor delays or worse (planned works do not count).
/// Polls every 5 minutes, one request per referenced line so a bad id cannot hide the others.
/// </summary>
public sealed class LineDisruptionSource : PollingAttentionSource
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private readonly TflApi _api;

    public LineDisruptionSource(HttpClient http, IConfiguration configuration, TimeProvider? time = null)
        : this(AttentionHttp.Configure(http), configuration["TFL:AppKey"], time ?? TimeProvider.System) { }

    internal LineDisruptionSource(HttpClient http, string? appKey, TimeProvider time) : base(time, PollInterval) =>
        _api = new TflApi(http) { AppKey = appKey };

    public override string Kind => "line_disrupted";

    protected override async Task<bool> QueryAsync(string argument, CancellationToken ct)
    {
        var statuses = await _api.GetLineStatusesAsync([argument.ToLowerInvariant()], ct).ConfigureAwait(false);
        if (statuses.Length == 0) throw new InvalidOperationException("no status returned");
        return statuses.Any(s => s.Health.NeedsAttention());
    }
}

/// <summary>
/// <c>road_disrupted:&lt;corridorId|any&gt;</c>: true while TfL reports a road disruption of at least <see cref="MinSeverity"/>
/// (configuration <c>Attention:RoadMinSeverity</c>: Minimal, Moderate, Serious or Severe; default Serious) on that corridor
/// (e.g. <c>a406</c>), or anywhere on the network for <c>any</c>. Polls every 5 minutes, only for referenced arguments.
/// </summary>
public sealed class RoadDisruptionSource : PollingAttentionSource
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    public const string Any = "any";
    private readonly TflApi _api;

    public RoadSeverity MinSeverity { get; }

    public RoadDisruptionSource(HttpClient http, IConfiguration configuration, TimeProvider? time = null)
        : this(AttentionHttp.Configure(http), configuration["TFL:AppKey"], ParseMin(configuration["Attention:RoadMinSeverity"]), time ?? TimeProvider.System) { }

    internal RoadDisruptionSource(HttpClient http, string? appKey, RoadSeverity minSeverity, TimeProvider time) : base(time, PollInterval)
    {
        _api = new TflApi(http) { AppKey = appKey };
        MinSeverity = minSeverity;
    }

    public override string Kind => "road_disrupted";

    private static RoadSeverity ParseMin(string? text) =>
        Enum.TryParse<RoadSeverity>(text?.Trim(), ignoreCase: true, out var s) ? s : RoadSeverity.Serious;

    protected override async Task<bool> QueryAsync(string argument, CancellationToken ct)
    {
        var corridors = argument.Equals(Any, StringComparison.OrdinalIgnoreCase) || argument.Length == 0 ? null : new[] { argument.ToLowerInvariant() };
        var disruptions = await _api.GetRoadDisruptionsAsync(corridors, ct).ConfigureAwait(false);
        return disruptions.Any(d => d.Severity >= MinSeverity);
    }
}

/// <summary>
/// <c>bus_due:&lt;stopId&gt;</c>: true while a bus is predicted within <see cref="DueMinutes"/> minutes (configuration
/// <c>Attention:BusDueMinutes</c>, default 3). Polls every 30 seconds, only for referenced stops.
/// </summary>
public sealed class BusDueSource : PollingAttentionSource
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    public const int DefaultDueMinutes = 3;
    private readonly TflApi _api;

    public int DueMinutes { get; }

    public BusDueSource(HttpClient http, IConfiguration configuration, TimeProvider? time = null)
        : this(AttentionHttp.Configure(http), configuration["TFL:AppKey"], int.TryParse(configuration["Attention:BusDueMinutes"], out var m) ? m : DefaultDueMinutes, time ?? TimeProvider.System) { }

    internal BusDueSource(HttpClient http, string? appKey, int dueMinutes, TimeProvider time) : base(time, PollInterval)
    {
        _api = new TflApi(http) { AppKey = appKey };
        DueMinutes = Math.Clamp(dueMinutes, 1, 30);
    }

    public override string Kind => "bus_due";

    protected override async Task<bool> QueryAsync(string argument, CancellationToken ct)
    {
        var arrivals = await _api.GetArrivalsAsync(argument, ct).ConfigureAwait(false);
        return arrivals.Any(a => a.TimeToStation >= 0 && a.TimeToStation <= DueMinutes * 60);
    }
}

/// <summary>
/// <c>ha_state:&lt;entity&gt;=&lt;value&gt;</c>: true while the Home Assistant entity's state equals the value (case-insensitive).
/// Uses <c>HomeAssistant:BaseUrl</c> / <c>HomeAssistant:Token</c> (configuration only); with either missing it never polls.
/// Polls every 15 seconds, only for referenced entities. The URL and token are never logged.
/// </summary>
public sealed class HomeAssistantStateSource : PollingAttentionSource
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly HaApi _api;

    public HomeAssistantStateSource(HttpClient http, IConfiguration configuration, TimeProvider? time = null)
        : this(AttentionHttp.Configure(http), configuration["HomeAssistant:BaseUrl"], configuration["HomeAssistant:Token"], time ?? TimeProvider.System) { }

    internal HomeAssistantStateSource(HttpClient http, string? baseUrl, string? token, TimeProvider time) : base(time, PollInterval) =>
        _api = new HaApi(http) { BaseUrl = baseUrl ?? "", Token = token ?? "" };

    public override string Kind => "ha_state";

    protected override bool CanPoll => _api.IsConfigured;

    protected override async Task<bool> QueryAsync(string argument, CancellationToken ct)
    {
        int eq = argument.IndexOf('=');
        if (eq <= 0) throw new ArgumentException("expected entity=value");
        var entity = argument[..eq].Trim();
        var wanted = argument[(eq + 1)..].Trim();

        var states = await _api.GetStatesAsync([new EntityRef(entity, null)], ct).ConfigureAwait(false);
        var state = states[0] ?? throw new InvalidOperationException("entity unavailable");
        return string.Equals(state.State, wanted, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// <c>spotify_playing</c>: true while the shared Spotify data store says music is playing and was refreshed recently.
/// While a rule references it, this source runs its own Spotify data service (when <c>Spotify:ClientId</c>/<c>ClientSecret</c> are
/// configured) so the answer is live even when the Spotify app is not on screen; the store lets only one service run at a time.
/// </summary>
public sealed class SpotifyPlayingSource : ILazyAttentionSource, IDisposable
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private readonly Func<(bool Loaded, bool Playing, DateTimeOffset? UpdatedAt)> _read;
    private readonly Func<CancellationToken, Task>? _run;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public SpotifyPlayingSource(HttpClient http, IConfiguration configuration, TimeProvider? time = null)
        : this(
            () => (SpotifyDataStore.Loaded, SpotifyDataStore.Value.IsPlaying, SpotifyDataStore.UpdatedAt),
            CreateRunner(http, configuration),
            time ?? TimeProvider.System) { }

    internal SpotifyPlayingSource(Func<(bool Loaded, bool Playing, DateTimeOffset? UpdatedAt)> read, Func<CancellationToken, Task>? run, TimeProvider time)
    {
        _read = read;
        _run = run;
        _time = time;
    }

    public string Kind => "spotify_playing";

    /// <summary>True while this source is running its own data service.</summary>
    public bool IsRunning { get { lock (_gate) return _cts != null; } }

    public bool IsActive(string? argument)
    {
        var (loaded, playing, updated) = _read();
        return loaded && playing && updated is { } at && _time.GetUtcNow() - at <= MaxAge;
    }

    public void SetReferenced(IReadOnlyCollection<string?> arguments)
    {
        lock (_gate)
        {
            bool wanted = arguments.Count > 0 && _run != null;
            if (wanted == (_cts != null)) return;

            if (!wanted)
            {
                _cts!.Cancel();
                _cts = null;
                return;
            }

            var cts = _cts = new CancellationTokenSource();
            var run = _run!;
            _ = Task.Run(async () => { try { await run(cts.Token).ConfigureAwait(false); } catch { } });
        }
    }

    public void Dispose() => SetReferenced(Array.Empty<string?>());

    private static Func<CancellationToken, Task>? CreateRunner(HttpClient http, IConfiguration configuration)
    {
        var id = configuration["Spotify:ClientId"];
        var secret = configuration["Spotify:ClientSecret"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return null;

        var client = AttentionHttp.Configure(http);
        return ct => new SpotifyFeed().RunServiceAsync(new SpotifyDataService(client, id, secret), ct);
    }
}

internal static class AttentionHttp
{
    /// <summary>Gives a freshly injected client a short timeout (a no-op if it has already been used).</summary>
    public static HttpClient Configure(HttpClient client)
    {
        try { client.Timeout = TimeSpan.FromSeconds(15); } catch (InvalidOperationException) { }
        return client;
    }
}
