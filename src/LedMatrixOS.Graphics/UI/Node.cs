using System.Numerics;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Base of everything in a widget tree. A frame runs in this order: <see cref="Update"/> (state, bound data, animations),
/// Measure and Arrange (only when something invalidated the layout), then <see cref="Paint"/>.
/// <para>
/// Layout: a parent measures each child (<see cref="Measure"/>), then hands it a slot (<see cref="Arrange"/>); the child's
/// <see cref="Width"/>, <see cref="Height"/>, margin and alignment decide where inside the slot it ends up. <see cref="Bounds"/>
/// is relative to the parent's origin (padding included).
/// </para>
/// <para>
/// <see cref="Position"/> is an offset from the laid-out spot, so it is free to animate without re-running layout.
/// In a plain <see cref="Panel"/> that spot is the panel's content corner, which makes it an absolute position there.
/// </para>
/// <para>
/// Opacity is applied as a group: the node (and all its children) draw normally, then each pixel they changed is blended
/// with what was underneath. The canvas API has no alpha channel and text is drawn with SetPixel, so a per-pixel BlendPixel
/// cannot reach every widget; an offscreen buffer per node would need every drawing call to take a target and a transparent
/// background. Blending against a pooled snapshot of the backdrop works for any widget, nests (opacities multiply down the
/// tree) and costs one frame copy plus a pass over the node's bounds, only while the node is partly transparent.
/// </para>
/// </summary>
public abstract class Node
{
    private Vector2 _position;
    private Tween<Vector2>? _positionTween;
    private bool _positionAnimating;

    private float _opacity = 1f;
    private Tween<float>? _opacityTween;
    private bool _opacityAnimating;

    private int? _width, _height;
    private Thickness _margin, _padding;
    private float _grow;
    private Align? _hAlign, _vAlign;
    private bool _visible = true;
    private int _gridRow, _gridColumn, _gridRowSpan = 1, _gridColumnSpan = 1;

    public Node? Parent { get; internal set; }

    /// <summary>The host driving this node, or null while it is not part of a hosted tree.</summary>
    public UiHost? Host { get; private set; }

    /// <summary>Layout rectangle relative to the parent's origin, set by <see cref="Arrange"/>.</summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>Where the node was drawn on the last <see cref="Paint"/> (includes <see cref="Position"/>).</summary>
    public Rectangle ScreenBounds { get; private set; }

    /// <summary>Outer size (margin included) computed by <see cref="Measure"/>.</summary>
    public Size DesiredSize { get; private set; }

    /// <summary>Offset added to the laid-out position. Setting it stops a running <see cref="AnimatePosition"/>.</summary>
    public Vector2 Position
    {
        get => _position;
        set
        {
            _positionTween?.Set(value);
            _positionAnimating = false;
            _position = value;
        }
    }

    /// <summary>0 (invisible) to 1. Multiplies with the opacity of every ancestor. Setting it stops a running <see cref="AnimateOpacity"/>.</summary>
    public float Opacity
    {
        get => _opacity;
        set
        {
            value = Math.Clamp(value, 0f, 1f);
            _opacityTween?.Set(value);
            _opacityAnimating = false;
            _opacity = value;
        }
    }

    /// <summary>Fixed width; null sizes to content or to the slot, depending on alignment.</summary>
    public int? Width { get => _width; set => SetLayout(ref _width, value); }
    public int? Height { get => _height; set => SetLayout(ref _height, value); }

    public Thickness Margin { get => _margin; set => SetLayout(ref _margin, value); }
    public Thickness Padding { get => _padding; set => SetLayout(ref _padding, value); }

    /// <summary>
    /// Flex weight in a <see cref="Stack"/>: nodes with a weight above zero split the space the others leave over, in proportion to their weights.
    /// </summary>
    public float Grow { get => _grow; set => SetLayout(ref _grow, value); }

    /// <summary>Overrides the container's default horizontal/vertical alignment for this node.</summary>
    public Align? HAlign { get => _hAlign; set => SetLayout(ref _hAlign, value); }
    public Align? VAlign { get => _vAlign; set => SetLayout(ref _vAlign, value); }

    /// <summary>Invisible nodes take no space, are not updated by their layout and draw nothing.</summary>
    public bool Visible { get => _visible; set => SetLayout(ref _visible, value); }

    /// <summary>Clips the node and its children to its bounds.</summary>
    public bool ClipChildren { get; set; }

    // Attached properties for Grid placement.
    public int GridRow { get => _gridRow; set => SetLayout(ref _gridRow, value); }
    public int GridColumn { get => _gridColumn; set => SetLayout(ref _gridColumn, value); }
    public int GridRowSpan { get => _gridRowSpan; set => SetLayout(ref _gridRowSpan, Math.Max(1, value)); }
    public int GridColumnSpan { get => _gridColumnSpan; set => SetLayout(ref _gridColumnSpan, Math.Max(1, value)); }

    /// <summary>
    /// Tweens <see cref="Position"/> to <paramref name="target"/> through the owning host's animator.
    /// A node that is not hosted yet jumps there at once.
    /// </summary>
    public void AnimatePosition(Vector2 target, TimeSpan duration, Func<float, float>? easing = null, Action? onComplete = null)
    {
        if (Host is null)
        {
            Position = target;
            onComplete?.Invoke();
            return;
        }

        _positionTween ??= new Tween<Vector2>(_position);
        _positionTween.Set(_position);
        Host.Animator.Animate(_positionTween, target, duration, easing, onComplete);
        _positionAnimating = true;
        _position = _positionTween.Value;
    }

    /// <summary>Tweens <see cref="Opacity"/> to <paramref name="target"/> through the owning host's animator.</summary>
    public void AnimateOpacity(float target, TimeSpan duration, Func<float, float>? easing = null, Action? onComplete = null)
    {
        target = Math.Clamp(target, 0f, 1f);
        if (Host is null)
        {
            Opacity = target;
            onComplete?.Invoke();
            return;
        }

        _opacityTween ??= new Tween<float>(_opacity);
        _opacityTween.Set(_opacity);
        Host.Animator.Animate(_opacityTween, target, duration, easing, onComplete);
        _opacityAnimating = true;
        _opacity = _opacityTween.Value;
    }

    /// <summary>Per-frame state update. Overrides call the base first so animated Position/Opacity stay current.</summary>
    public virtual void Update(FrameContext ctx)
    {
        if (_positionAnimating && _positionTween is { } p)
        {
            _position = p.Value;
            _positionAnimating = p.IsRunning;
        }

        if (_opacityAnimating && _opacityTween is { } o)
        {
            _opacity = o.Value;
            _opacityAnimating = o.IsRunning;
        }
    }

    /// <summary>
    /// Computes <see cref="DesiredSize"/> for the given available space (which already includes this node's margin).
    /// </summary>
    public void Measure(int availW, int availH)
    {
        if (!Visible)
        {
            DesiredSize = default;
            return;
        }

        int mh = Margin.Horizontal, mv = Margin.Vertical;
        int aw = Math.Max(0, availW - mh), ah = Math.Max(0, availH - mv);
        Size core = default;
        if (Width is null || Height is null) core = MeasureCore(Width ?? aw, Height ?? ah);
        DesiredSize = new Size((Width ?? core.Width) + mh, (Height ?? core.Height) + mv);
    }

    /// <summary>
    /// Places the node inside <paramref name="slot"/> (parent coordinates, margin included) and lays out its children.
    /// <paramref name="defaultH"/>/<paramref name="defaultV"/> apply unless <see cref="HAlign"/>/<see cref="VAlign"/> are set.
    /// </summary>
    public void Arrange(Rectangle slot, Align defaultH = Align.Stretch, Align defaultV = Align.Stretch)
    {
        var inner = new Rectangle(slot.X + Margin.Left, slot.Y + Margin.Top,
            Math.Max(0, slot.Width - Margin.Horizontal), Math.Max(0, slot.Height - Margin.Vertical));
        var (x, w) = Place(inner.X, inner.Width, Width, DesiredSize.Width - Margin.Horizontal, HAlign ?? defaultH);
        var (y, h) = Place(inner.Y, inner.Height, Height, DesiredSize.Height - Margin.Vertical, VAlign ?? defaultV);
        Bounds = new Rectangle(x, y, w, h);
        ArrangeCore();
    }

    /// <summary>
    /// Draws the node with its parent's screen origin at (<paramref name="originX"/>, <paramref name="originY"/>).
    /// Call it on a root node with 0, 0 to render a tree without a <see cref="UiHost"/>.
    /// </summary>
    public void Paint(FrameBuffer frame, int originX, int originY)
    {
        if (!Visible || _opacity <= 0f) return;

        int x = originX + Bounds.X + (int)MathF.Round(_position.X);
        int y = originY + Bounds.Y + (int)MathF.Round(_position.Y);
        ScreenBounds = new Rectangle(x, y, Bounds.Width, Bounds.Height);

        bool fade = _opacity < 1f;
        var backdrop = fade ? Host?.BeginGroup(frame) ?? CopyOf(frame) : null;
        bool clip = ClipChildren || fade;
        if (clip) frame.PushClip(ScreenBounds);

        OnRender(frame, ScreenBounds);
        PaintChildren(frame);

        if (fade)
        {
            if (Host is { } host) host.EndGroup(frame, backdrop!, ScreenBounds, _opacity);
            else UiHost.Blend(frame, backdrop!, ScreenBounds, _opacity);
        }

        if (clip) frame.PopClip();
    }

    protected virtual Size MeasureCore(int availW, int availH) => default;

    protected virtual void ArrangeCore()
    {
    }

    /// <summary>Draws this node's own content (not its children) into <paramref name="bounds"/>, in screen coordinates.</summary>
    protected virtual void OnRender(FrameBuffer frame, Rectangle bounds)
    {
    }

    protected virtual void PaintChildren(FrameBuffer frame)
    {
    }

    /// <summary>The area inside the padding, relative to this node's origin.</summary>
    protected Rectangle Content => new(Padding.Left, Padding.Top,
        Math.Max(0, Bounds.Width - Padding.Horizontal), Math.Max(0, Bounds.Height - Padding.Vertical));

    /// <summary>The area inside the padding, given this node's screen rectangle.</summary>
    protected Rectangle ContentOf(Rectangle bounds) => new(bounds.X + Padding.Left, bounds.Y + Padding.Top,
        Math.Max(0, bounds.Width - Padding.Horizontal), Math.Max(0, bounds.Height - Padding.Vertical));

    protected void InvalidateLayout() => Host?.InvalidateLayout();

    protected bool SetLayout<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        InvalidateLayout();
        return true;
    }

    internal virtual void AttachHost(UiHost? host)
    {
        Host = host;
        OnHostChanged();
    }

    protected virtual void OnHostChanged()
    {
    }

    private static FrameBuffer CopyOf(FrameBuffer frame)
    {
        var copy = new FrameBuffer(frame.Width, frame.Height);
        copy.CopyFrom(frame);
        return copy;
    }

    private static (int pos, int size) Place(int start, int avail, int? fixedSize, int desired, Align align)
    {
        int size = fixedSize ?? (align == Align.Stretch ? avail : Math.Min(Math.Max(0, desired), avail));
        int pos = align switch
        {
            Align.Center => start + (avail - size) / 2,
            Align.End => start + avail - size,
            _ => start,
        };
        return (pos, size);
    }
}
