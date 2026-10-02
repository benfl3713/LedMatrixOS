using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.HomeAssistant;

/// <summary>
/// Up to four tiles across the panel per page (a label, a big value and a unit), paging through the list with a short fade.
/// Strings are rasterised only when the visible page or its data changes, so steady-state drawing allocates nothing.
/// </summary>
internal sealed class TileBoard : Node
{
    public const int PerPage = 4;
    private const double Fade = 0.3;

    private static readonly Pixel Cyan = new(120, 220, 240), Green = new(96, 230, 130), Grey = new(120, 120, 130), Red = new(190, 70, 70), Ink = new(225, 225, 225);

    private readonly BdfFont _huge = Fonts.Big.Scale(2);
    private readonly TextRun[] _labels = new TextRun[PerPage], _values = new TextRun[PerPage], _units = new TextRun[PerPage];
    private IReadOnlyList<TileData> _tiles = [];
    private TimeSpan _time;
    private int _laidOutPage = -1, _laidOutWidth = -1;

    public TileBoard()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        for (int i = 0; i < PerPage; i++) { _labels[i] = new(); _values[i] = new(); _units[i] = new(); }
    }

    public int PageSeconds { get; set; } = 6;

    public IReadOnlyList<TileData> Tiles
    {
        get => _tiles;
        set { _tiles = value; _laidOutPage = -1; }
    }

    public int PageCount => Math.Max(1, (_tiles.Count + PerPage - 1) / PerPage);

    public int PageAt(TimeSpan time) => PageCount == 1 ? 0 : (int)(time.TotalSeconds / Math.Max(1, PageSeconds)) % PageCount;

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int page = PageAt(_time);
        int first = page * PerPage;
        int count = Math.Min(PerPage, _tiles.Count - first);
        if (count <= 0) return;

        double span = Math.Max(1, PageSeconds);
        double into = PageCount == 1 ? span : _time.TotalSeconds % span;
        float level = PageCount == 1 ? 1f : (float)Math.Clamp(Math.Min(into, span - into) / Fade, 0, 1);

        int tileW = bounds.Width / count;
        if (page != _laidOutPage || tileW != _laidOutWidth) Layout(first, count, tileW, page);

        for (int i = 0; i < count; i++)
        {
            var tile = _tiles[first + i];
            int x = bounds.X + i * tileW;
            var color = ColorOf(tile.Kind).WithBrightness(level);
            if (i > 0) frame.Fill(new Rectangle(x, bounds.Y + 6, 1, bounds.Height - 12), new Pixel(28, 28, 36));

            _labels[i].Draw(frame, x + (tileW - _labels[i].Width) / 2, bounds.Y + 5, Ink.WithBrightness(level * 0.8f));
            _values[i].Draw(frame, x + (tileW - _values[i].Width) / 2, bounds.Y + (bounds.Height - _values[i].Height) / 2 - 2, color, shadow: true);
            _units[i].Draw(frame, x + (tileW - _units[i].Width) / 2, bounds.Bottom - 12, Grey.WithBrightness(level));
            frame.Fill(new Rectangle(x + tileW / 2 - 8, bounds.Bottom - 3, 16, 2), color.WithBrightness(0.6f));
        }

        if (PageCount > 1)
            for (int p = 0; p < PageCount; p++)
                frame.SetPixel(bounds.Right / 2 - PageCount + p * 2, bounds.Bottom - 1, p == page ? Ink : new Pixel(50, 50, 60));
    }

    private void Layout(int first, int count, int tileW, int page)
    {
        _laidOutPage = page;
        _laidOutWidth = tileW;
        for (int i = 0; i < count; i++)
        {
            var tile = _tiles[first + i];
            int room = tileW - 6;
            _labels[i].Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(tile.Label, room));
            var font = FitFont(tile.Value, room);
            _values[i].Set(font, font.TruncateWithEllipsis(tile.Value, room));
            _units[i].Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(tile.Unit, room));
        }
    }

    private BdfFont FitFont(string value, int room)
    {
        if (_huge.MeasureText(value) <= room) return _huge;
        if (Fonts.Big.MeasureText(value) <= room) return Fonts.Big;
        return Fonts.Small;
    }

    private static Pixel ColorOf(TileKind kind) => kind switch
    {
        TileKind.Number => Cyan,
        TileKind.On => Green,
        TileKind.Off => Grey,
        TileKind.Unavailable => Red,
        _ => Ink,
    };
}
