using System.Globalization;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Bench;

/// <summary>
/// Generates the documents the performance lab measures against (Q209).
/// </summary>
/// <remarks>
/// <para>
/// <b>Generated, never copied.</b> The documents that show a stall are the
/// artist's own work, and private; a fixture of the same <em>shape</em> —
/// layer count, drawing count, strokes per drawing, canvas size, exposures on
/// twos — reproduces the cost without carrying the art. The shape is what the
/// render report prints, so a capture can be turned into a fixture by reading
/// four numbers off it.
/// </para>
/// <para>
/// <b>Deterministic from the seed</b>, ids included: a fixture generated today
/// and one generated next month are the same file, so a regression is the
/// build's and never the input's.
/// </para>
/// </remarks>
public static class Fixture
{
    /// <summary>What to generate.</summary>
    /// <param name="Layers">Drawing layers, above one paper layer.</param>
    /// <param name="Drawings">Distinct drawings across all layers.</param>
    /// <param name="Strokes">Strokes per drawing.</param>
    /// <param name="Points">Points per stroke.</param>
    /// <param name="Frames">Scene length in frames.</param>
    /// <param name="Step">Exposure step: 2 is "on twos".</param>
    public sealed record Shape(
        int Width, int Height, int Layers, int Drawings, int Strokes, int Points, int Frames, int Step, int Seed)
    {
        /// <summary>
        /// The owner's document of 2026-10-07 as its render report counts it:
        /// 1920×1080, 11 layers and 64 drawings <em>including the paper</em>,
        /// 1256 strokes over 21 frames, 7.3 MB saved — so 10 drawing layers and
        /// 63 drawings here, about 20 strokes each, long enough to land near
        /// that size.
        /// </summary>
        public static Shape OwnerShape => new(1920, 1080, 10, 63, 20, 150, 21, 2, 7);

        public override string ToString() =>
            $"{Width}x{Height}, {Layers} layers, {Drawings} drawings x {Strokes} strokes x {Points} points, " +
            $"{Frames} frames on {Step}s, seed {Seed}";
    }

    public static Doc Build(Shape s)
    {
        var rng = new Random(s.Seed);
        var doc = DocumentFactory.CreateDoc(s.Width, s.Height, 24, paperColor: "#f4f2ec");
        var scene = doc.Scene;
        scene.FrameCount = s.Frames;
        scene.Layers.RemoveAll(l => !l.IsBackground);

        // Drawings shared out across the layers as evenly as they go, each
        // layer's own drawings exposed in turn along the sheet on the step.
        var perLayer = Enumerable.Range(0, s.Layers)
            .Select(i => s.Drawings / s.Layers + (i < s.Drawings % s.Layers ? 1 : 0)).ToArray();
        var drawingIndex = 0;
        for (var li = 0; li < s.Layers; li++)
        {
            var layer = new Layer
            {
                Id = $"layer-{li:00}",
                Name = $"Layer {li + 1}",
            };
            // The first drawing on every layer is centred on the canvas, so a
            // scripted gesture knows where the content is without being told:
            // a drag from the middle lands on ink on whichever layer is active.
            var drawings = Enumerable.Range(0, Math.Max(1, perLayer[li]))
                .Select(d => Drawing(rng, s, drawingIndex++, centred: d == 0)).ToList();
            // Each drawing keyed once, spread evenly along the sheet and held
            // until the next — never the same drawing in two cels, which real
            // documents do not do. Keys land on the step (twos by default).
            var cels = new Frame?[s.Frames];
            for (var d = 0; d < drawings.Count; d++)
            {
                var at = d * s.Frames / drawings.Count;
                at -= at % Math.Max(1, s.Step);
                while (at < s.Frames && cels[at] is not null) at++;
                if (at < s.Frames) cels[at] = drawings[d];
            }
            foreach (var cel in cels) layer.Cels.Add(new Cel { Frame = cel });
            scene.Layers.Add(layer);
        }
        return doc;
    }

    private static Frame Drawing(Random rng, Shape s, int index, bool centred)
    {
        var frame = new Frame { Id = $"drawing-{index:000}" };
        // Each drawing sits in its own region, as a character's parts would, so
        // a transform has a real selection to move rather than the whole canvas.
        // Drawn from the generator either way, so centring one changes no other.
        var cx = s.Width * (0.2 + 0.6 * rng.NextDouble());
        var cy = s.Height * (0.2 + 0.6 * rng.NextDouble());
        if (centred)
        {
            cx = s.Width / 2.0;
            cy = s.Height / 2.0;
        }
        var reach = Math.Min(s.Width, s.Height) * 0.18;
        for (var k = 0; k < s.Strokes; k++)
        {
            var points = new List<StrokePoint>(s.Points);
            var angle = rng.NextDouble() * Math.Tau;
            var x = cx + (rng.NextDouble() - 0.5) * reach;
            var y = cy + (rng.NextDouble() - 0.5) * reach;
            var step = reach / s.Points * 1.5;
            for (var p = 0; p < s.Points; p++)
            {
                angle += (rng.NextDouble() - 0.5) * 0.35;
                x += Math.Cos(angle) * step;
                y += Math.Sin(angle) * step;
                var t = p / (double)Math.Max(1, s.Points - 1);
                points.Add(new StrokePoint(x, y, 0.35 + 0.6 * Math.Sin(Math.PI * t)));
            }
            frame.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = k % 5 == 0 ? "#3a5a8a" : "#1a1a1a",
                Points = points,
                Brush = new BrushSettings
                {
                    Size = 4 + rng.Next(0, 10),
                    Hardness = 0.8,
                    Opacity = 1,
                    Flow = 1,
                    Spacing = 0.15,
                },
            });
        }
        return frame;
    }

    /// <summary>
    /// <c>fixture --out path [--preset owner] [--width W --height H --layers N
    /// --drawings N --strokes N --points N --frames N --step N --seed N]</c>
    /// </summary>
    public static int Run(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        int Int(string name, int fallback) =>
            Arg(name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

        if (Arg("--out") is not { } output)
        {
            Console.Error.WriteLine("fixture: --out <path.lightbox.json> is required");
            return 2;
        }
        var b = Shape.OwnerShape; // the only preset so far
        var shape = new Shape(
            Int("--width", b.Width), Int("--height", b.Height), Int("--layers", b.Layers),
            Int("--drawings", b.Drawings), Int("--strokes", b.Strokes), Int("--points", b.Points),
            Int("--frames", b.Frames), Int("--step", b.Step), Int("--seed", b.Seed));
        var doc = Build(shape);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        DocJson.Save(doc, output);
        Console.WriteLine($"fixture: {shape} -> {output}");
        return 0;
    }
}
