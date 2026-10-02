using LedMatrixOS.Apps;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

public class WidgetAllocationAndSnapshotTests
{
    public WidgetAllocationAndSnapshotTests() => Fonts.Load();

    /// <summary>One of everything that runs every frame: bound text, marquee, clock, rolling number, pill pulse, progress, list, pager mid-cycle and a faded node.</summary>
    private static (Node root, Action<int> tick) RepresentativeTree()
    {
        IReadOnlyList<int> rows = [1, 2, 3];
        string status = "Good service";
        int counter = 0;
        var time = new FakeTime();
        var text = new TextStyle(Fonts.Small, Pixel.White);

        var root = new Dock
        {
            Top = new Stack(Orientation.Horizontal, gap: 2)
            {
                Height = 12,
                Children =
                {
                    new Pill("VIC", new Pixel(0, 160, 232), pulse: true),
                    new Label(() => status) { Style = text },
                    new MarqueeLabel("A fairly long scrolling station name that overflows") { Grow = 1 },
                    new Clock("HH:mm", time),
                    new RollingNumber(() => counter) { MinDigits = 2 },
                },
            },
            Bottom = new Stack(Orientation.Vertical)
            {
                Children =
                {
                    new Divider(),
                    new ProgressBar(0.4f) { Fill = new Pixel(255, 0, 0) },
                },
            },
            Left = new ListView<int>(() => rows, n => new Label("row " + n)) { Width = 80, Gap = 1 },
            Fill = new Panel
            {
                Children =
                {
                    new Pager(interval: 1.Seconds(), transition: new SlideTransition(MoveDirection.Left))
                    {
                        new Label("page one"),
                        new Label("page two"),
                    },
                    new Label("faded") { Opacity = 0.5f, HAlign = Align.End },
                },
            },
        };
        return (root, i => counter = i / 10);
    }

    [Fact]
    public void SteadyState_AllocatesNothingPerFrame()
    {
        var (root, tick) = RepresentativeTree();
        var stage = new Stage(root);
        var frame = new FrameBuffer(256, 64);
        long index = 0;

        void Frame()
        {
            tick((int)index);
            stage.Step(16);
            frame.Clear(Pixel.Black);
            stage.Host.Render(frame);
            index++;
        }

        // Warm up past a pager transition and a counter roll so lazily created buffers and pooled lists exist.
        for (int i = 0; i < 150; i++) Frame();

        // A real per-frame allocation shows up in every window; one-off JIT/tiering allocations only hit some, so take the minimum.
        var windows = new long[4];
        for (int w = 0; w < windows.Length; w++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Frame(); // 1.6 s: marquee scrolling, counter rolling, pager cycling, pill pulsing
            windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.True(windows.Min() == 0, "allocated bytes per 100-frame window: " + string.Join(", ", windows));
    }

    [Fact]
    public void ComposedScene_Snapshot()
    {
        var time = new FakeTime();
        var root = new Dock
        {
            Top = new Stack(Orientation.Horizontal, gap: 3)
            {
                CrossAlign = Align.Center,
                Padding = new Thickness(3, 2),
                Height = 16,
                Children =
                {
                    new Pill("VIC", new Pixel(0, 160, 232)) { Style = new TextStyle(Fonts.QuiteSmall, Pixel.White, Shadow: false) },
                    new Pill("CEN", new Pixel(220, 36, 31), pulse: false) { Style = new TextStyle(Fonts.QuiteSmall, Pixel.White, Shadow: false) },
                    new Label("Composed scene") { Grow = 1, Style = new TextStyle(Fonts.Small, new Pixel(255, 170, 40)) },
                    new Clock("HH:mm", time),
                },
            },
            Bottom = new Stack(Orientation.Vertical, gap: 1)
            {
                Padding = new Thickness(3, 1),
                Children =
                {
                    new ProgressBar(0.65f) { Thickness = 5, Border = new Pixel(90, 90, 90) },
                    new Divider(),
                },
            },
            Left = LabelGrid(),
            Fill = new Panel
            {
                Children =
                {
                    new RollingNumber(1234) { Mode = RollStyle.Flip, Style = new TextStyle(Fonts.Big, Pixel.White), HAlign = Align.Center, VAlign = Align.Center },
                    new Solid(new Pixel(0, 0, 255), 40, 10) { Opacity = 0.5f, HAlign = Align.End, VAlign = Align.Start },
                },
            },
        };

        var stage = new Stage(root, time: time);
        stage.Step(16);
        var frame = stage.Snapshot();
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, "widgets_composed");
    }

    private static Grid LabelGrid()
    {
        var style = new TextStyle(Fonts.Small, new Pixel(220, 220, 220));
        var grid = new Grid("12,12,*", "50,*", rowGap: 1, columnGap: 2) { Padding = 3, Width = 110 };
        grid.Add(new Label("Station") { Style = style }, 0, 0);
        grid.Add(new Label("Brixton") { Style = style, TextAlignment = TextAlign.Right }, 0, 1);
        grid.Add(new Label("Line") { Style = style }, 1, 0);
        grid.Add(new Label("Victoria") { Style = style, TextAlignment = TextAlign.Right }, 1, 1);
        grid.Add(new Divider(), 2, 0, columnSpan: 2);
        return grid;
    }

    private static FrameBuffer RenderDemo(TimeSpan until)
    {
        var app = new WidgetDemoApp { Time = new FakeTime() };
        app.OnActivatedAsync((64, 256), new ConfigurationBuilder().Build(), CancellationToken.None).GetAwaiter().GetResult();

        var step = TimeSpan.FromMilliseconds(50);
        TimeSpan t = TimeSpan.Zero;
        long i = 0;
        while (t < until)
        {
            t += step;
            app.Update(new FrameContext(t, step, i++), CancellationToken.None);
        }

        var frame = new FrameBuffer(256, 64);
        app.Render(frame, CancellationToken.None);
        return frame;
    }

    [Theory]
    [InlineData(1000, "widget_demo_t1000")]
    [InlineData(3200, "widget_demo_t3200_pager_transition")]
    [InlineData(4150, "widget_demo_t4150_list_reshuffle")]
    [InlineData(9000, "widget_demo_t9000")]
    public void WidgetDemoApp_Snapshot(int ms, string name)
    {
        var frame = RenderDemo(TimeSpan.FromMilliseconds(ms));
        Assert.False(SnapshotHelper.IsBlank(frame));
        SnapshotHelper.AssertMatchesSnapshot(frame, name);
    }

    [Fact]
    public void WidgetDemoApp_AnimatesBetweenFrames()
    {
        Assert.False(Stage.Same(RenderDemo(1.Seconds()), RenderDemo(4300.Ms())));
    }

    [Fact]
    public void WidgetDemoApp_IsDeterministic()
    {
        Assert.True(Stage.Same(RenderDemo(4300.Ms()), RenderDemo(4300.Ms())));
    }

    [Fact]
    public void WidgetDemoApp_IsRegistered()
    {
        Assert.Contains(typeof(WidgetDemoApp), BuiltInApps.GetAll());
        Assert.Equal("widget-demo", new WidgetDemoApp().Id);
    }

    [Fact]
    public void WidgetApp_RebuildsAfterReactivation()
    {
        var app = new WidgetDemoApp { Time = new FakeTime() };
        var config = new ConfigurationBuilder().Build();
        app.OnActivatedAsync((64, 256), config, CancellationToken.None).GetAwaiter().GetResult();
        app.Update(new FrameContext(TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16), 0), CancellationToken.None);
        var first = app.Root;
        Assert.NotNull(first);

        app.OnDeactivatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Null(app.Root);
        app.OnActivatedAsync((64, 256), config, CancellationToken.None).GetAwaiter().GetResult();
        app.Update(new FrameContext(TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16), 0), CancellationToken.None);
        Assert.NotSame(first, app.Root);
    }
}
