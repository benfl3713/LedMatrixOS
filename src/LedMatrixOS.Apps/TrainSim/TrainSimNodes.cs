using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.TrainSim;

/// <summary>Colours and text styles of the Train Sim display. Created in <c>Build</c>, once the fonts are loaded.</summary>
internal sealed class TrainSimStyles
{
    public readonly TextStyle Detail = new(Fonts.Small, TrainSimBoardModel.Ink, Shadow: false);
    public readonly TextStyle Distance = new(Fonts.Big, TrainSimBoardModel.Ink, Shadow: false);
    public readonly TextStyle Platform = new(Fonts.Small, TrainSimBoardModel.Amber, Shadow: false);
    public readonly TextStyle StopDistance = new(Fonts.Small, new Pixel(150, 190, 255), Shadow: false);
    public readonly TextStyle StopDwell = new(Fonts.Small, TrainSimBoardModel.Amber, Shadow: false);
    public readonly TextStyle Muted = new(Fonts.QuiteSmall, TrainSimBoardModel.Dim, Shadow: false);
    public readonly TextStyle Footer = new(Fonts.QuiteSmall, new Pixel(190, 190, 200), Shadow: false);
    public readonly TextStyle Clock = new(Fonts.QuiteSmall, Pixel.White, Shadow: false);
    public readonly TextStyle CardTitle = new(Fonts.Big, Pixel.White, Shadow: false);
    public readonly TextStyle CardHint = new(Fonts.Small, new Pixel(190, 190, 200), Shadow: false);
    public readonly TextStyle CardHint2 = new(Fonts.QuiteSmall, TrainSimBoardModel.Dim, Shadow: false);

    private readonly TextStyle[] _aspectWord = new TextStyle[5];

    public TrainSimStyles()
    {
        for (int i = 0; i < _aspectWord.Length; i++) _aspectWord[i] = new TextStyle(Fonts.QuiteSmall, AspectColors.Lit((SpeedAspect)i), Shadow: false);
    }

    public TextStyle AspectWord(SpeedAspect a) => _aspectWord[(int)a];
}

internal static class AspectColors
{
    public static readonly Pixel Red = new(255, 50, 45), Yellow = new(255, 200, 20), Green = new(60, 235, 100), Grey = new(110, 115, 130);

    public static Pixel Lit(SpeedAspect a) => a switch
    {
        SpeedAspect.Stop => Red,
        SpeedAspect.Yellow or SpeedAspect.DoubleYellow => Yellow,
        SpeedAspect.Green => Green,
        _ => Grey,
    };
}

/// <summary>The current speed, big: the 9x18 font drawn at twice the size, right aligned.</summary>
internal sealed class SpeedHeroNode(TrainSimBoardModel model) : Node
{
    public const int Scale = 2;
    private readonly TextRun _run = new();

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        _run.Set(Fonts.Big, model.SpeedText);
        int x = bounds.Right - _run.Width * Scale;
        int y = bounds.Y + (bounds.Height - _run.Height * Scale) / 2;
        var color = model.SpeedColor;
        for (int ix = 0; ix < _run.Width; ix++)
            for (int iy = 0; iy < _run.Height; iy++)
                if (_run.Ink(ix, iy)) frame.Fill(new Rectangle(x + ix * Scale, y + iy * Scale, Scale, Scale), color);
    }
}

/// <summary>A UK style limit sign: a red ring, white face and the black limit. Just a grey ring when no limit is known.</summary>
internal sealed class LimitSignNode(TrainSimBoardModel model) : Node
{
    private readonly TextRun _run = new();

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int r = Math.Min(bounds.Width, bounds.Height) / 2;
        int cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
        string text = model.LimitText;
        if (text.Length == 0)
        {
            frame.FillCircle(cx, cy, r, new Pixel(50, 54, 66));
            frame.FillCircle(cx, cy, r - 4, Pixel.Black);
            return;
        }

        frame.FillCircle(cx, cy, r, new Pixel(235, 30, 35));
        frame.FillCircle(cx, cy, r - 4, Pixel.White);
        var font = text.Length > 2 ? Fonts.QuiteSmall : Fonts.Small;
        _run.Set(font, text);
        int ty = font == Fonts.Small ? cy - 6 : cy - 3;
        _run.Draw(frame, cx - _run.Width / 2, ty, Pixel.Black);
    }
}

/// <summary>Speed against the limit: a thin bar with a white tick at the limit.</summary>
internal sealed class SpeedBarNode(TrainSimBoardModel model) : Node
{
    private static readonly Pixel Back = new(26, 30, 44);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.Fill(bounds, Back);
        double speed = model.SpeedMs;
        double scale = model.LimitMs is { } l and > 0 ? Math.Max(l * 1.25, speed * 1.02) : Math.Max(speed * 1.2, 30);
        int fill = (int)Math.Round(Math.Clamp(speed / scale, 0, 1) * bounds.Width);
        if (fill > 0) frame.Fill(new Rectangle(bounds.X, bounds.Y, fill, bounds.Height), model.SpeedColor);
        if (model.LimitMs is { } limit and > 0)
        {
            int x = bounds.X + Math.Min(bounds.Width - 1, (int)Math.Round(limit / scale * bounds.Width));
            frame.Fill(new Rectangle(x, bounds.Y - 1, 1, bounds.Height + 2), Pixel.White);
        }
    }
}

/// <summary>A centre-zero bar: brake grows left in red, power grows right in green.</summary>
internal sealed class PowerBrakeNode(TrainSimBoardModel model) : Node
{
    private static readonly Pixel Back = new(26, 30, 44), Power = new(70, 225, 105), Brake = new(240, 70, 55), Centre = new(120, 130, 150);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.Fill(bounds, Back);
        int half = bounds.Width / 2, cx = bounds.X + half;
        int p = (int)Math.Round(model.Power * (half - 1)), b = (int)Math.Round(model.Brake * (half - 1));
        if (p > 0) frame.Fill(new Rectangle(cx + 1, bounds.Y, p, bounds.Height), Power);
        if (b > 0) frame.Fill(new Rectangle(cx - b, bounds.Y, b, bounds.Height), Brake);
        frame.Fill(new Rectangle(cx, bounds.Y - 1, 1, bounds.Height + 2), Centre);
    }
}

/// <summary>A signal head with two lamp slots: red and green light the lower one, a single yellow the lower one, a double yellow both.</summary>
internal sealed class SignalLampNode(TrainSimBoardModel model) : Node
{
    private static readonly Pixel Housing = new(30, 33, 46), Unlit = new(18, 20, 28), Rim = new(70, 76, 96);

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.FillRoundedRect(bounds, 4, Housing);
        int cx = bounds.X + bounds.Width / 2;
        int top = bounds.Y + 6, bottom = bounds.Bottom - 6;
        var aspect = model.HasSignal ? model.Aspect : SpeedAspect.Unknown;
        var lit = AspectColors.Lit(aspect);
        bool upper = aspect == SpeedAspect.DoubleYellow;
        Lamp(frame, cx, top, upper ? lit : Unlit, upper);
        Lamp(frame, cx, bottom, lit, true);
    }

    private static void Lamp(FrameBuffer frame, int cx, int cy, Pixel color, bool lit)
    {
        frame.FillCircle(cx, cy, 5, color);
        if (lit) frame.SetPixel(cx - 2, cy - 2, Pixel.White.WithBrightness(0.55f));
        else frame.DrawCircle(cx, cy, 5, Rim);
    }
}

/// <summary>A down (limit drops) or up (limit rises) arrow in the colour of the next limit change.</summary>
internal sealed class LimitArrowNode(TrainSimBoardModel model) : Node
{
    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int dir = model.NextLimitDirection;
        if (dir == 0) return;
        var c = model.NextLimitColor;
        int cx = bounds.X + bounds.Width / 2, top = bounds.Y, bottom = bounds.Bottom - 1;
        int half = Math.Min(3, bounds.Width / 2);
        frame.DrawLine(cx, top, cx, bottom, c);
        if (dir > 0)
        {
            frame.DrawLine(cx, top, cx - half, top + half, c);
            frame.DrawLine(cx, top, cx + half, top + half, c);
        }
        else
        {
            frame.DrawLine(cx, bottom, cx - half, bottom - half, c);
            frame.DrawLine(cx, bottom, cx + half, bottom - half, c);
        }
    }
}

/// <summary>A thin bar in the card's accent colour.</summary>
internal sealed class AccentBarNode(TrainSimBoardModel model) : Node
{
    protected override void OnRender(FrameBuffer frame, Rectangle bounds) => frame.Fill(bounds, model.Accent);
}

internal static class TrainSimNodeExtensions
{
    /// <summary>Pins a node to an exact rectangle inside a <see cref="Panel"/>.</summary>
    public static T At<T>(this T node, int x, int y, int width, int height) where T : Node
    {
        node.HAlign = Align.Start;
        node.VAlign = Align.Start;
        node.Margin = new Thickness(x, y, 0, 0);
        node.Width = width;
        node.Height = height;
        return node;
    }
}
