using LedMatrixOS.Core;
using Xunit;

namespace LedMatrixOS.Tests;

public class AudioDataServiceTests
{
    private static AudioDataService Loud()
    {
        var service = new AudioDataService();
        service.AddAudioSamples(Enumerable.Range(0, 512).Select(i => 0.8f * MathF.Sin(i * 0.3f)).ToArray());
        return service;
    }

    [Fact]
    public void CopyFrequencyBands_FillsAsMuchAsFits()
    {
        var service = Loud();
        var full = new float[AudioDataService.FrequencyBandCount];
        Assert.Equal(AudioDataService.FrequencyBandCount, service.CopyFrequencyBands(full));
        Assert.Contains(full, v => v > 0f);

        var small = new float[10];
        Assert.Equal(10, service.CopyFrequencyBands(small));
        Assert.Equal(full.Take(10), small);
    }

    [Fact]
    public void CopyFrequencyBands_AllocatesNothing()
    {
        var service = Loud();
        var buffer = new float[AudioDataService.FrequencyBandCount];
        service.CopyFrequencyBands(buffer);   // warm up

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) service.CopyFrequencyBands(buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
