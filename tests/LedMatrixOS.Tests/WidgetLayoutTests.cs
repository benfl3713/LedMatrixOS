using System.Numerics;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;
using Xunit;

namespace LedMatrixOS.Tests;

public class WidgetLayoutTests
{
    private static Stage Layout(Node root, int w = 100, int h = 60)
    {
        var stage = new Stage(root, w, h);
        stage.Step(16);
        return stage;
    }

    [Fact]
    public void VerticalStack_PositionsWithGapAndPadding()
    {
        var a = new Solid(Pixel.White, 30, 10);
        var b = new Solid(Pixel.White, 20, 20);
        Layout(new Stack(Orientation.Vertical, gap: 2) { Padding = 1, Children = { a, b } });

        Assert.Equal(new Rectangle(1, 1, 30, 10), a.Bounds);
        Assert.Equal(new Rectangle(1, 13, 20, 20), b.Bounds);
    }

    [Fact]
    public void HorizontalStack_GrowSplitsLeftoverByWeight()
    {
        var fixedChild = new Solid(Pixel.White, 20, 8);
        var g1 = new Solid(Pixel.White, null, 8) { Grow = 1 };
        var g2 = new Solid(Pixel.White, null, 8) { Grow = 2 };
        Layout(new Stack(Orientation.Horizontal, gap: 2) { fixedChild, g1, g2 });

        // 100 - 20 fixed - 4 gaps = 76 shared 1:2 -> 25 and 51 (cumulative rounding keeps the total exact)
        Assert.Equal(20, fixedChild.Bounds.Width);
        Assert.Equal(25, g1.Bounds.Width);
        Assert.Equal(51, g2.Bounds.Width);
        Assert.Equal(22, g1.Bounds.X);
        Assert.Equal(49, g2.Bounds.X);
        Assert.Equal(100, g2.Bounds.Right);
    }

    [Fact]
    public void Stack_CrossAlignCentersAndStretches()
    {
        var narrow = new Solid(Pixel.White, 10, 10);
        var stretched = new Solid(Pixel.White, null, 10) { HAlign = Align.Stretch };
        Layout(new Stack(Orientation.Vertical) { CrossAlign = Align.Center, Children = { narrow, stretched } });

        Assert.Equal(45, narrow.Bounds.X);
        Assert.Equal(0, stretched.Bounds.X);
        Assert.Equal(100, stretched.Bounds.Width);
    }

    [Fact]
    public void Stack_InvisibleChildTakesNoSpace()
    {
        var a = new Solid(Pixel.White, 10, 10);
        var hidden = new Solid(Pixel.White, 10, 50) { Visible = false };
        var b = new Solid(Pixel.White, 10, 10);
        Layout(new Stack(Orientation.Vertical, gap: 1) { a, hidden, b });

        Assert.Equal(11, b.Bounds.Y);
    }

    [Fact]
    public void Grid_FixedAndStarTracks()
    {
        var a = new Solid(Pixel.White);
        var b = new Solid(Pixel.White);
        var c = new Solid(Pixel.White);
        var d = new Solid(Pixel.White);
        Layout(new Grid("10,*", "20,*,2*")
        {
            { a, 0, 0 },
            { b, 0, 1 },
            { c, 1, 2 },
            { d, 1, 0, 1, 2 },
        });

        // Columns: 20 fixed, then 80 shared 1:2 -> 27 and 53. Rows: 10 fixed, 50 star.
        Assert.Equal(new Rectangle(0, 0, 20, 10), a.Bounds);
        Assert.Equal(new Rectangle(20, 0, 27, 10), b.Bounds);
        Assert.Equal(new Rectangle(47, 10, 53, 50), c.Bounds);
        Assert.Equal(new Rectangle(0, 10, 47, 50), d.Bounds);
    }

    [Fact]
    public void Grid_AutoTrackSizesToContentAndGapsApply()
    {
        var wide = new Solid(Pixel.White, 17, 5);
        var rest = new Solid(Pixel.White);
        Layout(new Grid("*", "auto,*", columnGap: 3) { { wide, 0, 0 }, { rest, 0, 1 } });

        Assert.Equal(17, wide.Bounds.Width);
        Assert.Equal(new Rectangle(20, 0, 80, 60), rest.Bounds);
    }

    [Fact]
    public void Dock_EdgesThenFill()
    {
        var top = new Solid(Pixel.White, null, 10);
        var bottom = new Solid(Pixel.White, null, 8);
        var left = new Solid(Pixel.White, 20, null);
        var right = new Solid(Pixel.White, 15, null);
        var fill = new Solid(Pixel.White);
        Layout(new Dock { Top = top, Bottom = bottom, Left = left, Right = right, Fill = fill });

        Assert.Equal(new Rectangle(0, 0, 100, 10), top.Bounds);
        Assert.Equal(new Rectangle(0, 52, 100, 8), bottom.Bounds);
        Assert.Equal(new Rectangle(0, 10, 20, 42), left.Bounds);
        Assert.Equal(new Rectangle(85, 10, 15, 42), right.Bounds);
        Assert.Equal(new Rectangle(20, 10, 65, 42), fill.Bounds);
    }

    [Fact]
    public void Panel_AnchorsSingleChildWithMargin()
    {
        var child = new Solid(Pixel.White, 10, 6) { HAlign = Align.End, VAlign = Align.Center, Margin = 2 };
        Layout(new Panel { child });

        Assert.Equal(new Rectangle(100 - 2 - 10, 27, 10, 6), child.Bounds);
    }

    [Fact]
    public void RoadmapShape_NestedObjectInitializersWork()
    {
        var fill = new Solid(Pixel.White);
        var tail = new Solid(Pixel.White, 10, 8);
        Layout(new Dock
        {
            Fill = fill,
            Bottom = new Stack(Orientation.Horizontal, gap: 1)
            {
                new Solid(Pixel.White, 5, 8),
                new Solid(Pixel.White, null, 8) { Grow = 1 },
                tail,
            },
        });

        Assert.Equal(new Rectangle(0, 0, 100, 52), fill.Bounds);
        Assert.Equal(90, tail.Bounds.X);
    }

    [Fact]
    public void ClipChildren_StopsDrawingAtBounds()
    {
        var overflow = new Solid(Pixel.White, 20, 20) { Position = new Vector2(5, 5) };
        var stage = Layout(new Panel { ClipChildren = true, Width = 10, Height = 10, Children = { overflow } });
        var frame = stage.Render();

        Assert.Equal(Pixel.White, frame.GetPixel(9, 9));
        Assert.Equal(Pixel.Black, frame.GetPixel(10, 10));
        Assert.Equal(Pixel.Black, frame.GetPixel(12, 7));
    }

    [Fact]
    public void WithoutClipChildren_ChildrenDrawOutside()
    {
        var overflow = new Solid(Pixel.White, 20, 20) { Position = new Vector2(5, 5) };
        var frame = Layout(new Panel { Width = 10, Height = 10, Children = { overflow } }).Render();
        Assert.Equal(Pixel.White, frame.GetPixel(20, 20));
    }

    [Fact]
    public void Opacity_BlendsWithBackdrop()
    {
        var frame = Layout(new Panel { new Solid(Pixel.White, 10, 10) { Opacity = 0.5f } }).Render();
        Assert.Equal(new Pixel(128, 128, 128), frame.GetPixel(3, 3));
        Assert.Equal(Pixel.Black, frame.GetPixel(11, 3));
    }

    [Fact]
    public void Opacity_BlendsOverOtherNodes()
    {
        var root = new Panel
        {
            new Solid(new Pixel(200, 0, 0), 10, 10),
            new Solid(new Pixel(0, 0, 200), 10, 10) { Opacity = 0.25f },
        };
        var frame = Layout(root).Render();
        Assert.Equal(new Pixel(150, 0, 50), frame.GetPixel(4, 4));
    }

    [Fact]
    public void Opacity_MultipliesDownTheTree()
    {
        var root = new Panel
        {
            new Panel { Opacity = 0.5f, Width = 10, Height = 10, Children = { new Solid(Pixel.White, 10, 10) { Opacity = 0.5f } } },
        };
        var frame = Layout(root).Render();
        Assert.Equal(new Pixel(64, 64, 64), frame.GetPixel(2, 2));
    }

    [Fact]
    public void ZeroOpacity_DrawsNothing()
    {
        var frame = Layout(new Panel { new Solid(Pixel.White, 10, 10) { Opacity = 0f } }).Render();
        Assert.True(SnapshotHelper.IsBlank(frame));
    }

    [Fact]
    public void AnimateOpacityAndPosition_RunThroughHostAnimator()
    {
        var node = new Solid(Pixel.White, 10, 10);
        var stage = Layout(new Panel { node });

        node.AnimateOpacity(0f, 200.Ms());
        node.AnimatePosition(new Vector2(20, 0), 200.Ms());
        stage.Step(100);

        Assert.Equal(0.5f, node.Opacity, 0.01f);
        Assert.Equal(10f, node.Position.X, 0.01f);

        stage.Step(100);
        Assert.Equal(0f, node.Opacity);
        Assert.Equal(20f, node.Position.X);
    }

    [Fact]
    public void SettingPosition_StopsItsAnimation()
    {
        var node = new Solid(Pixel.White, 10, 10);
        var stage = Layout(new Panel { node });
        node.AnimatePosition(new Vector2(50, 0), 200.Ms());
        stage.Step(50);
        node.Position = new Vector2(3, 0);
        stage.Step(300);
        Assert.Equal(3f, node.Position.X);
    }

    [Fact]
    public void Animate_WithoutHostJumpsToTarget()
    {
        var node = new Solid(Pixel.White, 10, 10);
        bool done = false;
        node.AnimateOpacity(0.3f, 1.Seconds(), onComplete: () => done = true);
        Assert.Equal(0.3f, node.Opacity);
        Assert.True(done);
    }
}
