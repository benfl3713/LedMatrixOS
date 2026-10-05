namespace LedMatrixOS.Core;

/// <summary>
/// Service to receive and process audio data for visualizer apps
/// </summary>
public class AudioDataService
{
    private float[] _frequencyBands = new float[64];
    private readonly object _lock = new();
    private DateTime _lastUpdate = DateTime.MinValue;
    
    public const int FrequencyBandCount = 64;

    /// <summary>
    /// Folds raw audio samples into the frequency bands
    /// </summary>
    public void AddAudioSamples(float[] samples)
    {
        if (samples == null || samples.Length == 0) return;
        
        ProcessAudioData(samples);
    }

    /// <summary>
    /// Copies the current band values into <paramref name="destination"/> (at most <see cref="FrequencyBandCount"/> entries) and returns
    /// how many were written. It allocates nothing, so it is safe to call from render code.
    /// </summary>
    public int CopyFrequencyBands(Span<float> destination)
    {
        lock (_lock)
        {
            DecayIfStale();
            int count = Math.Min(destination.Length, _frequencyBands.Length);
            _frequencyBands.AsSpan(0, count).CopyTo(destination);
            return count;
        }
    }

    // Decay old data if no recent updates
    private void DecayIfStale()
    {
        if ((DateTime.UtcNow - _lastUpdate).TotalMilliseconds <= 100) return;

        for (int i = 0; i < _frequencyBands.Length; i++)
        {
            _frequencyBands[i] *= 0.9f; // Decay
        }
    }

    private void ProcessAudioData(float[] samples)
    {
        lock (_lock)
        {
            // Simple frequency band estimation using sliding windows
            // This is a basic implementation - for better results, use FFT
            int samplesPerBand = Math.Max(1, samples.Length / FrequencyBandCount);
            
            for (int i = 0; i < FrequencyBandCount && i < samples.Length / samplesPerBand; i++)
            {
                float sum = 0;
                int count = 0;
                
                for (int j = 0; j < samplesPerBand && (i * samplesPerBand + j) < samples.Length; j++)
                {
                    int idx = i * samplesPerBand + j;
                    sum += Math.Abs(samples[idx]);
                    count++;
                }
                
                float average = count > 0 ? sum / count : 0;
                
                // More responsive smoothing - favor new data
                _frequencyBands[i] = _frequencyBands[i] * 0.3f + average * 0.7f;
                
                // Clamp to 0-1 range
                _frequencyBands[i] = Math.Clamp(_frequencyBands[i], 0, 1);
            }
            
            _lastUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Check if audio data is recent (within last 200ms)
    /// </summary>
    public bool HasRecentData()
    {
        return (DateTime.UtcNow - _lastUpdate).TotalMilliseconds < 200;
    }

    /// <summary>
    /// Clear all audio data
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Fill(_frequencyBands, 0);
            _lastUpdate = DateTime.MinValue;
        }
    }
}
