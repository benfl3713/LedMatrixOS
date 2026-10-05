using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LedMatrixOS.Tests;

public class CrashCardTests
{
    private sealed class StubApp : IMatrixApp
    {
        public string Id => "stub";
        public string Name => "Stub App";
        public int FrameRate => 30;
        public Task OnActivatedAsync((int height, int width) d, IConfiguration c, CancellationToken t) => Task.CompletedTask;
        public Task OnDeactivatedAsync(CancellationToken t) => Task.CompletedTask;
        public void Update(FrameContext context, CancellationToken t) { }
        public void Render(FrameBuffer frame, CancellationToken t) { }
    }

    [Fact]
    public void Guard_ShowsCrashThenRetriesAfterDelay()
    {
        var guard = new CrashGuard { RetryAfter = TimeSpan.FromSeconds(5) };
        Assert.Null(guard.Active(TimeSpan.Zero));

        guard.Record(new StubApp(), new InvalidOperationException("boom"), TimeSpan.FromSeconds(10));
        Assert.Equal("InvalidOperationException: boom", guard.Active(TimeSpan.FromSeconds(12))!.Message);
        Assert.Equal("Stub App", guard.Active(TimeSpan.FromSeconds(14.9))!.AppName);
        Assert.Null(guard.Active(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void Guard_ClearDropsCrash()
    {
        var guard = new CrashGuard();
        guard.Record(new StubApp(), new Exception("x"), TimeSpan.Zero);
        guard.Clear();
        Assert.Null(guard.Active(TimeSpan.Zero));
    }

    [Fact]
    public void Card_Snapshot()
    {
        Fonts.Load();
        var card = new CrashCard();
        var frame = SnapshotHelper.Render(f => card.Render(f, new CrashInfo("Tube Departures",
            "HttpRequestException: Response status code does not indicate success: 429 (Too Many Requests) while calling the TfL arrivals API for station 940GZZLUBST")));
        TubeFixtures.Preview(frame, "crash_card");
        SnapshotHelper.AssertMatchesSnapshot(frame, "crash_card");
    }

    [Fact]
    public void Card_LongUnbrokenWordDoesNotOverflow()
    {
        Fonts.Load();
        var card = new CrashCard();
        var frame = SnapshotHelper.Render(f => card.Render(f, new CrashInfo("X", new string('W', 200))));
        Assert.False(SnapshotHelper.IsBlank(frame));
    }
}

public class FontsTests
{
    [Fact]
    public void Load_IsIdempotentAndSafeToCallConcurrently()
    {
        Fonts.Load();
        var big = Fonts.Big;
        var small = Fonts.Small;

        Parallel.For(0, 16, _ => Fonts.Load());

        Assert.Same(big, Fonts.Big);
        Assert.Same(small, Fonts.Small);
    }
}
