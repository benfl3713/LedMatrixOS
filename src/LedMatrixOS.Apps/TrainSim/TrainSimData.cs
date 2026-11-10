using System.Globalization;
using System.Text.Json;

namespace LedMatrixOS.Apps.TrainSim;

/// <summary>What the bridge says about the game (<c>connection.status</c>).</summary>
public enum BridgeStatus { Unknown, NoKey, WaitingForGame, Connected, NoTrain }

public sealed record TsSignal(string Aspect, double DistanceMetres);

public sealed record TsLimit(double LimitMs, double DistanceMetres);

public sealed record TsStop(string Name, string Platform, double DistanceMetres, bool Calling);

/// <summary>
/// The latest telemetry, reduced to what the display needs. Immutable: the source swaps the reference, render code only reads it.
/// SI units (metres, m/s); null means unavailable.
/// </summary>
public sealed record TrainSimSnapshot
{
    /// <summary>The bridge itself is reachable (the socket is open and a snapshot has arrived).</summary>
    public bool Reachable { get; init; }
    public string Address { get; init; } = "";
    public DateTimeOffset? LastMessageAt { get; init; }

    public BridgeStatus Status { get; init; }
    public string GameName { get; init; } = "";

    public string LocoClass { get; init; } = "";
    public int? FormationLength { get; init; }

    public double? SpeedMs { get; init; }
    public double? AccelerationMs2 { get; init; }
    public double? PowerHandle { get; init; }
    public double? TrainBrakeHandle { get; init; }
    public double? SpeedLimitMs { get; init; }
    public double? Gradient { get; init; }

    public IReadOnlyList<TsSignal> Signals { get; init; } = [];
    public IReadOnlyList<TsLimit> Limits { get; init; } = [];
    public IReadOnlyList<TsStop> Stops { get; init; } = [];

    public string ServiceName { get; init; } = "";

    /// <summary>The game clock (<c>world.localTimeIso</c>), already in route-local time.</summary>
    public DateTimeOffset? WorldTime { get; init; }
    public double GmtOffsetHours { get; init; }

    public double? WaitRemainingSeconds { get; init; }
    public string ObjectiveKind { get; init; } = "";

    public static TrainSimSnapshot Offline(string address) => new() { Address = address };
}

/// <summary>A source of telemetry. <see cref="Current"/> is never null and never throws; failures show as <see cref="TrainSimSnapshot.Reachable"/> = false.</summary>
public interface ITrainSimSource
{
    TrainSimSnapshot Current { get; }

    /// <summary>Starts (or restarts, when the address differs) the background work for the bridge at <paramref name="address"/>.</summary>
    void Start(string address);

    void Stop();
}

/// <summary>Fixed data for tests and goldens.</summary>
public sealed class DemoTrainSimSource(TrainSimSnapshot snapshot) : ITrainSimSource
{
    public TrainSimSnapshot Current { get; set; } = snapshot;
    public string? StartedAddress { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }

    public void Start(string address) { StartedAddress = address; Starts++; }
    public void Stop() => Stops++;
}

/// <summary>
/// Holds the bridge's telemetry sections and turns them into snapshots. A <c>snapshot</c> message replaces everything; an <c>update</c>
/// replaces just the top-level sections it contains. Anything else (<c>recorded</c>, junk) is ignored. Nothing here throws on odd input.
/// </summary>
public sealed class TswTelemetryState
{
    private readonly Dictionary<string, JsonElement> _sections = new(StringComparer.Ordinal);

    /// <summary>Applies one message; returns true when it changed the telemetry.</summary>
    public bool Apply(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Apply(doc.RootElement);
        }
        catch (JsonException) { return false; }
    }

    public bool Apply(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
        bool snapshot = type.ValueEquals("snapshot");
        if (!snapshot && !type.ValueEquals("update")) return false;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return false;

        if (snapshot) _sections.Clear();
        foreach (var section in data.EnumerateObject()) _sections[section.Name] = section.Value.Clone();
        return true;
    }

    /// <summary>Parses a complete Telemetry JSON document (the <c>/telemetry</c> body) as a snapshot.</summary>
    public static TrainSimSnapshot ParseTelemetry(string json, string address = "")
    {
        var state = new TswTelemetryState();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var section in doc.RootElement.EnumerateObject()) state._sections[section.Name] = section.Value.Clone();
        }
        catch (JsonException) { }
        return state.Build(address, reachable: true, lastMessage: null);
    }

    public TrainSimSnapshot Build(string address, bool reachable, DateTimeOffset? lastMessage)
    {
        var conn = Section("connection");
        var loco = Section("loco");
        var drive = Section("drive");
        var aid = Section("driverAid");
        var position = Section("position");
        var track = Section("track");
        var world = Section("world");
        var objective = Section("objective");

        return new TrainSimSnapshot
        {
            Reachable = reachable,
            Address = address,
            LastMessageAt = lastMessage,
            Status = ParseStatus(Str(conn, "status")),
            GameName = Str(conn, "gameName"),
            LocoClass = Str(loco, "objectClass"),
            FormationLength = Num(loco, "formationLength") is { } f ? (int)f : null,
            SpeedMs = Num(drive, "speed"),
            AccelerationMs2 = Num(drive, "acceleration"),
            PowerHandle = Num(drive, "powerHandle"),
            TrainBrakeHandle = Num(drive, "trainBrakeHandle"),
            SpeedLimitMs = Num(aid, "speedLimit"),
            Gradient = Num(aid, "gradient"),
            Signals = Items(aid, "signals", 4, e => Str(e, "aspect") is { Length: > 0 } aspect && Num(e, "distance") is { } d ? new TsSignal(aspect, d) : null),
            Limits = Items(aid, "limits", 4, e => Num(e, "limit") is { } limit && Num(e, "distance") is { } d ? new TsLimit(limit, d) : null),
            ServiceName = Str(position, "serviceName"),
            Stops = Items(track, "stops", 4, e => Str(e, "name") is { Length: > 0 } name && Num(e, "distance") is { } d
                ? new TsStop(name, Str(e, "platform"), d, Bool(e, "calling") ?? true) : null),
            WorldTime = ParseTime(Str(world, "localTimeIso")),
            GmtOffsetHours = Num(world, "gmtOffset") ?? 0,
            WaitRemainingSeconds = Num(objective, "waitRemaining"),
            ObjectiveKind = Str(objective, "kind"),
        };
    }

    private JsonElement? Section(string name) =>
        _sections.TryGetValue(name, out var e) && e.ValueKind == JsonValueKind.Object ? e : null;

    private static BridgeStatus ParseStatus(string s) => s switch
    {
        "no-key" => BridgeStatus.NoKey,
        "waiting-for-game" => BridgeStatus.WaitingForGame,
        "connected" => BridgeStatus.Connected,
        "no-train" => BridgeStatus.NoTrain,
        _ => BridgeStatus.Unknown,
    };

    private static string Str(JsonElement? obj, string name) =>
        obj is { } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double? Num(JsonElement? obj, string name)
    {
        if (obj is not { } o || !o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        return v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
    }

    private static bool? Bool(JsonElement? obj, string name) =>
        obj is { } o && o.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;

    private static T[] Items<T>(JsonElement? obj, string name, int max, Func<JsonElement, T?> read) where T : class
    {
        if (obj is not { } o || !o.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<T>(max);
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (read(e) is { } item) list.Add(item);
            if (list.Count == max) break;
        }
        return list.ToArray();
    }

    private static DateTimeOffset? ParseTime(string iso) =>
        iso.Length > 0 && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
