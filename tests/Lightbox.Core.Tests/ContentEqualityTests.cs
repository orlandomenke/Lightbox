using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The compare that decides whether an undo step may reuse a frozen drawing
/// (docs/DESIGN-undo-shares-unchanged.md). A wrong "same" restores the wrong
/// drawing on undo, silently, so the guard is mechanical: every property the
/// serializer writes, anywhere under a drawing, is changed on its own and must
/// read as changed. A property added later is covered without anyone listing it.
/// </summary>
public class ContentEqualityTests(ITestOutputHelper output)
{
    [Fact]
    public void ACopyIsTheSame()
    {
        var frame = Inflate(new Frame());
        Assert.True(ContentEquality.Same(frame, frame.Clone()));
    }

    /// <summary>Hash01 seeds dabs from the bits: 0.0 and -0.0 are different drawings.</summary>
    [Fact]
    public void DoublesCompareByTheirBits()
    {
        var a = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(0.0, 1, 1)] }] };
        var b = a.Clone();
        b.Strokes[0].Points[0] = new StrokePoint(-0.0, 1, 1);
        Assert.False(ContentEquality.Same(a, b));
    }

    /// <summary>
    /// Points have a hand-written fast path, since they are the volume. It names
    /// six fields, so this pins that the point writes exactly those six: a
    /// seventh would otherwise be compared by nothing.
    /// </summary>
    [Fact]
    public void ThePointFastPathComparesEveryFieldAPointWrites()
    {
        var written = WrittenProperties(typeof(StrokePoint)).Select(p => p.Name).Order().ToArray();
        Assert.Equal(["Pressure", "Speed", "TiltX", "TiltY", "X", "Y"], written);

        var a = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(1, 2, 0.5, 0.1, 0.2, 0.3)] }] };
        foreach (var change in new Func<StrokePoint, StrokePoint>[]
                 {
                     p => p with { X = 9 }, p => p with { Y = 9 }, p => p with { Pressure = 0.9 },
                     p => p with { TiltX = null }, p => p with { TiltY = 0.9 }, p => p with { Speed = 9 },
                 })
        {
            var b = a.Clone();
            b.Strokes[0].Points[0] = change(b.Strokes[0].Points[0]);
            Assert.False(ContentEquality.Same(a, b));
        }
    }

    [Fact]
    public void EveryWrittenPropertyOfADrawingCountsTowardTheSame()
    {
        var exemplar = Inflate(new Frame());
        var sites = new List<Site>();
        Walk(exemplar, [], sites, depth: 0, seen: new HashSet<object>(ReferenceEqualityComparer.Instance));

        var file = Json(exemplar);
        var missed = new List<string>();
        var skipped = new List<string>();
        var noOps = new List<string>();
        foreach (var site in sites)
        {
            var copy = exemplar.Clone();
            var (owner, replace) = Navigate(copy, site.Path);
            // Shared with the exemplar (an immutable record, a baked sample): a
            // change in place would change both, so it is replaced with a copy,
            // as real code does.
            var shared = owner is not null && ReferenceEquals(owner, Navigate(exemplar, site.Path).Owner);
            if (owner is null || !Perturb(owner, site.Property, replace, shared))
            {
                skipped.Add(site.Name);
                continue;
            }
            // The oracle is the file: a change the file does not show is no
            // change (an untouched medium writes nothing either way).
            if (Json(copy) == file)
            {
                noOps.Add(site.Name);
                continue;
            }
            if (ContentEquality.Same(exemplar, copy)) missed.Add(site.Name);
        }

        output.WriteLine($"{sites.Count} properties, {skipped.Count} skipped, {noOps.Count} changes the file does not show");
        foreach (var s in noOps) output.WriteLine($"  no-op {s}");
        foreach (var s in skipped) output.WriteLine($"  skipped {s}");
        Assert.True(sites.Count > 60, $"only {sites.Count} properties reached — the walk is not looking");
        Assert.Contains(sites, s => s.Name.EndsWith("Strokes[0].Points"));
        Assert.Contains(sites, s => s.Name.EndsWith("Strokes[0].Brush.Size"));
        Assert.Empty(missed);
        Assert.Empty(skipped);
    }

    // ---- the walk ----------------------------------------------------------------

    private sealed record Site(string Name, IReadOnlyList<Step> Path, PropertyInfo Property);

    /// <summary>One move from an object to the next: a property, or the first item of a collection.</summary>
    private sealed record Step(PropertyInfo? Property);

    private static bool Written(PropertyInfo p) =>
        p.GetIndexParameters().Length == 0
        && p.GetMethod is { IsPublic: true, IsStatic: false }
        && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always };

    private static IEnumerable<PropertyInfo> WrittenProperties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(Written)
            // A get-only property that is not a collection is derived from the others.
            .Where(p => p.SetMethod is { IsPublic: true } || IsCollection(p.PropertyType));

    private static bool IsCollection(Type t) => t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t);

    private static bool IsLeaf(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);
    }

    private static void Walk(object obj, List<Step> path, List<Site> sites, int depth, HashSet<object> seen)
    {
        if (depth > 6 || !seen.Add(obj)) return;
        foreach (var p in WrittenProperties(obj.GetType()))
        {
            var name = string.Join("", path.Select(s => s.Property is null ? "[0]" : "." + s.Property.Name)) + "." + p.Name;
            sites.Add(new Site(name.TrimStart('.'), [.. path], p));
            var value = p.GetValue(obj);
            if (value is null || IsLeaf(p.PropertyType) || p.PropertyType.IsValueType) continue;
            var next = new List<Step>(path) { new(p) };
            if (value is IDictionary dict)
            {
                foreach (DictionaryEntry e in dict)
                {
                    if (e.Value is { } v && !IsLeaf(v.GetType()) && !v.GetType().IsValueType)
                        Walk(v, [.. next, new Step(null)], sites, depth + 1, seen);
                    break;
                }
            }
            else if (value is IList list)
            {
                // A collection's own properties (a list's Capacity) are not content: its items are.
                if (list.Count > 0 && list[0] is { } item && !IsLeaf(item.GetType()) && !item.GetType().IsValueType
                    && item is not IEnumerable)
                    Walk(item, [.. next, new Step(null)], sites, depth + 1, seen);
            }
            else if (!IsCollection(p.PropertyType))
            {
                Walk(value, next, sites, depth + 1, seen);
            }
        }
    }

    /// <summary>The object at the end of the path, and how to put a replacement in its place.</summary>
    private static (object? Owner, Action<object>? Replace) Navigate(object root, IReadOnlyList<Step> path)
    {
        object? at = root;
        Action<object>? replace = null;
        foreach (var step in path)
        {
            if (at is null) return (null, null);
            var parent = at;
            if (step.Property is { } p)
            {
                at = p.GetValue(parent);
                replace = p.SetMethod is { IsPublic: true } ? v => p.SetValue(parent, v) : null;
            }
            else if (parent is IDictionary d && d.Count > 0)
            {
                var key = d.Keys.Cast<object>().First();
                at = d[key];
                replace = v => d[key] = v;
            }
            else if (parent is IList { Count: > 0 } l)
            {
                at = l[0];
                replace = v => l[0] = v;
            }
            else
            {
                return (null, null);
            }
        }
        return (at, replace);
    }

    private static bool IsInitOnly(PropertyInfo p) =>
        p.SetMethod?.ReturnParameter.GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true;

    // ---- filling and changing ------------------------------------------------------

    /// <summary>Every optional member made present, so the walk reaches the types beneath it.</summary>
    private static T Inflate<T>(T obj, int depth = 0) where T : class
    {
        if (depth > 5) return obj;
        foreach (var p in WrittenProperties(obj.GetType()))
        {
            var current = p.GetValue(obj);
            if (current is IList { Count: 0 } emptyList && ElementType(p.PropertyType) is { } et
                && Sample(et, depth) is { } item)
            {
                emptyList.Add(item);
                continue;
            }
            if (current is IDictionary { Count: 0 } emptyDict && p.PropertyType.GetGenericArguments() is [var kt, var vt]
                && Sample(kt, depth) is { } key && Sample(vt, depth) is { } val)
            {
                emptyDict[key] = val;
                continue;
            }
            if (current is null && p.SetMethod is { IsPublic: true } && Sample(p.PropertyType, depth) is { } made)
            {
                p.SetValue(obj, made);
                continue;
            }
            if (current is not null && !IsLeaf(p.PropertyType) && !p.PropertyType.IsValueType && !IsCollection(p.PropertyType))
                Inflate(current, depth + 1);
        }
        return obj;
    }

    private static Type? ElementType(Type t) =>
        t.IsArray ? t.GetElementType() : t.GetGenericArguments() is [var e] ? e : null;

    /// <summary>A present, non-default value of a type, or null when it cannot make one.</summary>
    private static object? Sample(Type t, int depth)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return "s";
        if (u == typeof(double)) return 0.75;
        if (u == typeof(float)) return 0.75f;
        if (u == typeof(int)) return 3;
        if (u == typeof(long)) return 3L;
        if (u == typeof(bool)) return true;
        if (u.IsEnum) return Enum.GetValues(u).Cast<object>().Last();
        if (u == typeof(StrokePoint)) return new StrokePoint(1, 2, 0.5, 0.1, 0.2, 0.3);
        if (u.IsValueType) return Activator.CreateInstance(u);
        if (u.IsAbstract || u.IsInterface) return null;
        if (IsCollection(u))
        {
            var made = u.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(u) : null;
            if (made is IList l && ElementType(u) is { } et && Sample(et, depth + 1) is { } item) l.Add(item);
            if (made is IDictionary d && u.GetGenericArguments() is [var kt, var vt]
                && Sample(kt, depth + 1) is { } key && Sample(vt, depth + 1) is { } val) d[key] = val;
            return made;
        }
        if (u.GetConstructor(Type.EmptyTypes) is null)
        {
            // A positional record: its narrowest constructor, every argument sampled.
            var ctor = u.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            var args = ctor?.GetParameters().Select(p => Sample(p.ParameterType, depth + 1)).ToArray();
            return ctor is null || args!.Any(a => a is null) ? null : Inflate(ctor.Invoke(args), depth + 1);
        }
        return Inflate(Activator.CreateInstance(u)!, depth + 1);
    }

    /// <summary>Change one property of <paramref name="owner"/> in place. False when it cannot.</summary>
    private static string Json(Frame f) => System.Text.Json.JsonSerializer.Serialize(f, Lightbox.Core.Serialization.DocJson.Compact);

    private static bool Perturb(object owner, PropertyInfo p, Action<object>? replace, bool shared)
    {
        // Shared between copies by design (Frame.Clone's anchors, shapes and
        // baked samples), so real code never changes one in place: it puts a
        // changed copy in its place. The guard does the same, or it would change both.
        if ((shared || IsInitOnly(p)) && !owner.GetType().IsValueType)
        {
            if (replace is null) return false;
            var copy = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(owner, null)!;
            if (!PerturbInPlace(copy, p)) return false;
            replace(copy);
            return true;
        }
        return PerturbInPlace(owner, p);
    }

    private static bool PerturbInPlace(object owner, PropertyInfo p)
    {
        var current = p.GetValue(owner);
        if (current is IList list && !p.PropertyType.IsArray)
        {
            if (list.Count > 0) list.RemoveAt(list.Count - 1);
            else if (ElementType(p.PropertyType) is { } et && Sample(et, 0) is { } item) list.Add(item);
            else return false;
            return true;
        }
        if (current is IDictionary dict)
        {
            if (dict.Count == 0) return false;
            dict.Remove(dict.Keys.Cast<object>().First());
            return true;
        }
        if (p.SetMethod is not { IsPublic: true }) return false;
        var changed = Changed(current, p.PropertyType);
        if (changed is null && Nullable.GetUnderlyingType(p.PropertyType) is null && p.PropertyType.IsValueType) return false;
        if (Equals(changed, current)) return false;
        p.SetValue(owner, changed);
        return true;
    }

    private static object? Changed(object? current, Type t)
    {
        if (current is null) return Sample(t, 0);
        if (Nullable.GetUnderlyingType(t) is not null) return null;
        return current switch
        {
            double d => d + 0.5,
            float f => f + 0.5f,
            int i => i + 1,
            long l => l + 1,
            bool b => !b,
            string s => s + "x",
            Enum e => Enum.GetValues(t).Cast<object>().FirstOrDefault(v => !v.Equals(e)),
            StrokePoint sp => sp with { X = sp.X + 0.5 },
            _ when t.IsValueType => ChangedStruct(current),
            _ => null, // a reference: made absent
        };
    }

    private static object? ChangedStruct(object boxed)
    {
        foreach (var p in boxed.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.SetMethod is null || !IsLeaf(p.PropertyType)) continue;
            var changed = Changed(p.GetValue(boxed), p.PropertyType);
            if (changed is null) continue;
            p.SetValue(boxed, changed);
            return boxed;
        }
        return null;
    }
}
