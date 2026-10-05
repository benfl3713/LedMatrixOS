using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;

namespace LedMatrixOS.Apps.Tube;

/// <summary>
/// Line-status helpers the rail apps share, and the live stop/station/dock pickers of the TfL apps: stateless
/// <see cref="ISettingOptionsProvider"/>s (one per kind of place) that the options endpoint calls without an app instance.
/// </summary>
internal static class TflLookups
{
    // ---- line status (rail apps) ------------------------------------------------------------------------------------------------

    /// <summary>All unique line IDs that serve a station (from unfiltered arrivals).</summary>
    public static string[] LineIdsOf(TflArrival[]? arrivals) => (arrivals ?? [])
        .Where(a => !string.IsNullOrWhiteSpace(a.LineId)).Select(a => a.LineId)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id).ToArray();

    /// <summary>Statuses of the lines serving the station; which lines those are is only known once arrivals have loaded.</summary>
    public static async Task<LineStatus[]> FetchLineStatusesAsync(TflApi api, ILiveData<TflArrival[]> arrivals, CancellationToken ct)
    {
        string[] lineIds;
        while ((lineIds = LineIdsOf(arrivals.Value)).Length == 0) await Task.Delay(250, ct);
        return await api.GetLineStatusesAsync(lineIds, ct);
    }
}

/// <summary>Picks TfL rail stations (Tube, DLR, Overground, Elizabeth line, tram).</summary>
internal sealed class TflStationOptions(TflApi api) : ISettingOptionsProvider
{
    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct) =>
        (await api.FindStationsAsync(query, ct)).Select(m => new SettingOption(m.Id, m.Name, string.IsNullOrEmpty(m.Detail) ? null : m.Detail)).ToList();

    public async Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) =>
        await api.GetStationNameAsync(value, ct) is { Length: > 0 } name ? name : null;
}

/// <summary>Picks TfL bus stops.</summary>
internal sealed class TflBusStopOptions(TflApi api) : ISettingOptionsProvider
{
    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct) =>
        (await api.FindBusStopsAsync(query, ct)).Select(m => new SettingOption(m.Id, m.Name)).ToList();

    public async Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) =>
        await api.GetStopLabelAsync(value, ct) is { Length: > 0 } name ? name : null;
}

/// <summary>Picks Santander Cycles docking stations.</summary>
internal sealed class TflDockOptions(TflApi api) : ISettingOptionsProvider
{
    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct) =>
        (await api.FindBikePointsAsync(query, ct)).Select(m => new SettingOption(m.Id, m.Name)).ToList();

    public async Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) =>
        (await api.GetBikePointAsync(value, ct)).Name is { Length: > 0 } name ? name : null;
}
