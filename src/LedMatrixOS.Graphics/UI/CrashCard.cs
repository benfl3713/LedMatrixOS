using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>Full-screen "app crashed" card: a red title bar and the exception text, word-wrapped. Wrapping is cached per message.</summary>
public sealed class CrashCard
{
    private readonly TextRun _title = new();
    private readonly List<TextRun> _lines = new();
    private CrashInfo? _shown;
    private int _width;

    public void Render(FrameBuffer frame, CrashInfo crash)
    {
        if (!Equals(crash, _shown) || frame.Width != _width)
        {
            _shown = crash;
            _width = frame.Width;
            Layout(crash, frame.Width);
        }

        var red = new Pixel(190, 30, 30);
        frame.Fill(new Rectangle(0, 0, frame.Width, 9), red);
        _title.Draw(frame, 2, 1, Pixel.White);

        int rowHeight = Fonts.ExtraSmall.BoundingBox.Y + 1;
        int y = 11;
        foreach (var line in _lines)
        {
            if (y + rowHeight > frame.Height) break;
            line.Draw(frame, 2, y, new Pixel(235, 200, 200));
            y += rowHeight;
        }
    }

    private void Layout(CrashInfo crash, int width)
    {
        BdfFont font = Fonts.ExtraSmall;
        _title.Set(Fonts.QuiteSmall, font == null ? "" : font.TruncateWithEllipsis($"{crash.AppName.ToUpperInvariant()} CRASHED", width - 4));
        _lines.Clear();

        int max = width - 4;
        var current = "";
        foreach (var word in crash.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= max) { current = candidate; continue; }
            if (current.Length > 0) AddLine(font, current);
            current = font.MeasureText(word) <= max ? word : font.TruncateWithEllipsis(word, max);
        }
        if (current.Length > 0) AddLine(font, current);
    }

    private void AddLine(BdfFont font, string text)
    {
        var run = new TextRun();
        run.Set(font, text);
        _lines.Add(run);
    }
}
