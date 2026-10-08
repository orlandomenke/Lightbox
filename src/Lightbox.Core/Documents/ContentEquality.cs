using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// Whether two parts of a document hold the same content: every property the
/// serializer writes, doubles bit for bit (docs/DESIGN-undo-shares-unchanged.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>Complete by construction, because a wrong "same" loses a drawing.</b> An
/// undo step reuses a frozen drawing when this says the live one has not changed.
/// If it missed a field, undo would restore the wrong drawing, silently — the
/// shape B379 had in a hand-listed clone. So it walks every public property the
/// serializer would write, found by reflection once per type, rather than a list
/// someone keeps up to date. <c>ContentEqualityTests</c> changes each one in turn.
/// </para>
/// <para>
/// <b>Errs strict.</b> Doubles compare by their bits, because <c>Hash01</c> seeds
/// every dab from them and <c>0.0</c> and <c>-0.0</c> are different marks.
/// Collections compare in order, dictionaries too, because order is what the file
/// writes. A wrong "changed" costs one copy; a wrong "same" costs a drawing.
/// </para>
/// <para>
/// <b>Points are the bytes, so they get their own loop</b>: a span walk with no
/// boxing. On the owner-shaped fixture that compares 189,000 points in about the
/// time <c>Doc.Clone</c> copies them, and allocates nothing.
/// </para>
/// </remarks>
public static class ContentEquality
{
    private const int MaxDepth = 64;

    private static readonly ConcurrentDictionary<Type, Func<object, object, int, bool>> ByType = new();

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> would write the same document content.</summary>
    public static bool Same(object? a, object? b) => Same(a, b, 0);

    private static bool Same(object? a, object? b, int depth)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        var type = a.GetType();
        if (type != b.GetType()) return false;
        if (depth > MaxDepth) return false; // deeper than any document goes: say changed
        return ByType.GetOrAdd(type, Build)(a, b, depth);
    }

    private static Func<object, object, int, bool> Build(Type t)
    {
        if (t == typeof(double)) return (a, b, _) => Bits((double)a) == Bits((double)b);
        if (t == typeof(float)) return (a, b, _) => BitConverter.SingleToInt32Bits((float)a) == BitConverter.SingleToInt32Bits((float)b);
        if (t == typeof(string)) return (a, b, _) => string.Equals((string)a, (string)b, StringComparison.Ordinal);
        if (t.IsPrimitive || t.IsEnum || t == typeof(decimal)) return (a, b, _) => a.Equals(b);
        if (t == typeof(StrokePoint)) return (a, b, _) => SamePoint((StrokePoint)a, (StrokePoint)b);
        if (t == typeof(List<StrokePoint>)) return (a, b, _) => SamePoints((List<StrokePoint>)a, (List<StrokePoint>)b);
        if (typeof(IDictionary).IsAssignableFrom(t)) return (a, b, d) => SameDictionary((IDictionary)a, (IDictionary)b, d);
        if (typeof(IEnumerable).IsAssignableFrom(t)) return (a, b, d) => SameSequence((IEnumerable)a, (IEnumerable)b, d);
        return SameProperties(t);
    }

    /// <summary>
    /// Every property the serializer writes, compared in turn, as one compiled
    /// function per type. Compiled rather than read by reflection because a brush
    /// has dozens of doubles and a drawing has hundreds of brushes: boxing each one
    /// cost a fifth of the copy this compare exists to avoid.
    /// </summary>
    private static Func<object, object, int, bool> SameProperties(Type t)
    {
        var a = Expression.Parameter(typeof(object), "a");
        var b = Expression.Parameter(typeof(object), "b");
        var depth = Expression.Parameter(typeof(int), "depth");
        var ta = Expression.Variable(t, "ta");
        var tb = Expression.Variable(t, "tb");
        var next = Expression.Add(depth, Expression.Constant(1));

        Expression all = Expression.Constant(true);
        foreach (var written in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0
                                 && p.GetMethod is { IsPublic: true }
                                 && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
                     .Reverse())
        {
            var p = ComparedThrough.TryGetValue((t, written.Name), out var source) ? t.GetProperty(source)! : written;
            all = Expression.AndAlso(SameProperty(Expression.Property(ta, p), Expression.Property(tb, p), next), all);
        }

        var body = Expression.Block(
            [ta, tb],
            Expression.Assign(ta, Expression.Convert(a, t)),
            Expression.Assign(tb, Expression.Convert(b, t)),
            all);
        return Expression.Lambda<Func<object, object, int, bool>>(body, a, b, depth).Compile();
    }

    /// <summary>
    /// A written property whose getter is costly, compared through the field it is
    /// derived from instead — only where the two agree exactly.
    /// </summary>
    /// <remarks>
    /// <c>BrushSettings.MediumOnDisk</c> is the medium, or nothing while it is
    /// untouched, and deciding "untouched" serializes the medium: twice a stroke,
    /// it was most of a drawing's compare. Comparing <c>Medium</c> gives the same
    /// answer — two untouched media are equal either way, and any other pair
    /// differs either way — without the serializing.
    /// </remarks>
    private static readonly Dictionary<(Type, string), string> ComparedThrough = new()
    {
        [(typeof(BrushSettings), nameof(BrushSettings.MediumOnDisk))] = nameof(BrushSettings.Medium),
    };

    /// <summary>One property of each side, compared without boxing where the type allows.</summary>
    private static Expression SameProperty(Expression x, Expression y, Expression depth)
    {
        var t = x.Type;
        if (t == typeof(double) || t == typeof(double?) || t == typeof(float) || t == typeof(string)
            || t == typeof(int) || t == typeof(long) || t == typeof(bool))
        {
            return Expression.Call(typeof(ContentEquality).GetMethod(nameof(Leaf), BindingFlags.NonPublic | BindingFlags.Static, [t, t])!, x, y);
        }
        if (t.IsEnum) return Expression.Equal(Expression.Convert(x, typeof(long)), Expression.Convert(y, typeof(long)));
        return Expression.Call(
            typeof(ContentEquality).GetMethod(nameof(Same), BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(object), typeof(object), typeof(int)])!,
            Expression.Convert(x, typeof(object)), Expression.Convert(y, typeof(object)), depth);
    }

    private static bool Leaf(double a, double b) => Bits(a) == Bits(b);
    private static bool Leaf(double? a, double? b) => Same(a, b);
    private static bool Leaf(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    private static bool Leaf(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
    private static bool Leaf(int a, int b) => a == b;
    private static bool Leaf(long a, long b) => a == b;
    private static bool Leaf(bool a, bool b) => a == b;

    private static bool SameDictionary(IDictionary a, IDictionary b, int depth)
    {
        if (a.Count != b.Count) return false;
        var x = a.GetEnumerator();
        var y = b.GetEnumerator();
        while (x.MoveNext())
        {
            if (!y.MoveNext()) return false;
            if (!Same(x.Key, y.Key, depth + 1) || !Same(x.Value, y.Value, depth + 1)) return false;
        }
        return !y.MoveNext();
    }

    private static bool SameSequence(IEnumerable a, IEnumerable b, int depth)
    {
        if (a is ICollection ca && b is ICollection cb && ca.Count != cb.Count) return false;
        var x = a.GetEnumerator();
        var y = b.GetEnumerator();
        while (x.MoveNext())
        {
            if (!y.MoveNext() || !Same(x.Current, y.Current, depth + 1)) return false;
        }
        return !y.MoveNext();
    }

    private static bool SamePoints(List<StrokePoint> a, List<StrokePoint> b)
    {
        var x = CollectionsMarshal.AsSpan(a);
        var y = CollectionsMarshal.AsSpan(b);
        if (x.Length != y.Length) return false;
        for (var i = 0; i < x.Length; i++)
        {
            if (!SamePoint(x[i], y[i])) return false;
        }
        return true;
    }

    private static bool SamePoint(in StrokePoint p, in StrokePoint q) =>
        Bits(p.X) == Bits(q.X) && Bits(p.Y) == Bits(q.Y) && Bits(p.Pressure) == Bits(q.Pressure)
        && Same(p.TiltX, q.TiltX) && Same(p.TiltY, q.TiltY) && Same(p.Speed, q.Speed);

    private static bool Same(double? a, double? b) =>
        a.HasValue == b.HasValue && (!a.HasValue || Bits(a.Value) == Bits(b!.Value));

    private static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
}
