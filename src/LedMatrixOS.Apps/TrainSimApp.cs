using LedMatrixOS.Apps.TrainSim;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LedMatrixOS.Apps;

/// <summary>
/// A live cab display for Train Sim World, fed by the tsw-companion bridge running on the gaming PC: current speed against the limit,
/// the next signal and limit change, the next calling stop (or its dwell countdown), power/brake, the loco and the game clock.
/// </summary>
/// <remarks>
/// The bridge address is the Bridge Address setting when set, otherwise <c>TrainSim:BridgeAddress</c> from configuration, otherwise
/// <c>localhost:31280</c>. The socket is opened when the app activates and closed when it deactivates.
/// </remarks>
public sealed class TrainSimApp : WidgetApp
{
    public override string Id => "train-sim";
    public override string Name => "Train Sim Live";
    public override int FrameRate => 30;

    public const string DefaultAddress = "localhost:31280";

    [Setting("Bridge Address", Description = "Where the tsw-companion bridge runs, as host:port (for example 192.168.1.20:31280). Empty uses TrainSim:BridgeAddress from the configuration, else localhost:31280.")]
    public string BridgeAddress { get; set; } = "";

    [Setting("Units", Description = "Speed and distance units: miles per hour (yards and miles) or kilometres per hour (metres and kilometres).", Options = ["mph", "km/h"])]
    public string Units { get; set; } = "mph";

    [Setting("Show Next Stop", Description = "Show the next calling stop (or its dwell countdown) under the signal and limit.")]
    public bool ShowNextStop { get; set; } = true;

    private readonly ITrainSimSource _source;
    private readonly TrainSimBoardModel _model = new();
    private string _configAddress = "";
    private bool _active;

    private Node? _live, _card, _stopRow, _arrow, _limitText;
    private Label? _aspectWord;
    private SpeedAspect _appliedAspect = (SpeedAspect)(-1);
    private TrainSimStyles? _styles;

    [ActivatorUtilitiesConstructor]
    public TrainSimApp() : this(new TswBridgeSource()) { }

    public TrainSimApp(ITrainSimSource source) => _source = source;

    // ---- view -------------------------------------------------------------------------------------------------------------------

    protected override Node Build()
    {
        var styles = _styles = new TrainSimStyles();
        var m = _model;

        var left = new Panel
        {
            Width = 96,
            HAlign = Align.Start,
            VAlign = Align.Stretch,
            Children =
            {
                new SpeedHeroNode(m).At(2, 2, 56, 36),
                new LimitSignNode(m).At(64, 2, 28, 28),
                new Label(() => m.UnitText) { Style = styles.Muted, HAlign = Align.Start, VAlign = Align.Start, Margin = new Thickness(66, 33, 0, 0) },
                new SpeedBarNode(m).At(4, 42, 88, 4),
                new Label("B") { Style = styles.Muted, HAlign = Align.Start, VAlign = Align.Start, Margin = new Thickness(4, 47, 0, 0) },
                new PowerBrakeNode(m).At(12, 48, 70, 5),
                new Label("P") { Style = styles.Muted, HAlign = Align.Start, VAlign = Align.Start, Margin = new Thickness(85, 47, 0, 0) },
            },
        };

        _aspectWord = new Label(() => m.SignalWord) { Style = styles.AspectWord(SpeedAspect.Unknown), HAlign = Align.End, VAlign = Align.Center };
        var signalRow = new Stack(Orientation.Horizontal, gap: 4)
        {
            Height = 22,
            HAlign = Align.Stretch,
            CrossAlign = Align.Center,
            Children =
            {
                new SignalLampNode(m) { Width = 24, Height = 22 },
                new Label(() => m.SignalDistance) { Style = styles.Distance },
                new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _aspectWord } },
            },
        };

        _arrow = new LimitArrowNode(m) { Width = 7, Height = 9 };
        _limitText = new Label(() => m.NextLimitText) { Style = styles.Detail };
        var limitRow = new Stack(Orientation.Horizontal, gap: 4)
        {
            Height = 12,
            HAlign = Align.Stretch,
            CrossAlign = Align.Center,
            Children = { new Panel { Width = 8, Height = 12, Children = { _arrow } }, _limitText },
        };

        _stopRow = new Stack(Orientation.Horizontal, gap: 4)
        {
            Height = 12,
            HAlign = Align.Stretch,
            CrossAlign = Align.Center,
            Children =
            {
                new MarqueeLabel(() => m.StopName) { Style = styles.Detail, Grow = 1 },
                new Label(() => m.StopPlatform) { Style = styles.Platform },
                new Label(() => m.StopRight) { Style = styles.StopDistance },
            },
        };

        var right = new Stack(Orientation.Vertical, gap: 2)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Grow = 1,
            Margin = new Thickness(5, 2, 4, 0),
            Children = { signalRow, limitRow, _stopRow },
        };

        var content = new Stack(Orientation.Horizontal)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children = { left, new Block(new Pixel(30, 36, 56), width: 1) { Margin = new Thickness(0, 3) }, right },
        };

        var footer = new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.End,
            Height = 10,
            Children =
            {
                new Block(new Pixel(14, 14, 20)),
                new Stack(Orientation.Horizontal, gap: 6)
                {
                    HAlign = Align.Stretch,
                    VAlign = Align.Stretch,
                    CrossAlign = Align.Center,
                    Padding = new Thickness(3, 0),
                    Children =
                    {
                        new MarqueeLabel(() => m.LocoText) { Style = styles.Footer, Grow = 1 },
                        new Label(() => m.GradeText) { Style = styles.Muted },
                        new Label(() => m.ClockText) { Style = styles.Clock },
                    },
                },
            },
        };

        _live = new Stack(Orientation.Vertical)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children = { new Panel { Grow = 1, HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { content } }, footer },
        };

        _card = new Stack(Orientation.Vertical, gap: 4)
        {
            HAlign = Align.Center,
            VAlign = Align.Center,
            CrossAlign = Align.Center,
            Visible = false,
            Children =
            {
                new Label(() => m.Title) { Style = styles.CardTitle, HAlign = Align.Center },
                new AccentBarNode(m) { Width = 64, Height = 2, HAlign = Align.Center },
                new Label(() => m.Hint) { Style = styles.CardHint, HAlign = Align.Center },
                new Label(() => m.Hint2) { Style = styles.CardHint2, HAlign = Align.Center },
            },
        };

        return new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Children = { _live, _card } };
    }

    // ---- per frame ----------------------------------------------------------------------------------------------------------------

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame

        bool mph = !string.Equals(Units, "km/h", StringComparison.OrdinalIgnoreCase);
        _model.Refresh(_source.Current, mph, ShowNextStop);

        bool live = _model.Card == TrainSimCard.Live;
        _live!.Visible = live;
        _card!.Visible = !live;
        _stopRow!.Visible = _model.HasStop;
        _arrow!.Visible = _limitText!.Visible = _model.HasNextLimit;

        var aspect = _model.HasSignal ? _model.Aspect : SpeedAspect.Unknown;
        if (aspect != _appliedAspect)
        {
            _appliedAspect = aspect;
            _aspectWord!.Style = _styles!.AspectWord(aspect);
        }

        base.Update(context, cancellationToken);
    }

    // ---- settings and lifecycle -----------------------------------------------------------------------------------------------

    internal string EffectiveAddress => TswBridgeSource.NormaliseAddress(
        !string.IsNullOrWhiteSpace(BridgeAddress) ? BridgeAddress
        : !string.IsNullOrWhiteSpace(_configAddress) ? _configAddress
        : DefaultAddress);

    protected override void OnSettingChanged(string key)
    {
        if (key == "bridgeAddress" && _active) _source.Start(EffectiveAddress);
    }

    public override Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _model.Reset();
        _configAddress = configuration["TrainSim:BridgeAddress"] ?? "";
        _active = true;
        _source.Start(EffectiveAddress);
        return base.OnActivatedAsync(dimensions, configuration, cancellationToken);
    }

    public override async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _active = false;
        _source.Stop();
        await base.OnDeactivatedAsync(cancellationToken);
    }

    internal TrainSimBoardModel Model => _model;
}
