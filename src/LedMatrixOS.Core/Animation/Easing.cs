namespace LedMatrixOS.Core.Animation;

/// <summary>
/// Easing functions mapping linear progress t (0-1) to eased progress. All return 0 at t=0 and 1 at t=1;
/// the back and elastic families overshoot in between. Use them as <c>Func&lt;float, float&gt;</c>.
/// </summary>
public static class Easing
{
    private const float BackC1 = 1.70158f;
    private const float BackC2 = BackC1 * 1.525f;
    private const float BackC3 = BackC1 + 1f;
    private const float ElasticC4 = 2f * MathF.PI / 3f;
    private const float ElasticC5 = 2f * MathF.PI / 4.5f;

    public static float Linear(float t) => t;

    public static float InQuad(float t) => t * t;
    public static float OutQuad(float t) => t * (2f - t);
    public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Pow2(-2f * t + 2f) / 2f;

    public static float InCubic(float t) => t * t * t;
    public static float OutCubic(float t) => 1f - Pow3(1f - t);
    public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Pow3(-2f * t + 2f) / 2f;

    public static float InQuart(float t) => t * t * t * t;
    public static float OutQuart(float t) => 1f - Pow2(Pow2(1f - t));
    public static float InOutQuart(float t) => t < 0.5f ? 8f * t * t * t * t : 1f - Pow2(Pow2(-2f * t + 2f)) / 2f;

    public static float InSine(float t) => 1f - MathF.Cos(t * MathF.PI / 2f);
    public static float OutSine(float t) => MathF.Sin(t * MathF.PI / 2f);
    public static float InOutSine(float t) => -(MathF.Cos(MathF.PI * t) - 1f) / 2f;

    public static float InExpo(float t) => t <= 0f ? 0f : MathF.Pow(2f, 10f * t - 10f);
    public static float OutExpo(float t) => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
    public static float InOutExpo(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t < 0.5f
            ? MathF.Pow(2f, 20f * t - 10f) / 2f
            : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f;
    }

    public static float InBack(float t) => BackC3 * t * t * t - BackC1 * t * t;
    public static float OutBack(float t) => 1f + BackC3 * Pow3(t - 1f) + BackC1 * Pow2(t - 1f);
    public static float InOutBack(float t) => t < 0.5f
        ? Pow2(2f * t) * ((BackC2 + 1f) * 2f * t - BackC2) / 2f
        : (Pow2(2f * t - 2f) * ((BackC2 + 1f) * (2f * t - 2f) + BackC2) + 2f) / 2f;

    public static float InElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * ElasticC4);
    }

    public static float OutElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * ElasticC4) + 1f;
    }

    public static float InOutElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t < 0.5f
            ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * ElasticC5)) / 2f
            : MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * ElasticC5) / 2f + 1f;
    }

    public static float OutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }

    public static float InBounce(float t) => 1f - OutBounce(1f - t);
    public static float InOutBounce(float t) => t < 0.5f
        ? (1f - OutBounce(1f - 2f * t)) / 2f
        : (1f + OutBounce(2f * t - 1f)) / 2f;

    /// <summary>
    /// CSS-style cubic bezier easing through (0,0), (p1x,p1y), (p2x,p2y), (1,1).
    /// </summary>
    public static Func<float, float> CubicBezier(double p1x, double p1y, double p2x, double p2y)
        => t => (float)EvaluateBezierProgress(t, p1x, p1y, p2x, p2y);

    private static double EvaluateBezierProgress(double t, double p1x, double p1y, double p2x, double p2y)
    {
        // Solve x(u)=t with binary search, then return y(u) for a CSS-style cubic bezier.
        var low = 0.0;
        var high = 1.0;
        var u = t;

        for (var i = 0; i < 12; i++)
        {
            u = (low + high) * 0.5;
            var x = Bezier(u, p1x, p2x);
            if (x < t)
                low = u;
            else
                high = u;
        }

        return Bezier(u, p1y, p2y);
    }

    private static double Bezier(double t, double p1, double p2)
    {
        var inv = 1.0 - t;
        return 3.0 * inv * inv * t * p1
             + 3.0 * inv * t * t * p2
             + t * t * t;
    }

    private static float Pow2(float v) => v * v;
    private static float Pow3(float v) => v * v * v;
}
