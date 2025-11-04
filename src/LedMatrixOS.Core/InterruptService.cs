using System.Collections.Concurrent;

namespace LedMatrixOS.Core;

public class InterruptService
{
    private ConcurrentQueue<InterruptRequest> InterruptQueue { get; } = new();

    public void RequestInterrupt(InterruptRequest request)
    {
        InterruptQueue.Enqueue(request);
    }

    public bool HasInterrupt()
    {
        return InterruptQueue.Any();
    }

    public int RunInterrupt(FrameBuffer frameBuffer)
    {
        if (!InterruptQueue.TryPeek(out var interrupt))
            return 60;

        interrupt.Renderer(frameBuffer);
        if (interrupt.CompleteInterrupt())
            InterruptQueue.TryDequeue(out _);

        return interrupt.TargetFps;
    }
}

public record InterruptRequest(Action<FrameBuffer> Renderer, Func<bool> CompleteInterrupt, int TargetFps = 60);
