using System.Diagnostics;
using LedMatrixOS.Core.Overlays;
using LedMatrixOS.Core.Transitions;
using Microsoft.Extensions.Logging;

namespace LedMatrixOS.Core;

public sealed class RenderEngine : IDisposable
{
    private readonly IMatrixDevice _device;
    private readonly AppManager _apps;
    private readonly ILogger? _logger;
    private readonly FrameBuffer _frame;       // what is presented
    private readonly FrameBuffer _appFrame;    // the new app's render target while a transition runs
    private readonly FrameBuffer _oldFrame;    // snapshot of the last presented frame at app switch
    private CancellationTokenSource? _cts;

    private const int _targetFps = 60;
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(5);

    // Transition state (touched by the render loop; the activation event only sets _transitionPending)
    private volatile bool _transitionPending;
    private bool _hasPresentedFrame;
    private ITransition? _activeTransition;
    private TimeSpan _transitionStart;
    private bool _transitionStarted;

    // Throttled error logging
    private readonly CrashGuard _crashGuard = new();
    private TimeSpan _lastErrorLog = TimeSpan.MinValue;
    private int _suppressedErrors;

    public TransitionRegistry Transitions { get; }

    /// <summary>Latest presented frame, for live previews.</summary>
    public FrameBroadcaster Broadcaster { get; } = new();

    /// <summary>Draws the crash card shown in place of an app that threw (set by the host; Core has no fonts). Falls back to a plain red screen.</summary>
    public Action<FrameBuffer, CrashInfo>? CrashRenderer { get; set; }

    /// <summary>Toasts, badges and alerts composited over whatever is running .</summary>
    public OverlayManager Overlays { get; }

    /// <summary>Name of a registered transition or "random" (a new pick for every app switch).</summary>
    public string TransitionName { get; set; } = "slide-up";

    public RenderEngine(IMatrixDevice device, AppManager apps,
        TransitionRegistry? transitions = null, ILogger<RenderEngine>? logger = null, OverlayManager? overlays = null)
    {
        _device = device;
        _apps = apps;
        _logger = logger;
        Transitions = transitions ?? new TransitionRegistry();
        Overlays = overlays ?? new OverlayManager(device.Width, device.Height);
        _frame = new FrameBuffer(device.Width, device.Height);
        _appFrame = new FrameBuffer(device.Width, device.Height);
        _oldFrame = new FrameBuffer(device.Width, device.Height);

        // Subscribe to app activation events
        _apps.AppActivated += OnAppActivated;
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

    private void OnAppActivated(object? sender, IMatrixApp newApp)
    {
        // The render loop snapshots the last presented frame before drawing the new app.
        _transitionPending = true;
        _crashGuard.Clear();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var sw = new Stopwatch();
        sw.Start();
        var last = sw.Elapsed;
        long frameIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = sw.Elapsed;
            var delta = now - last;
            last = now;

            // Only render if the device is enabled
            if (_device.IsEnabled)
            {
                var app = _apps.ActiveApp;
                if (app != null && _crashGuard.Active(now) is { } crash)
                {
                    _frame.Clear(Pixel.Black);
                    if (CrashRenderer != null) CrashRenderer(_frame, crash);
                    else _frame.Clear(new Pixel(120, 0, 0));
                    PresentFrame();
                    _hasPresentedFrame = true;
                }
                else if (app != null)
                {
                    try
                    {
                        BeginTransitionIfPending();
                        var ctx = new FrameContext(now, delta, frameIndex++);
                        app.Update(ctx, cancellationToken);

                        if (_activeTransition is { } transition)
                        {
                            _appFrame.Clear(Pixel.Black);
                            app.Render(_appFrame, cancellationToken);

                            if (!_transitionStarted)
                            {
                                _transitionStart = now;
                                _transitionStarted = true;
                            }

                            float t = (float)((now - _transitionStart).TotalSeconds / transition.Duration.TotalSeconds);
                            if (t >= 1f)
                            {
                                _activeTransition = null;
                                _frame.CopyFrom(_appFrame);
                            }
                            else
                            {
                                transition.Render(_oldFrame, _appFrame, _frame, TransitionEasing.OutCubic(Math.Max(t, 0f)));
                            }
                        }
                        else
                        {
                            _frame.Clear(Pixel.Black);
                            app.Render(_frame, cancellationToken);
                        }

                        Overlays.Update(delta);
                        Overlays.RenderOverlays(_frame, ctx);

                        PresentFrame();
                        _hasPresentedFrame = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Keep the loop running, but don't flood the log at 60fps
                        _activeTransition = null;
                        _crashGuard.Record(app, ex, now);
                        LogFrameError(ex, app, now);
                    }
                }
            }

            // sleep to maintain target FPS
            var targetFrameTime = TimeSpan.FromSeconds(1.0 / (_activeTransition != null ? 60 : TargetFps));
            var frameTime = sw.Elapsed - now;
            var sleep = targetFrameTime - frameTime;
            if (sleep > TimeSpan.Zero)
            {
                try { await Task.Delay(sleep, cancellationToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { }
            }
        }
    }

    private void PresentFrame()
    {
        Broadcaster.Publish(_frame);
        _device.Present(_frame);
    }

    private void BeginTransitionIfPending()
    {
        if (!_transitionPending) return;
        _transitionPending = false;

        // Nothing on screen yet (first app), so nothing to transition from
        if (!_hasPresentedFrame) return;

        var transition = Transitions.Resolve(TransitionName);
        if (transition == null) return;

        _oldFrame.CopyFrom(_frame);
        _activeTransition = transition;
        _transitionStarted = false;
    }

    private void LogFrameError(Exception ex, IMatrixApp app, TimeSpan now)
    {
        if (_lastErrorLog != TimeSpan.MinValue && now - _lastErrorLog < ErrorLogInterval)
        {
            _suppressedErrors++;
            return;
        }

        var suppressed = _suppressedErrors;
        _suppressedErrors = 0;
        _lastErrorLog = now;

        if (_logger != null)
            _logger.LogError(ex, "App {App} failed while rendering a frame ({Suppressed} similar errors suppressed)",
                app.GetType().Name, suppressed);
        else
            Console.Error.WriteLine($"App {app.GetType().Name} failed while rendering a frame ({suppressed} similar errors suppressed): {ex}");
    }

    public void Dispose()
    {
        Stop();
        _apps.AppActivated -= OnAppActivated;
    }
}
