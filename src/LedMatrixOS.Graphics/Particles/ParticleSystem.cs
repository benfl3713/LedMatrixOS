using LedMatrixOS.Core;

namespace LedMatrixOS.Graphics.Particles;

/// <summary>
/// Fixed-capacity pooled particle simulation. No allocation after construction: spawns beyond capacity are dropped.
/// Deterministic for a given seeded <see cref="Random"/> and sequence of calls.
/// </summary>
public sealed class ParticleSystem
{
    private struct Particle
    {
        public Emitter Emitter;
        public float X, Y, VX, VY;
        public float Age, Life;
        public Pixel Color; // only used when the emitter has a palette
    }

    private readonly Particle[] _pool;
    private readonly Random _rng;
    private readonly List<Emitter> _emitters = new();
    private int _count;

    public int Width { get; }
    public int Height { get; }
    public int Capacity => _pool.Length;
    public int Count => _count;
    public IReadOnlyList<Emitter> Emitters => _emitters;

    /// <param name="width">Bounds used by <see cref="EdgeMode.Wrap"/> and <see cref="EdgeMode.Bounce"/>.</param>
    public ParticleSystem(int width, int height, int capacity, Random? rng = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Width = width;
        Height = height;
        _pool = new Particle[capacity];
        _rng = rng ?? new Random();
    }

    public Emitter Add(Emitter emitter)
    {
        _emitters.Add(emitter);
        return emitter;
    }

    public void Remove(Emitter emitter) => _emitters.Remove(emitter);

    public void Clear() => _count = 0;

    /// <summary>Spawns <paramref name="count"/> particles at once (as many as fit in the pool).</summary>
    public void Burst(Emitter emitter, int count)
    {
        for (int i = 0; i < count; i++) Spawn(emitter);
    }

    public void Update(float dt)
    {
        foreach (var e in _emitters)
        {
            if (!e.Enabled || e.Rate <= 0f) continue;
            e.Accumulator += e.Rate * dt;
            while (e.Accumulator >= 1f)
            {
                e.Accumulator -= 1f;
                Spawn(e);
            }
        }

        for (int i = 0; i < _count;)
        {
            ref var p = ref _pool[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _pool[i] = _pool[--_count]; // swap-remove; re-check the swapped-in particle
                continue;
            }

            var e = p.Emitter;
            p.VX += e.GravityX * dt;
            p.VY += e.GravityY * dt;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            if (e.Edge != EdgeMode.None) ApplyEdge(ref p, e.Edge);
            i++;
        }
    }

    /// <summary>
    /// Blends all live particles over the frame. Size 1 or less splats bilinearly across the four nearest pixels
    /// (pixel centres sit on integer coordinates); larger sizes draw a nearest-pixel square.
    /// </summary>
    public void Render(FrameBuffer frame)
    {
        for (int i = 0; i < _count; i++)
        {
            ref var p = ref _pool[i];
            var e = p.Emitter;
            float t = p.Age / p.Life;
            float alpha = e.AlphaStart + (e.AlphaEnd - e.AlphaStart) * t;
            if (alpha <= 0f) continue;
            alpha = Math.Min(alpha, 1f);
            var color = e.Palette != null ? p.Color : e.Evaluate(t);

            if (e.Size > 1f)
            {
                int side = (int)(e.Size + 0.5f);
                int x0 = (int)MathF.Round(p.X) - side / 2, y0 = (int)MathF.Round(p.Y) - side / 2;
                for (int y = 0; y < side; y++)
                    for (int x = 0; x < side; x++)
                        frame.BlendPixel(x0 + x, y0 + y, color, alpha);
                continue;
            }

            float fx = MathF.Floor(p.X), fy = MathF.Floor(p.Y);
            int ix = (int)fx, iy = (int)fy;
            float wx = p.X - fx, wy = p.Y - fy;
            frame.BlendPixel(ix, iy, color, alpha * (1 - wx) * (1 - wy));
            frame.BlendPixel(ix + 1, iy, color, alpha * wx * (1 - wy));
            frame.BlendPixel(ix, iy + 1, color, alpha * (1 - wx) * wy);
            frame.BlendPixel(ix + 1, iy + 1, color, alpha * wx * wy);
        }
    }

    private void Spawn(Emitter e)
    {
        if (_count >= _pool.Length) return;
        ref var p = ref _pool[_count++];
        p.Emitter = e;
        p.X = e.X + e.Width * _rng.NextSingle();
        p.Y = e.Y + e.Height * _rng.NextSingle();
        float angle = (e.Angle + (_rng.NextSingle() - 0.5f) * e.Spread) * (MathF.PI / 180f);
        float speed = e.SpeedMin + (e.SpeedMax - e.SpeedMin) * _rng.NextSingle();
        p.VX = MathF.Cos(angle) * speed;
        p.VY = MathF.Sin(angle) * speed;
        p.Age = 0f;
        p.Life = Math.Max(e.LifetimeMin + (e.LifetimeMax - e.LifetimeMin) * _rng.NextSingle(), 0.001f);
        p.Color = e.Palette is { Length: > 0 } pal ? pal[_rng.Next(pal.Length)] : Pixel.White;
    }

    private void ApplyEdge(ref Particle p, EdgeMode mode)
    {
        if (mode == EdgeMode.Wrap)
        {
            p.X = Mod(p.X, Width);
            p.Y = Mod(p.Y, Height);
            return;
        }

        float maxX = Width - 1, maxY = Height - 1;
        if (p.X < 0f) { p.X = -p.X; p.VX = -p.VX; }
        else if (p.X > maxX) { p.X = 2 * maxX - p.X; p.VX = -p.VX; }
        if (p.Y < 0f) { p.Y = -p.Y; p.VY = -p.VY; }
        else if (p.Y > maxY) { p.Y = 2 * maxY - p.Y; p.VY = -p.VY; }
        p.X = Math.Clamp(p.X, 0f, maxX);
        p.Y = Math.Clamp(p.Y, 0f, maxY);
    }

    private static float Mod(float v, float m)
    {
        v %= m;
        return v < 0f ? v + m : v;
    }
}
