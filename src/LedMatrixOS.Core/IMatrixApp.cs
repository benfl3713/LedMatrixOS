using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core;

public interface IMatrixApp
{
    string Id { get; }
    string Name { get; }
    int FrameRate { get; }
    Task OnActivatedAsync((int height, int width) valueTuple, IConfiguration configuration, CancellationToken cancellationToken);
    Task OnDeactivatedAsync(CancellationToken cancellationToken);
    void Update(TimeSpan deltaTime, CancellationToken cancellationToken);
    /// <summary>
    /// Per-frame update with timing context. Default member so existing implementers need no change;
    /// it forwards to the legacy TimeSpan overload.
    /// </summary>
    void Update(FrameContext context, CancellationToken cancellationToken) => Update(context.Delta, cancellationToken);
    void Render(FrameBuffer frame, CancellationToken cancellationToken);
}

