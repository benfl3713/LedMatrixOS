using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Effects;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Toys;

/// <summary>
/// Hosts the geometric pattern styles. In "auto" it cycles through them on a timer and blends each hand-over with an
/// <see cref="ITransition"/> (rendering the outgoing and incoming pattern into two scratch buffers); picking a style shows it
/// (also through a transition). Pattern time is driven by frame time and the speed setting only, so it is deterministic.
/// </summary>
public sealed class GeometricField : Node
{
    public static readonly string[] PatternNames = ["spirograph", "polygons", "lissajous", "kaleidoscope", "tessellation"];

    private static readonly Func<float, float> XfadeEase = Easing.InOutQuad;

    private readonly GeoPattern[] _patterns =
    [
        new SpirographPattern(), new PolygonsPattern(), new LissajousPattern(), new KaleidoscopePattern(), new TessellationPattern(),
    ];

    private readonly ITransition[] _transitions =
    [
        new CrossfadeTransition { Duration = TimeSpan.FromMilliseconds(1100) },
        new DissolveTransition { Duration = TimeSpan.FromMilliseconds(1100) },
        new IrisTransition { Duration = TimeSpan.FromMilliseconds(1100) },
        new WipeTransition(MoveDirection.Left) { Duration = TimeSpan.FromMilliseconds(1100) },
        new SlideTransition(MoveDirection.Up) { Duration = TimeSpan.FromMilliseconds(1100) },
    ];

    private readonly GlowEffect _glow = new() { Threshold = 130f, Strength = 0.75f, Radius = 2 };
    private readonly Tween<float> _xfade = new(0f);
    private readonly Action _finished;
    private readonly Random _rng = new(4242);

    private FrameBuffer? _a, _b;
    private int _cur, _next = -1, _transitionIndex;
    private float _t, _sinceSwitch;
    private FrameContext _ctx;
    private Pixel[] _pal = ToyPalettes.Get("neon");
    private string _pattern = "auto", _paletteName = "neon";
    private int _speed = 100, _interval = 12;
    private string _lastPattern = "";

    public GeometricField()
    {
        _finished = () =>
        {
            _cur = _next;
            _next = -1;
            _sinceSwitch = 0;
        };
    }

    public string Pattern { get => _pattern; set => _pattern = value; }
    public int Speed { get => _speed; set => _speed = Math.Clamp(value, 10, 400); }
    /// <summary>Seconds between automatic switches.</summary>
    public int Interval { get => _interval; set => _interval = Math.Clamp(value, 3, 120); }

    public string PaletteName
    {
        get => _paletteName;
        set { _paletteName = value; _pal = ToyPalettes.Get(value); }
    }

    /// <summary>Name of the pattern currently shown (the outgoing one during a transition).</summary>
    public string CurrentPattern => PatternNames[_cur];
    public bool IsTransitioning => _next >= 0;
    public string TransitionName => _transitions[_transitionIndex].Name;
    public float AnimationTime => _t;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _ctx = ctx;
        float dt = Math.Clamp((float)ctx.Delta.TotalSeconds, 0f, 0.1f);
        _t += dt * _speed / 100f;
        _sinceSwitch += dt;

        if (_lastPattern == "")
        {
            // First frame: show the requested style straight away.
            int first = Array.IndexOf(PatternNames, _pattern);
            _cur = first < 0 ? 0 : first;
            _lastPattern = _pattern;
        }

        if (_next >= 0) return;

        if (_pattern != _lastPattern)
        {
            _lastPattern = _pattern;
            int target = Array.IndexOf(PatternNames, _pattern);
            if (target >= 0 && target != _cur) StartTransition(target);
            else if (target < 0) _sinceSwitch = _interval; // switched back to auto: move on soon
            return;
        }

        if (_pattern == "auto" && _sinceSwitch >= _interval) StartTransition((_cur + 1) % PatternNames.Length);
    }

    private void StartTransition(int target)
    {
        if (Host is null) { _cur = target; return; }
        _next = target;
        _transitionIndex = _rng.Next(_transitions.Length);
        _xfade.Set(0f);
        Host.Animator.Animate(_xfade, 1f, _transitions[_transitionIndex].Duration, XfadeEase, _finished);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        if (_next < 0)
        {
            _patterns[_cur].Render(frame, _t, _pal);
        }
        else
        {
            _a ??= new FrameBuffer(frame.Width, frame.Height);
            _b ??= new FrameBuffer(frame.Width, frame.Height);
            _patterns[_cur].Render(_a, _t, _pal);
            _patterns[_next].Render(_b, _t, _pal);
            _transitions[_transitionIndex].Render(_a, _b, frame, _xfade.Value);
        }

        _glow.Apply(frame, _ctx);
    }
}
