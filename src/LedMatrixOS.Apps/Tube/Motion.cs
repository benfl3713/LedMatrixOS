using System.Numerics;
using LedMatrixOS.Core.Animation;
using LedMatrixOS.Graphics.UI;

namespace LedMatrixOS.Apps.Tube;

/// <summary>Entrance helpers shared by the Tube apps.</summary>
internal static class Motion
{
    /// <summary>
    /// Parks <paramref name="node"/> at <paramref name="from"/> (an offset from its laid-out place) and, after <paramref name="delay"/>,
    /// slides it home. The node must already be hosted so the animation runs on the app's animator.
    /// </summary>
    public static void SlideIn(Node node, Animator animator, Vector2 from, TimeSpan delay, TimeSpan? duration = null, Func<float, float>? easing = null)
    {
        node.Position = from;
        var run = duration ?? TimeSpan.FromMilliseconds(450);
        animator.Add(Timeline.Sequence(
            Timeline.Delay(delay),
            Timeline.Do(() => node.AnimatePosition(Vector2.Zero, run, easing ?? Easing.OutCubic))));
    }
}
