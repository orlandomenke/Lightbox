using System.Diagnostics;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;

namespace Lightbox.Bench;

/// <summary>
/// Playback's first loop, phase 1 (docs/DESIGN-playback-first-loop.md, Q214): what
/// a scene's playback tiles cost in memory and in time, against the budget they
/// have to live in.
/// </summary>
/// <remarks>
/// <para>
/// Idle warming (phase 2) renders the playback range ahead of the play button.
/// Whether it can warm the whole range or only a window around the playhead
/// depends on one comparison: the tiles of every drawing against
/// <see cref="MemoryBudget.TileCache"/>. This makes that comparison with numbers.
/// </para>
/// <para>
/// <b>It also reports the pyramid levels</b>, which <c>TileFrameCache</c> does not
/// count against its budget: playback zoomed out composes from a coarser level,
/// built on first use and then held beside the store.
/// </para>
/// <para>
/// <b>An artist's own document stays theirs.</b> <c>--doc</c> reads it where it
/// is; only counts and sizes are printed, never names or content.
/// </para>
/// </remarks>
public static class TileMemory
{
    //   dotnet run --project tools/Lightbox.Bench -c Release -- tiles
    //   dotnet run --project tools/Lightbox.Bench -c Release -- tiles --doc "C:\path\to\a.lightbox.json"
    public static int Run(string[] args)
    {
        if (args.Contains("--scaling")) return Scaling(Fixture.Build(Fixture.Shape.OwnerShape));
        if (args.Contains("--compare")) return Compare();
        var docAt = Arg(args, "--doc");
        var cases = docAt is not null
            ? [("your document", DocJson.Load(docAt))]
            : new List<(string, Doc)>
            {
                ("owner-shaped 1080p", Fixture.Build(Fixture.Shape.OwnerShape)),
                ("owner-shaped 4K", Fixture.Build(Fixture.Shape.OwnerShape with { Width = 3840, Height = 2160 })),
            };

        var here = MemoryBudget.TileCache();
        var minimum = Math.Clamp((long)(MemoryBudget.MinimumSpecBytes / 32.0), 128L << 20, 2L << 30);
        Console.WriteLine($"Tile cache budget: {Mb(here)} on this machine ({Mb(MemoryBudget.Available)} available), " +
                          $"{Mb(minimum)} on the {Mb(MemoryBudget.MinimumSpecBytes)} minimum spec.");
        Console.WriteLine($"Workers for idle warming: {Math.Max(1, Environment.ProcessorCount - 1)} of {Environment.ProcessorCount} cores.\n");

        foreach (var (name, doc) in cases) Measure(name, doc, here, minimum);
        return 0;
    }

    private static void Measure(string name, Doc doc, long here, long minimum)
    {
        var scene = doc.Scene;
        int w = scene.Width, h = scene.Height;
        // One render per drawing, as the playback cache keys it: by frame id.
        var frames = scene.Layers
            .SelectMany(l => l.Cels.Select(c => c.Frame).OfType<Frame>())
            .GroupBy(f => f.Id).Select(g => g.First()).ToList();
        var tileable = frames.Where(f => TileFrameCache.CanTileFrame(f)).ToList();

        var store = new List<long>();
        var level1 = new List<long>();
        var level2 = new List<long>();
        var ms = new List<double>();
        foreach (var frame in tileable)
        {
            var sw = Stopwatch.StartNew();
            var (s, pyramid) = TileFrameCache.RenderDetached(frame, w, h);
            sw.Stop();
            ms.Add(sw.Elapsed.TotalMilliseconds);
            store.Add(s.AllocatedBytes);
            level1.Add(pyramid.Level(1).AllocatedBytes);
            level2.Add(pyramid.Level(2).AllocatedBytes);
            pyramid.Dispose();
            s.Dispose();
        }

        // Idle warming's other half: the same renders across N-1 workers.
        var workers = Math.Max(1, Environment.ProcessorCount - 1);
        var wall = Stopwatch.StartNew();
        Parallel.ForEach(tileable, new ParallelOptions { MaxDegreeOfParallelism = workers }, frame =>
        {
            var (s, pyramid) = TileFrameCache.RenderDetached(frame, w, h);
            pyramid.Dispose();
            s.Dispose();
        });
        wall.Stop();

        var total = store.Sum();
        var withLevel1 = total + level1.Sum();
        Console.WriteLine($"{name}: {w}x{h}, {frames.Count} drawings, {tileable.Count} tileable, {scene.FrameCount} frames");
        Console.WriteLine($"  tiles, level 0      {Mb(total),10}  (per drawing median {Mb(Median(store))}, largest {Mb(store.DefaultIfEmpty().Max())}; " +
                          $"a whole canvas would be {Mb((long)w * h * 4)})");
        Console.WriteLine($"  + level 1 (zoom <1) {Mb(withLevel1),10}  (+{Mb(level1.Sum())}; level 2 adds {Mb(level2.Sum())})");
        Console.WriteLine($"  fits this machine   {Fits(total, here)} / with level 1 {Fits(withLevel1, here)}");
        Console.WriteLine($"  fits minimum spec   {Fits(total, minimum)} / with level 1 {Fits(withLevel1, minimum)}");
        Console.WriteLine($"  render, one core    {ms.Sum() / 1000:0.0} s total, median {Median(ms):0} ms a drawing, slowest {ms.DefaultIfEmpty().Max():0} ms");
        Console.WriteLine($"  render, {workers} workers  {wall.Elapsed.TotalSeconds:0.0} s wall ({ms.Sum() / Math.Max(1, wall.Elapsed.TotalMilliseconds):0.0}x)\n");
    }

    /// <summary>
    /// The same renders at 1, 2, 4, 8 and N-1 workers. Idle warming was planned on the
    /// assumption that they scale with cores, and the first measurement said they do
    /// not (1.6x on 15): where the curve flattens says whether the cause is shared.
    /// </summary>
    private static int Scaling(Doc doc)
    {
        var scene = doc.Scene;
        var frames = scene.Layers.SelectMany(l => l.Cels.Select(c => c.Frame).OfType<Frame>())
            .GroupBy(f => f.Id).Select(g => g.First()).Where(f => TileFrameCache.CanTileFrame(f)).ToList();
        // How many times a stroke is stamped: once per tile its bounds reach, since
        // RasterizeByTile calls StampStroke for each (stroke, tile) pair.
        var (probe, probePyramid) = TileFrameCache.RenderDetached(frames[0], scene.Width, scene.Height);
        var size = probe.Grid.TileSize;
        probePyramid.Dispose();
        probe.Dispose();
        long strokes = 0, pairs = 0;
        foreach (var f in frames)
        {
            foreach (var st in f.Strokes)
            {
                if (st.Points.Count == 0) continue;
                var pad = st.Brush.Size;
                var x0 = st.Points.Min(q => q.X) - pad; var x1 = st.Points.Max(q => q.X) + pad;
                var y0 = st.Points.Min(q => q.Y) - pad; var y1 = st.Points.Max(q => q.Y) + pad;
                var tx = (int)Math.Floor(Math.Min(x1, scene.Width) / size) - (int)Math.Floor(Math.Max(0, x0) / size) + 1;
                var ty = (int)Math.Floor(Math.Min(y1, scene.Height) / size) - (int)Math.Floor(Math.Max(0, y0) / size) + 1;
                strokes++;
                pairs += Math.Max(1, tx) * Math.Max(1, ty);
            }
        }
        Console.WriteLine($"  tile size {size}: {strokes} strokes stamped {pairs} times, {pairs / (double)strokes:0.0}x each (bounding-box estimate)");
        // What one render allocates on the managed heap: the GC's cost is this times the rate.
        var a0 = GC.GetTotalAllocatedBytes(precise: true);
        foreach (var f in frames.Take(8))
        {
            var (s0, p0) = TileFrameCache.RenderDetached(f, scene.Width, scene.Height);
            p0.Dispose();
            s0.Dispose();
        }
        var perDrawing = (GC.GetTotalAllocatedBytes(precise: true) - a0) / 8;
        Console.WriteLine($"  managed allocation per drawing render: {Mb(perDrawing)}");
        // Which types: the runtime samples an allocation about every 100 KB and names it.
        using (var ticks = new AllocationTicks())
        {
            foreach (var f in frames.Take(8))
            {
                var (s1, p1) = TileFrameCache.RenderDetached(f, scene.Width, scene.Height);
                p1.Dispose();
                s1.Dispose();
            }
            Thread.Sleep(1500); // events arrive asynchronously
            foreach (var (type, kb) in ticks.Top(12)) Console.WriteLine($"    ~{kb / 8 / 1024.0,7:0.0} MB/drawing  {type}");
        }
        double? one = null;
        foreach (var workers in new[] { 1, 2, 4, 8, Math.Max(1, Environment.ProcessorCount - 1) }.Distinct())
        {
            var wall = Stopwatch.StartNew();
            Parallel.ForEach(frames, new ParallelOptions { MaxDegreeOfParallelism = workers }, frame =>
            {
                var (s, p) = TileFrameCache.RenderDetached(frame, scene.Width, scene.Height);
                p.Dispose();
                s.Dispose();
            });
            wall.Stop();
            one ??= wall.Elapsed.TotalSeconds;
            Console.WriteLine($"  {workers,2} workers  {wall.Elapsed.TotalSeconds,6:0.0} s  {one / wall.Elapsed.TotalSeconds,4:0.0}x  " +
                              $"(GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, pause {GC.GetTotalPauseDuration().TotalSeconds:0.0} s)");
        }
        return 0;
    }

    /// <summary>
    /// Phase 2a's question: does rendering a drawing once over the whole canvas and
    /// cutting it into tiles give the same bytes as stamping each stroke per tile —
    /// and how much faster is it? Same pixels is the bar; a faster route that is not
    /// bit-identical is a different picture (invariant 1).
    /// </summary>
    private static int Compare()
    {
        foreach (var (name, shape) in new[]
        {
            ("1080p", Fixture.Shape.OwnerShape),
            ("4K", Fixture.Shape.OwnerShape with { Width = 3840, Height = 2160 }),
        })
        {
            var doc = Fixture.Build(shape);
            var scene = doc.Scene;
            var info = new SkiaSharp.SKImageInfo(scene.Width, scene.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            var frames = scene.Layers.SelectMany(l => l.Cels.Select(c => c.Frame).OfType<Frame>())
                .GroupBy(f => f.Id).Select(g => g.First()).Where(f => TileFrameCache.CanTileFrame(f)).ToList();
            double byTile = 0, whole = 0;
            int identical = 0, differing = 0;
            long worstBytes = 0;
            foreach (var frame in frames)
            {
                using var a = new TileStore();
                var sw = Stopwatch.StartNew();
                TiledRasterizer.Rasterize(a, frame.Strokes, info);
                byTile += sw.Elapsed.TotalMilliseconds;

                using var b = new TileStore();
                sw.Restart();
                TiledRasterizer.RasterizeWhole(b, frame.Strokes, info);
                whole += sw.Elapsed.TotalMilliseconds;

                var diff = DifferingBytes(a, b, scene.Width, scene.Height);
                if (diff == 0) identical++;
                else { differing++; worstBytes = Math.Max(worstBytes, diff); }
            }
            Console.WriteLine($"{name}: {frames.Count} drawings | per tile {byTile / 1000:0.0} s, whole then cut {whole / 1000:0.0} s " +
                              $"({byTile / Math.Max(1, whole):0.0}x) | identical {identical}, differing {differing}" +
                              (differing > 0 ? $" (worst {worstBytes} bytes differ)" : ""));
        }
        return 0;
    }

    /// <summary>Bytes that differ between two stores; a tile one lacks counts as transparent.</summary>
    private static long DifferingBytes(TileStore a, TileStore b, int w, int h)
    {
        var ta = a.Intersecting(0, 0, w, h).ToDictionary(t => t.Coord, t => t.Bitmap);
        var tb = b.Intersecting(0, 0, w, h).ToDictionary(t => t.Coord, t => t.Bitmap);
        long differ = 0;
        foreach (var coord in ta.Keys.Union(tb.Keys))
        {
            var pa = ta.TryGetValue(coord, out var x) ? x.Bytes : null;
            var pb = tb.TryGetValue(coord, out var y) ? y.Bytes : null;
            var n = Math.Max(pa?.Length ?? 0, pb?.Length ?? 0);
            for (var i = 0; i < n; i++)
            {
                var va = pa is not null && i < pa.Length ? pa[i] : (byte)0;
                var vb = pb is not null && i < pb.Length ? pb[i] : (byte)0;
                if (va != vb) differ++;
            }
        }
        return differ;
    }

    private static string Fits(long bytes, long budget) =>
        bytes <= budget ? $"yes, {100.0 * bytes / budget:0}% of the budget" : $"NO, {bytes / (double)budget:0.0}x the budget";

    private static long Median(List<long> v) => v.Count == 0 ? 0 : v.Order().ElementAt(v.Count / 2);

    private static double Median(List<double> v) => v.Count == 0 ? 0 : v.Order().ElementAt(v.Count / 2);

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>The runtime's sampled allocation events, summed by type.</summary>
    private sealed class AllocationTicks : System.Diagnostics.Tracing.EventListener
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _bytes = new();

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
            }
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName is not { } n || !n.StartsWith("GCAllocationTick") || e.Payload is null) return;
            var names = e.PayloadNames!;
            var type = e.Payload[names.IndexOf("TypeName")]?.ToString() ?? "?";
            var amount = Convert.ToInt64(e.Payload[names.IndexOf("AllocationAmount64")] ?? e.Payload[names.IndexOf("AllocationAmount")]);
            _bytes.AddOrUpdate(type, amount, (_, b) => b + amount);
        }

        public IEnumerable<(string Type, long Kb)> Top(int n) =>
            _bytes.OrderByDescending(kv => kv.Value).Take(n).Select(kv => (kv.Key, kv.Value / 1024));
    }
}
