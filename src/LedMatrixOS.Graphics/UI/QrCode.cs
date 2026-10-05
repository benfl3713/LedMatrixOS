using LedMatrixOS.Core;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Graphics.UI;

/// <summary>
/// Draws a QR code for <see cref="Text"/> into a square, centred in its slot, with a quiet zone around it. The module matrix is encoded
/// once when <see cref="Text"/> or <see cref="Ecc"/> changes and cached, so painting allocates nothing. Modules are drawn at the largest
/// whole pixel scale that fits (a scanner needs square, uniform modules). <see cref="On"/> is the dark module colour, <see cref="Off"/>
/// the light one; most scanners want dark-on-light, so the defaults are black on white, but any pair with enough contrast works.
/// </summary>
public sealed class QrCode : Node
{
    private string _text = "";
    private QrEcc _ecc = QrEcc.M;
    private QrMatrix? _matrix;
    private int _quiet = 2;

    public QrCode(string text = "")
    {
        Text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text && (_matrix is not null || value.Length == 0)) return;
            _text = value;
            Rebuild();
        }
    }

    public QrEcc Ecc
    {
        get => _ecc;
        set
        {
            if (_ecc == value) return;
            _ecc = value;
            Rebuild();
        }
    }

    /// <summary>Dark module colour.</summary>
    public Pixel On { get; set; } = Pixel.Black;

    /// <summary>Light module and quiet zone colour.</summary>
    public Pixel Off { get; set; } = Pixel.White;

    /// <summary>Quiet zone width in modules (the spec asks for 4; 2 still scans on a lit display and saves space).</summary>
    public int QuietZone
    {
        get => _quiet;
        set => SetLayout(ref _quiet, Math.Clamp(value, 0, 8));
    }

    /// <summary>The encoded matrix, or null when <see cref="Text"/> is empty or too long for version 10.</summary>
    public QrMatrix? Matrix => _matrix;

    /// <summary>Modules per side including the quiet zone, or 0 when there is nothing to show.</summary>
    public int TotalModules => _matrix is null ? 0 : _matrix.Size + 2 * _quiet;

    /// <summary>Pixels per module the code will be drawn at in the given square, or 0 if it does not fit at 1.</summary>
    public int ScaleFor(int side) => TotalModules == 0 ? 0 : side / TotalModules;

    private void Rebuild()
    {
        _matrix = _text.Length > 0 && QrMatrix.TryEncode(_text, _ecc, out var m) ? m : null;
        InvalidateLayout();
    }

    protected override Size MeasureCore(int availW, int availH)
    {
        int side = Math.Min(availW, availH);
        int total = TotalModules;
        if (total > 0 && side >= total) side = side / total * total;   // snap to a whole scale so the slot hugs the code
        return new Size(side + Padding.Horizontal, side + Padding.Vertical);
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        var area = ContentOf(bounds);
        int side = Math.Min(area.Width, area.Height);
        if (_matrix is not { } m) return;

        int total = m.Size + 2 * _quiet;
        int scale = side / total;
        if (scale < 1) return;

        int px = total * scale;
        int ox = area.X + (area.Width - px) / 2;
        int oy = area.Y + (area.Height - px) / 2;
        frame.Fill(new Rectangle(ox, oy, px, px), Off);

        for (int y = 0; y < m.Size; y++)
        {
            int x = 0;
            while (x < m.Size)
            {
                if (!m[x, y]) { x++; continue; }
                int run = 1;
                while (x + run < m.Size && m[x + run, y]) run++;
                frame.Fill(new Rectangle(ox + (x + _quiet) * scale, oy + (y + _quiet) * scale, run * scale, scale), On);
                x += run;
            }
        }
    }
}
