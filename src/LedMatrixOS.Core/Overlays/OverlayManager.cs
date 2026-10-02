using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// Manages the active overlays: priority queue, lifetime, rendering, and clipping.
/// Overlays are rendered in reverse priority order (lower priority first, so higher priority appears on top).
/// </summary>
public sealed class OverlayManager
{
    private readonly List<IOverlay> _overlays = new();
    private readonly object _gate = new();
    private readonly FrameBuffer _clipBuffer;

    public int Width => _clipBuffer.Width;
    public int Height => _clipBuffer.Height;

    public int Count { get { lock (_gate) return _overlays.Count; } }

    public OverlayManager(int width = 256, int height = 64)
    {
        _clipBuffer = new FrameBuffer(width, height);
    }

    /// <summary>Add an overlay to the manager. It will be rendered next frame.</summary>
    public void Add(IOverlay overlay)
    {
        lock (_gate)
        {
        _overlays.Add(overlay);
        _overlays.Sort((a, b) => a.Priority.CompareTo(b.Priority)); // Maintain priority order
            }
    }

    /// <summary>Update all overlays and remove dismissed ones.</summary>
    public void Update(TimeSpan deltaTime)
    {
        lock (_gate)
        {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            if (_overlays[i] is OverlayBase overlay)
            {
                if (!overlay.Update(deltaTime))
                {
                    _overlays.RemoveAt(i);
                }
            }
        }
            }
    }

    /// <summary>Composite all overlays onto the frame. Called after the main app render.</summary>
    public void RenderOverlays(FrameBuffer frame, FrameContext context)
    {
        lock (_gate)
        {
        // Render in priority order (lowest first, so they layer correctly)
        foreach (var overlay in _overlays)
        {
            if (overlay.Opacity <= 0) continue;

            var bounds = overlay.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;

            // Render overlay to a clipped buffer
            _clipBuffer.Clear(Pixel.Black);
            overlay.Render(_clipBuffer, context);

            // Composite the clipped result onto the main frame
            BlendRegionToFrame(frame, bounds, _clipBuffer, overlay.Opacity);
        }
            }
    }

    /// <summary>Dismiss all overlays with priority <= maxPriority (for input handling, low priority first).</summary>
    public void DismissUpTo(int maxPriority)
    {
        lock (_gate)
        {
        foreach (var overlay in _overlays)
        {
            if (overlay.Priority <= maxPriority && overlay is OverlayBase b)
                b.Dismiss();
        }
            }
    }

    /// <summary>Dismiss a specific overlay by ID.</summary>
    public bool Dismiss(string id)
    {
        lock (_gate)
        {
        var overlay = _overlays.FirstOrDefault(o => o.Id == id);
        if (overlay is OverlayBase b)
        {
            b.Dismiss();
            return true;
        }
        return false;
            }
    }

    /// <summary>Drop every overlay with this id immediately (no fade-out). Returns true if any was removed.</summary>
    public bool Remove(string id)
    {
        lock (_gate) return _overlays.RemoveAll(o => o.Id == id) > 0;
    }

    /// <summary>Clear all overlays immediately.</summary>
    public void Clear()
    {
        lock (_gate)
        {
        _overlays.Clear();
            }
    }

    private void BlendRegionToFrame(FrameBuffer frame, Rectangle bounds, FrameBuffer source, float opacity)
    {
        // Composite the source region onto the frame with the given opacity
        int sourceY = 0;
        for (int y = bounds.Y; y < bounds.Bottom && sourceY < source.Height; y++, sourceY++)
        {
            int sourceX = 0;
            for (int x = bounds.X; x < bounds.Right && sourceX < source.Width; x++, sourceX++)
            {
                var srcPixel = source.GetPixel(sourceX, sourceY);
                frame.BlendPixel(x, y, srcPixel, opacity);
            }
        }
    }
}
