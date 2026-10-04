using LedMatrixOS.Apps.Clocks;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps;

// Flip style: a retro split-flap clock. Big charcoal flip cards with cream numerals show hours and minutes, smaller cards show
// the seconds, and an AM/PM flap and weekday / day / month flaps sit alongside. Every change is a physically convincing flip:
// the top flap falls forward and darkens, the new bottom flap swings down with a little bounce, and a shadow sweeps the lower half.
public sealed partial class ClockApp
{
    private readonly List<FlipCard> _cards = new();

    private void FlipSettingChanged(string key)
    {
        switch (key)
        {
            case "textColor":
            case "backgroundColor":
                var style = FlipStyle.From(BackgroundColor, TextColor);
                foreach (var c in _cards) c.Style = style;
                break;
            case "show24Hour":
                if (ShowAmPm) Host.Root = Build();
                break;
            case "showSeconds":
            case "showDate":
            case "showAmPm":
                Host.Root = Build();
                break;
        }
    }

    private Node BuildFlip()
    {
        _cards.Clear();
        _state = new ClockState(Time);
        _state.Refresh();
        var st = _state;
        var style = FlipStyle.From(BackgroundColor, TextColor);

        const int bigW = 34, bigH = 56, secW = 22, secH = 36, dateH = 14;
        var bigDigits = FaceSet.Digits(GlyphAtlas.Get(22, 38, 6f, 0), bigW, bigH);
        var secDigits = FaceSet.Digits(GlyphAtlas.Get(13, 24, 3.8f, 0), secW, secH);

        int order = 0;
        FlipCard Card(FaceSet faces, Func<int> src)
        {
            var c = new FlipCard(faces, src, style);
            c.StartEntrance(TimeSpan.FromMilliseconds(150 + order++ * 90));
            _cards.Add(c);
            return c;
        }

        bool ampm = !Show24Hour && ShowAmPm;
        bool sidebar = ShowSeconds || ShowDate || ampm;
        int groupW = 4 * bigW + 4 + 12;
        int sideW = 66;
        int total = groupW + (sidebar ? 6 + sideW : 0);
        int left = (256 - total) / 2;

        var main = new Stack(Orientation.Horizontal, gap: 2)
        {
            Margin = new Thickness(left, 4, 0, 0),
            HAlign = Align.Start,
            CrossAlign = Align.Center,
            Height = bigH + 3,
        };
        main.Add(Card(bigDigits, () => Show24Hour ? st.Hour / 10 : (st.Hour12 >= 10 ? 1 : 10)));
        main.Add(Card(bigDigits, () => (Show24Hour ? st.Hour : st.Hour12) % 10));
        main.Add(new ColonNode(st, () => style.Glyph, bigH, 2.4f, pad: 2) { Pulse = 0.8f, Width = 12 });
        main.Add(Card(bigDigits, () => st.Minute / 10));
        main.Add(Card(bigDigits, () => st.Minute % 10));

        var root = new Panel
        {
            new ClockStateNode(_state),
            new FlipBackdrop(),
            main,
        };

        if (sidebar)
        {
            int sx = left + groupW + 6;
            int y = 4;
            if (ShowSeconds)
            {
                var secs = new Stack(Orientation.Horizontal, gap: 2)
                {
                    Margin = new Thickness(sx, y, 0, 0),
                    HAlign = Align.Start,
                    VAlign = Align.Start,
                };
                secs.Add(Card(secDigits, () => st.Second / 10));
                secs.Add(Card(secDigits, () => st.Second % 10));
                root.Add(secs);
                if (ampm)
                {
                    var ap = new FlipCard(FaceSet.Texts(["AM", "PM"], Fonts.Small, 18, dateH), () => st.IsPm ? 1 : 0, style);
                    ap.StartEntrance(TimeSpan.FromMilliseconds(150 + order++ * 90));
                    _cards.Add(ap);
                    ap.Margin = new Thickness(sx + 2 * secW + 4, y, 0, 0);
                    ap.HAlign = Align.Start;
                    ap.VAlign = Align.Start;
                    root.Add(ap);
                }
                y += secH + 6;
            }
            else if (ampm)
            {
                var ap = new FlipCard(FaceSet.Texts(["AM", "PM"], Fonts.Small, 22, dateH), () => st.IsPm ? 1 : 0, style);
                ap.StartEntrance(TimeSpan.FromMilliseconds(150 + order++ * 90));
                _cards.Add(ap);
                ap.Margin = new Thickness(sx, 14, 0, 0);
                ap.HAlign = Align.Start;
                ap.VAlign = Align.Start;
                root.Add(ap);
            }

            if (ShowDate)
            {
                int dy = ShowSeconds ? 64 - dateH - 3 - 1 : 32 + (ampm ? 4 : -dateH / 2);
                var days = FaceSet.Texts(ClockState.DayShort, Fonts.Small, 22, dateH);
                var nums = FaceSet.Texts(Enumerable.Range(1, 31).Select(i => ClockState.TwoDigits(i)).ToArray(), Fonts.Small, 16, dateH);
                var months = FaceSet.Texts(ClockState.MonthShort, Fonts.Small, 22, dateH);
                var row = new Stack(Orientation.Horizontal, gap: 2)
                {
                    Margin = new Thickness(sx, dy, 0, 0),
                    HAlign = Align.Start,
                    VAlign = Align.Start,
                };
                row.Add(Card(days, () => (int)st.DayOfWeek));
                row.Add(Card(nums, () => st.Day - 1));
                row.Add(Card(months, () => st.Month - 1));
                root.Add(row);
            }
        }

        return root;
    }
}
