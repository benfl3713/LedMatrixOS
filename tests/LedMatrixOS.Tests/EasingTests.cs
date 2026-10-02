using LedMatrixOS.Core.Animation;
using Xunit;

namespace LedMatrixOS.Tests;

public class EasingTests
{
    public static TheoryData<string> Names => new()
    {
        nameof(Easing.Linear),
        nameof(Easing.InQuad), nameof(Easing.OutQuad), nameof(Easing.InOutQuad),
        nameof(Easing.InCubic), nameof(Easing.OutCubic), nameof(Easing.InOutCubic),
        nameof(Easing.InQuart), nameof(Easing.OutQuart), nameof(Easing.InOutQuart),
        nameof(Easing.InSine), nameof(Easing.OutSine), nameof(Easing.InOutSine),
        nameof(Easing.InExpo), nameof(Easing.OutExpo), nameof(Easing.InOutExpo),
        nameof(Easing.InBack), nameof(Easing.OutBack), nameof(Easing.InOutBack),
        nameof(Easing.InElastic), nameof(Easing.OutElastic), nameof(Easing.InOutElastic),
        nameof(Easing.InBounce), nameof(Easing.OutBounce), nameof(Easing.InOutBounce),
    };

    private static Func<float, float> Get(string name)
        => (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), typeof(Easing).GetMethod(name)!);

    [Theory]
    [MemberData(nameof(Names))]
    public void Boundaries_AreZeroAndOne(string name)
    {
        var f = Get(name);
        Assert.Equal(0f, f(0f), 5);
        Assert.Equal(1f, f(1f), 5);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Midpoint_IsFinite(string name)
        => Assert.True(float.IsFinite(Get(name)(0.5f)));

    [Fact]
    public void KnownValues()
    {
        Assert.Equal(0.3f, Easing.Linear(0.3f));
        Assert.Equal(0.25f, Easing.InQuad(0.5f), 5);
        Assert.Equal(0.75f, Easing.OutQuad(0.5f), 5);
        Assert.Equal(0.5f, Easing.InOutQuad(0.5f), 5);
        Assert.Equal(0.125f, Easing.InCubic(0.5f), 5);
        Assert.Equal(0.875f, Easing.OutCubic(0.5f), 5);
        Assert.Equal(0.5f, Easing.InOutCubic(0.5f), 5);
        Assert.Equal(0.0625f, Easing.InQuart(0.5f), 5);
        Assert.Equal(0.5f, Easing.InOutSine(0.5f), 5);
        Assert.Equal(0.5f, Easing.InOutExpo(0.5f), 5);
        Assert.Equal(0.5f, Easing.InOutBack(0.5f), 5);
        Assert.Equal(0.5f, Easing.InOutBounce(0.5f), 5);
    }

    [Fact]
    public void BackAndElastic_Overshoot()
    {
        Assert.True(Max(Easing.OutBack) > 1f);
        Assert.True(Min(Easing.InBack) < 0f);
        Assert.True(Max(Easing.InOutBack) > 1f && Min(Easing.InOutBack) < 0f);
        Assert.True(Max(Easing.OutElastic) > 1f);
        Assert.True(Min(Easing.InElastic) < 0f);
    }

    [Fact]
    public void Bounce_StaysWithinZeroAndOne()
    {
        Assert.True(Min(Easing.OutBounce) >= 0f && Max(Easing.OutBounce) <= 1f);
        Assert.True(Min(Easing.InBounce) >= 0f && Max(Easing.InBounce) <= 1f);
    }

    [Fact]
    public void NonOvershootingEasings_AreMonotonic()
    {
        foreach (var f in new Func<float, float>[] { Easing.InQuad, Easing.OutCubic, Easing.InOutQuart, Easing.InOutSine, Easing.InOutExpo })
        {
            var prev = f(0f);
            for (var i = 1; i <= 100; i++)
            {
                var v = f(i / 100f);
                Assert.True(v >= prev - 1e-6f);
                prev = v;
            }
        }
    }

    [Fact]
    public void CubicBezier_TubeCurve_MatchesOriginalImplementation()
    {
        var eased = Easing.CubicBezier(0.22, 1.0, 0.36, 1.0);
        foreach (var t in new[] { 0f, 0.05f, 0.1f, 0.25f, 0.4f, 0.5f, 0.75f, 0.9f, 1f })
            Assert.Equal((float)OldTubeBezier(t, 0.22, 1.0, 0.36, 1.0), eased(t));
    }

    [Fact]
    public void CubicBezier_LinearControlPoints_AreNearLinear()
    {
        var f = Easing.CubicBezier(0.0, 0.0, 1.0, 1.0);
        for (var i = 0; i <= 10; i++)
            Assert.Equal(i / 10f, f(i / 10f), 2);
    }

    // Verbatim copy of TubeDeparturesApp.EvaluateBezierProgress/CubicBezier before they moved.
    private static double OldTubeBezier(double t, double p1x, double p1y, double p2x, double p2y)
    {
        var low = 0.0;
        var high = 1.0;
        var u = t;

        for (var i = 0; i < 12; i++)
        {
            u = (low + high) * 0.5;
            var x = OldCubic(u, p1x, p2x);
            if (x < t)
                low = u;
            else
                high = u;
        }

        return OldCubic(u, p1y, p2y);
    }

    private static double OldCubic(double t, double p1, double p2)
    {
        var inv = 1.0 - t;
        return 3.0 * inv * inv * t * p1
             + 3.0 * inv * t * t * p2
             + t * t * t;
    }

    private static float Max(Func<float, float> f)
    {
        var m = float.MinValue;
        for (var i = 0; i <= 1000; i++) m = Math.Max(m, f(i / 1000f));
        return m;
    }

    private static float Min(Func<float, float> f)
    {
        var m = float.MaxValue;
        for (var i = 0; i <= 1000; i++) m = Math.Min(m, f(i / 1000f));
        return m;
    }
}
