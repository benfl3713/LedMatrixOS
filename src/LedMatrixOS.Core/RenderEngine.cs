using System.Diagnostics;

namespace LedMatrixOS.Core;

public sealed class RenderEngine : IDisposable
{
    private readonly IMatrixDevice _device;
    private readonly AppManager _apps;
    private readonly FrameBuffer _frame;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;

    private int _targetFps = 60;
    
    // Transition animation state
    private bool _isTransitioning;
    private FrameBuffer? _oldFrame;
    private int _transitionOffset;
    private const int TransitionSpeed = 10; // pixels per frame

    public RenderEngine(IMatrixDevice device, AppManager apps)
    {
        _device = device;
        _apps = apps;
        _frame = new FrameBuffer(device.Width, device.Height);
        
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
        // Capture the current frame before switching
        if (_apps.ActiveApp != null)
        {
            _oldFrame = new FrameBuffer(_device.Width, _device.Height);
            // Copy current frame to old frame
            for (int y = 0; y < _device.Height; y++)
            {
                for (int x = 0; x < _device.Width; x++)
                {
                    _oldFrame.SetPixel(x, y, _frame.GetPixel(x, y));
                }
            }
            _transitionOffset = 0;
            _isTransitioning = true;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var sw = new Stopwatch();
        var targetFrameTime = TimeSpan.FromSeconds(1.0 / (_isTransitioning ? 60 : TargetFps));
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
                        
                        // Apply transition animation if active
                        if (_isTransitioning && _oldFrame != null)
                        {
                            ApplyTransition();
                        }
                        
                        _device.Present(_frame);
                    }
                    catch
                    {
                        // Ignore individual frame errors to keep loop running
                    }
                }
            }

            // sleep to maintain target FPS
            var frameTime = sw.Elapsed - now;
            var sleep = targetFrameTime - frameTime;
            if (sleep > TimeSpan.Zero)
            {
                try { await Task.Delay(sleep, cancellationToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { }
            }
        }
    }
    
    private void ApplyTransition()
    {
        if (_oldFrame == null) return;
        
        var compositeFrame = new FrameBuffer(_device.Width, _device.Height);
        
        // Calculate positions
        int oldFrameX = -_transitionOffset;
        int newFrameX = _device.Width - _transitionOffset;
        
        // Draw old frame (moving left)
        for (int y = 0; y < _device.Height; y++)
        {
            for (int x = 0; x < _device.Width; x++)
            {
                int oldX = x - oldFrameX;
                if (oldX >= 0 && oldX < _device.Width)
                {
                    compositeFrame.SetPixel(x, y, _oldFrame.GetPixel(oldX, y));
                }
            }
        }
        
        // Draw new frame (moving in from right)
        for (int y = 0; y < _device.Height; y++)
        {
            for (int x = 0; x < _device.Width; x++)
            {
                int newX = x - newFrameX;
                if (newX >= 0 && newX < _device.Width)
                {
                    compositeFrame.SetPixel(x, y, _frame.GetPixel(newX, y));
                }
            }
        }
        
        // Copy composite back to main frame
        for (int y = 0; y < _device.Height; y++)
        {
            for (int x = 0; x < _device.Width; x++)
            {
                _frame.SetPixel(x, y, compositeFrame.GetPixel(x, y));
            }
        }
        
        // Update transition progress
        _transitionOffset += TransitionSpeed;
        
        // Check if transition is complete
        if (_transitionOffset >= _device.Width)
        {
            _isTransitioning = false;
            _oldFrame = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _apps.AppActivated -= OnAppActivated;
    }
}
