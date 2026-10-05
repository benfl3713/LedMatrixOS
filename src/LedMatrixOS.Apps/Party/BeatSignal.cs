namespace LedMatrixOS.Apps.Party;

/// <summary>
/// A small, reusable beat signal. Feed it the audio bands every frame: with live audio it detects bass hits; without, it ticks a synthetic
/// beat at <see cref="Bpm"/> so visuals keep dancing. <see cref="Beat"/> is true only on the frame a beat lands; <see cref="Pulse"/> jumps to 1
/// on a beat and decays to 0 (about a quarter of a second), which is what most visuals want to multiply by. No allocations.
/// </summary>
public sealed class BeatSignal
{
    private const float PulseDecayPerSecond = 4f;
    private const float MinBeatGap = 0.2f;
    private const int BassBands = 6;

    private float _bassAvg, _prevBass, _since = 10f, _phase;

    /// <summary>True on the frame a beat landed.</summary>
    public bool Beat { get; private set; }

    /// <summary>1 on a beat, falling linearly to 0.</summary>
    public float Pulse { get; private set; }

    /// <summary>Overall loudness 0..1 (real audio), or a gentle swell with the synthetic beat.</summary>
    public float Level { get; private set; }

    /// <summary>True when the last update used real audio rather than the synthetic beat.</summary>
    public bool IsLive { get; private set; }

    /// <summary>Tempo of the synthetic beat.</summary>
    public float Bpm { get; set; } = 124f;

    /// <summary>Beats fired since the start (both sources); handy for "every fourth beat" logic.</summary>
    public int Count { get; private set; }

    public void Update(float dt, ReadOnlySpan<float> bands, bool live)
    {
        dt = Math.Clamp(dt, 0f, 0.1f);
        Beat = false;
        IsLive = live && bands.Length > 0;
        _since += dt;

        if (IsLive)
        {
            int n = Math.Min(BassBands, bands.Length);
            float bass = 0f, all = 0f;
            for (int i = 0; i < bands.Length; i++)
            {
                all += bands[i];
                if (i < n) bass += bands[i];
            }
            bass /= n;
            Level = Math.Clamp(all / bands.Length * 3f, 0f, 1f);

            bool rising = bass > _prevBass;
            if (bass > _bassAvg * 1.3f + 0.08f && rising && _since > MinBeatGap) Fire();
            _bassAvg += (bass - _bassAvg) * Math.Min(1f, dt * 3f);
            _prevBass = bass;
        }
        else
        {
            float period = 60f / Math.Clamp(Bpm, 30f, 240f);
            _phase += dt;
            if (_phase >= period)
            {
                _phase -= period;
                if (_phase > period) _phase = 0f;
                Fire();
            }
            Level = 0.45f + 0.4f * Pulse;
        }

        if (!Beat) Pulse = Math.Max(0f, Pulse - dt * PulseDecayPerSecond);
    }

    private void Fire()
    {
        Beat = true;
        Pulse = 1f;
        _since = 0f;
        Count++;
    }
}
