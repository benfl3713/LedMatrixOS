using LedMatrixOS.Core;
using LedMatrixOS.Core.Screens;
using LedMatrixOS.Graphics.Text;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedMatrixOS.Apps.Screens;

/// <summary>
/// Renders one frame of an unsaved <see cref="ScreenDefinition"/> off-screen. Uses a private <see cref="ScreenStore"/> and a throwaway
/// <see cref="ScreenApp"/>, so the active app, the render loop and screens.json are untouched. Concurrency is capped at one render
/// (callers wait their turn) so rapid editing cannot overload the device.
/// </summary>
public sealed class ScreenPreviewRenderer(IConfiguration configuration, HttpClient http, int width, int height)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int Width { get; } = width;
    public int Height { get; } = height;

    public async Task<byte[]> RenderPngAsync(ScreenDefinition screen, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return ToPng(await RenderAsync(screen, ct)); }
        finally { _gate.Release(); }
    }

    public async Task<FrameBuffer> RenderAsync(ScreenDefinition screen, CancellationToken ct)
    {
        Fonts.Load();
        var store = new ScreenStore();
        store.Replace(screen);
        var app = new ScreenApp(store, http) { ScreenId = screen.Id };
        await app.OnActivatedAsync((Height, Width), configuration, ct);
        try
        {
            long index = 0;
            var now = TimeSpan.Zero;
            var step = TimeSpan.FromMilliseconds(50);
            void Tick() { now += step; app.Update(new FrameContext(now, step, index++), ct); }

            Tick();
            // Give network-backed bindings (weather, Home Assistant) a moment to resolve; otherwise placeholders are shown.
            if (app.BindingCount > 0)
            {
                await Task.Delay(400, ct);
                Tick();
            }
            for (int i = 0; i < 10; i++) Tick();

            var frame = new FrameBuffer(Width, Height);
            frame.Clear(Pixel.Black);
            app.Render(frame, ct);
            return frame;
        }
        finally
        {
            await app.OnDeactivatedAsync(CancellationToken.None);
        }
    }

    public static byte[] ToPng(FrameBuffer frame)
    {
        using var image = new Image<Rgba32>(frame.Width, frame.Height);
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                image[x, y] = new Rgba32(p.R, p.G, p.B, 255);
            }
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
