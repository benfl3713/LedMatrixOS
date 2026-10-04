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
    public void CopyFrequencyBands_MatchesGetFrequencyBands()
    {
        var service = Loud();
        var expected = service.GetFrequencyBands();

        var copy = new float[AudioDataService.FrequencyBandCount];
        int count = service.CopyFrequencyBands(copy);

        Assert.Equal(AudioDataService.FrequencyBandCount, count);
        Assert.Equal(expected, copy);
        Assert.Contains(copy, v => v > 0f);
    }

    [Fact]
    public void CopyFrequencyBands_FillsOnlyAsMuchAsFits()
    {
        var service = Loud();
        var full = service.GetFrequencyBands();

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
