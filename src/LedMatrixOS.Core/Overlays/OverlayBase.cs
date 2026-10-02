using SixLabors.ImageSharp;

namespace LedMatrixOS.Core.Overlays;

/// <summary>
/// Base class for overlays. Manages lifetime, opacity, and transition state.
/// </summary>
public abstract class OverlayBase : IOverlay
{
    private float _opacity = 0f;
    private TimeSpan _elapsedTime = TimeSpan.Zero;

    public string Id { get; }
    public int Priority { get; set; }
    public Rectangle Bounds { get; set; }
    public float Opacity => _opacity;

    /// <summary>
    /// How long the overlay should remain visible. If zero, it must be manually dismissed.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Transition in/out duration. During this time, Opacity animates from 0→1 (in) or 1→0 (out).
    /// </summary>
    public TimeSpan TransitionDuration { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// True when the overlay is exiting and Opacity is animating to 0.
    /// </summary>
    public bool IsExiting { get; private set; }

    public bool ShouldDismiss => IsExiting && _opacity <= 0;

    public OverlayBase(string id, int priority, Rectangle bounds)
    {
        Id = id;
        Priority = priority;
        Bounds = bounds;
    }

    /// <summary>
    /// Update overlay state. Returns true if the overlay is still active.
    /// </summary>
    public virtual bool Update(TimeSpan deltaTime)
    {
        _elapsedTime += deltaTime;

        if (!IsExiting && Duration > TimeSpan.Zero && _elapsedTime >= Duration)
        {
            IsExiting = true;
        }

        // Update opacity during transitions
        if (IsExiting)
        {
            _opacity = Math.Max(0, 1 - (float)(_elapsedTime.TotalMilliseconds - Duration.TotalMilliseconds) / (float)TransitionDuration.TotalMilliseconds);
        }
        else if (_elapsedTime < TransitionDuration)
        {
            _opacity = (float)(_elapsedTime.TotalMilliseconds / TransitionDuration.TotalMilliseconds);
        }
        else
        {
            _opacity = 1f;
        }

        return !ShouldDismiss;
    }

    /// <summary>Request immediate dismissal of this overlay.</summary>
    public void Dismiss()
    {
        IsExiting = true;
    }

    public abstract void Render(FrameBuffer frame, FrameContext context);
}
