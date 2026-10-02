using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Tests;

/// <summary>Drives a toy WidgetApp with deterministic frame times.</summary>
internal sealed class ToyRunner
{
    private long _frame;

    public ToyRunner(WidgetApp app, int stepMs = 16)
    {
        Fonts.Load();
        App = app;
        StepMs = stepMs;
        app.Time = new FakeTime();
        app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();
        Frame = new FrameBuffer(256, 64);
    }

    public WidgetApp App { get; }
    public int StepMs { get; }
    public FrameBuffer Frame { get; }
    public TimeSpan Time { get; private set; }

    public void Advance(int ms, Action? perFrame = null)
    {
        int target = (int)Time.TotalMilliseconds + ms;
        while ((int)Time.TotalMilliseconds < target)
        {
            var delta = TimeSpan.FromMilliseconds(StepMs);
            Time += delta;
            App.Update(new FrameContext(Time, delta, _frame++), CancellationToken.None);
            perFrame?.Invoke();
        }
    }

    public FrameBuffer Render()
    {
        Frame.Clear(Pixel.Black);
        App.Render(Frame, CancellationToken.None);
        return Frame;
    }

    public FrameBuffer Snapshot()
    {
        Render();
        var copy = new FrameBuffer(Frame.Width, Frame.Height);
        copy.CopyFrom(Frame);
        return copy;
    }

    public static FrameBuffer RunTo(WidgetApp app, int ms, int stepMs = 16)
    {
        var r = new ToyRunner(app, stepMs);
        r.Advance(ms);
        return r.Snapshot();
    }
}
