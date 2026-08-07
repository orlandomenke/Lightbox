using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The line every AI feature has to stay on: a reading is an input to
/// <em>authoring</em>, never to rendering.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/DESIGN-subject-reading.md</c> names this as the reading's first
/// test, before any provider work: <b>delete every reading from a finished
/// document and re-render — it must be byte-identical.</b> The day it fails is
/// the day something started consulting a model's opinion at render time, and
/// invariant 2 is gone.
/// </para>
/// <para>
/// <b>It is meant to look trivially true, and that is the point.</b> Right now
/// nothing in the render path can see a taxonomy, so this test cannot fail
/// without somebody adding a way for it to — which is exactly the change it
/// exists to catch. The camera has the same shape of test for the same reason.
/// </para>
/// </remarks>
public class SubjectReadingIsNotRenderedTests
{
    private const int W = 160;
    private const int H = 96;

    private static List<Stroke> Art() =>
    [
        new()
        {
            Tool = ToolKind.Brush,
            Color = "#2050b0",
            Label = "near-arm",
            Points = [new(20, 20, 0.5), new(70, 40, 0.9), new(120, 30, 0.4)],
            Brush = new BrushSettings { Size = 9, Hardness = 0.7, Spacing = 0.12 },
        },
        new()
        {
            Tool = ToolKind.Brush,
            Color = "#b02050",
            Label = "torso",
            Points = [new(40, 70, 0.8), new(110, 66, 0.6)],
            Brush = new BrushSettings { Size = 14, Hardness = 0.3, Spacing = 0.1 },
        },
    ];

    private static string Render(List<Stroke> strokes)
    {
        using var bitmap = FrameRasterizer.Rasterize(strokes, W, H, 1.0);
        return Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
    }

    [Fact]
    public void DeletingEveryReadingChangesNoPixel()
    {
        var strokes = Art();

        // A project that has read its subject, and the same one after the
        // reading is thrown away. The strokes are untouched in both.
        var read = new ProjectManifest();
        var knight = ProjectFolders.Add(read, "Knight");
        knight.Taxonomy = new SubjectTaxonomy
        {
            Kind = "biped",
            Reviewed = true,
            Parts =
            [
                new SubjectPart { Name = "torso", Depth = 0 },
                new SubjectPart { Name = "near-arm", Parent = "torso", Depth = 2 },
            ],
        };
        var withReading = Render(strokes);

        knight.Taxonomy = null;
        var withoutReading = Render(strokes);

        Assert.Equal(withReading, withoutReading);
    }

    /// <summary>
    /// The stroke labels a reading talks about are already in the record, and
    /// they must not reach a pixel either — otherwise "delete the reading" would
    /// be the wrong test, because the naming would have leaked into the art.
    /// </summary>
    [Fact]
    public void RenamingAPartChangesNoPixel()
    {
        var named = Art();
        var renamed = Art();
        renamed[0].Label = "left-arm";
        renamed[1].Label = null;

        Assert.Equal(Render(named), Render(renamed));
    }
}
