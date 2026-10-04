using LedMatrixOS.Core.Settings;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps.HomeAssistant;

/// <summary>
/// Entity picker of the Home Assistant Tiles app. Browse-capable: with no query it lists the first entities, a query narrows by id or
/// friendly name. Connection details come from configuration only and never leave the server; when they are missing, or Home Assistant
/// cannot be reached, the list holds one explanatory row with an empty value (clients show it but do not offer it as a pick).
/// </summary>
internal sealed class HaEntityOptions(HaApi api, IConfiguration configuration) : ISettingOptionsProvider
{
    public const int MaxResults = 50;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private IReadOnlyList<HaState>? _cached;
    private DateTime _cachedAt;

    /// <summary>Clock for the cache (overridable for tests).</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public bool Browse => true;

    public async Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct)
    {
        var baseUrl = configuration["HomeAssistant:BaseUrl"] ?? "";
        var token = configuration["HomeAssistant:Token"] ?? "";
        if (baseUrl.Length == 0 || token.Length == 0)
            return [new SettingOption("", "Home Assistant is not configured", "Set HomeAssistant:BaseUrl and HomeAssistant:Token on the device")];

        IReadOnlyList<HaState> states;
        try { states = await GetStatesAsync(baseUrl, token, ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [new SettingOption("", "Could not reach Home Assistant", "Check HomeAssistant:BaseUrl and the token")];
        }

        var q = query.Trim();
        return states
            .Where(s => q.Length == 0
                || s.EntityId.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (s.FriendlyName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(s => new SettingOption(s.EntityId, string.IsNullOrWhiteSpace(s.FriendlyName) ? s.EntityId : s.FriendlyName, s.EntityId))
            .ToList();
    }

    private async Task<IReadOnlyList<HaState>> GetStatesAsync(string baseUrl, string token, CancellationToken ct)
    {
        lock (_gate)
            if (_cached is not null && Now() - _cachedAt < CacheTtl) return _cached;

        api.BaseUrl = baseUrl;
        api.Token = token;
        var states = await api.GetAllStatesAsync(ct);
        lock (_gate)
        {
            _cached = states;
            _cachedAt = Now();
        }
        return states;
    }
}
