using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core;

public abstract class MatrixAppBase : IMatrixApp
{
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private CancellationTokenSource _lifecycleCts = new CancellationTokenSource();

    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual int FrameRate { get; } = 60;

    public virtual Task OnActivatedAsync((int height, int width) valueTuple, IConfiguration configuration, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        var cts = _lifecycleCts;
        await cts.CancelAsync();
        _lifecycleCts = new CancellationTokenSource();

        while (_backgroundTasks.TryTake(out var task))
        {
            try
            {
                await Task.WhenAny(task, Task.Delay(50, cancellationToken)).ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }

    protected void RunInBackground(Func<CancellationToken, Task> work)
    {
        var token = _lifecycleCts.Token;
        var task = Task.Run(() => work(token), token);
        _backgroundTasks.Add(task);
    }

    public abstract void Update(TimeSpan deltaTime, CancellationToken cancellationToken);
    public abstract void Render(FrameBuffer frame, CancellationToken cancellationToken);
}

