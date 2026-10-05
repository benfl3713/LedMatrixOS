using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Scheduling;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps.Attention;

/// <summary>
/// <c>bin_day</c>: true from the Bin Day app's <c>Evening Hour</c> (default 17) the evening before a collection until the end of the
/// collection day. It reads the rules from the persisted Bin Day app settings (<see cref="AppSettingsStorage"/>, keys <c>bins</c> and
/// <c>eveningHour</c>), falling back to configuration <c>BinDay:Bins</c> / <c>BinDay:EveningHour</c> until the app has been configured.
/// The rules are pure local computation. Calendar one-offs are included too: when the Bin Day <c>Calendar Keyword</c> is set (setting <c>calendarKeyword</c>), events in the
/// <c>Calendar:IcsUrl</c> feed whose title contains it count as collections, using the feed fetched at most every 15 minutes in the background (a failed fetch keeps the
/// last good copy). <see cref="SetReferenced"/> only records whether a rule uses it.
/// </summary>
public sealed class BinDayDueSource : ILazyAttentionSource
{
    public const string AppId = "bin-day";
    public const int DefaultEveningHour = 17;

    /// <summary>How long a fetched calendar feed is reused.</summary>
    public static readonly TimeSpan CalendarCacheDuration = TimeSpan.FromMinutes(15);

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Func<(string? Bins, int? EveningHour)> _read;
    private readonly Func<string?> _keyword;
    private readonly Func<CancellationToken, Task<List<CalEvent>>>? _fetch;
    private readonly TimeProvider _time;
    private string? _cachedBins, _cachedKeyword;
    private CollectionSchedule _schedule = new([]);
    private List<Collection> _extras = [];
    private DateTimeOffset? _fetchedAt;
    private int _fetching;

    public BinDayDueSource(AppSettingsStorage storage, IConfiguration configuration, TimeProvider? time = null, HttpClient? http = null)
        : this(() => ReadSettings(storage, configuration), () => ReadKeyword(storage), ct => FetchFeed(http ?? SharedHttp, (configuration["Calendar:IcsUrl"] ?? "").Trim(), time ?? TimeProvider.System, ct),
            time ?? TimeProvider.System) { }

    internal BinDayDueSource(Func<(string? Bins, int? EveningHour)> read, TimeProvider time)
        : this(read, () => null, null, time) { }

    internal BinDayDueSource(Func<(string? Bins, int? EveningHour)> read, Func<string?> keyword, Func<CancellationToken, Task<List<CalEvent>>>? fetch, TimeProvider time)
    {
        _read = read;
        _keyword = keyword;
        _fetch = fetch;
        _time = time;
    }

    public string Kind => "bin_day";

    /// <summary>True while at least one schedule rule uses this condition.</summary>
    public bool IsReferenced { get; private set; }

    public void SetReferenced(IReadOnlyCollection<string?> arguments) => IsReferenced = arguments.Count > 0;

    public bool IsActive(string? argument)
    {
        var (bins, hour) = _read();
        var keyword = (_keyword() ?? "").Trim();
        if (keyword.Length > 0 && _fetch is not null) KickRefresh();
        lock (this)
        {
            if (bins != _cachedBins || keyword != _cachedKeyword)
            {
                _cachedBins = bins;
                _cachedKeyword = keyword;
                _schedule = new CollectionSchedule(BinParser.ParseBins(bins).Rules, keyword.Length > 0 ? _extras : null);
                _scheduleExtras = _extras;
            }
            else if (keyword.Length > 0 && !ReferenceEquals(_scheduleExtras, _extras))
            {
                _schedule = new CollectionSchedule(_schedule.Rules, _extras);
                _scheduleExtras = _extras;
            }
            return _schedule.IsDue(_time.GetLocalNow().DateTime, Math.Clamp(hour ?? DefaultEveningHour, 0, 23));
        }
    }

    private List<Collection>? _scheduleExtras;

    // The feed is fetched in the background so a rule evaluation never waits on the network; the answer uses the last good copy meanwhile.
    private void KickRefresh()
    {
        DateTimeOffset? at;
        lock (this) at = _fetchedAt;
        if (at is { } t && _time.GetUtcNow() - t < CalendarCacheDuration) return;
        if (Interlocked.CompareExchange(ref _fetching, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await RefreshCalendarAsync(CancellationToken.None); }
            catch { /* offline or bad feed: keep the last good copy */ }
            finally { Volatile.Write(ref _fetching, 0); }
        });
    }

    /// <summary>Fetches the calendar feed now and keeps the collections whose title contains the keyword. Public to tests; <see cref="IsActive"/> calls it on a timer.</summary>
    internal async Task RefreshCalendarAsync(CancellationToken ct)
    {
        var keyword = (_keyword() ?? "").Trim();
        if (_fetch is null || keyword.Length == 0) return;
        var events = await _fetch(ct);
        var grey = new Pixel(120, 120, 130);
        var zone = _time.LocalTimeZone;
        var extras = events
            .Where(e => e.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(e => new Collection(e.Title, grey, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Start, zone).DateTime)))
            .ToList();
        lock (this)
        {
            _extras = extras;
            _fetchedAt = _time.GetUtcNow();
        }
    }

    private static async Task<List<CalEvent>> FetchFeed(HttpClient http, string url, TimeProvider time, CancellationToken ct)
    {
        if (url.Length == 0) return [];
        if (url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase)) url = "https" + url[6..];
        var text = await http.GetStringAsync(url, ct);
        var now = time.GetUtcNow();
        return IcsParser.Parse(text, now.AddDays(-1), now.AddDays(60), time.LocalTimeZone);
    }

    private static string? ReadKeyword(AppSettingsStorage storage) =>
        storage.GetAppSettings(AppId) is { } saved && saved.TryGetValue("calendarKeyword", out var k) && k is string s ? s : null;

    private static (string? Bins, int? EveningHour) ReadSettings(AppSettingsStorage storage, IConfiguration configuration)
    {
        var saved = storage.GetAppSettings(AppId);
        string? bins = saved is not null && saved.TryGetValue("bins", out var b) && b is string s && s.Trim().Length > 0 ? s : configuration["BinDay:Bins"];
        int? hour = null;
        if (saved is not null && saved.TryGetValue("eveningHour", out var h))
            hour = h switch { int i => i, double d => (int)d, long l => (int)l, string str when int.TryParse(str, out var p) => p, _ => null };
        hour ??= int.TryParse(configuration["BinDay:EveningHour"], out var c) ? c : null;
        return (bins, hour);
    }
}
