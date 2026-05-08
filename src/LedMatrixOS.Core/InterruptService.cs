using System.Collections.Concurrent;

namespace LedMatrixOS.Core;

public class InterruptService
{
    // ── Legacy FrameBuffer-based queue (used by the old RenderEngine) ─────────
    private ConcurrentQueue<InterruptRequest> InterruptQueue { get; } = new();

    public void RequestInterrupt(InterruptRequest request)
    {
        InterruptQueue.Enqueue(request);
    }

    public bool HasInterrupt() => InterruptQueue.Any();

    public int RunInterrupt(FrameBuffer frameBuffer)
    {
        if (!InterruptQueue.TryPeek(out var interrupt))
            return 60;

        interrupt.Renderer(frameBuffer);
        if (interrupt.CompleteInterrupt())
            InterruptQueue.TryDequeue(out _);

        return interrupt.TargetFps;
    }

    // ── MonoGame SpriteBatch-based queue (used by MatrixGame) ─────────────────

    /// <summary>
    /// Raised on the game thread when a MonoGame interrupt overlay is enqueued.
    /// Consumed by <c>MatrixGame</c> to draw the overlay on top of the active scene.
    /// </summary>
    public event EventHandler? MonoGameInterruptEnqueued;

    private ConcurrentQueue<MonoGameInterruptRequest> MonoGameInterruptQueue { get; } = new();

    public void RequestInterrupt(MonoGameInterruptRequest request)
    {
        MonoGameInterruptQueue.Enqueue(request);
        MonoGameInterruptEnqueued?.Invoke(this, EventArgs.Empty);
    }

    public bool HasMonoGameInterrupt() => MonoGameInterruptQueue.Any();

    /// <summary>
    /// Peeks at the head of the MonoGame interrupt queue and invokes its renderer.
    /// Automatically dequeues when <see cref="MonoGameInterruptRequest.CompleteInterrupt"/> returns true.
    /// </summary>
    /// <param name="drawOverlay">
    /// Callback that the interrupt renderer uses to draw; receives a configured
    /// <c>SpriteBatch</c> that is already inside Begin/End so the renderer only
    /// needs to issue draw calls.
    /// </param>
    /// <returns>
    /// The interrupt's target FPS, or 60 if the queue is empty.
    /// </returns>
    public int RunMonoGameInterrupt(Action<MonoGameInterruptRequest> drawOverlay)
    {
        if (!MonoGameInterruptQueue.TryPeek(out var interrupt))
            return 60;

        drawOverlay(interrupt);

        if (interrupt.CompleteInterrupt())
            MonoGameInterruptQueue.TryDequeue(out _);

        return interrupt.TargetFps;
    }
}

public record InterruptRequest(Action<FrameBuffer> Renderer, Func<bool> CompleteInterrupt, int TargetFps = 60);

/// <summary>
/// An interrupt overlay for the MonoGame pipeline.
/// <para>
/// <see cref="Renderer"/> receives the active <c>RenderTarget2D</c> (already set on
/// the device) and a <c>SpriteBatch</c> that has NOT yet had Begin called —
/// the renderer owns Begin/End to allow custom blend states.
/// </para>
/// </summary>
public record MonoGameInterruptRequest(
    Action<object /* SpriteBatch */> Renderer,
    Func<bool> CompleteInterrupt,
    int TargetFps = 60);
