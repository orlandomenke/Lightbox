using System.Buffers.Binary;
using System.Text;
using Lightbox.Import;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

public class BrushImportTests
{
    private static void BE32(MemoryStream ms, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        ms.Write(b);
    }

    private static void BE16(MemoryStream ms, short v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        ms.Write(b);
    }

    private static SKBitmap DecodeTip(string b64) => SKBitmap.Decode(Convert.FromBase64String(b64));

    // ---- GBR / GIH ------------------------------------------------------------

    [Fact]
    public void Gbr_ImportsNameSpacingAndTipAlpha()
    {
        var bytes = BrushEngineV2Tests.GbrFixture(size: 16, square: true);
        var brush = Assert.Single(GbrReader.Read(bytes));

        Assert.Equal("fixture", brush.Name);
        Assert.Equal(16, brush.SizePx);
        Assert.Equal(0.2, brush.Spacing, 6); // 20 percent
        using var tip = DecodeTip(brush.TipPngBase64!);
        Assert.Equal(16, tip.Width);
        Assert.Equal(255, tip.GetPixel(8, 8).Alpha); // gray 255 → opaque alpha
    }

    [Fact]
    public void Gih_ImportsMultipleTips()
    {
        var one = BrushEngineV2Tests.GbrFixture(12, true);
        var two = BrushEngineV2Tests.GbrFixture(12, true);
        using var ms = new MemoryStream();
        ms.Write(Encoding.UTF8.GetBytes("pipe brush\n"));
        ms.Write(Encoding.UTF8.GetBytes("2 ncells:2\n"));
        ms.Write(one);
        ms.Write(two);

        var brushes = GihReader.Read(ms.ToArray());
        Assert.Equal(2, brushes.Count);
        Assert.Equal("pipe brush 1", brushes[0].Name);
        Assert.NotNull(brushes[1].TipPngBase64);
    }

    // ---- ABR ------------------------------------------------------------------

    [Fact]
    public void AbrV2_ImportsSampledBrush()
    {
        const int w = 10, h = 10;
        using var ms = new MemoryStream();
        BE16(ms, 2); // version
        BE16(ms, 1); // count

        using var block = new MemoryStream();
        BE32(block, 0); // misc
        BE16(block, 25); // spacing percent
        var name = "PsBrush";
        BE32(block, name.Length);
        block.Write(Encoding.BigEndianUnicode.GetBytes(name));
        block.WriteByte(0); // antialiasing
        BE16(block, 0); BE16(block, 0); BE16(block, h); BE16(block, w); // short bounds
        BE32(block, 0); BE32(block, 0); BE32(block, h); BE32(block, w); // long bounds
        BE16(block, 8); // depth
        for (var i = 0; i < w * h; i++) block.WriteByte(200);

        BE16(ms, 2); // type = sampled
        BE32(ms, (int)block.Length);
        block.WriteTo(ms);

        var brush = Assert.Single(AbrReader.Read(ms.ToArray()));
        Assert.Equal("PsBrush", brush.Name);
        Assert.Equal(10, brush.SizePx);
        Assert.Equal(0.25, brush.Spacing, 6);
        using var tip = DecodeTip(brush.TipPngBase64!);
        Assert.Equal(200, tip.GetPixel(5, 5).Alpha);
    }

    [Fact]
    public void AbrV6_ImportsRawSampledBrush()
    {
        const int w = 8, h = 8;
        using var samp = new MemoryStream();
        // one brush block
        using var brushBody = new MemoryStream();
        brushBody.Write(new byte[47]); // sub-version 1 preamble
        BE32(brushBody, 0); BE32(brushBody, 0); BE32(brushBody, h); BE32(brushBody, w);
        BE16(brushBody, 8); // depth
        brushBody.WriteByte(0); // compression: raw
        for (var i = 0; i < w * h; i++) brushBody.WriteByte(180);

        using var ms = new MemoryStream();
        BE16(ms, 6); // version
        BE16(ms, 1); // sub-version
        ms.Write("8BIM"u8);
        ms.Write("samp"u8);
        BE32(ms, (int)(4 + brushBody.Length)); // section length
        BE32(ms, (int)brushBody.Length);
        brushBody.WriteTo(ms);

        var brush = Assert.Single(AbrReader.Read(ms.ToArray()));
        Assert.Equal(8, brush.SizePx);
        using var tip = DecodeTip(brush.TipPngBase64!);
        Assert.Equal(180, tip.GetPixel(4, 4).Alpha);
    }

    // ---- KPP ------------------------------------------------------------------

    [Fact]
    public void Kpp_ImportsParameterSubset()
    {
        // A minimal PNG with a tEXt chunk keyword "preset".
        var xml = "<Preset name=\"Krita Wet\" paintopid=\"paintbrush\">" +
                  "<param name=\"OpacityValue\" type=\"internal\">0.7</param>" +
                  "<param name=\"FlowValue\" type=\"internal\">0.4</param>" +
                  "<param name=\"brush_definition\" type=\"string\">" +
                  "&lt;Brush type=\"auto_brush\" diameter=\"37.5\" spacing=\"0.08\"/&gt;" +
                  "</param></Preset>";

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); // PNG signature
        var body = Encoding.UTF8.GetBytes("preset\0" + xml);
        BE32(ms, body.Length);
        ms.Write("tEXt"u8);
        ms.Write(body);
        BE32(ms, 0); // fake CRC — the reader doesn't verify

        var brush = KppReader.Read("fallback-name", ms.ToArray());
        Assert.Equal("Krita Wet", brush.Name);
        Assert.Equal(0.7, brush.Opacity, 6);
        Assert.Equal(0.4, brush.Flow, 6);
        Assert.Equal(37.5, brush.SizePx, 6);
        Assert.Equal(0.08, brush.Spacing, 6);
        Assert.Null(brush.TipPngBase64); // Krita tips are external resources
    }

    [Fact]
    public void UnsupportedExtension_Throws()
    {
        Assert.Throws<FormatException>(() => BrushImport.Read("brush.xyz", [1, 2, 3]));
    }
}
