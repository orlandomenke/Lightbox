namespace Lightbox.Core.Inbetween;

public enum Easing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
}

public static class EasingOps
{
    public static double Ease(double t, Easing kind) => kind switch
    {
        Easing.EaseIn => t * t,
        Easing.EaseOut => 1 - (1 - t) * (1 - t),
        Easing.EaseInOut => t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t),
        _ => t,
    };
}
