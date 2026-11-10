using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.TrainSim;

/// <summary>Which face the app shows.</summary>
internal enum TrainSimCard { Live, Offline, WaitingForGame, NoTrain, NoKey, NoData }

/// <summary>
/// The snapshot turned into display values: every string is formatted when the snapshot (or a setting) changes, never per frame, so the
/// render path only reads cached fields.
/// </summary>
internal sealed class TrainSimBoardModel
{
    public static readonly Pixel Ink = new(235, 235, 240), Green = new(70, 225, 105), Amber = new(255, 176, 0), Red = new(240, 60, 55), Dim = new(120, 130, 150);

    private static readonly string[] Numbers = Enumerable.Range(0, 400).Select(i => i.ToString()).ToArray();

    private TrainSimSnapshot? _snapshot;
    private bool _mph = true, _showStop = true, _first = true;

    public TrainSimCard Card { get; private set; } = TrainSimCard.Offline;
    public string Title { get; private set; } = "";
    public string Hint { get; private set; } = "";
    public string Hint2 { get; private set; } = "";
    public Pixel Accent { get; private set; } = Dim;

    // speed
    public string SpeedText { get; private set; } = "--";
    public Pixel SpeedColor { get; private set; } = Ink;
    public double SpeedMs { get; private set; }
    public double? LimitMs { get; private set; }
    public string LimitText { get; private set; } = "";
    public string UnitText { get; private set; } = "mph";

    // power and brake, 0..1
    public float Power { get; private set; }
    public float Brake { get; private set; }

    // next signal
    public bool HasSignal { get; private set; }
    public SpeedAspect Aspect { get; private set; }
    public string SignalDistance { get; private set; } = "";
    public string SignalWord { get; private set; } = "";

    // next limit change
    public bool HasNextLimit { get; private set; }
    public int NextLimitDirection { get; private set; }
    public Pixel NextLimitColor { get; private set; } = Ink;
    public string NextLimitText { get; private set; } = "";

    // next stop
    public bool HasStop { get; private set; }
    public string StopName { get; private set; } = "";
    public string StopPlatform { get; private set; } = "";
    public string StopRight { get; private set; } = "";
    public bool StopDwell { get; private set; }

    // footer
    public string LocoText { get; private set; } = "";
    public string GradeText { get; private set; } = "";
    public string ClockText { get; private set; } = "";

    public void Reset() { _snapshot = null; _first = true; }

    public void Refresh(TrainSimSnapshot snapshot, bool mph, bool showStop)
    {
        if (!_first && ReferenceEquals(snapshot, _snapshot) && mph == _mph && showStop == _showStop) return;
        _first = false;
        _snapshot = snapshot;
        _mph = mph;
        _showStop = showStop;

        ApplyCard(snapshot);
        if (Card != TrainSimCard.Live) return;

        UnitText = mph ? "mph" : "km/h";

        // speed against the limit
        LimitMs = snapshot.SpeedLimitMs;
        LimitText = LimitMs is { } l ? Num(TrainSimFormat.Speed(l, mph)) : "";
        if (snapshot.SpeedMs is { } speed)
        {
            SpeedMs = Math.Max(0, speed);
            SpeedText = Num(TrainSimFormat.Speed(speed, mph));
            SpeedColor = TrainSimFormat.Classify(speed, LimitMs) switch { SpeedState.Normal => Ink, SpeedState.Amber => Amber, _ => Red };
        }
        else
        {
            SpeedMs = 0;
            SpeedText = "--";
            SpeedColor = Dim;
        }

        // power and brake: a combined handle goes negative when braking
        float power = 0, brake = 0;
        if (snapshot.PowerHandle is { } p) { if (p > 0) power = Math.Min(1f, (float)p / 100f); else if (p < 0) brake = Math.Min(1f, (float)-p / 100f); }
        if (snapshot.TrainBrakeHandle is { } tb and > 0) brake = Math.Max(brake, Math.Min(1f, tb <= 1 ? (float)tb : (float)tb / 100f));
        Power = power;
        Brake = brake;

        // signal
        if (snapshot.Signals.Count > 0)
        {
            var s = snapshot.Signals[0];
            HasSignal = true;
            Aspect = TrainSimFormat.Aspect(s.Aspect);
            SignalDistance = TrainSimFormat.Distance(s.DistanceMetres, mph);
            SignalWord = TrainSimFormat.AspectWord(Aspect);
        }
        else
        {
            HasSignal = false;
            Aspect = SpeedAspect.Unknown;
            SignalDistance = "--";
            SignalWord = "NO SIGNAL";
        }

        // next limit change
        HasNextLimit = false;
        foreach (var lim in snapshot.Limits)
        {
            if (lim.DistanceMetres < 0) continue;
            HasNextLimit = true;
            int shown = TrainSimFormat.Speed(lim.LimitMs, mph);
            NextLimitDirection = LimitMs is { } now ? Math.Sign(TrainSimFormat.Speed(lim.LimitMs, mph) - TrainSimFormat.Speed(now, mph)) : 0;
            NextLimitColor = NextLimitDirection < 0 ? Amber : NextLimitDirection > 0 ? Green : Ink;
            NextLimitText = Num(shown) + " in " + TrainSimFormat.Distance(lim.DistanceMetres, mph);
            break;
        }

        // next stop
        TsStop? stop = null;
        foreach (var st in snapshot.Stops) if (st.Calling) { stop = st; break; }
        stop ??= snapshot.Stops.Count > 0 ? snapshot.Stops[0] : null;
        double? wait = snapshot.WaitRemainingSeconds is > 0 ? snapshot.WaitRemainingSeconds : null;
        HasStop = showStop && (stop is not null || wait is not null);
        StopDwell = wait is not null;
        StopName = stop?.Name ?? "At platform";
        StopPlatform = stop is { Platform.Length: > 0 } ? "P" + stop.Platform : "";
        StopRight = wait is { } w ? "Wait " + TrainSimFormat.Countdown(w) : stop is not null ? TrainSimFormat.Distance(stop.DistanceMetres, mph) : "";

        // footer
        var loco = TrainSimFormat.LocoName(snapshot.LocoClass);
        LocoText = snapshot.ServiceName.Length > 0 ? (loco.Length > 0 ? loco + "  " + snapshot.ServiceName : snapshot.ServiceName) : loco;
        GradeText = snapshot.Gradient is { } g && Math.Abs(g) >= 0.5 ? (g > 0 ? "Up " : "Down ") + Math.Abs(g).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%" : "";
        ClockText = TrainSimFormat.Clock(snapshot.WorldTime);
    }

    private void ApplyCard(TrainSimSnapshot s)
    {
        if (!s.Reachable)
        {
            Card = TrainSimCard.Offline;
            Title = "BRIDGE OFFLINE";
            Hint = "Trying " + (s.Address.Length > 0 ? s.Address : "the bridge");
            Hint2 = "Run the tsw-companion bridge on the gaming PC";
            Accent = Red;
            return;
        }

        switch (s.Status)
        {
            case BridgeStatus.Connected:
                Card = TrainSimCard.Live;
                return;
            case BridgeStatus.WaitingForGame:
                Card = TrainSimCard.WaitingForGame;
                Title = "WAITING FOR GAME";
                Hint = "Bridge connected to " + s.Address;
                Hint2 = "Start Train Sim World and load a route";
                Accent = Amber;
                return;
            case BridgeStatus.NoTrain:
                Card = TrainSimCard.NoTrain;
                Title = "NO TRAIN";
                Hint = s.GameName.Length > 0 ? s.GameName : "Game is running";
                Hint2 = "Take a driver's seat to see live data";
                Accent = Amber;
                return;
            case BridgeStatus.NoKey:
                Card = TrainSimCard.NoKey;
                Title = "NO API KEY";
                Hint = "The bridge cannot read the game";
                Hint2 = "Add the Train Sim World API key to the bridge";
                Accent = Amber;
                return;
            default:
                Card = TrainSimCard.NoData;
                Title = "WAITING FOR DATA";
                Hint = "Bridge connected to " + s.Address;
                Hint2 = "No telemetry yet";
                Accent = Dim;
                return;
        }
    }

    private static string Num(int n) => (uint)n < Numbers.Length ? Numbers[n] : n.ToString();
}
