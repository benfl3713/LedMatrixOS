using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Core;

public readonly record struct Pixel(byte R, byte G, byte B)
{
    public static readonly Pixel Black = new(0, 0, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out byte r, out byte g, out byte b)
    {
        r = R; g = G; b = B;
    }
    
    public static Pixel operator /(Pixel pixel, int divisor) => new Pixel((byte)(pixel.R / divisor), (byte)(pixel.G / divisor), (byte)(pixel.B / divisor));
}

public sealed class FrameBuffer
{
    public int Width { get; }
    public int Height { get; }

    private readonly Pixel[] _pixels; // row-major

    public FrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
        Width = width; Height = height;
        _pixels = new Pixel[width * height];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Index(int x, int y) => y * Width + x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Pixel pixel)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _pixels[Index(x, y)] = pixel;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pixel GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return Pixel.Black;
        return _pixels[Index(x, y)];
    }

    public void Clear(Pixel? pixel = null)
    {
        pixel ??= new Pixel(0, 0, 0);
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = pixel.Value;
    }

    public ReadOnlySpan<Pixel> GetPixelsSpan() => _pixels;
    
    public ReadOnlySpan<Pixel> GetPixelRowSpan(int y) => _pixels.AsSpan().Slice(Index(0, y));
    
    public void RenderImage(Image<Rgb24> image)
    {
        for (int y = 0; y < Math.Min(image.Height, Height); y++)
        {
            for (int x = 0; x < Math.Min(image.Width, Width); x++)
            {
                var pixel = image[x, y];
                SetPixel(x, y, new Pixel(pixel.R, pixel.G, pixel.B));
            }
        }
    }
}

public interface IMatrixDevice
{
    int Width { get; }
    int Height { get; }
    byte Brightness { get; set; }
    bool IsEnabled { get; set; }

    void Present(FrameBuffer buffer);
}

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

public interface IConfigurableApp : IMatrixApp
{
    IEnumerable<AppSetting> GetSettings();
    void UpdateSetting(string key, object value);
}

public record AppSetting(string Key, string Name, string Description, AppSettingType Type, object DefaultValue, object CurrentValue, object? MinValue = null, object? MaxValue = null, string[]? Options = null);

public enum AppSettingType
{
    Boolean,
    Integer,
    String,
    Color,
    Select
}

public sealed class AppManager
{
    private readonly IConfiguration _configuration;
    private readonly int _height;
    private readonly int _width;
    private readonly Dictionary<string, Type> _appsById = new(StringComparer.OrdinalIgnoreCase);
    private IMatrixApp? _activeApp;

    public IEnumerable<Type> Apps => _appsById.Values;
    public IMatrixApp? ActiveApp => _activeApp;
    
    public event EventHandler<IMatrixApp>? AppActivated;

    public AppManager(IConfiguration configuration, int height, int width)
    {
        _configuration = configuration;
        _height = height;
        _width = width;
    }

    public void Register(Type app)
    {
        // validate if app implements IMatrixApp
        if (!typeof(IMatrixApp).IsAssignableFrom(app)) throw new ArgumentException("Type must implement IMatrixApp", nameof(app));
        
        var instance = (IMatrixApp?)Activator.CreateInstance(app);
        if (instance == null) throw new InvalidOperationException("Failed to create instance of app");
        
        var id = instance.Id;
        
        _appsById[id] = app;
    }

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken)
    {
        if (!_appsById.TryGetValue(id, out var next)) return false;

        if (_activeApp != null)
        {
            try { await _activeApp.OnDeactivatedAsync(cancellationToken).ConfigureAwait(false); }
            catch { /* swallow app errors on deactivate */ }
        }

        var nextApp = (IMatrixApp?)Activator.CreateInstance(next);
        await nextApp.OnActivatedAsync((_height, _width), _configuration, cancellationToken).ConfigureAwait(false);
        _activeApp = nextApp;
        
        // Raise the AppActivated event
        AppActivated?.Invoke(this, nextApp);
        
        return true;
    }
}

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

public abstract class MatrixAppBase : IMatrixApp
{
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private CancellationTokenSource? _lifecycleCts;

    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual int FrameRate { get; } = 60;

    public virtual Task OnActivatedAsync((int height, int width) valueTuple, IConfiguration configuration, CancellationToken cancellationToken)
    {
        _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return Task.CompletedTask;
    }

    public virtual async Task OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        var cts = _lifecycleCts;
        if (cts != null)
        {
            await cts.CancelAsync();
            _lifecycleCts = null;
        }

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
        var token = _lifecycleCts?.Token ?? CancellationToken.None;
        var task = Task.Run(() => work(token), token);
        _backgroundTasks.Add(task);
    }

    public abstract void Update(TimeSpan deltaTime, CancellationToken cancellationToken);
    public abstract void Render(FrameBuffer frame, CancellationToken cancellationToken);
}
