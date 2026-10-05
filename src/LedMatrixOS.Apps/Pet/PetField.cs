using LedMatrixOS.Core;
using LedMatrixOS.Graphics;
using LedMatrixOS.Graphics.UI;
using SixLabors.ImageSharp;

namespace LedMatrixOS.Apps.Pet;

/// <summary>
/// The pet's room as one node: a wall, a floor, a window showing day, night or rain, and the creature, whose face and movement follow its mood.
/// All motion is a function of the frame time, so a given time always draws the same picture and nothing allocates per frame.
/// </summary>
public sealed class PetField : Node
{
    private static readonly Pixel Wall = new(52, 40, 58), WallLow = new(40, 30, 46), Floor = new(98, 66, 44), FloorLine = new(70, 46, 30);
    private static readonly Pixel Body = new(255, 184, 110), BodyShade = new(226, 150, 84), Belly = new(255, 226, 180), Ink = new(40, 24, 24);
    private static readonly Pixel Heart = new(255, 80, 120), Bowl = new(120, 150, 200);

    private TimeSpan _time;

    public PetMood Mood { get; set; } = PetMood.Content;
    public bool IsNight { get; set; }
    public bool IsRaining { get; set; }

    public override void Update(FrameContext ctx)
    {
        base.Update(ctx);
        _time = ctx.Time;
    }

    protected override void OnRender(FrameBuffer frame, Rectangle bounds)
    {
        float t = (float)_time.TotalSeconds;
        int floorTop = bounds.Bottom - 12;

        frame.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, floorTop - bounds.Y), Wall);
        frame.Fill(new Rectangle(bounds.X, floorTop - 8, bounds.Width, 8), WallLow);
        frame.Fill(new Rectangle(bounds.X, floorTop, bounds.Width, bounds.Bottom - floorTop), Floor);
        frame.Fill(new Rectangle(bounds.X, floorTop, bounds.Width, 1), FloorLine);
        for (int x = bounds.X + 14; x < bounds.Right; x += 31) frame.Fill(new Rectangle(x, floorTop + 1, 1, bounds.Bottom - floorTop - 1), FloorLine);

        DrawWindow(frame, new Rectangle(bounds.Right - 54, bounds.Y + 6, 40, 30), t);

        int cx = bounds.X + bounds.Width / 3;
        DrawPet(frame, cx, floorTop + 4, t);
        if (Mood == PetMood.Hungry) DrawBowl(frame, cx + 30, floorTop + 3);
    }

    private void DrawWindow(FrameBuffer frame, Rectangle r, float t)
    {
        var sky = IsNight ? new Pixel(10, 14, 40) : IsRaining ? new Pixel(110, 122, 140) : new Pixel(110, 180, 235);
        frame.Fill(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Pixel(150, 110, 70));
        frame.Fill(r, sky);

        if (IsNight)
        {
            for (int i = 0; i < 9; i++)
            {
                int sx = r.X + 3 + (i * 37 + 11) % (r.Width - 6), sy = r.Y + 2 + (i * 19 + 5) % (r.Height - 10);
                float twinkle = 0.5f + 0.5f * MathF.Sin(t * 1.5f + i * 2.1f);
                frame.SetPixel(sx, sy, new Pixel(255, 255, 220).WithBrightness(0.4f + 0.6f * twinkle));
            }

            frame.FillCircle(r.X + r.Width - 11, r.Y + 9, 5, new Pixel(240, 235, 200));
            frame.FillCircle(r.X + r.Width - 9, r.Y + 8, 4, sky);   // crescent
        }
        else if (IsRaining)
        {
            for (int i = 0; i < 16; i++)
            {
                float speed = 22 + (i % 4) * 6;
                int dx = r.X + 1 + (i * 13 + 3) % (r.Width - 2);
                int dy = r.Y + (int)((t * speed + i * 9.7f) % r.Height);
                frame.SetPixel(dx, dy, new Pixel(170, 200, 255));
                if (dy > r.Y) frame.SetPixel(dx, dy - 1, new Pixel(120, 150, 210));
            }
        }
        else
        {
            frame.FillCircle(r.X + 9, r.Y + 8, 4, new Pixel(255, 225, 90));
            int cloud = r.X + (int)((t * 3f) % (r.Width + 10)) - 8;
            frame.FillEllipse(new Rectangle(cloud, r.Y + 14, 9, 4), Pixel.White);
        }

        // Frame cross, drawn over the sky.
        var bar = new Pixel(150, 110, 70);
        frame.Fill(new Rectangle(r.X + r.Width / 2, r.Y, 1, r.Height), bar);
        frame.Fill(new Rectangle(r.X, r.Y + r.Height / 2, r.Width, 1), bar);
    }

    private void DrawPet(FrameBuffer frame, int cx, int baseY, float t)
    {
        int lift = 0, shake = 0, squash = 0;
        switch (Mood)
        {
            case PetMood.Happy:
                lift = (int)MathF.Round(MathF.Abs(MathF.Sin(t * 5f)) * 5f);
                break;
            case PetMood.Content:
                squash = MathF.Sin(t * 1.6f) > 0.4f ? 1 : 0;
                break;
            case PetMood.Sleeping:
                squash = MathF.Sin(t * 1.0f) > 0f ? 1 : 0;
                break;
            case PetMood.Sad:
                squash = 3;
                break;
            case PetMood.Hungry:
                shake = (int)(t * 4f) % 2 == 0 ? -1 : 1;
                squash = 1;
                break;
        }

        int w = 34, h = 26 - squash;
        int left = cx - w / 2 + shake, top = baseY - h - lift;

        // Shadow stays on the floor while the pet hops.
        frame.FillEllipse(new Rectangle(cx - 14 + shake, baseY - 2, 28, 5), new Pixel(60, 40, 26));

        // Ears
        int earDrop = Mood == PetMood.Sad ? 2 : 0;
        for (int side = 0; side < 2; side++)
        {
            int ex = side == 0 ? left + 3 : left + w - 10;
            for (int row = 0; row < 7; row++)
                frame.Fill(new Rectangle(ex + (side == 0 ? row / 3 : -row / 3 + 3) - 0, top - 5 + earDrop + row, 7 - row, 1), row < 4 && row > 0 ? BodyShade : Body);
        }

        frame.FillEllipse(new Rectangle(left, top, w, h), Body);
        frame.FillEllipse(new Rectangle(left + 8, top + h / 2 - 1, w - 16, h / 2), Belly);
        frame.FillEllipse(new Rectangle(left + 2, top + h - 5, 9, 5), BodyShade);                  // feet
        frame.FillEllipse(new Rectangle(left + w - 11, top + h - 5, 9, 5), BodyShade);

        int eyeY = top + h / 3;
        int eyeL = cx - 8 + shake, eyeR = cx + 7 + shake;
        bool blink = Mood is PetMood.Content or PetMood.Hungry && (t % 4f) < 0.12f;

        switch (Mood)
        {
            case PetMood.Sleeping:
                frame.DrawLine(eyeL - 2, eyeY + 1, eyeL + 2, eyeY + 1, Ink);
                frame.DrawLine(eyeR - 2, eyeY + 1, eyeR + 2, eyeY + 1, Ink);
                DrawZs(frame, cx + 14, top - 2, t);
                break;
            case PetMood.Happy:
                for (int e = 0; e < 2; e++)
                {
                    int ex = e == 0 ? eyeL : eyeR;
                    frame.SetPixel(ex - 2, eyeY + 1, Ink); frame.SetPixel(ex - 1, eyeY, Ink); frame.SetPixel(ex, eyeY, Ink);
                    frame.SetPixel(ex + 1, eyeY, Ink); frame.SetPixel(ex + 2, eyeY + 1, Ink);
                }

                DrawHearts(frame, cx, top, t);
                break;
            default:
                if (blink)
                {
                    frame.DrawLine(eyeL - 1, eyeY + 1, eyeL + 1, eyeY + 1, Ink);
                    frame.DrawLine(eyeR - 1, eyeY + 1, eyeR + 1, eyeY + 1, Ink);
                }
                else
                {
                    frame.Fill(new Rectangle(eyeL - 1, eyeY - 1, 3, 4), Ink);
                    frame.Fill(new Rectangle(eyeR - 1, eyeY - 1, 3, 4), Ink);
                    frame.SetPixel(eyeL, eyeY - 1, Pixel.White);
                    frame.SetPixel(eyeR, eyeY - 1, Pixel.White);
                }

                if (Mood == PetMood.Sad)
                {
                    int tear = (int)(t * 6f) % 6;
                    frame.SetPixel(eyeL - 2, eyeY + 2 + tear / 2, new Pixel(120, 190, 255));
                    frame.DrawLine(eyeL - 3, eyeY - 4, eyeL + 1, eyeY - 3, Ink);              // worried brows
                    frame.DrawLine(eyeR + 3, eyeY - 4, eyeR - 1, eyeY - 3, Ink);
                }

                break;
        }

        // Mouth
        int my = eyeY + 7;
        int mx = cx + shake;
        switch (Mood)
        {
            case PetMood.Happy:
                frame.Fill(new Rectangle(mx - 3, my, 7, 1), Ink);
                frame.Fill(new Rectangle(mx - 2, my + 1, 5, 2), new Pixel(200, 60, 80));
                break;
            case PetMood.Content:
                frame.SetPixel(mx - 2, my, Ink); frame.SetPixel(mx - 1, my + 1, Ink); frame.SetPixel(mx, my + 1, Ink);
                frame.SetPixel(mx + 1, my + 1, Ink); frame.SetPixel(mx + 2, my, Ink);
                break;
            case PetMood.Sad:
                frame.SetPixel(mx - 2, my + 1, Ink); frame.SetPixel(mx - 1, my, Ink); frame.SetPixel(mx, my, Ink);
                frame.SetPixel(mx + 1, my, Ink); frame.SetPixel(mx + 2, my + 1, Ink);
                break;
            case PetMood.Hungry:
                frame.FillEllipse(new Rectangle(mx - 2, my - 1, 5, 5), Ink);
                break;
            default:
                frame.Fill(new Rectangle(mx - 1, my, 3, 1), Ink);
                break;
        }

        // Cheeks
        if (Mood is PetMood.Happy or PetMood.Content)
        {
            frame.SetPixel(eyeL - 4, eyeY + 4, new Pixel(255, 130, 130));
            frame.SetPixel(eyeR + 4, eyeY + 4, new Pixel(255, 130, 130));
        }
    }

    private static void DrawHearts(FrameBuffer frame, int cx, int top, float t)
    {
        for (int i = 0; i < 2; i++)
        {
            float age = (t * 0.6f + i * 0.5f) % 1f;
            int x = cx + (i == 0 ? -14 : 12) + (int)(MathF.Sin(age * 6f) * 2);
            int y = top - 4 - (int)(age * 14f);
            var c = Heart.WithBrightness(1f - age * 0.7f);
            frame.SetPixel(x, y, c); frame.SetPixel(x + 2, y, c);
            frame.Fill(new Rectangle(x, y + 1, 3, 1), c);
            frame.SetPixel(x + 1, y + 2, c);
        }
    }

    private static void DrawZs(FrameBuffer frame, int x, int y, float t)
    {
        for (int i = 0; i < 3; i++)
        {
            float age = (t * 0.35f + i / 3f) % 1f;
            int zx = x + (int)(age * 10) + i * 2, zy = y - (int)(age * 16);
            int s = 2 + i;
            var c = new Pixel(200, 210, 255).WithBrightness(1f - age * 0.6f);
            frame.Fill(new Rectangle(zx, zy, s, 1), c);
            frame.DrawLine(zx + s - 1, zy, zx, zy + s - 1, c);
            frame.Fill(new Rectangle(zx, zy + s - 1, s, 1), c);
        }
    }

    private static void DrawBowl(FrameBuffer frame, int x, int y)
    {
        frame.FillEllipse(new Rectangle(x, y - 4, 14, 7), Bowl);
        frame.FillEllipse(new Rectangle(x + 2, y - 4, 10, 3), new Pixel(30, 36, 60));   // empty
    }
}
