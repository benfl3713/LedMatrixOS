using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// An overlay is a composited layer rendered on top of the active app.
/// Overlays have priority, duration, position and transition animations.
/// </summary>
public interface IOverlay
{
    /// <summary>Unique identifier for this overlay instance.</summary>
    string Id { get; }

    /// <summary>Priority (0–255). Higher priority overlays render on top and consume dismiss actions first.</summary>
    int Priority { get; }

    /// <summary>Position in the frame. The overlay is clipped to this bounds and composited with alpha blending.</summary>
    Rectangle Bounds { get; }

    /// <summary>Opacity (0–1). Used during transition enter/exit.</summary>
    float Opacity { get; }

    /// <summary>True if this overlay should be dismissed (expires after duration, or user action).</summary>
    bool ShouldDismiss { get; }

    /// <summary>Render this overlay into the frame. Called only if Opacity > 0.</summary>
    /// <remarks>
    /// The frame is already clipped to Bounds before this call.
    /// Implementations should render relative to (0, 0) within the bounds.
    /// </remarks>
    void Render(FrameBuffer frame, FrameContext context);
}
