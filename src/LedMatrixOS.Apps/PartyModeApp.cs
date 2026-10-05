using LedMatrixOS.Apps.Party;
using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Party mode: cycles between equalizer bars, fire and particle bursts, all reacting to the same <see cref="BeatSignal"/>. The audio comes
/// from <see cref="AudioDataService"/> (the phone microphone stream); with no audio arriving a synthetic beat at the configured tempo keeps the
/// show going. Fire flares on every beat and the bursts go off on them; <see cref="Beat"/> is public so other code can reuse the pulse.
/// </summary>
public sealed class PartyModeApp : WidgetApp
{
    private static readonly string[] Cycle = ["Bars", "Fire", "Bursts"];

    private readonly AudioDataService? _audio;
    private readonly float[] _bands = new float[AudioDataService.FrequencyBandCount];
    private readonly EqualizerApp _equalizerApp;
    private readonly FireApp _fireApp;
    private EqualizerVisual _bars = null!;
    private FireVisual _fire = null!;
    private BurstVisual _bursts = null!;
    private TimeSpan _modeSince;
    private bool _modeStarted;
    private int _index;

    public PartyModeApp() : this(null) { }

    [ActivatorUtilitiesConstructor]
    public PartyModeApp(AudioDataService? audio)
    {
        _audio = audio;
        _equalizerApp = new EqualizerApp(audio ?? new AudioDataService()) { AudioSource = "Microphone", AutoGenerate = true, Glow = true };
        _fireApp = new FireApp { Embers = true, Glow = true };
    }

    public override string Id => "party";
    public override string Name => "Party Mode";

    [Setting("Mode", Description = "Which visual to show; Auto cycles through all of them.", Options = ["Auto", "Bars", "Fire", "Bursts"])]
    public string Mode { get; set; } = "Auto";

    [Setting("Seconds Per Mode", Description = "How long Auto stays on each visual.", Min = 3, Max = 120)]
    public int SecondsPerMode { get; set; } = 12;

    [Setting("Fallback Tempo", Description = "Beats per minute of the synthetic beat used when no audio is streaming.", Min = 60, Max = 200)]
    public int Tempo { get; set; } = 124;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    /// <summary>The beat signal driving the show. Reusable: read <see cref="BeatSignal.Beat"/>, <see cref="BeatSignal.Pulse"/> or <see cref="BeatSignal.Level"/>.</summary>
    public BeatSignal Beat { get; } = new();

    /// <summary>The visual on screen right now: Bars, Fire or Bursts.</summary>
    public string CurrentMode => Mode == "Auto" ? Cycle[_index] : Mode;

    internal int BurstParticles => _bursts?.ParticleCount ?? 0;

    protected override Node Build()
    {
        int seed = Seed ?? Random.Shared.Next();
        _equalizerApp.Seed = seed;
        _bars = new EqualizerVisual(_equalizerApp, seed);
        _fire = new FireVisual(_fireApp, seed + 1);
        _bursts = new BurstVisual(Beat, seed + 2);
        _modeStarted = false;
        _index = 0;
        return new Panel { Children = { _bars, _fire, _bursts } };
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        bool live = false;
        if (_audio is not null && _audio.HasRecentData())
        {
            live = _audio.CopyFrequencyBands(_bands) > 0;
        }
        Beat.Bpm = Tempo;
        Beat.Update((float)context.Delta.TotalSeconds, _bands, live);

        if (!_modeStarted)
        {
            _modeStarted = true;
            _modeSince = context.Time;
        }
        else if (Mode == "Auto" && context.Time - _modeSince >= TimeSpan.FromSeconds(Math.Max(3, SecondsPerMode)))
        {
            _modeSince = context.Time;
            _index = (_index + 1) % Cycle.Length;
        }

        string mode = CurrentMode;
        _bars.Visible = mode == "Bars";
        _fire.Visible = mode == "Fire";
        _bursts.Visible = mode == "Bursts";

        // Fire flares with the beat: 4 resting, up to 10 at the peak of a pulse.
        _fireApp.Intensity = 4 + (int)MathF.Round(Beat.Pulse * 6f);
        _equalizerApp.Sensitivity = 5;

        base.Update(context, cancellationToken);
    }
}
