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
    void Render(FrameBuffer frame, CancellationToken cancellationToken);
}

