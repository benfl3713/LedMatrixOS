using System.Diagnostics;

namespace LedMatrixOS.Core;

public sealed class RenderEngine
{
    private readonly IMatrixDevice _device;
    private readonly AppManager _apps;
    private readonly FrameBuffer _frame;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;

    private int _targetFps = 60;

    public RenderEngine(IMatrixDevice device, AppManager apps)
    {
        _device = device;
        _apps = apps;
        _frame = new FrameBuffer(device.Width, device.Height);
    }

    public int TargetFps
    {
        get => _apps.ActiveApp?.FrameRate ?? _targetFps;
    }

    public bool IsRunning => _cts != null;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => RunLoopAsync(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var sw = new Stopwatch();
        var targetFrameTime = TimeSpan.FromSeconds(1.0 / TargetFps);
        sw.Start();
        var last = sw.Elapsed;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = sw.Elapsed;
            var delta = now - last;
            last = now;

            // Only render if the device is enabled
            if (_device.IsEnabled)
            {
                var app = _apps.ActiveApp;
                if (app != null)
                {
                    try
                    {
                        app.Update(delta, cancellationToken);
                        _frame.Clear(Pixel.Black);
                        app.Render(_frame, cancellationToken);
                        _device.Present(_frame);
                    }
                    catch
                    {
                        // Ignore individual frame errors to keep loop running
                    }
                }
            }

            // sleep to maintain target FPS
            targetFrameTime = TimeSpan.FromSeconds(1.0 / TargetFps);
            var frameTime = sw.Elapsed - now;
            var sleep = targetFrameTime - frameTime;
            if (sleep > TimeSpan.Zero)
            {
                try { await Task.Delay(sleep, cancellationToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { }
            }
        }
    }
}

