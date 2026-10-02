using LedMatrixOS.Apps.Calendar;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// The next calendar event from an iCalendar (.ics) feed, big, with the following few beside it. The feed URL is configuration only
/// (<c>Calendar:IcsUrl</c>) because private feed URLs carry a secret; it is polled every 15 minutes and expanded 30 days ahead.
/// </summary>
public class CalendarApp : WidgetApp
{
    public override string Id => "calendar";
    public override string Name => "Calendar";
    public override int FrameRate => 15;

    [Setting("Show All Day Events", Description = "Include all-day events (birthdays, holidays) in the list.")]
    public bool ShowAllDay { get; set; } = true;

    private static readonly IReadOnlyList<CalEvent> None = [];

    private readonly HttpClient _http;
    private string _url = "";
    private volatile ILiveData<List<CalEvent>>? _data;
    private CancellationTokenSource? _pollCts;
    private EventBoard? _board;
    private List<CalEvent>? _lastSource;
    private IReadOnlyList<CalEvent> _filtered = None;
    private bool _lastShowAll = true;

    /// <summary>Zone events are shown in; replaced by tests for deterministic output.</summary>
    internal TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;

    public CalendarApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
        _http = httpClient;
    }

    protected override Node Build() => _board = new EventBoard();

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;
        var data = _data;
        var source = data?.Value;
        if (!ReferenceEquals(source, _lastSource) || ShowAllDay != _lastShowAll)
        {
            _lastSource = source;
            _lastShowAll = ShowAllDay;
            _filtered = source == null ? None : ShowAllDay ? source : source.Where(e => !e.AllDay).ToList();
        }

        var now = TimeZoneInfo.ConvertTime(Time.GetUtcNow(), Zone);
        _board!.Events = _filtered;
        _board.Now = now;
        _board.Message =
            _url.Length == 0 ? BoardMessage.NotConfigured :
            source == null ? (data is { Error: not null } ? BoardMessage.Offline : BoardMessage.Loading) :
            CalendarFormat.FirstUpcoming(_filtered, now) < 0 ? BoardMessage.Empty : BoardMessage.None;

        base.Update(context, cancellationToken);
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _url = (configuration["Calendar:IcsUrl"] ?? "").Trim();
        _pollCts?.Cancel();
        _data = null;
        _lastSource = null;
        if (_url.Length == 0) return;

        var url = _url.StartsWith("webcal", StringComparison.OrdinalIgnoreCase) ? "https" + _url[6..] : _url;
        var cts = _pollCts = new CancellationTokenSource();
        _data = Poll(TimeSpan.FromMinutes(15), ct => FetchAsync(url, ct), cts.Token);
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _pollCts?.Cancel();
        await base.OnDeactivatedAsync(cancellationToken);
    }

    private async Task<List<CalEvent>> FetchAsync(string url, CancellationToken ct)
    {
        var text = await _http.GetStringAsync(url, ct);
        var now = DateTimeOffset.UtcNow;
        return IcsParser.Parse(text, now.AddDays(-1), now.AddDays(30), Zone);
    }

    internal EventBoard? Board => _board;

    /// <summary>Test seam: fixed events, as if the feed had loaded (call before the first frame).</summary>
    internal void UseData(ILiveData<List<CalEvent>>? events, string url = "https://example.com/cal.ics")
    {
        _url = url;
        _data = events;
        _lastSource = null;
    }
}
