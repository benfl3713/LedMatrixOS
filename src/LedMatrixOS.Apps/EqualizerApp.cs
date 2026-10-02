using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// Punchy music visualiser. Bands come from <see cref="AudioDataService"/> (phone microphone stream); with no audio it plays a synthetic
/// demo groove (Auto Generate) or, when that is off, a slow idle "breathing" ripple. Fast-attack/slow-release smoothing, peak-hold caps that
/// fall with gravity, per-style gradients, beat detection driving particles and bloom, and five styles.
/// </summary>
public sealed class EqualizerApp : WidgetApp
{
    private AudioDataService? _audioService;
    private EqualizerVisual? _visual;
    private Label? _hint;

    public EqualizerApp()
    {
    }

    [ActivatorUtilitiesConstructor]
    public EqualizerApp(AudioDataService audioService)
    {
        _audioService = audioService;
    }

    public override string Id => "equalizer";
    public override string Name => "Equalizer Visualizer";

    [Setting("Number of Bars", Description = "How many bars to display", Min = 8, Max = 64)]
    public int BarCount { get; set; } = 32;

    [Setting("Smoothness", Description = "Animation smoothness (1-10)", Min = 1, Max = 10)]
    public int Smoothness { get; set; } = 5;

    [Setting("Color Mode", Description = "Color scheme for the bars", Options = ["Rainbow", "Blue", "Green", "Red", "Cyan", "Heat", "White"])]
    public string ColorMode { get; set; } = "Rainbow";

    [Setting("Auto Generate", Description = "Play a demo groove when no audio is arriving (otherwise an idle ripple)")]
    public bool AutoGenerate { get; set; } = true;

    [Setting("Audio Source", Description = "Source of audio data", Options = ["Auto", "Microphone"])]
    public string AudioSource { get; set; } = "Auto";

    [Setting("Style", Description = "Visualiser style", Options = ["Bars", "Mirrored", "Wave", "Dots", "Radial"])]
    public string Style { get; set; } = "Bars";

    [Setting("Peak Caps", Description = "Hold a falling cap at each bar's recent peak")]
    public bool PeakCaps { get; set; } = true;

    [Setting("Sensitivity", Description = "Gain applied to incoming audio (1-10)", Min = 1, Max = 10)]
    public int Sensitivity { get; set; } = 5;

    [Setting("Glow", Description = "Beat-reactive bloom (turn off on slow hardware)")]
    public bool Glow { get; set; } = true;

    /// <summary>Fixes the random sequence (tests); null picks a random seed per activation.</summary>
    public int? Seed { get; set; }

    public void SetAudioService(AudioDataService audioService) => _audioService = audioService;

    /// <summary>True while the app is showing real audio (tests and diagnostics).</summary>
    public bool IsShowingLiveAudio => _visual?.Source == EqualizerVisual.SourceKind.Live;

    internal AudioDataService? AudioService => _audioService;

    protected override Node Build()
    {
        _visual = new EqualizerVisual(this, Seed ?? Random.Shared.Next());
        _hint = new Label("LISTENING...")
        {
            Style = new TextStyle(Fonts.QuiteSmall, new Pixel(120, 120, 150), Shadow: false),
            HAlign = Align.Center,
            VAlign = Align.Start,
            Margin = new Thickness(0, 2),
            Visible = false,
        };
        return new Panel { Children = { _visual, _hint } };
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        base.Update(context, cancellationToken);
        if (_hint is not null && _visual is not null)
            _hint.Visible = _visual.Source == EqualizerVisual.SourceKind.Idle && AudioSource == "Microphone";
    }
}
