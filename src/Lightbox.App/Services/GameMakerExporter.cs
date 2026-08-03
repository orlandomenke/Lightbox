using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using SkiaSharp;

namespace Lightbox.App.Services;

public sealed record GameMakerExportOptions
{
    /// <summary>
    /// Sheet settings, of which only the trim and the background handling survive.
    /// </summary>
    /// <remarks>
    /// Packing, columns and padding are decided by the <c>_stripN</c> convention rather
    /// than by the artist — see <see cref="GameMakerExporter"/>. Offering controls that
    /// the exporter then overrides is how an artist learns the controls lie, so the
    /// export window hides them for this target.
    /// </remarks>
    public SpriteSheetOptions Sheet { get; init; } = new();
}

/// <param name="StripPaths">One strip per animation, in tag order.</param>
/// <param name="Notes">
/// What GameMaker cannot express about this document, in plain words.
/// </param>
public sealed record GameMakerExportResult(
    IReadOnlyList<string> StripPaths,
    string MetadataPath,
    int FrameCount,
    IReadOnlyList<string> Notes,
    SpriteSheetResult? Sheet = null);

/// <summary>
/// Export for GameMaker: one <c>_stripN</c> strip per animation, and no project files.
/// </summary>
/// <remarks>
/// <para>
/// <b>GameMaker is the one engine with no import-time scripting</b>, so the pattern the
/// other three share — Lightbox writes data, an engine-side script builds the asset — is
/// not available. The roadmap had this item blocked on whether to write <c>.yy</c> project
/// files: JSON, so writable in principle, but version-coupled and carrying GUIDs the IDE
/// expects to own, where a file written against the wrong release produces a project that
/// will not open.
/// </para>
/// <para>
/// <b>It is unblocked by the <c>_stripN</c> filename convention, which makes <c>.yy</c>
/// unnecessary.</b> A strip named <c>walk_strip8.png</c> is sliced into eight frames on
/// import, with the cell width taken as the image width divided by eight. It is a naming
/// rule rather than a format, so there is nothing to guess and no version to be wrong
/// about — the same conclusion the Godot decision reached, from the other direction.
/// </para>
/// <para>
/// <b>The layout is forced, not offered, and that is a consequence rather than a
/// preference.</b> Because the cell width is derived <em>by division</em>, the strip must
/// be one row of uniform cells with no padding. So this exporter overrides columns,
/// packing and padding, and refuses a per-frame trim — under any of those the division
/// yields the wrong width and the artist gets frames sliced through the middle, which
/// looks like a drawing error rather than an import setting.
/// </para>
/// <para>
/// <b>One strip per tag, because a GameMaker sprite is one animation.</b> There is no
/// clip list on a sprite to hold several, so a tagged document produces a file per tag and
/// an untagged one produces a single strip. The strips are cut out of the single-row sheet
/// rather than exported one at a time: on one row a tag is a contiguous horizontal span, so
/// a crop is exact, and it keeps this target from reaching into the sheet exporter that
/// four other targets depend on.
/// </para>
/// <para>
/// <b>What GameMaker cannot express is reported rather than dropped.</b> A sprite has one
/// animation speed, no reverse flag and no ping-pong, so a tag's direction and its loop
/// flag have nowhere to go. Those become notes on the result and in the sidecar. The
/// timing itself survives intact, because the sheet already writes one cell per timeline
/// frame — a hold on 2s is two identical cells, which is exactly how a strip expresses it.
/// </para>
/// </remarks>
public static class GameMakerExporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class StripRecord
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";

        [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";

        [JsonPropertyName("frames")] public int Frames { get; set; }

        [JsonPropertyName("from")] public int From { get; set; }

        [JsonPropertyName("to")] public int To { get; set; }
    }

    private sealed class GameMakerBlock
    {
        /// <summary>Every cell is this wide, which is what the convention requires.</summary>
        [JsonPropertyName("frameWidth")] public int FrameWidth { get; set; }

        [JsonPropertyName("frameHeight")] public int FrameHeight { get; set; }

        /// <summary>
        /// The speed to set in the sprite editor, in <b>frames per second</b>.
        /// </summary>
        /// <remarks>
        /// Stated with its unit because the editor offers two — frames per second, and
        /// frames per game frame — and the number means different things under each.
        /// <c>image_speed</c> then multiplies it and defaults to 1, so nothing else needs
        /// setting for the animation to run at the speed it was drawn at.
        /// </remarks>
        [JsonPropertyName("imageFps")] public int ImageFps { get; set; }

        /// <summary>
        /// The sprite origin in pixels from a cell's top-left. Absent with no pivot.
        /// </summary>
        [JsonPropertyName("origin")] public double[]? Origin { get; set; }

        [JsonPropertyName("strips")] public List<StripRecord> Strips { get; set; } = [];

        /// <summary>What GameMaker cannot express about this document. Absent when empty.</summary>
        [JsonPropertyName("notes")] public List<string>? Notes { get; set; }
    }

    public static GameMakerExportResult Export(
        Doc doc, string sheetPath, GameMakerExportOptions? options = null)
    {
        var opts = options ?? new GameMakerExportOptions();
        var scene = doc.Scene;
        var notes = new List<string>();

        var frames = Math.Max(1, scene.FrameCount);
        var trim = opts.Sheet.Trim;
        if (trim == SpriteTrim.PerFrame)
        {
            // Refused rather than honoured: under a per-frame trim the cells differ, and
            // GameMaker derives the cell width by division.
            trim = SpriteTrim.Union;
            notes.Add(
                "A per-frame trim cannot be used for a strip, because GameMaker works out "
                + "the frame width by dividing. Exported with a union trim instead.");
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(sheetPath))!;
        var stem = Path.GetFileNameWithoutExtension(sheetPath);

        // The whole animation as one row, written to the name the untagged case wants.
        // When there are tags this file is cut up and removed again.
        var wholePath = Path.Combine(folder, GameMakerConvert.StripFileName(stem, frames));
        var sheet = SpriteSheetExporter.Export(doc, wholePath, opts.Sheet with
        {
            Trim = trim,
            Pack = SpritePack.Grid,
            Columns = frames,
            Padding = 0,
        });

        using var written = JsonDocument.Parse(File.ReadAllText(sheet.MetadataPath));
        var root = written.RootElement;
        var cells = root.GetProperty("frames").EnumerateArray().ToList();

        var block = new GameMakerBlock
        {
            FrameWidth = sheet.CellWidth,
            FrameHeight = sheet.CellHeight,
            ImageFps = Math.Max(1, scene.Fps),
        };

        if (scene.Pivot is { } pivot && cells.Count > 0)
        {
            var source = cells[0].GetProperty("spriteSourceSize");
            var (ox, oy) = GameMakerConvert.SpriteOrigin(
                pivot.X, pivot.Y,
                source.GetProperty("x").GetInt32(), source.GetProperty("y").GetInt32());
            block.Origin = [ox, oy];
        }

        var spans = SpansFor(root, cells.Count, notes);
        var paths = new List<string>();

        if (spans.Count == 1 && spans[0].Name is null)
        {
            // Untagged: the file already written is the answer, under the right name.
            block.Strips.Add(new StripRecord
            {
                File = Path.GetFileName(wholePath),
                Sprite = GameMakerConvert.ReadStripName(wholePath).Name,
                Frames = cells.Count,
                From = 0,
                To = Math.Max(0, cells.Count - 1),
            });
            paths.Add(wholePath);
        }
        else
        {
            paths.AddRange(CutStrips(wholePath, folder, stem, spans, sheet.CellWidth, block));

            // Only the intermediate this call created a moment ago, and only once every
            // strip cut out of it has been written.
            if (paths.Count > 0 && File.Exists(wholePath)) File.Delete(wholePath);
        }

        if (notes.Count > 0) block.Notes = notes;

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject()) merged[property.Name] = property.Value;
        merged["gamemaker"] = JsonSerializer.SerializeToElement(block, Json);
        File.WriteAllText(sheet.MetadataPath, JsonSerializer.Serialize(merged, Json));

        return new GameMakerExportResult(paths, sheet.MetadataPath, cells.Count, notes, sheet);
    }

    /// <summary>One span per animation; a single unnamed span when there are no tags.</summary>
    private static List<(string? Name, int From, int To)> SpansFor(
        JsonElement root, int cellCount, List<string> notes)
    {
        var last = Math.Max(0, cellCount - 1);
        if (!root.GetProperty("meta").TryGetProperty("frameTags", out var tags)
            || tags.GetArrayLength() == 0)
        {
            return [(null, 0, last)];
        }

        var spans = new List<(string?, int, int)>();
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tag in tags.EnumerateArray())
        {
            var raw = tag.TryGetProperty("name", out var n) ? n.GetString() : null;
            var name = GameMakerConvert.AssetName(raw);
            if (used.TryGetValue(name, out var seen)) name = $"{name}_{seen}";
            used[name] = seen + 1;

            var from = Math.Clamp(tag.GetProperty("from").GetInt32(), 0, last);
            var to = Math.Clamp(tag.GetProperty("to").GetInt32(), from, last);
            spans.Add((name, from, to));

            if (tag.TryGetProperty("direction", out var dir)
                && !string.Equals(dir.GetString(), "forward", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(
                    $"\"{raw}\" plays {dir.GetString()}, which a GameMaker sprite cannot do "
                    + "on its own — drive image_index in code, or draw the return frames.");
            }
            if (tag.TryGetProperty("loop", out var loop) && !loop.GetBoolean())
            {
                notes.Add(
                    $"\"{raw}\" does not loop, and a GameMaker sprite always does — stop it "
                    + "in code on the last frame.");
            }
        }

        return spans;
    }

    /// <summary>
    /// Cut one strip per span out of the single-row sheet.
    /// </summary>
    /// <remarks>
    /// A crop rather than a second export, and exactly correct because the sheet is one
    /// row: a span of cells is a contiguous horizontal band, full height. Re-exporting per
    /// tag would mean teaching the shared sheet exporter about frame ranges, which four
    /// other targets depend on and none of them needs.
    /// </remarks>
    private static List<string> CutStrips(
        string wholePath, string folder, string stem,
        List<(string? Name, int From, int To)> spans, int cellWidth, GameMakerBlock block)
    {
        var paths = new List<string>();

        using var whole = SKBitmap.Decode(wholePath)
            ?? throw new InvalidOperationException($"Could not read {wholePath} back to cut it.");

        foreach (var span in spans)
        {
            var count = span.To - span.From + 1;
            var left = span.From * cellWidth;
            var width = Math.Min(count * cellWidth, Math.Max(0, whole.Width - left));
            if (width <= 0) continue;

            var name = GameMakerConvert.StripFileName($"{stem}_{span.Name}", count);
            var path = Path.Combine(folder, name);

            using var strip = new SKBitmap(width, whole.Height, whole.ColorType, whole.AlphaType);
            using (var canvas = new SKCanvas(strip))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(whole, new SKRect(left, 0, left + width, whole.Height),
                    new SKRect(0, 0, width, whole.Height));
            }

            using var image = SKImage.FromBitmap(strip);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);

            block.Strips.Add(new StripRecord
            {
                File = name,
                Sprite = GameMakerConvert.ReadStripName(name).Name,
                Frames = count,
                From = span.From,
                To = span.To,
            });
            paths.Add(path);
        }

        return paths;
    }
}
