using Lightbox.Core.Documents;
using Lightbox.Import;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>A PSD turned into a document, with anything lossy said out loud.</summary>
public sealed record PsdImportResult(Doc Document, IReadOnlyList<string> Notes);

/// <summary>
/// Turns a <see cref="PsdImage"/> into a Lightbox <see cref="Doc"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Imported pixels land on <see cref="Frame.PngBase64"/></b> — the baseline
/// that has been in the model all along for "pixels with no stroke provenance",
/// and whose own comment noted that nothing in the application had ever written
/// one. This is its first producer. Invariant 1 is not bent by that: a frame's
/// pixels are <c>baseline + strokes stamped on top</c>, so a PSD layer arrives as
/// a drawing an artist can paint over, and every stroke they add is recorded as a
/// stroke.
/// </para>
/// <para>
/// <b>Baselines are canvas-sized</b> (decided 2026-08-24), even though a PSD
/// layer carries its own smaller bounds, because
/// <c>FrameRasterizer.Materialize</c> draws a baseline stretched across the whole
/// canvas — a layer stored at its own size would be scaled up to fill the frame.
/// The alternative was a nullable rect beside the baseline, which saves memory
/// proportional to real content and changes a type that <c>ImageResize</c>,
/// <c>Crop</c>, <c>Transform</c> and <c>LayerMerge</c> all read. That is a
/// follow-up rather than a prerequisite: PNG collapses the transparent margin, so
/// the file stays reasonable and only decode time pays.
/// </para>
/// <para>
/// <b>One frame, many layers.</b> A PSD is a single image, so the document is one
/// cel deep and as wide as the layer stack. Nothing here invents a timeline.
/// </para>
/// </remarks>
public static class PsdDocumentImport
{
    /// <summary>Read a PSD's bytes straight into a new document.</summary>
    /// <exception cref="FormatException">The bytes are not a readable PSD.</exception>
    /// <exception cref="PsdUnsupportedException">
    /// The file is well-formed and uses features Lightbox cannot represent. The
    /// exception lists every one of them with the Photoshop step that fixes it.
    /// </exception>
    public static PsdImportResult Open(byte[] bytes, string? documentName = null)
    {
        using var psd = PsdReader.Read(bytes);
        return Build(psd, documentName);
    }

    public static PsdImportResult Build(PsdImage psd, string? documentName = null)
    {
        var scene = new Scene
        {
            Name = string.IsNullOrWhiteSpace(documentName) ? "Imported" : documentName,
            Width = psd.Width,
            Height = psd.Height,
            Fps = 12,
            FrameCount = 1,
            // No paper layer is added. A PSD carries its own background as an
            // ordinary opaque layer, and putting Lightbox's paper underneath it
            // would add a layer the artist never had.
            TransparentBackground = true,
        };

        var notes = new List<string>(psd.Notes);
        var scopes = new Stack<GroupScope>();

        foreach (var entry in psd.Layers)
        {
            switch (entry.Role)
            {
                // Reading a PSD bottom-first means a folder's *closing* divider
                // arrives before its contents and the header that names it arrives
                // last. So the divider opens a scope and the header closes it.
                case PsdLayerRole.GroupEnd:
                    scopes.Push(new GroupScope());
                    break;

                case PsdLayerRole.GroupOpen or PsdLayerRole.GroupClosed:
                    CloseGroup(scene, scopes, entry);
                    break;

                default:
                    var layer = BuildLayer(entry, psd.Width, psd.Height);
                    if (layer is null) break;
                    scene.Layers.Add(layer);
                    if (scopes.Count > 0) scopes.Peek().Members.Add(layer);
                    break;
            }
        }

        // A folder whose header never arrived — a malformed or truncated stack.
        // Its members stay in the scene, ungrouped, rather than disappearing.
        if (scopes.Count > 0)
            notes.Add($"{scopes.Count} unterminated layer folder(s) were flattened.");

        if (scene.Layers.Count == 0)
        {
            var flattened = FlattenedLayer(psd);
            if (flattened is not null) scene.Layers.Add(flattened);
        }

        if (scene.Layers.Count == 0)
            throw new FormatException("PSD: no layers and no composite to read.");

        return new PsdImportResult(new Doc
        {
            Scene = scene,
            Palettes = [DocumentFactory.DefaultPalette()],
        }, notes);
    }

    /// <summary>
    /// Turn the innermost open scope into a folder named by its header.
    /// </summary>
    /// <remarks>
    /// Photoshop nests folders and a Lightbox <see cref="LayerGroup"/> is one
    /// level deep, so nesting is flattened and the path is kept in the name
    /// ("Characters / Head"). That loses no pixels: nesting is organisation, and
    /// the only part of it that reaches the image — whether an enclosing folder is
    /// hidden or locked — is folded into the flattened folder instead.
    /// </remarks>
    private static void CloseGroup(Scene scene, Stack<GroupScope> scopes, PsdLayer header)
    {
        if (scopes.Count == 0) return;
        var scope = scopes.Pop();

        var name = header.Name;
        foreach (var outer in scopes) name = $"{outer.Name ?? "Folder"} / {name}";

        var group = new LayerGroup
        {
            Name = name,
            Visible = header.Visible && scopes.All(s => s.Visible),
        };
        scene.LayerGroups.Add(group);
        foreach (var member in scope.Members) member.GroupId = group.Id;

        // The enclosing folder still owns everything this one held, so its own
        // header sees them when it closes.
        if (scopes.Count > 0)
        {
            var parent = scopes.Peek();
            parent.Name ??= header.Name;
            parent.Members.AddRange(scope.Members);
            if (!header.Visible) parent.Visible = false;
        }
    }

    private static Layer? BuildLayer(PsdLayer entry, int canvasWidth, int canvasHeight)
    {
        var blend = PsdBlendMap.For(entry.BlendKey);
        if (blend is null) return null; // the reader refuses these; belt and braces

        var baseline = Baseline(entry, canvasWidth, canvasHeight);
        return new Layer
        {
            Name = entry.Name,
            Kind = LayerKind.Painted,
            Visible = entry.Visible,
            Locked = entry.Locked,
            Opacity = entry.Opacity,
            BlendMode = blend.Value,
            Cels = [new Cel { Frame = new Frame { PngBase64 = baseline } }],
        };
    }

    /// <summary>
    /// A layer's own pixels composited onto a canvas-sized transparent bitmap.
    /// </summary>
    private static string? Baseline(PsdLayer entry, int canvasWidth, int canvasHeight)
    {
        if (entry.Pixels is null) return null;

        var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        // The reader hands back unpremultiplied pixels; drawing them onto this
        // premultiplied surface is where Skia performs the multiply.
        using var image = SKImage.FromBitmap(entry.Pixels);
        canvas.DrawImage(image, entry.Left, entry.Top);
        canvas.Flush();
        return Lightbox.Raster.PngCodec.Encode(bitmap);
    }

    /// <summary>The one layer a flattened PSD amounts to.</summary>
    private static Layer? FlattenedLayer(PsdImage psd)
    {
        if (psd.Composite is null) return null;
        return new Layer
        {
            Name = "Flattened",
            Kind = LayerKind.Painted,
            Cels =
            [
                new Cel
                {
                    Frame = new Frame { PngBase64 = Lightbox.Raster.PngCodec.Encode(psd.Composite) },
                },
            ],
        };
    }

    private sealed class GroupScope
    {
        public string? Name;
        public bool Visible = true;
        public List<Layer> Members { get; } = [];
    }
}
