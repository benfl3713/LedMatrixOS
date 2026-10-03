using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Scheduling;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps.Attention;

/// <summary>
/// <c>bin_day</c>: true from the Bin Day app's <c>Evening Hour</c> (default 17) the evening before a collection until the end of the
/// collection day. It reads the rules from the persisted Bin Day app settings (<see cref="AppSettingsStorage"/>, keys <c>bins</c> and
/// <c>eveningHour</c>), falling back to configuration <c>BinDay:Bins</c> / <c>BinDay:EveningHour</c> until the app has been configured.
/// Pure local computation: nothing is polled, so <see cref="SetReferenced"/> only records whether a rule uses it. Calendar one-offs are not considered.
/// </summary>
public sealed class BinDayDueSource : ILazyAttentionSource
{
    public const string AppId = "bin-day";
    public const int DefaultEveningHour = 17;

    private readonly Func<(string? Bins, int? EveningHour)> _read;
    private readonly TimeProvider _time;
    private string? _cachedBins;
    private CollectionSchedule _schedule = new([]);

    public BinDayDueSource(AppSettingsStorage storage, IConfiguration configuration, TimeProvider? time = null)
        : this(() => ReadSettings(storage, configuration), time ?? TimeProvider.System) { }

    internal BinDayDueSource(Func<(string? Bins, int? EveningHour)> read, TimeProvider time)
    {
        _read = read;
        _time = time;
    }

    public string Kind => "bin_day";

    /// <summary>True while at least one schedule rule uses this condition.</summary>
    public bool IsReferenced { get; private set; }

    public void SetReferenced(IReadOnlyCollection<string?> arguments) => IsReferenced = arguments.Count > 0;

    public bool IsActive(string? argument)
    {
        var (bins, hour) = _read();
        lock (this)
        {
            if (bins != _cachedBins)
            {
                _cachedBins = bins;
                _schedule = new CollectionSchedule(BinParser.ParseBins(bins).Rules);
            }
            return _schedule.IsDue(_time.GetLocalNow().DateTime, Math.Clamp(hour ?? DefaultEveningHour, 0, 23));
        }
    }

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
