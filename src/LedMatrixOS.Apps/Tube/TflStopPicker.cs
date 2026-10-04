using LedMatrixOS.Core.Data;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Tube;

/// <summary>
/// The search-as-you-type stop/station picker shared by the TfL apps: a search setting plus a "select" setting whose options follow the
/// query ("id | name" entries). The owning app supplies the query, the search function (rail or bus), what to do with a picked id and how
/// to run work in the background. Also holds the line-status helpers the rail apps share.
/// </summary>
internal sealed class TflStopPicker
{
    public const string NoSearchHint = "Type at least 2 chars";

    private readonly string _searchKey, _selectKey, _selectLabel, _selectDescription;
    private readonly Func<string> _query;
    private readonly Func<string, CancellationToken, Task<(string[] Options, bool Succeeded)>> _search;
    private readonly Action<string> _onSelected;
    private readonly Action<Func<CancellationToken, Task>> _runInBackground;

    private volatile string[] _options = [NoSearchHint];
    private string _lastQuery = "";

    public TflStopPicker(string searchKey, string selectKey, string selectLabel, string selectDescription, Func<string> query,
        Func<string, CancellationToken, Task<(string[] Options, bool Succeeded)>> search, Action<string> onSelected,
        Action<Func<CancellationToken, Task>> runInBackground)
    {
        _searchKey = searchKey;
        _selectKey = selectKey;
        _selectLabel = selectLabel;
        _selectDescription = selectDescription;
        _query = query;
        _search = search;
        _onSelected = onSelected;
        _runInBackground = runInBackground;
    }

    /// <summary>Yields the app's own settings with the select setting inserted right after the search setting.</summary>
    public IEnumerable<AppSetting> WithSelect(IEnumerable<AppSetting> settings)
    {
        foreach (var setting in settings)
        {
            yield return setting;
            if (setting.Key == _searchKey)
                yield return new AppSetting(_selectKey, _selectLabel, _selectDescription, AppSettingType.Select, "", "", Options: _options);
        }
    }

    /// <summary>Handles a pick from the select setting; returns false when <paramref name="key"/> is not the select setting.</summary>
    public bool TryUpdate(string key, object value)
    {
        if (key != _selectKey) return false;

        var selected = value.ToString() ?? "";
        var split = selected.IndexOf(" | ", StringComparison.Ordinal);
        if (split > 0 && selected[..split].Trim() is { Length: > 0 } id) _onSelected(id);
        return true;
    }

    /// <summary>Call when the search setting changed.</summary>
    public void OnQueryChanged()
    {
        var query = _query().Trim();
        if (query.Length < 2)
        {
            _options = [NoSearchHint];
            return;
        }

        _runInBackground(ct => SearchAsync(query, ct));
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            if (string.Equals(query, _lastQuery, StringComparison.OrdinalIgnoreCase) && _options.Length > 0) return;

            var (options, succeeded) = await _search(query, ct);
            if (succeeded) _lastQuery = query;

            // The user may have kept typing while this was in flight; only the latest query may publish its results
            if (string.Equals(query, _query().Trim(), StringComparison.OrdinalIgnoreCase)) _options = options;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch
        {
            _options = ["Search failed"];
        }
    }

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
