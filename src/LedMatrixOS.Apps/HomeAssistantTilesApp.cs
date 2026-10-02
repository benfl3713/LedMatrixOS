using LedMatrixOS.Apps.HomeAssistant;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// Shows Home Assistant entities on the panel (the reverse of the HA integration): up to four tiles per page, paging through the list.
/// Connection details come from configuration only (<c>HomeAssistant:BaseUrl</c>, <c>HomeAssistant:Token</c> - a long-lived access token),
/// never from a setting, so the token cannot be read back over the API.
/// </summary>
public class HomeAssistantTilesApp : WidgetApp
{
    public override string Id => "ha-tiles";
    public override string Name => "Home Assistant";
    public override int FrameRate => 20;

    [Setting("Entities", Description = "Comma separated entity IDs, optionally labelled: sensor.lounge_temp|Lounge, light.kitchen|Kitchen")]
    public string Entities { get; set; } = "";

    [Setting("Page Seconds", Description = "How long each page of four tiles stays up.", Min = 3, Max = 30)]
    public int PageSeconds { get; set; } = 6;

    private readonly HaApi _api;
    private volatile ILiveData<HaState?[]>? _data;
    private CancellationTokenSource? _pollCts;
    private TileBoard? _board;
    private List<EntityRef> _entities = [];
    private HaState?[]? _lastStates;
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
        if (_entitiesDirty || !ReferenceEquals(states, _lastStates))
        {
            _entitiesDirty = false;
            _lastStates = states;
            _board!.Tiles = BuildTiles(states);
        }
        _board!.PageSeconds = PageSeconds;
        base.Update(context, cancellationToken);
    }

    private TileData[] BuildTiles(HaState?[]? states)
    {
        if (!_api.IsConfigured) return [new TileData("HOME ASSISTANT", "SETUP", "set BaseUrl and Token", TileKind.Text)];
        if (_entities.Count == 0) return [new TileData("NO ENTITIES", "ADD", "choose entities in app", TileKind.Text)];

        var tiles = new TileData[_entities.Count];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = HaFormat.ToTile(_entities[i], states != null && i < states.Length ? states[i] : null);
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
        if (!_api.IsConfigured || _entities.Count == 0) return;

        var entities = _entities;
        var cts = _pollCts = new CancellationTokenSource();
        _data = Poll(TimeSpan.FromSeconds(30), ct => _api.GetStatesAsync(entities, ct), cts.Token);
    }

    internal TileBoard? Board => _board;

    /// <summary>Test seam: fixed entity list and states (call before the first frame).</summary>
    internal void UseData(string entities, ILiveData<HaState?[]>? states, string baseUrl = "http://ha.local", string token = "t")
    {
        _api.BaseUrl = baseUrl;
        _api.Token = token;
        Entities = entities;
        _entities = HaFormat.ParseEntities(entities);
        _data = states;
        _entitiesDirty = true;
    }
}
