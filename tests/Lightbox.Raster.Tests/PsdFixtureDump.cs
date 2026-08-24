namespace Lightbox.Raster.Tests;

/// <summary>
/// Writes every <see cref="PsdFixture"/> to disk so another PSD implementation
/// can be asked whether they are real Photoshop files.
/// </summary>
/// <remarks>
/// <para>
/// Not a guard, and it asserts nothing — it is the harness for the cross-check
/// <see cref="PsdFixture"/> describes, kept because the check is worth being able
/// to repeat and because rebuilding a dumper from scratch each time is how a
/// check stops being run. Idle unless <c>LIGHTBOX_PSD_DUMP</c> names a directory:
/// </para>
/// <code>
/// LIGHTBOX_PSD_DUMP=/tmp/psd dotnet test tests/Lightbox.Raster.Tests \
///     --filter FullyQualifiedName~PsdFixtureDump
/// python3 -c "from psd_tools import PSDImage; ..."   # compare against the fixtures
/// </code>
/// <para>
/// Its first run earned its keep: every fixture then omitted the image data
/// section, which <c>psd_tools</c> refused outright as corrupt. Real PSDs always
/// carry a flattened composite, and the reader had been tested only against files
/// no other application would open.
/// </para>
/// </remarks>
public class PsdFixtureDump
{
    [Fact]
    public void WriteFixturesForCrossCheckingAgainstAnotherImplementation()
    {
        var dir = Environment.GetEnvironmentVariable("LIGHTBOX_PSD_DUMP");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        foreach (var (name, fixture) in Cases())
        {
            File.WriteAllBytes(Path.Combine(dir, name), fixture.Build());
        }
    }

    private static IEnumerable<(string Name, PsdFixture Fixture)> Cases()
    {
        yield return ("rgb-raw.psd", new PsdFixture
        {
            Width = 8,
            Height = 8,
            Layers =
            {
                PsdLayerFixture.Solid("Background", 20, 40, 60, a: 255, right: 8, bottom: 8),
                PsdLayerFixture.Solid("Patch", 200, 100, 50, a: 128, left: 2, top: 3, right: 6, bottom: 7),
            },
        });

        foreach (var compression in new[]
                 { PsdCompression.Rle, PsdCompression.Zip, PsdCompression.ZipPredicted })
        {
            yield return ($"rgb-{compression}.psd".ToLowerInvariant(), new PsdFixture
            {
                Width = 6,
                Height = 6,
                Layers =
                {
                    PsdLayerFixture.Solid("Compressed", 90, 140, 210, a: 255,
                        right: 6, bottom: 6, compression: compression),
                },
            });
        }

        yield return ("groups.psd", new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Inside", 1, 2, 3, a: 255),
                PsdLayerFixture.Group("Characters", 1),
            },
        });

        yield return ("meta.psd", new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Solid("Shadow", 0, 0, 0, a: 255, blend: "mul ",
                    opacity: 128, visible: false),
            },
        });

        yield return ("deep16.psd", new PsdFixture
        {
            Depth = 16,
            Layers = { PsdLayerFixture.Solid("Deep", 77, 88, 99, a: 255) },
        });

        yield return ("flat.psd", new PsdFixture
        {
            Width = 4, Height = 4, CompositeChannels = 4, CompositeFill = [12, 34, 56, 255],
        });

        yield return ("large.psb", new PsdFixture
        {
            Psb = true,
            Width = 6,
            Height = 6,
            Layers =
            {
                PsdLayerFixture.Solid("Wide rows", 33, 66, 99, a: 255,
                    right: 6, bottom: 6, compression: PsdCompression.Rle),
            },
        });
    }
}
