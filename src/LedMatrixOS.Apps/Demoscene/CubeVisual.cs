using LedMatrixOS.Apps.Visuals;
using LedMatrixOS.Core;

namespace LedMatrixOS.Apps.Demoscene;

/// <summary>Three tumbling cubes: back-face culled, depth sorted, flat-shaded filled faces with anti-aliased wireframe edges over a dot grid.</summary>
internal sealed class CubeVisual : IDemoVisual
{
    private static readonly float[] Vx = [-1, 1, 1, -1, -1, 1, 1, -1];
    private static readonly float[] Vy = [-1, -1, 1, 1, -1, -1, 1, 1];
    private static readonly float[] Vz = [-1, -1, -1, -1, 1, 1, 1, 1];

    // quads wound counter-clockwise seen from outside; normals match
    private static readonly int[] Faces =
    [
        0, 3, 2, 1,   // front (-z)
        4, 5, 6, 7,   // back (+z)
        0, 4, 7, 3,   // left
        1, 2, 6, 5,   // right
        0, 1, 5, 4,   // top (-y)
        3, 7, 6, 2,   // bottom (+y)
    ];

    private static readonly float[] Nx = [0, 0, -1, 1, 0, 0];
    private static readonly float[] Ny = [0, 0, 0, 0, -1, 1];
    private static readonly float[] Nz = [-1, 1, 0, 0, 0, 0];

    private static readonly int[] Edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];

    private readonly float[] _rx = new float[8], _ry = new float[8], _rz = new float[8];
    private readonly float[] _sx = new float[8], _sy = new float[8];
    private readonly float[] _fnx = new float[6], _fny = new float[6], _fnz = new float[6], _fz = new float[6];
    private readonly int[] _order = new int[6];
    private int _w, _h;

    public string Name => "Cube";

    public void Resize(int width, int height) { _w = width; _h = height; }

    public void Draw(Pixel[] dst, in DemoFrame f)
    {
        int w = _w, h = _h;
        var colors = f.Ramp.Colors;

        // backdrop: dim dot grid sliding to the left
        var dot = DemoCanvas.Shade(colors[110], 0.5f);
        int scroll = (int)(f.T * 6f) & 15;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            var bg = DemoCanvas.Shade(colors[(int)((0.12f + 0.25f * y / (h - 1f)) * 255f)], 0.28f);
            for (int x = 0; x < w; x++) dst[row + x] = bg;
        }
        for (int y = 4; y < h; y += 8)
            for (int x = 16 - scroll; x < w; x += 16)
                if (x >= 0) dst[y * w + x] = dot;

        float u = h * 0.5f;
        DrawCube(dst, w, h, f, w * 0.5f, h * 0.5f, u * 0.8f * f.Scale, f.T, 1f, 0.4f);
        DrawCube(dst, w, h, f, w * 0.17f, h * 0.5f, u * 0.52f * f.Scale, f.T, -1.3f, 2.1f);
        DrawCube(dst, w, h, f, w * 0.83f, h * 0.5f, u * 0.52f * f.Scale, f.T, 1.6f, 4.2f);
    }

    private void DrawCube(Pixel[] dst, int w, int h, in DemoFrame f, float cx, float cy, float size, float t, float spin, float phase)
    {
        float a = t * 0.55f * spin + phase, b = t * 0.8f * spin + phase * 0.7f, c = t * 0.35f * spin;
        float sa = FastTrig.Sin(a / Kit.TwoPi), ca = FastTrig.Cos(a / Kit.TwoPi);
        float sb = FastTrig.Sin(b / Kit.TwoPi), cb = FastTrig.Cos(b / Kit.TwoPi);
        float sc = FastTrig.Sin(c / Kit.TwoPi), cc = FastTrig.Cos(c / Kit.TwoPi);
        const float cam = 4.2f;
        float focal = size * cam * 0.62f;

        for (int i = 0; i < 8; i++)
        {
            Rotate(Vx[i], Vy[i], Vz[i], sa, ca, sb, cb, sc, cc, out float x, out float y, out float z);
            _rx[i] = x; _ry[i] = y; _rz[i] = z;
            float p = focal / (z + cam);
            _sx[i] = cx + x * p;
            _sy[i] = cy + y * p;
        }

        int visible = 0;
        for (int fc = 0; fc < 6; fc++)
        {
            Rotate(Nx[fc], Ny[fc], Nz[fc], sa, ca, sb, cb, sc, cc, out float nx, out float ny, out float nz);
            // visible when the outward normal points to the camera (camera at -z, perspective included)
            int v0 = Faces[fc * 4];
            float vx = -_rx[v0], vy = -_ry[v0], vz = -cam - _rz[v0];
            if (nx * vx + ny * vy + nz * vz <= 0f) continue;
            _fnx[fc] = nx; _fny[fc] = ny; _fnz[fc] = nz;
            _fz[fc] = (_rz[Faces[fc * 4]] + _rz[Faces[fc * 4 + 1]] + _rz[Faces[fc * 4 + 2]] + _rz[Faces[fc * 4 + 3]]) * 0.25f;
            _order[visible++] = fc;
        }

        // far to near (insertion sort; at most three faces are visible)
        for (int i = 1; i < visible; i++)
        {
            int key = _order[i];
            int j = i - 1;
            while (j >= 0 && _fz[_order[j]] < _fz[key]) { _order[j + 1] = _order[j]; j--; }
            _order[j + 1] = key;
        }

        var colors = f.Ramp.Colors;
        for (int i = 0; i < visible; i++)
        {
            int fc = _order[i];
            // light from the upper left, in front
            float lit = -(_fnx[fc] * -0.45f + _fny[fc] * -0.55f + _fnz[fc] * -0.7f);
            float shade = 0.55f + 0.65f * MathF.Max(0f, lit);
            var col = DemoCanvas.Shade(colors[(int)((0.5f + 0.09f * fc) * 255f)], shade);
            int v0 = Faces[fc * 4], v1 = Faces[fc * 4 + 1], v2 = Faces[fc * 4 + 2], v3 = Faces[fc * 4 + 3];
            Triangle(dst, w, h, _sx[v0], _sy[v0], _sx[v1], _sy[v1], _sx[v2], _sy[v2], col);
            Triangle(dst, w, h, _sx[v0], _sy[v0], _sx[v2], _sy[v2], _sx[v3], _sy[v3], col);
        }

        // wireframe: every edge of every visible face, bright and anti-aliased
        var edge = DemoCanvas.Shade(colors[235], 1f);
        for (int i = 0; i < visible; i++)
        {
            int fc = _order[i];
            for (int e = 0; e < 4; e++)
            {
                int p0 = Faces[fc * 4 + e], p1 = Faces[fc * 4 + ((e + 1) & 3)];
                DemoCanvas.Line(dst, w, h, _sx[p0], _sy[p0], _sx[p1], _sy[p1], edge, 0.9f, false);
            }
        }
    }

    private static void Rotate(float x, float y, float z, float sa, float ca, float sb, float cb, float sc, float cc, out float ox, out float oy, out float oz)
    {
        float y1 = y * ca - z * sa, z1 = y * sa + z * ca;     // around x
        float x2 = x * cb + z1 * sb, z2 = -x * sb + z1 * cb;  // around y
        ox = x2 * cc - y1 * sc;                               // around z
        oy = x2 * sc + y1 * cc;
        oz = z2;
    }

    private static void Triangle(Pixel[] dst, int w, int h, float x0, float y0, float x1, float y1, float x2, float y2, Pixel col)
    {
        float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        if (MathF.Abs(area) < 1e-3f) return;
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))));
        int maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2))));
        int maxY = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))));
        float sign = area > 0 ? 1f : -1f;
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float e0 = ((x1 - x0) * (py - y0) - (px - x0) * (y1 - y0)) * sign;
                float e1 = ((x2 - x1) * (py - y1) - (px - x1) * (y2 - y1)) * sign;
                float e2 = ((x0 - x2) * (py - y2) - (px - x2) * (y0 - y2)) * sign;
                if (e0 >= 0f && e1 >= 0f && e2 >= 0f) dst[y * w + x] = col;
            }
        }
    }
}
