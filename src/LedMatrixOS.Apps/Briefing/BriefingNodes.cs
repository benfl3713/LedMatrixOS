using System.Numerics;
using LedMatrixOS.Apps.BinDay;
using LedMatrixOS.Apps.Tube;
using LedMatrixOS.Apps.Weather;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Briefing;

/// <summary>Where the progress strip is: how many cards, which one is showing and how far through its time it is.</summary>
internal sealed class BriefingProgress
{
    public int Count = 1, Index;
    public float Fraction;
    public Pixel Accent = new(255, 190, 40);
}

/// <summary>A row of segments along the bottom, one per card: finished ones dim, the current one filling over its time.</summary>
internal sealed class ProgressStrip : Node
{
    private readonly BriefingProgress _state;

    public ProgressStrip(BriefingProgress state)
    {
        _state = state;
        Height = 7;
        HAlign = Align.Stretch;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        frame.Fill(bounds, new Pixel(8, 8, 12));
        int count = Math.Max(1, _state.Count);
        const int margin = 10, gap = 3, h = 3;
        int total = bounds.Width - margin * 2 - gap * (count - 1);
        if (total < count) return;

        int y = bounds.Y + 2;
        var accent = _state.Accent;
        for (int i = 0; i < count; i++)
        {
            int x0 = bounds.X + margin + i * total / count + i * gap;
            int x1 = bounds.X + margin + (i + 1) * total / count + i * gap;
            int w = Math.Max(1, x1 - x0);

            if (i < _state.Index) frame.Fill(new Rectangle(x0, y, w, h), accent.WithBrightness(0.55f));
            else if (i == _state.Index)
            {
                frame.Fill(new Rectangle(x0, y, w, h), accent.WithBrightness(0.22f));
                int filled = (int)MathF.Round(w * Math.Clamp(_state.Fraction, 0f, 1f));
                if (filled > 0) frame.Fill(new Rectangle(x0, y, filled, h), accent);
            }
            else frame.Fill(new Rectangle(x0, y, w, h), new Pixel(36, 38, 48));
        }
    }
}

internal static class BriefingNodes
{
    public static readonly Pixel GreetingAccent = new(255, 190, 40), WeatherAccent = new(70, 170, 235), CalendarAccent = new(110, 210, 140),
        CommuteAccent = new(255, 130, 70), BinsAccent = new(90, 200, 120), SignOffAccent = new(180, 130, 255);

    private static readonly Pixel Text = new(235, 240, 248), Muted = new(150, 165, 190);

    public static Pixel AccentOf(BriefingPage page) => page switch
    {
        BriefingPage.Greeting => GreetingAccent,
        BriefingPage.Weather => WeatherAccent,
        BriefingPage.Calendar => CalendarAccent,
        BriefingPage.Commute => CommuteAccent,
        BriefingPage.Bins => BinsAccent,
        _ => SignOffAccent,
    };

    private static string TagOf(BriefingPage page) => page switch
    {
        BriefingPage.Weather => "WEATHER",
        BriefingPage.Calendar => "TODAY",
        BriefingPage.Commute => "COMMUTE",
        BriefingPage.Bins => "BINS",
        _ => "",
    };

    private static TextStyle Big(Pixel c) => new(Fonts.Big, c, true, new Pixel(0, 0, 0));
    private static TextStyle Small(Pixel c) => new(Fonts.Small, c, true, new Pixel(0, 0, 0));
    private static TextStyle Tiny(Pixel c) => new(Fonts.QuiteSmall, c, false);

    /// <summary>One card: dark tinted background, a coloured band on the left, and the content that slides in once the card has arrived.</summary>
    public static Node Card(BriefingPage page, BriefingModel m, Animator animator)
    {
        var accent = AccentOf(page);
        var content = new Panel { HAlign = Align.Stretch, VAlign = Align.Stretch, Margin = new Thickness(12, 0, 8, 0) };

        switch (page)
        {
            case BriefingPage.Greeting: Greeting(content, m, accent); break;
            case BriefingPage.Weather: WeatherCard(content, m, accent); break;
            case BriefingPage.Calendar: CalendarCard(content, m, accent); break;
            case BriefingPage.Commute: CommuteCard(content, m); break;
            case BriefingPage.Bins: BinsCard(content, m); break;
            default: SignOff(content, accent); break;
        }

        var band = new Block(accent) { Width = 6, HAlign = Align.Start, VAlign = Align.Stretch };
        var tag = TagOf(page);
        if (tag.Length > 0)
            content.Add(new Label(tag) { Style = Tiny(accent), HAlign = Align.End, VAlign = Align.Start, Margin = new Thickness(0, 2, 0, 0) });

        // Entrance: the band wipes in from the left edge, the content follows a beat later while the slide transition settles.
        Motion.SlideIn(band, animator, new Vector2(-8, 0), TimeSpan.Zero, 300.Ms());
        Motion.SlideIn(content, animator, new Vector2(20, 0), 220.Ms(), 420.Ms());

        return new Panel
        {
            HAlign = Align.Stretch,
            VAlign = Align.Stretch,
            Children = { new Block(accent.WithBrightness(0.1f)), band, content },
        };
    }

    private static Node Left(Node child)
    {
        child.HAlign = Align.Start;
        child.VAlign = Align.Center;
        return child;
    }

    private static void Greeting(Panel p, BriefingModel m, Pixel accent)
    {
        var greeting = new Label(() => m.GreetingText) { Style = Big(accent) };
        p.Add(Left(new Stack(Orientation.Vertical, gap: 3)
        {
            Children =
            {
                greeting,
                new Label(() => m.DateText) { Style = Small(Text) },
            },
        }));
        var bigStyle = Big(accent);
        var smallStyle = Small(accent);
        p.Add(new Updater { Tick = () => { var want = m.GreetingSmall ? smallStyle : bigStyle; if (!ReferenceEquals(greeting.Style, want)) greeting.Style = want; } });

        var right = new Stack(Orientation.Horizontal, gap: 6) { HAlign = Align.End, VAlign = Align.Center, CrossAlign = Align.Center };
        right.Add(new WeatherGlyph(() => (m.WeatherKind, m.WeatherDay), 28));
        right.Add(new Label(() => m.TempText) { Style = Big(Text) });
        p.Add(right);
        p.Add(new Updater { Tick = () => { if (right.Visible != m.HasWeather) right.Visible = m.HasWeather; } });
    }

    private static void WeatherCard(Panel p, BriefingModel m, Pixel accent)
    {
        var row = new Stack(Orientation.Horizontal, gap: 8) { HAlign = Align.Start, VAlign = Align.Center, CrossAlign = Align.Center };
        row.Add(new WeatherGlyph(() => (m.WeatherKind, m.WeatherDay), 26));
        row.Add(new Stack(Orientation.Vertical, gap: 1)
        {
            Children =
            {
                new Label(() => m.HighLowText) { Style = Big(Text) },
                new Label(() => m.WeatherDescText) { Style = Small(Muted) },
                new Label(() => m.RainText) { Style = Small(accent) },
            },
        });
        p.Add(row);

        var chart = new Stack(Orientation.Vertical, gap: 2) { HAlign = Align.End, VAlign = Align.Center, Width = 92 };
        chart.Add(new Label("RAIN NEXT 12H") { Style = Tiny(Muted) });
        chart.Add(new Sparkline { Source = () => m.RainValues, Min = 0, Max = 100, Line = accent, FillBrightness = 0.3f, Height = 28, VAlign = Align.Start });
        p.Add(chart);
    }

    private static void CalendarCard(Panel p, BriefingModel m, Pixel accent)
    {
        var rows = new Stack(Orientation.Vertical, gap: 4) { HAlign = Align.Stretch, VAlign = Align.Center };
        for (int i = 0; i < BriefingModel.MaxEvents; i++)
        {
            int k = i;
            rows.Add(new Stack(Orientation.Horizontal, gap: 6)
            {
                Height = 13,
                CrossAlign = Align.Center,
                Children =
                {
                    new Label(() => m.EventTimes[k]) { Style = Small(accent), Width = 44 },
                    new MarqueeLabel(() => m.EventTitles[k]) { Style = Small(Text), Width = 170 },
                },
            });
        }
        p.Add(rows);
        p.Add(new Label(() => m.NothingText) { Style = Big(Text), HAlign = Align.Start, VAlign = Align.Center });
    }

    private static void CommuteCard(Panel p, BriefingModel m)
    {
        var col = new Stack(Orientation.Vertical, gap: 4) { HAlign = Align.Stretch, VAlign = Align.Center };
        col.Add(new Label(() => m.LeaveText) { Style = new TextStyle(Fonts.Big, Text, true, new Pixel(0, 0, 0)) });
        col.Add(new MarqueeLabel(() => m.TrainText) { Style = Small(Muted), Width = 220 });

        var pill = new Pill("", Muted) { Style = new TextStyle(Fonts.QuiteSmall, new Pixel(8, 10, 16), false), Height = 10 };
        var lineRow = new Stack(Orientation.Horizontal, gap: 5)
        {
            CrossAlign = Align.Center,
            Children = { pill, new MarqueeLabel(() => m.LineText) { Style = Small(Text), Width = 150 } },
        };
        col.Add(lineRow);
        p.Add(col);
        p.Add(new Updater { Tick = () => { pill.Text = m.LineName; pill.Background = m.LineColor; lineRow.Visible = m.HasLineStatus; } });
    }

    private static void BinsCard(Panel p, BriefingModel m)
    {
        var icons = new Stack(Orientation.Horizontal, gap: 4) { HAlign = Align.Start, VAlign = Align.Center };
        for (int i = 0; i < m.BinCount; i++)
            icons.Add(new BinIcon(mini: false, phaseSeconds: i * 0.7f) { Colour = m.BinColors[i] });
        p.Add(icons);

        int iconsWidth = m.BinCount * (BinIcon.FullWidth + 4);
        var text = new Stack(Orientation.Vertical, gap: 3)
        {
            HAlign = Align.Stretch,
            VAlign = Align.Center,
            Margin = new Thickness(iconsWidth + 6, 0, 0, 0),
            Children =
            {
                new Label(() => m.BinHeadline) { Style = Big(Text) },
                new MarqueeLabel(() => m.BinNames) { Style = Small(BinStyles.Amber), HAlign = Align.Stretch },
                new MarqueeLabel(() => m.BinSecond) { Style = Small(Muted), HAlign = Align.Stretch },
            },
        };
        p.Add(text);
    }

    private static void SignOff(Panel p, Pixel accent)
    {
        p.Add(new Label("Have a great day") { Style = Big(accent), HAlign = Align.Center, VAlign = Align.Center });
    }

    /// <summary>A node with no size that runs a callback once per frame (to push model values into widgets whose properties are not bindable).</summary>
    private sealed class Updater : Node
    {
        public Action? Tick { get; init; }

        public override void Update(FrameContext ctx)
        {
            base.Update(ctx);
            Tick?.Invoke();
        }
    }
}
