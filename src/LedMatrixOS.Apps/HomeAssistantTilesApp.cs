using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// Shows Home Assistant entities on the panel (the reverse of the HA integration): up to four tiles per page, paged by the shared Pager. Entities can opt into a glyph (icon) or a 24h history sparkline (spark).
/// Connection details come from configuration only (<c>HomeAssistant:BaseUrl</c>, <c>HomeAssistant:Token</c> - a long-lived access token),
/// never from a setting, so the token cannot be read back over the API.
/// </summary>
public class HomeAssistantTilesApp : WidgetApp
{
    public override string Id => "ha-tiles";
    public override string Name => "Home Assistant";
    public override int FrameRate => 20;

    [Setting("Entities", Editor = "ha_entities", Description = "Comma separated entities as id|Label|flags (label and flags optional): sensor.lounge_temp|Lounge|spark, light.kitchen|Kitchen|icon. Flags: icon = pixel glyph for light, switch, lock and door/window/motion sensors; spark = 24h history line for numeric sensors. Unknown flags are ignored.")]
    public string Entities { get; set; } = "";

    [Setting("Page Seconds", Description = "How long each page of four tiles stays up.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    private readonly HaApi _api;
    private volatile ILiveData<HaState?[]>? _data;
    private CancellationTokenSource? _pollCts;
    private TileBoard? _board;
    private List<EntityRef> _entities = [];
    private volatile ILiveData<float[]?[]>? _history;
    private HaState?[]? _lastStates;
    private float[]?[]? _lastHistory;
    private TileData[]? _tiles;
    private bool _active, _entitiesDirty = true;

    public HomeAssistantTilesApp(HttpClient httpClient)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        _api = new HaApi(httpClient);
    }

    protected override Node Build() => _board = new TileBoard { PageSeconds = PageSeconds };

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;
        var states = _data?.Value;
        var history = _history?.Value;
        if (_entitiesDirty || !ReferenceEquals(states, _lastStates) || !ReferenceEquals(history, _lastHistory))
        {
            _entitiesDirty = false;
            _lastStates = states;
            _lastHistory = history;
            // A poll that returns the same values must not rebuild the pages (it would restart their layout), so compare by value.
            var tiles = BuildTiles(states, history);
            if (_tiles is null || !tiles.AsSpan().SequenceEqual(_tiles))
            {
                _tiles = tiles;
                _board!.Tiles = tiles;
            }
        }
        _board!.PageSeconds = PageSeconds;
        base.Update(context, cancellationToken);
    }

    private TileData[] BuildTiles(HaState?[]? states, float[]?[]? history)
    {
        if (!_api.IsConfigured) return [new TileData("HOME ASSISTANT", "SETUP", "set BaseUrl and Token", TileKind.Text)];
        if (_entities.Count == 0) return [new TileData("NO ENTITIES", "ADD", "choose entities in app", TileKind.Text)];

        var tiles = new TileData[_entities.Count];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = HaFormat.ToTile(_entities[i], states != null && i < states.Length ? states[i] : null, history != null && i < history.Length ? history[i] : null);
        return tiles;
    }

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _api.BaseUrl = configuration["HomeAssistant:BaseUrl"] ?? "";
        _api.Token = configuration["HomeAssistant:Token"] ?? "";
        if (string.IsNullOrEmpty(Entities)) Entities = configuration["HomeAssistant:Entities"] ?? "";
        _active = true;
        Restart();
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        _pollCts?.Cancel();
        await base.OnDeactivatedAsync(cancellationToken);
    }

    protected override void OnSettingChanged(string key)
    {
        if (_active && key == "entities") Restart();
    }

    private void Restart()
    {
        _pollCts?.Cancel();
        _entities = HaFormat.ParseEntities(Entities);
        _entitiesDirty = true;
        _data = null;
        _history = null;
        if (!_api.IsConfigured || _entities.Count == 0) return;

        var entities = _entities;
        var cts = _pollCts = new CancellationTokenSource();
        _data = Poll(TimeSpan.FromSeconds(30), ct => _api.GetStatesAsync(entities, ct), cts.Token);
        if (entities.Any(e => (e.Flags & TileFlags.Spark) != 0))
            _history = Poll(TimeSpan.FromMinutes(10), ct => _api.GetHistoryAsync(entities, ct), cts.Token);
    }

    internal TileBoard? Board => _board;

    /// <summary>Test seam: fixed entity list and states (call before the first frame).</summary>
    internal void UseData(string entities, ILiveData<HaState?[]>? states, string baseUrl = "http://ha.local", string token = "t", ILiveData<float[]?[]>? history = null)
    {
        _api.BaseUrl = baseUrl;
        _api.Token = token;
        Entities = entities;
        _entities = HaFormat.ParseEntities(entities);
        _data = states;
        _history = history;
        _entitiesDirty = true;
    }
}
