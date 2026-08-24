namespace Lightbox.Core.Inbetween;

public enum Easing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,

    /// <summary>
    /// Freeze on this key until the next one — pose-to-pose rather than
    /// tweened, so a key can be reused exactly the way a drawing is held
    /// (Q152). At the next key's own frame the next key shows, which is what
    /// every other easing already does there.
    /// </summary>
    Hold,
}

public static class EasingOps
{
    public static double Ease(double t, Easing kind) => kind switch
    {
        Easing.EaseIn => t * t,
        Easing.EaseOut => 1 - (1 - t) * (1 - t),
        Easing.EaseInOut => t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t),
        Easing.Hold => t < 1 ? 0 : 1,
        _ => t,
    };
}
