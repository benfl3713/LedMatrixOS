using BdfFontParser;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Core.Transitions;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.HomeAssistant;

/// <summary>One page of up to <see cref="TileBoard.PerPage"/> tiles. Compared by reference so the pager rebuilds only when the app hands it new pages.</summary>
internal sealed class TilePage(TileData[] tiles)
{
    public TileData[] Tiles { get; } = tiles;
}

/// <summary>
/// Up to four tiles across the panel per page, paged by the shared <see cref="Pager"/> with page dots overlaid.
/// Each tile is a <see cref="TileNode"/> (label, value or glyph, optional history sparkline, unit and a colour bar).
/// Pages are rebuilt only when <see cref="Tiles"/> is assigned, so steady-state drawing allocates nothing.
/// </summary>
internal sealed class TileBoard : Panel
{
    public const int PerPage = 4;

    private readonly Pager _pager;
    private IReadOnlyList<TilePage> _pages = [];

    public TileBoard()
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        _pager = new Pager(1, TimeSpan.FromSeconds(6), new SlideTransition(MoveDirection.Left) { Duration = TimeSpan.FromMilliseconds(500) }, Easing.InOutCubic)
            .Bind(() => _pages, BuildPage);
        Add(_pager);
        Add(new PageDots(_pager));
    }

    public int PageSeconds
    {
        get => (int)_pager.Interval.TotalSeconds;
        set => _pager.Interval = TimeSpan.FromSeconds(Math.Max(1, value));
    }

    public Pager Pager => _pager;

    public int PageCount => _pager.PageCount;

    public IReadOnlyList<TileData> Tiles
    {
        get => _tiles;
        set
        {
            _tiles = value;
            var pages = new List<TilePage>();
            for (int i = 0; i < value.Count; i += PerPage)
                pages.Add(new TilePage(value.Skip(i).Take(PerPage).ToArray()));
            _pages = pages;
        }
    }

    private IReadOnlyList<TileData> _tiles = [];

    private static Node BuildPage(TilePage page)
    {
        var grid = new Grid([GridLength.Star()], Enumerable.Repeat(GridLength.Star(), page.Tiles.Length).ToArray()) { HAlign = Align.Stretch, VAlign = Align.Stretch };
        for (int i = 0; i < page.Tiles.Length; i++)
            grid.Add(new TileNode(page.Tiles[i], divider: i > 0), 0, i);
        return grid;
    }
}

/// <summary>The dots at the bottom centre: one per page, the current one bright. Drawn above the pager so they do not slide.</summary>
internal sealed class PageDots : Node
{
    private static readonly Pixel Ink = new(225, 225, 225), Dim = new(50, 50, 60);
    private readonly Pager _pager;

    public PageDots(Pager pager)
    {
        _pager = pager;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int count = _pager.PageCount;
        if (count <= 1) return;
        int page = _pager.IsTransitioning ? (_pager.PageIndex + 1) % count : _pager.PageIndex;
        for (int p = 0; p < count; p++)
            frame.SetPixel(bounds.Right / 2 - count + p * 2, bounds.Bottom - 1, p == page ? Ink : Dim);
    }
}

/// <summary>A tile: the face (text, glyph, bar) plus an optional history <see cref="Sparkline"/> child.</summary>
internal sealed class TileNode : Panel
{
    public TileNode(TileData data, bool divider)
    {
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
        var series = data.Series;
        Add(new TileFace(data, divider));
        if (series is { Length: > 1 })
        {
            Add(new Sparkline
            {
                Values = series,
                Line = TileFace.ColorOf(TileKind.Number),
                FillBrightness = 0.2f,
                ShowLatest = true,
                HAlign = Align.Stretch,
                VAlign = Align.Stretch,
                Margin = new Thickness(6, TileFace.SparkTop, 6, TileFace.SparkBottomInset),
            });
        }
    }
}

/// <summary>Draws one tile's text, glyph and bar. Strings are rasterised once per width, never per frame.</summary>
internal sealed class TileFace : Node
{
    public const int SparkTop = 30, SparkBottomInset = 18;

    private static readonly Pixel Cyan = new(120, 220, 240), Green = new(96, 230, 130), Grey = new(120, 120, 130), Red = new(190, 70, 70), Ink = new(225, 225, 225), Amber = new(255, 190, 70);
    private static readonly BdfFont Huge = Fonts.Big.Scale(2);

    private readonly TileData _tile;
    private readonly bool _divider;
    private readonly TextRun _label = new(), _value = new(), _unit = new();
    private int _laidOutWidth = -1;

    public TileFace(TileData tile, bool divider)
    {
        _tile = tile;
        _divider = divider;
        HAlign = Align.Stretch;
        VAlign = Align.Stretch;
    }

    public static Pixel ColorOf(TileKind kind) => kind switch
    {
        TileKind.Number => Cyan,
        TileKind.On => Green,
        TileKind.Off => Grey,
        TileKind.Warm => Amber,
        TileKind.Unavailable => Red,
        _ => Ink,
    };

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        int tileW = bounds.Width;
        if (tileW != _laidOutWidth) Layout(tileW);

        var color = ColorOf(_tile.Kind);
        int x = bounds.X;
        bool spark = _tile.Series is { Length: > 1 };

        if (_divider) frame.Fill(new Rectangle(x, bounds.Y + 6, 1, bounds.Height - 12), new Pixel(28, 28, 36));

        _label.Draw(frame, x + (tileW - _label.Width) / 2, bounds.Y + 5, Ink.WithBrightness(0.8f));

        if (_tile.Glyph != Glyph.None)
        {
            var rows = Glyphs.Rows(_tile.Glyph);
            int w = Glyphs.Width * 2, h = rows.Length * 2;
            Glyphs.Draw(frame, rows, x + (tileW - w) / 2, bounds.Y + (bounds.Height - h) / 2 - 2, color);
        }
        else
        {
            int vy = spark ? bounds.Y + 14 : bounds.Y + (bounds.Height - _value.Height) / 2 - 2;
            _value.Draw(frame, x + (tileW - _value.Width) / 2, vy, color, shadow: true);
        }

        _unit.Draw(frame, x + (tileW - _unit.Width) / 2, bounds.Bottom - 12, Grey);
        frame.Fill(new Rectangle(x + tileW / 2 - 8, bounds.Bottom - 3, 16, 2), color.WithBrightness(0.6f));
    }

    private void Layout(int tileW)
    {
        _laidOutWidth = tileW;
        int room = tileW - 6;
        _label.Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(_tile.Label, room));
        var font = FitFont(_tile.Value, room);
        _value.Set(font, font.TruncateWithEllipsis(_tile.Value, room));
        _unit.Set(Fonts.QuiteSmall, Fonts.QuiteSmall.TruncateWithEllipsis(_tile.Unit, room));
    }

    private BdfFont FitFont(string value, int room)
    {
        bool spark = _tile.Series is { Length: > 1 };
        if (!spark && Huge.MeasureText(value) <= room) return Huge;
        if (Fonts.Big.MeasureText(value) <= room) return Fonts.Big;
        return Fonts.Small;
    }
}

/// <summary>Small pixel icons (drawn at 2x) for on/off entities. '#' is solid, 'o' a dimmer accent, '.' empty.</summary>
internal static class Glyphs
{
    public const int Width = 11;

    private static readonly string[] Bulb =
    [
        "...#####...",
        "..#######..",
        ".#########.",
        ".#########.",
        ".#########.",
        ".#########.",
        "..#######..",
        "...#####...",
        "....###....",
        "...ooooo...",
        "....ooo....",
        "....ooo....",
    ];

    private static readonly string[] Plug =
    [
        "..##...##..",
        "..##...##..",
        "..##...##..",
        ".#########.",
        ".#########.",
        ".#########.",
        ".#########.",
        "..#######..",
        "...#####...",
        "....ooo....",
        "....ooo....",
        "....ooo....",
    ];

    private static readonly string[] DoorClosed =
    [
        ".#########.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        ".#oooooo##.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        ".#ooooooo#.",
        "###########",
    ];

    private static readonly string[] DoorOpen =
    [
        ".#########.",
        ".#.......#.",
        ".#oo.....#.",
        ".#ooo....#.",
        ".#ooo....#.",
        ".#ooo....#.",
        ".#ooo....#.",
        ".#ooo....#.",
        ".#ooo....#.",
        ".#oo.....#.",
        ".#.......#.",
        "###########",
    ];

    private static readonly string[] Motion =
    [
        "...#####...",
        ".##.....##.",
        ".#..###..#.",
        "#..#####..#",
        "#.#######.#",
        "#.#######.#",
        "#..#####..#",
        ".#..###..#.",
        ".##.....##.",
        "...#####...",
    ];

    private static readonly string[] LockClosed =
    [
        "...#####...",
        "..##...##..",
        "..#.....#..",
        "..#.....#..",
        ".#########.",
        ".#########.",
        ".###...###.",
        ".###...###.",
        ".####.####.",
        ".####.####.",
        ".#########.",
        ".#########.",
    ];

    private static readonly string[] LockOpen =
    [
        "....#####..",
        "...##...##.",
        "...#.....#.",
        "...#.....#.",
        "...#.......",
        ".#########.",
        ".#########.",
        ".###...###.",
        ".###...###.",
        ".####.####.",
        ".#########.",
        ".#########.",
    ];

    public static string[] Rows(Glyph glyph) => glyph switch
    {
        Glyph.Bulb => Bulb,
        Glyph.Plug => Plug,
        Glyph.DoorClosed => DoorClosed,
        Glyph.DoorOpen => DoorOpen,
        Glyph.Motion => Motion,
        Glyph.LockClosed => LockClosed,
        Glyph.LockOpen => LockOpen,
        _ => [],
    };

    public static void Draw(FrameBuffer frame, string[] rows, int x, int y, Pixel color)
    {
        var accent = color.WithBrightness(0.45f);
        for (int r = 0; r < rows.Length; r++)
        {
            var row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                char ch = row[c];
                if (ch == '.') continue;
                frame.Fill(new Rectangle(x + c * 2, y + r * 2, 2, 2), ch == 'o' ? accent : color);
            }
        }
    }
}
