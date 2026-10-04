using System.Collections.Concurrent;

namespace LedMatrixOS.Core.Settings;

/// <summary>One pickable value of a Search/MultiSearch setting.</summary>
public sealed record SettingOption(string Value, string Label, string? Subtitle = null);

/// <summary>
/// Answers the live lookups behind Search/MultiSearch settings. Implementations are stateless: they never see an app instance,
/// so the lookups work for apps that are not active.
/// </summary>
public interface ISettingOptionsProvider
{
    /// <summary>Options matching <paramref name="query"/> (already trimmed, at least two characters).</summary>
    Task<IReadOnlyList<SettingOption>> GetOptionsAsync(string appId, string key, string query, CancellationToken ct);

    /// <summary>The display label of one stored id, or null when it cannot be resolved. May throw; callers handle that.</summary>
    Task<string?> GetLabelAsync(string appId, string key, string value, CancellationToken ct) => Task.FromResult<string?>(null);
}

/// <summary>Maps (app id, setting key) to the provider behind a Search/MultiSearch setting, with small caches for options and labels.</summary>
public sealed class SettingOptionsRegistry
{
    public const int MinQueryLength = 2;
    private const int MaxCacheEntries = 500;
    private static readonly TimeSpan OptionsTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LabelTimeout = TimeSpan.FromSeconds(2.5);

    private readonly ConcurrentDictionary<(string, string), ISettingOptionsProvider> _providers = new();
    private readonly ConcurrentDictionary<(string, string, string), (DateTime At, IReadOnlyList<SettingOption> Options)> _options = new();
    private readonly ConcurrentDictionary<(string, string, string), string> _labels = new();
    private readonly Func<DateTime> _now;

    public SettingOptionsRegistry(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    private static (string, string) Key(string appId, string key) => (appId.ToLowerInvariant(), key.ToLowerInvariant());

    public void Register(string appId, string key, ISettingOptionsProvider provider) => _providers[Key(appId, key)] = provider;

    public bool Has(string appId, string key) => _providers.ContainsKey(Key(appId, key));

    public bool TryGet(string appId, string key, out ISettingOptionsProvider provider) => _providers.TryGetValue(Key(appId, key), out provider!);

    /// <summary>
    /// Options for a query. Queries shorter than <see cref="MinQueryLength"/> give an empty list without calling the provider;
    /// a provider failure also gives an empty list. Only cancellation by the caller propagates.
    /// </summary>
    public async Task<IReadOnlyList<SettingOption>> SearchAsync(string appId, string key, string? query, CancellationToken ct)
    {
        query = (query ?? "").Trim();
        if (query.Length < MinQueryLength || !TryGet(appId, key, out var provider)) return [];

        var cacheKey = (appId.ToLowerInvariant(), key.ToLowerInvariant(), query.ToLowerInvariant());
        if (_options.TryGetValue(cacheKey, out var hit) && _now() - hit.At < OptionsTtl) return hit.Options;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(LookupTimeout);
            var options = await provider.GetOptionsAsync(appId, key, query, timeout.Token).ConfigureAwait(false);
            if (_options.Count >= MaxCacheEntries) _options.Clear();
            _options[cacheKey] = (_now(), options);
            return options;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [];
        }
    }

    /// <summary>The label of one stored id; null when unknown. Cached once resolved; never throws (except for cancellation).</summary>
    public async Task<string?> LabelAsync(string appId, string key, string value, CancellationToken ct)
    {
        value = value.Trim();
        if (value.Length == 0 || !TryGet(appId, key, out var provider)) return null;

        var cacheKey = (appId.ToLowerInvariant(), key.ToLowerInvariant(), value);
        if (_labels.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(LabelTimeout);
            var label = await provider.GetLabelAsync(appId, key, value, timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(label)) return null;
            if (_labels.Count >= MaxCacheEntries) _labels.Clear();
            return _labels[cacheKey] = label;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Fills in <see cref="AppSetting.CurrentLabel"/> / <see cref="AppSetting.CurrentLabels"/> for the Search/MultiSearch settings
    /// that have a provider. An id whose label cannot be resolved shows as the id itself.
    /// </summary>
    public async Task<IReadOnlyList<AppSetting>> WithLabelsAsync(string appId, IReadOnlyList<AppSetting> settings, CancellationToken ct)
    {
        bool NeedsLabel(AppSetting s) => s.Type is AppSettingType.Search or AppSettingType.MultiSearch && Has(appId, s.Key);
        if (!settings.Any(NeedsLabel)) return settings;

        return await Task.WhenAll(settings.Select(async s =>
        {
            if (!NeedsLabel(s)) return s;
            var value = s.CurrentValue?.ToString() ?? "";
            if (s.Type == AppSettingType.Search)
                return s with { CurrentLabel = value.Length == 0 ? "" : await LabelAsync(appId, s.Key, value, ct).ConfigureAwait(false) ?? value };

            var ids = SettingsBinder.SplitIds(value);
            var labels = await Task.WhenAll(ids.Select(async id => await LabelAsync(appId, s.Key, id, ct).ConfigureAwait(false) ?? id)).ConfigureAwait(false);
            return s with { CurrentLabels = labels };
        })).ConfigureAwait(false);
    }
}
