using Lightbox.Import;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What <see cref="PsdReader"/> promises: read the layers it can represent
/// faithfully, and refuse — by name, all at once — the ones it cannot.
/// </summary>
public class PsdReadTests(ITestOutputHelper output)
{
    private static SKColor PixelAt(PsdLayer layer, int x, int y) =>
        layer.Pixels!.GetPixel(x, y);

    // ---- the shape of a file --------------------------------------------------

    [Fact]
    public void ACanvasComesBackAtThePsdsOwnSize()
    {
        var bytes = new PsdFixture
        {
            Width = 7,
            Height = 5,
            Layers = { PsdLayerFixture.Solid("Base", 10, 20, 30, right: 7, bottom: 5) },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(7, psd.Width);
        Assert.Equal(5, psd.Height);
        Assert.Single(psd.Layers);
    }

    [Fact]
    public void NotAPhotoshopFileIsAFormatErrorRatherThanACrash()
    {
        var ex = Assert.Throws<FormatException>(() => PsdReader.Read("not a psd at all"u8.ToArray()));
        Assert.Contains("8BPS", ex.Message);
    }

    [Fact]
    public void ATruncatedFileIsAFormatErrorRatherThanACrash()
    {
        var whole = new PsdFixture { Layers = { PsdLayerFixture.Solid("Base", 1, 2, 3) } }.Build();
        // Every prefix of a real file: none may throw anything but FormatException,
        // because a half-copied download is the ordinary way this happens.
        for (var cut = 1; cut < whole.Length; cut++)
        {
            var prefix = whole.AsSpan(0, cut).ToArray();
            try
            {
                PsdReader.Read(prefix)?.Dispose();
            }
            catch (FormatException)
            {
            }
            catch (PsdUnsupportedException)
            {
            }
            catch (Exception e)
            {
                Assert.Fail($"Prefix of {cut} bytes threw {e.GetType().Name}: {e.Message}");
            }
        }
    }

    [Fact]
    public void AnImplausibleCanvasIsRefusedBeforeAnythingIsAllocated()
    {
        var bytes = new PsdFixture { Width = 4, Height = 4 }.Build();
        // Rewrite the height field in the header to something no PSD may hold.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(14), 900_000);

        var ex = Assert.Throws<FormatException>(() => PsdReader.Read(bytes));
        Assert.Contains("implausible", ex.Message);
    }

    // ---- pixels ---------------------------------------------------------------

    [Fact]
    public void ChannelsBecomeRgbaAtTheLayersOwnOffset()
    {
        var bytes = new PsdFixture
        {
            Width = 8,
            Height = 8,
            Layers = { PsdLayerFixture.Solid("Patch", 200, 100, 50, left: 2, top: 3, right: 6, bottom: 7) },
        }.Build();

        using var psd = PsdReader.Read(bytes);
        var layer = psd.Layers[0];

        Assert.Equal(2, layer.Left);
        Assert.Equal(3, layer.Top);
        Assert.Equal(4, layer.Width);
        Assert.Equal(4, layer.Height);
        var pixel = PixelAt(layer, 0, 0);
        Assert.Equal(200, pixel.Red);
        Assert.Equal(100, pixel.Green);
        Assert.Equal(50, pixel.Blue);
        Assert.Equal(255, pixel.Alpha);
    }

    [Fact]
    public void AbsentTransparencyMeansOpaqueRatherThanEmpty()
    {
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("No alpha channel", 40, 60, 80, a: null) },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(255, PixelAt(psd.Layers[0], 0, 0).Alpha);
    }

    [Fact]
    public void ATranslucentChannelComesBackUnpremultiplied()
    {
        // Half-transparent pure white, which Photoshop stores as (255,255,255,128).
        // The reader hands back exactly that: it is a reader, and premultiplying
        // here would bake a lossy conversion into the one place that is supposed
        // to be faithful. Skia performs the multiply when these pixels are drawn
        // onto a premultiplied document surface.
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Half white", 255, 255, 255, a: 128) },
        }.Build();

        using var psd = PsdReader.Read(bytes);
        var layer = psd.Layers[0];
        var pixel = PixelAt(layer, 0, 0);

        output.WriteLine($"stored (255,255,255,128) → read ({pixel.Red},{pixel.Green},{pixel.Blue},{pixel.Alpha})");
        Assert.Equal(SKAlphaType.Unpremul, layer.Pixels!.AlphaType);
        Assert.Equal(128, pixel.Alpha);
        Assert.Equal(255, pixel.Red);
        Assert.Equal(255, pixel.Green);
        Assert.Equal(255, pixel.Blue);
    }

    [Theory]
    [InlineData(PsdCompression.Raw)]
    [InlineData(PsdCompression.Rle)]
    [InlineData(PsdCompression.Zip)]
    [InlineData(PsdCompression.ZipPredicted)]
    public void EveryCompressionSchemeDecodesToTheSamePixels(PsdCompression compression)
    {
        var bytes = new PsdFixture
        {
            Width = 6,
            Height = 6,
            Layers =
            {
                PsdLayerFixture.Solid(
                    "Compressed", 90, 140, 210, a: 255, right: 6, bottom: 6, compression: compression),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);
        var layer = psd.Layers[0];

        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                var pixel = PixelAt(layer, x, y);
                Assert.Equal(90, pixel.Red);
                Assert.Equal(140, pixel.Green);
                Assert.Equal(210, pixel.Blue);
            }
        }
    }

    [Fact]
    public void RleSurvivesAGradientRatherThanOnlyAFlatFill()
    {
        // A flat fill is all repeat-runs and a gradient is all literals, so a
        // decoder can pass the fill test with the literal branch broken.
        const int size = 16;
        var ramp = new byte[size * size];
        for (var i = 0; i < ramp.Length; i++) ramp[i] = (byte)(i * 7 % 251);

        var bytes = new PsdFixture
        {
            Width = size,
            Height = size,
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Ramp",
                    Right = size,
                    Bottom = size,
                    Compression = PsdCompression.Rle,
                    Red = ramp,
                    Green = ramp,
                    Blue = ramp,
                },
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                Assert.Equal(ramp[y * size + x], PixelAt(psd.Layers[0], x, y).Red);
            }
        }
    }

    [Fact]
    public void SixteenBitComesDownToEightAndSaysSo()
    {
        var bytes = new PsdFixture
        {
            Depth = 16,
            Layers = { PsdLayerFixture.Solid("Deep", 77, 88, 99, a: 255) },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(77, PixelAt(psd.Layers[0], 0, 0).Red);
        Assert.Contains(psd.Notes, n => n.Contains("16 bits"));
    }

    [Fact]
    public void AGrayscaleDocumentBecomesGrey_NotRedOnly()
    {
        var bytes = new PsdFixture
        {
            ColorMode = 1,
            CompositeChannels = 1,
            Layers =
            {
                new PsdLayerFixture { Name = "Ink", Red = Enumerable.Repeat((byte)120, 16).ToArray() },
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);
        var pixel = PixelAt(psd.Layers[0], 0, 0);

        Assert.Equal(120, pixel.Red);
        Assert.Equal(120, pixel.Green);
        Assert.Equal(120, pixel.Blue);
    }

    // ---- layer metadata -------------------------------------------------------

    [Fact]
    public void NameVisibilityOpacityAndBlendModeAllSurvive()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Solid("Shadow", 0, 0, 0, blend: "mul ", opacity: 128, visible: false),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);
        var layer = psd.Layers[0];

        Assert.Equal("Shadow", layer.Name);
        Assert.False(layer.Visible);
        Assert.Equal("mul ", layer.BlendKey);
        Assert.InRange(layer.Opacity, 0.50, 0.51);
    }

    [Fact]
    public void TheUnicodeNameWinsOverThePascalOne()
    {
        // Photoshop writes both; the Pascal one is Latin-1 and mangles anything
        // outside it, so a layer named in Japanese only survives via `luni`.
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "fallback",
                    UnicodeName = "背景レイヤー",
                    Red = Enumerable.Repeat((byte)1, 16).ToArray(),
                },
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal("背景レイヤー", psd.Layers[0].Name);
    }

    [Fact]
    public void AProtectionFlagLocksTheLayer()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Locked",
                    ProtectionFlags = 1,
                    Red = Enumerable.Repeat((byte)5, 16).ToArray(),
                },
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.True(psd.Layers[0].Locked);
    }

    [Fact]
    public void FoldersArriveAsBracketsInTheOrderThePsdStatesThem()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                PsdLayerFixture.Group("</Layer group>", 3),
                PsdLayerFixture.Solid("Inside", 1, 2, 3),
                PsdLayerFixture.Group("Characters", 1),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(PsdLayerRole.GroupEnd, psd.Layers[0].Role);
        Assert.Equal(PsdLayerRole.Raster, psd.Layers[1].Role);
        Assert.Equal(PsdLayerRole.GroupOpen, psd.Layers[2].Role);
        Assert.Null(psd.Layers[2].Pixels);
        Assert.True(psd.Layers[2].IsGroupMarker);
    }

    [Fact]
    public void PassThroughIsReadRatherThanRefused()
    {
        // Photoshop's default for a new folder. Refusing it would refuse almost
        // every grouped PSD for a distinction Lightbox folders do not make.
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture { Name = "Group", SectionType = 1, BlendKey = "pass" },
                PsdLayerFixture.Solid("Art", 9, 9, 9),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal("pass", psd.Layers[0].BlendKey);
    }

    // ---- a flattened file -----------------------------------------------------

    [Fact]
    public void APsdWithNoLayersIsReadFromItsComposite()
    {
        var bytes = new PsdFixture
        {
            Width = 4,
            Height = 4,
            CompositeChannels = 4,
            CompositeFill = [12, 34, 56, 255],
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Empty(psd.Layers);
        Assert.NotNull(psd.Composite);
        var pixel = psd.Composite!.GetPixel(1, 1);
        Assert.Equal(12, pixel.Red);
        Assert.Equal(34, pixel.Green);
        Assert.Equal(56, pixel.Blue);
    }

    // ---- refusal --------------------------------------------------------------

    [Fact]
    public void ALayerMaskIsRefusedByNameRatherThanIgnored()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Masked",
                    MaskLength = 20,
                    Red = Enumerable.Repeat((byte)7, 16).ToArray(),
                },
            },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        var reason = Assert.Single(ex.Reasons);
        Assert.Equal("A layer mask", reason.Feature);
        Assert.Equal("Masked", reason.LayerName);
        Assert.Contains("Layer Mask", reason.Remedy);
    }

    [Fact]
    public void AClippingMaskIsRefused()
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Clipped",
                    Clipping = 1,
                    Red = Enumerable.Repeat((byte)7, 16).ToArray(),
                },
            },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        Assert.Contains(ex.Reasons, r => r.Feature == "A clipping mask");
    }

    [Theory]
    [InlineData("lfx2", "Layer effects")]
    [InlineData("TySh", "A text layer")]
    [InlineData("SoLd", "A smart object")]
    [InlineData("curv", "A Curves adjustment layer")]
    [InlineData("SoCo", "A solid-colour fill layer")]
    [InlineData("vmsk", "A vector mask")]
    public void EveryFeatureWithNoLightboxModelIsRefusedWithItsOwnName(string key, string feature)
    {
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture
                {
                    Name = "Fancy",
                    ExtraKeys = [key],
                    Red = Enumerable.Repeat((byte)7, 16).ToArray(),
                },
            },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        Assert.Contains(ex.Reasons, r => r.Feature == feature);
    }

    [Fact]
    public void ABlendModeLightboxDoesNotShareIsRefusedByItsDropdownName()
    {
        var bytes = new PsdFixture
        {
            Layers = { PsdLayerFixture.Solid("Burned", 1, 2, 3, blend: "lbrn") },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        Assert.Contains(ex.Reasons, r => r.Feature.Contains("Linear Burn"));
    }

    [Fact]
    public void EveryReasonIsCollectedBeforeRefusing_NotJustTheFirst()
    {
        // The point of the whole design: an artist fixing one problem per attempt
        // gives up long before a production file opens.
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture { Name = "Has a mask", MaskLength = 20, Red = Ones() },
                PsdLayerFixture.Solid("Odd blend", 1, 2, 3, blend: "vLit"),
                new PsdLayerFixture { Name = "Adjustment", ExtraKeys = ["levl"], Red = Ones() },
            },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        Assert.Equal(3, ex.Reasons.Count);
        output.WriteLine(ex.Message);
        Assert.Contains("Has a mask", ex.Message);
        Assert.Contains("Odd blend", ex.Message);
        Assert.Contains("Adjustment", ex.Message);
    }

    [Fact]
    public void ACmykDocumentIsRefusedWithTheConversionThatFixesIt()
    {
        var bytes = new PsdFixture
        {
            ColorMode = 4,
            CompositeChannels = 4,
            Layers = { PsdLayerFixture.Solid("Print", 1, 2, 3) },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        Assert.Contains(ex.Reasons, r => r.Feature.Contains("CMYK") && r.Remedy.Contains("RGB Color"));
    }

    [Fact]
    public void ThirtyTwoBitIsRefusedAndSixteenBitIsNot()
    {
        var deep = new PsdFixture
        {
            Depth = 32,
            Layers = { PsdLayerFixture.Solid("HDR", 1, 2, 3) },
        }.Build();

        var ex = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(deep));
        Assert.Contains(ex.Reasons, r => r.Feature.Contains("32 bits"));

        using var fine = PsdReader.Read(new PsdFixture
        {
            Depth = 16,
            Layers = { PsdLayerFixture.Solid("Deep", 1, 2, 3) },
        }.Build());
        Assert.Single(fine.Layers);
    }

    [Fact]
    public void AGroupMarkersClippingByteIsNotMistakenForAClippingMask()
    {
        // Photoshop writes a clipping byte on folder brackets where it means
        // nothing. Reading it as a refusal would decline every grouped PSD.
        var bytes = new PsdFixture
        {
            Layers =
            {
                new PsdLayerFixture { Name = "Folder", SectionType = 1, Clipping = 1 },
                PsdLayerFixture.Solid("Art", 5, 5, 5),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(2, psd.Layers.Count);
    }

    private static byte[] Ones() => Enumerable.Repeat((byte)1, 16).ToArray();

    // ---- PSB ------------------------------------------------------------------

    [Fact]
    public void TheLargeDocumentFormatReadsThroughTheSameParser()
    {
        var bytes = new PsdFixture
        {
            Psb = true,
            Width = 5,
            Height = 5,
            Layers = { PsdLayerFixture.Solid("Big", 60, 70, 80, a: 255, right: 5, bottom: 5) },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(5, psd.Width);
        Assert.Equal("Big", psd.Layers[0].Name);
        Assert.Equal(60, PixelAt(psd.Layers[0], 0, 0).Red);
    }

    [Fact]
    public void APsbWithRleChannelsUsesWideRowLengths()
    {
        // The one thing that silently differs between the two formats: a PSB's
        // scanline table is int32 per row where a PSD's is int16. Read it the
        // narrow way and every row length is garbage.
        var bytes = new PsdFixture
        {
            Psb = true,
            Width = 6,
            Height = 6,
            Layers =
            {
                PsdLayerFixture.Solid(
                    "Wide rows", 33, 66, 99, a: 255, right: 6, bottom: 6,
                    compression: PsdCompression.Rle),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Equal(33, PixelAt(psd.Layers[0], 0, 0).Red);
        Assert.Equal(99, PixelAt(psd.Layers[0], 5, 5).Blue);
    }
}
