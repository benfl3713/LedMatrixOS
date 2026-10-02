using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Xunit;

namespace LedMatrixOS.Tests;

public class WidgetBehaviourTests
{
    // ---- Label / bindings ----

    [Fact]
    public void Label_BindsToFuncAndLiveData()
    {
        string current = "one";
        var live = new FakeLive<string> { Value = "alpha" };
        var fromFunc = new Label(() => current);
        var fromLive = new Label(live);
        var stage = new Stage(new Stack { Children = { fromFunc, fromLive } });

        stage.Step(16);
        Assert.Equal(fromFunc.Bounds.Width, Fonts.Small.MeasureText("one"));
        Assert.Equal(Fonts.Small.MeasureText("alpha"), fromLive.Bounds.Width);

        current = "three";
        live.Value = "be";
        stage.Step(16);
        Assert.Equal(Fonts.Small.MeasureText("three"), fromFunc.Bounds.Width);
        Assert.Equal(Fonts.Small.MeasureText("be"), fromLive.Bounds.Width);
    }

    [Fact]
    public void Label_DrawsText()
    {
        var stage = new Stage(new Label("Hi"));
        stage.Step(16);
        Assert.False(SnapshotHelper.IsBlank(stage.Render()));
    }

    [Fact]
    public void Clock_UsesInjectedTimeProvider()
    {
        var time = new FakeTime();
        var clock = new Clock("HH:mm:ss", time) { HAlign = Align.Start };
        var stage = new Stage(clock, time: time);
        stage.Step(16);
        Assert.Equal(Fonts.Small.MeasureText("13:45:07"), clock.Bounds.Width);

        var withSeconds = stage.Snapshot();
        time.Now = time.Now.AddMinutes(1);
        stage.Step(16);
        // Same width, different glyphs: the minute digit changed.
        Assert.False(Stage.Same(withSeconds, stage.Snapshot()));
    }

    // ---- MarqueeLabel ----

    private static (MarqueeLabel marquee, Stage stage) LongMarquee()
    {
        var marquee = new MarqueeLabel(new string('A', 20)) { Width = 50, PauseDuration = 2.Seconds(), Speed = 30f };
        return (marquee, new Stage(new Panel { Children = { marquee } }));
    }

    [Fact]
    public void Marquee_PausesScrollsPausesAndResets()
    {
        var (marquee, stage) = LongMarquee();
        int overflow = Fonts.Small.MeasureText(new string('A', 20)) - 50;
        Assert.True(overflow > 0);

        stage.Step(100, 20); // 2.0s of frames: initial pause (the first frame only primes layout)
        Assert.Equal(0, marquee.Offset);

        stage.Step(100, 5); // 0.5s into the scroll
        Assert.InRange(marquee.Offset, 12, 16);

        stage.Step(100, 20); // scrolling takes overflow / 30 = 2.3 s, so it has reached the end
        Assert.Equal(overflow, marquee.Offset);

        stage.Step(100, 10); // holding at the end
        Assert.Equal(overflow, marquee.Offset);

        stage.Step(100, 20); // pause over: jumps back and starts again
        Assert.True(marquee.Offset < overflow);
    }

    [Fact]
    public void Marquee_FittingTextNeverScrolls()
    {
        var marquee = new MarqueeLabel("Hi") { Width = 80 };
        var stage = new Stage(new Panel { Children = { marquee } });
        stage.Step(500, 40);
        Assert.Equal(0, marquee.Offset);
    }

    [Fact]
    public void Marquee_IsDeterministicForTheSameFrameTimes()
    {
        var (a, stageA) = LongMarquee();
        var (b, stageB) = LongMarquee();
        stageA.Step(50, 100);
        stageB.Step(50, 100);
        Assert.Equal(a.Offset, b.Offset);
        Assert.True(Stage.Same(stageA.Render(), stageB.Render()));
    }

    [Fact]
    public void Marquee_ChangingTextRestartsThePause()
    {
        string text = new string('A', 20);
        var marquee = new MarqueeLabel(() => text) { Width = 50 };
        var stage = new Stage(new Panel { Children = { marquee } });
        stage.Step(100, 30);
        Assert.True(marquee.Offset > 0);

        text = new string('B', 21);
        stage.Step(100, 2);
        Assert.Equal(0, marquee.Offset);
    }

    // ---- RollingNumber ----

    [Fact]
    public void RollingNumber_FirstValueDoesNotAnimate()
    {
        var number = new RollingNumber(42);
        var stage = new Stage(number);
        stage.Step(16);
        Assert.False(number.IsRolling);
        Assert.Equal(2, number.DigitAt(0));
        Assert.Equal(4, number.DigitAt(1));
        Assert.Equal(-1, number.DigitAt(2));
    }

    [Fact]
    public void RollingNumber_OdometerRollsOnlyChangedDigits()
    {
        var number = new RollingNumber(20);
        var stage = new Stage(number);
        stage.Step(16);

        number.Value = 21;
        stage.Step(16);
        Assert.True(number.RollProgress(0) < 1f);
        Assert.Equal(1f, number.RollProgress(1)); // the tens digit did not change

        stage.Step(500);
        Assert.False(number.IsRolling);
    }

    [Fact]
    public void RollingNumber_MidRollFramesDifferFromStartAndEnd()
    {
        var number = new RollingNumber(0) { Style = new TextStyle(Fonts.Big, Pixel.White) };
        var stage = new Stage(number);
        stage.Step(16);
        var before = stage.Snapshot();

        number.Value = 1;
        stage.Step(16); // the change is noticed here and the roll starts
        stage.Step(175); // about half of the 350 ms roll
        Assert.True(number.IsRolling);
        float p = number.RollProgress(0);
        Assert.InRange(p, 0.1f, 0.99f);
        var mid = stage.Snapshot();

        stage.Step(400);
        Assert.False(number.IsRolling);
        var after = stage.Snapshot();

        Assert.False(Stage.Same(before, mid));
        Assert.False(Stage.Same(mid, after));
        Assert.False(Stage.Same(before, after));

        // Once settled it looks exactly like a counter that was created showing 1.
        var fresh = new Stage(new RollingNumber(1) { Style = new TextStyle(Fonts.Big, Pixel.White) });
        fresh.Step(16);
        Assert.True(Stage.Same(after, fresh.Snapshot()));
    }

    [Fact]
    public void RollingNumber_DirectionFollowsTheValue()
    {
        var up = new RollingNumber(5);
        var down = new RollingNumber(5);
        var upStage = new Stage(up);
        var downStage = new Stage(down);
        upStage.Step(16);
        downStage.Step(16);
        up.Value = 6;
        down.Value = 4;
        upStage.Step(16);
        downStage.Step(16);
        upStage.Step(120);
        downStage.Step(120);
        Assert.False(Stage.Same(upStage.Snapshot(), downStage.Snapshot()));
    }

    [Fact]
    public void RollingNumber_FlipModeAnimatesAndSettles()
    {
        var number = new RollingNumber(3) { Mode = RollStyle.Flip, MinDigits = 2, Style = new TextStyle(Fonts.Big, Pixel.White) };
        var stage = new Stage(number);
        stage.Step(16);
        var before = stage.Snapshot();

        number.Value = 4;
        stage.Step(16);
        stage.Step(130); // first half: old top flap folding
        var early = stage.Snapshot();
        stage.Step(250); // second half: new bottom flap unfolding
        var late = stage.Snapshot();
        stage.Step(400);
        var settled = stage.Snapshot();

        Assert.False(number.IsRolling);
        Assert.False(Stage.Same(before, early));
        Assert.False(Stage.Same(early, late));
        Assert.False(Stage.Same(late, settled));
    }

    [Fact]
    public void RollingNumber_MinDigitsPadsWithZeros()
    {
        var number = new RollingNumber(7) { MinDigits = 3 };
        new Stage(number).Step(16);
        Assert.Equal(7, number.DigitAt(0));
        Assert.Equal(0, number.DigitAt(1));
        Assert.Equal(0, number.DigitAt(2));
    }

    // ---- Pager ----

    private static Pager ThreePages(out Solid[] pages)
    {
        pages =
        [
            new Solid(new Pixel(255, 0, 0)),
            new Solid(new Pixel(0, 255, 0)),
            new Solid(new Pixel(0, 0, 255)),
        ];
        var pager = new Pager(interval: 3.Seconds(), transition: new SlideTransition(MoveDirection.Left) { Duration = 400.Ms() });
        foreach (var p in pages) pager.Add(p);
        return pager;
    }

    [Fact]
    public void Pager_CyclesOnItsInterval()
    {
        var pager = ThreePages(out var pages);
        var stage = new Stage(pager);

        stage.Step(100); // first frame shows page 0
        Assert.Same(pages[0], pager.CurrentPage);
        Assert.False(pager.IsTransitioning);

        stage.Step(100, 28); // 2.9 s after the first frame
        Assert.False(pager.IsTransitioning);
        Assert.Equal(0, pager.PageIndex);

        stage.Step(100, 2); // interval reached
        Assert.True(pager.IsTransitioning);

        stage.Step(100, 5); // transition (400 ms) over
        Assert.False(pager.IsTransitioning);
        Assert.Equal(1, pager.PageIndex);
        Assert.Same(pages[1], pager.CurrentPage);

        stage.Step(100, 40);
        Assert.Equal(2, pager.PageIndex);
        stage.Step(100, 40);
        Assert.Equal(0, pager.PageIndex);
    }

    [Fact]
    public void Pager_MidTransitionShowsBothPages()
    {
        var pager = ThreePages(out _);
        var stage = new Stage(pager, 100, 20);
        stage.Step(100, 31); // transition started
        stage.Step(100, 2);  // ~ 200 ms into it
        var frame = stage.Render();

        Assert.True(pager.IsTransitioning);
        Assert.Equal(new Pixel(255, 0, 0), frame.GetPixel(2, 5));
        Assert.Equal(new Pixel(0, 255, 0), frame.GetPixel(97, 5));
    }

    [Fact]
    public void Pager_BoundDataSplitsIntoPagesAndRebuildsOnChange()
    {
        IReadOnlyList<string> items = ["a", "b", "c", "d", "e"];
        var pager = new Pager(pageSize: 2, interval: 3.Seconds()).Bind(() => items, s => new Label(s));
        var stage = new Stage(pager);
        stage.Step(100);

        Assert.Equal(3, pager.PageCount);
        var grid = Assert.IsType<Grid>(pager.CurrentPage);
        Assert.Equal(2, grid.Children.Count);

        items = ["x"];
        stage.Step(100);
        Assert.Equal(1, pager.PageCount);
        Assert.Single(Assert.IsType<Grid>(pager.CurrentPage).Children);
    }

    [Fact]
    public void Pager_SamePagesContentDoesNotRebuild()
    {
        IReadOnlyList<string> items = ["a", "b", "c"];
        var pager = new Pager(pageSize: 2).Bind(() => items, s => new Label(s));
        var stage = new Stage(pager);
        stage.Step(100);
        var page = pager.CurrentPage;

        items = ["a", "b", "c"]; // a new list instance with equal contents
        stage.Step(100);
        Assert.Same(page, pager.CurrentPage);
    }

    // ---- ListView ----

    private static ListView<int> Numbers(Func<IReadOnlyList<int>> source) =>
        new(source, n => new Solid(new Pixel(200, 200, 200), null, 10)) { Gap = 2, Width = 40 };

    [Fact]
    public void ListView_InitialItemsAppearInstantly()
    {
        IReadOnlyList<int> items = [1, 2, 3];
        var list = Numbers(() => items);
        var stage = new Stage(list);
        stage.Step(16);

        Assert.Equal(3, list.Count);
        Assert.All(new[] { 1, 2, 3 }, k => Assert.Equal(1f, list.NodeFor(k)!.Opacity));
        Assert.Equal(0, list.NodeFor(1)!.Bounds.Y);
        Assert.Equal(12, list.NodeFor(2)!.Bounds.Y);
        Assert.Equal(24, list.NodeFor(3)!.Bounds.Y);
    }

    [Fact]
    public void ListView_ReorderKeepsNodesAndSlidesThemToNewSlots()
    {
        IReadOnlyList<int> items = [1, 2, 3];
        var list = Numbers(() => items);
        var stage = new Stage(list);
        stage.Step(16);
        var n1 = list.NodeFor(1)!; var n2 = list.NodeFor(2)!; var n3 = list.NodeFor(3)!;

        items = [3, 1, 2];
        stage.Step(16);

        Assert.Same(n1, list.NodeFor(1));
        Assert.Same(n2, list.NodeFor(2));
        Assert.Same(n3, list.NodeFor(3));
        // Laid out at the new slots, but still drawn near the old positions.
        Assert.Equal(0, n3.Bounds.Y);
        Assert.True(n3.Position.Y > 15f, $"n3 should start about 24px below its new slot, offset {n3.Position.Y}");
        Assert.True(n1.Position.Y < -5f);

        stage.Step(100);
        float mid = n3.Position.Y;
        Assert.InRange(mid, 1f, 23f);

        stage.Step(500);
        Assert.Equal(0f, n3.Position.Y);
        Assert.Equal(0f, n1.Position.Y);
        Assert.Equal(12, n1.Bounds.Y);
        Assert.Equal(24, n2.Bounds.Y);
    }

    [Fact]
    public void ListView_InsertedKeyFadesInWhileOthersKeepTheirNodes()
    {
        IReadOnlyList<int> items = [1, 2];
        var list = Numbers(() => items);
        var stage = new Stage(list);
        stage.Step(16);
        var n1 = list.NodeFor(1)!; var n2 = list.NodeFor(2)!;

        items = [1, 9, 2];
        stage.Step(16);
        var added = list.NodeFor(9)!;
        Assert.Same(n1, list.NodeFor(1));
        Assert.Same(n2, list.NodeFor(2));
        Assert.True(added.Opacity < 0.2f);

        stage.Step(120);
        Assert.InRange(added.Opacity, 0.2f, 0.99f);
        Assert.True(n2.Bounds.Y > 12, "the row below makes room as the new one expands");

        stage.Step(500);
        Assert.Equal(1f, added.Opacity);
        Assert.Equal(12, added.Bounds.Y);
        Assert.Equal(24, n2.Bounds.Y);
        Assert.Equal(0f, n2.Position.Y);
    }

    [Fact]
    public void ListView_RemovedKeyFadesAndCollapsesBeforeBeingDropped()
    {
        IReadOnlyList<int> items = [1, 2, 3];
        var list = Numbers(() => items);
        var stage = new Stage(list);
        stage.Step(16);
        var n2 = list.NodeFor(2)!;
        var n3 = list.NodeFor(3)!;

        items = [1, 3];
        stage.Step(16);
        Assert.Null(list.NodeFor(2));
        Assert.Equal(3, list.Children.Count); // still in the tree, animating out
        Assert.Equal(2, list.Count);

        stage.Step(100);
        Assert.InRange(n2.Opacity, 0.01f, 0.99f);
        Assert.InRange(n3.Bounds.Y, 13, 23); // collapsing

        stage.Step(400);
        Assert.Equal(2, list.Children.Count);
        Assert.Null(n2.Parent);
        Assert.Equal(12, n3.Bounds.Y);
    }

    [Fact]
    public void ListView_ChangedItemWithSameKeyKeepsNodeAndNotifies()
    {
        IReadOnlyList<(int id, string name)> items = [(1, "a"), (2, "b")];
        Node? updated = null;
        var list = new ListView<(int id, string name)>(() => items, i => new Label(i.name), i => i.id)
        {
            ItemChanged = (node, item) => updated = node,
        };
        var stage = new Stage(list);
        stage.Step(16);
        var node = list.NodeFor(2)!;

        items = [(1, "a"), (2, "bb")];
        stage.Step(16);
        Assert.Same(node, list.NodeFor(2));
        Assert.Same(node, updated);
    }

    [Fact]
    public void ListView_DuplicateKeysAreSkipped()
    {
        IReadOnlyList<int> items = [1, 1, 2];
        var list = Numbers(() => items);
        new Stage(list).Step(16);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void ListView_HorizontalOrientationLaysOutLeftToRight()
    {
        IReadOnlyList<int> items = [1, 2];
        var list = new ListView<int>(() => items, n => new Solid(Pixel.White, 10, 6)) { Orientation = Orientation.Horizontal, Gap = 1 };
        new Stage(list).Step(16);
        Assert.Equal(0, list.NodeFor(1)!.Bounds.X);
        Assert.Equal(11, list.NodeFor(2)!.Bounds.X);
    }

    // ---- Pill / Divider / ProgressBar / Icon ----

    [Fact]
    public void Pill_PulseChangesBrightnessOverTime()
    {
        var pill = new Pill("VIC", new Pixel(0, 160, 232), pulse: true);
        var stage = new Stage(pill);
        stage.Step(16);
        var a = stage.Snapshot();
        stage.Step(300);
        var b = stage.Snapshot();
        Assert.False(Stage.Same(a, b));

        // The same time yields the same frame.
        var other = new Stage(new Pill("VIC", new Pixel(0, 160, 232), pulse: true));
        other.Step(16);
        other.Step(300);
        Assert.True(Stage.Same(b, other.Snapshot()));
    }

    [Fact]
    public void ProgressBarAndDivider_Draw()
    {
        var root = new Stack(Orientation.Vertical, gap: 1)
        {
            Children =
            {
                new ProgressBar(0.5f) { Fill = new Pixel(255, 0, 0), Background = new Pixel(0, 0, 255), Thickness = 3 },
                new Divider { Color = new Pixel(9, 9, 9) },
            },
        };
        var stage = new Stage(root, 100, 20);
        stage.Step(16);
        var frame = stage.Render();

        Assert.Equal(new Pixel(255, 0, 0), frame.GetPixel(10, 1));
        Assert.Equal(new Pixel(0, 0, 255), frame.GetPixel(90, 1));
        Assert.Equal(new Pixel(9, 9, 9), frame.GetPixel(50, 4));
    }

    [Fact]
    public void Icon_PlaysSpriteFramesWithFrameTime()
    {
        Pixel[] red = [new Pixel(255, 0, 0)];
        Pixel[] green = [new Pixel(0, 255, 0)];
        byte[] alpha = [255];
        var sprite = new Graphics.Sprite(1, 1,
        [
            new Graphics.Sprite.SpriteFrame(red, alpha, 100.Ms()),
            new Graphics.Sprite.SpriteFrame(green, alpha, 100.Ms()),
        ]);
        var stage = new Stage(new Icon(sprite));
        stage.Step(10);
        Assert.Equal(new Pixel(255, 0, 0), stage.Render().GetPixel(0, 0));
        stage.Step(100);
        Assert.Equal(new Pixel(0, 255, 0), stage.Render().GetPixel(0, 0));
    }
}
