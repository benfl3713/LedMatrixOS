using System.Collections.Concurrent;
using LedMatrixOS.Core.Data;
using LedMatrixOS.Core.Overlays;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Core;

public abstract class MatrixAppBase : IMatrixApp
{
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private CancellationTokenSource _lifecycleCts = new CancellationTokenSource();

    private readonly List<string> _ownedOverlays = new();

    /// <summary>
    /// Where this app raises overlays. Set by <see cref="AppManager"/> when the app is activated; null when the app runs
    /// without an engine (tests, settings-only instances), in which case the overlay helpers do nothing.
    /// </summary>
    public IOverlayService? OverlayService { get; set; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual int FrameRate { get; } = 60;

    public virtual Task OnActivatedAsync((int height, int width) valueTuple, IConfiguration configuration, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        RemoveOwnedOverlays();

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

    /// <summary>Adds an overlay on this app's behalf and remembers its id so it is removed when the app deactivates. Returns false when there is no overlay service.</summary>
    protected bool RaiseOverlay(IOverlay overlay)
    {
        var service = OverlayService;
        if (service is null) return false;
        lock (_ownedOverlays) _ownedOverlays.Add(overlay.Id);
        service.Add(overlay);
        return true;
    }

    /// <summary>Removes an overlay this app raised (no fade-out).</summary>
    protected void RemoveOverlay(string id)
    {
        lock (_ownedOverlays) _ownedOverlays.Remove(id);
        OverlayService?.Remove(id);
    }

    private void RemoveOwnedOverlays()
    {
        string[] ids;
        lock (_ownedOverlays) { ids = _ownedOverlays.ToArray(); _ownedOverlays.Clear(); }
        var service = OverlayService;
        if (service is null) return;
        foreach (var id in ids) service.Remove(id);
    }

    protected void RunInBackground(Func<CancellationToken, Task> work)
    {
        var token = _lifecycleCts.Token;
        var task = Task.Run(() => work(token), token);
        _backgroundTasks.Add(task);
    }

    /// <summary>
    /// Polls <paramref name="fetch"/> every <paramref name="interval"/> until the app is deactivated or
    /// <paramref name="stop"/> is cancelled (use it to replace a poll whose inputs changed).
    /// </summary>
    protected ILiveData<T> Poll<T>(TimeSpan interval, Func<CancellationToken, Task<T>> fetch, CancellationToken stop = default)
    {
        var data = new PollingLiveData<T>(interval, fetch);
        RunInBackground(async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stop);
            await data.RunAsync(linked.Token).ConfigureAwait(false);
        });
        return data;
    }

    /// <summary>Called by the engine every frame.</summary>
    public virtual void Update(FrameContext context, CancellationToken cancellationToken)
    {
    }

    public abstract void Render(FrameBuffer frame, CancellationToken cancellationToken);
}

