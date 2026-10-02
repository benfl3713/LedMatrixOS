using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Owns a widget tree and drives it: advances the <see cref="Animator"/>, updates nodes, lays the tree out when something
/// changed and renders it. Each frame runs Update, then Layout (only if invalidated), then Render. <see cref="WidgetApp"/> wraps one of these.
/// Not thread-safe: use it from the render thread only.
/// </summary>
public sealed class UiHost
{
    private readonly Stack<FrameBuffer> _backdrops = new();
    private Node? _root;
    private bool _layoutDirty = true;

    public UiHost(int width, int height, TimeProvider? time = null)
    {
        Width = width;
        Height = height;
        Time = time ?? TimeProvider.System;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Animator Animator { get; } = new();

    /// <summary>Source of wall-clock time for widgets such as <see cref="Clock"/>. Inject a fake one in tests.</summary>
    public TimeProvider Time { get; }

    /// <summary>The frame being processed (default before the first <see cref="Update"/>).</summary>
    public FrameContext Frame { get; private set; }

    public Node? Root
    {
        get => _root;
        set
        {
            _root?.AttachHost(null);
            _root = value;
            _root?.AttachHost(this);
            _layoutDirty = true;
        }
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        _layoutDirty = true;
    }

    public void InvalidateLayout() => _layoutDirty = true;

    public void Update(FrameContext ctx)
    {
        Frame = ctx;
        Animator.Update(ctx);
        _root?.Update(ctx);
        LayoutIfDirty();
    }

    public void Render(FrameBuffer frame)
    {
        Resize(frame.Width, frame.Height);
        LayoutIfDirty();
        _root?.Paint(frame, 0, 0);
    }

    private void LayoutIfDirty()
    {
        if (!_layoutDirty || _root is null) return;
        _layoutDirty = false;
        _root.Measure(Width, Height);
        _root.Arrange(new Rectangle(0, 0, Width, Height));
    }

    // Group opacity: the node draws normally, then every pixel it changed is blended back toward the saved backdrop.
    // Backdrops are pooled, so a tree that fades nodes allocates only while its nesting depth is still growing.
    internal FrameBuffer BeginGroup(FrameBuffer frame)
    {
        if (!_backdrops.TryPop(out var backdrop) || backdrop.Width != frame.Width || backdrop.Height != frame.Height)
            backdrop = new FrameBuffer(frame.Width, frame.Height);
        backdrop.CopyFrom(frame);
        return backdrop;
    }

    internal void EndGroup(FrameBuffer frame, FrameBuffer backdrop, Rectangle area, float opacity)
    {
        Blend(frame, backdrop, area, opacity);
        _backdrops.Push(backdrop);
    }

    internal static void Blend(FrameBuffer frame, FrameBuffer backdrop, Rectangle area, float opacity)
    {
        int x0 = Math.Max(area.Left, 0), x1 = Math.Min(area.Right, frame.Width);
        int y0 = Math.Max(area.Top, 0), y1 = Math.Min(area.Bottom, frame.Height);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                var drawn = frame.GetPixel(x, y);
                var before = backdrop.GetPixel(x, y);
                if (drawn != before) frame.SetPixel(x, y, Pixel.Lerp(before, drawn, opacity));
            }
        }
    }
}
