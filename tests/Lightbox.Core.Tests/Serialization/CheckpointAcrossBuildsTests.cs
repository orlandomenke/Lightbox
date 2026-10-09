using System.Security.Cryptography;
using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A build never vouches for pixels of a record it cannot fully read (Q230's
/// sensitivity review).
/// </summary>
/// <remarks>
/// <para>
/// Keeping a newer build's keys (Q230) made the bytes the two builds hash the
/// same: the older build writes the kept key back exactly where the newer one
/// writes it as a field. So a checkpoint the older build rendered — without
/// the newer option, which it cannot apply — fingerprinted equal in the newer
/// build, which then showed those pixels as the drawing. Stale art, and nothing
/// to say so.
/// </para>
/// <para>
/// So a fingerprint over kept data is marked: this build's own checkpoints of
/// such a record still match each other, and can never match a build that
/// reads the data, in either direction. A record with nothing kept fingerprints
/// exactly as before, so every checkpoint already saved stays valid.
/// </para>
/// </remarks>
public class CheckpointAcrossBuildsTests
{
    private static Doc Drawing(out Frame frame, int strokes = 4)
    {
        var doc = new Doc();
        frame = new Frame();
        for (var i = 0; i < strokes; i++)
        {
            frame.Strokes.Add(new Stroke
            {
                Color = "#203040",
                Brush = new BrushSettings { Size = 3 + i },
                Points = [new StrokePoint(i, 2, 1), new StrokePoint(i + 5, 6, 1)],
            });
        }
        doc.Scene.Layers.Add(new Layer { Name = "L", Cels = [new Cel { Frame = frame }] });
        return doc;
    }

    /// <summary>The fingerprint as it was computed before anything was kept.</summary>
    private static string Plain(Doc doc, Frame frame, int count)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add<T>(T value) => hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(value, DocJson.Compact));
        Add(count);
        // Internal to Core: reached the way the fingerprint reaches it.
        Add(typeof(Doc).GetMethod("RenderShell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(doc, null));
        for (var i = 0; i < count; i++) Add(frame.Strokes[i]);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static Dictionary<string, JsonElement> Later() =>
        new() { ["fromALaterBuild"] = JsonDocument.Parse("1").RootElement.Clone() };

    [Fact]
    public void ARecordWithNothingKeptFingerprintsAsBefore()
    {
        var doc = Drawing(out var frame);
        Assert.Equal(Plain(doc, frame, 4), CheckpointFingerprint.Of(doc, frame, 4));
    }

    [Theory]
    [InlineData("stroke")]
    [InlineData("brush")]
    [InlineData("document")]
    [InlineData("scene")]
    [InlineData("drawing")]
    [InlineData("symbol")]
    [InlineData("a drawing in a symbol")]
    public void KeptDataAnywhereTheFingerprintCoversMarksIt(string where)
    {
        var doc = Drawing(out var frame);
        switch (where)
        {
            case "stroke": frame.Strokes[2].Unknown = Later(); break;
            case "brush": frame.Strokes[1].Brush.Unknown = Later(); break;
            case "document": doc.Unknown = Later(); break;
            case "scene": doc.Scene.Unknown = Later(); break;
            case "drawing": frame.Unknown = Later(); break;
            // Symbols ride in the render shell and are written by their own
            // converter, as the drawings inside them are by theirs.
            case "symbol": doc.Symbols = new() { ["s"] = new Symbol { Id = "s", Unknown = Later() } }; break;
            case "a drawing in a symbol":
                doc.Symbols = new()
                {
                    ["s"] = new Symbol
                    {
                        Id = "s",
                        Layers = [new Layer { Cels = [new Cel { Frame = new Frame { Unknown = Later() } }] }],
                    },
                };
                break;
        }

        var marked = CheckpointFingerprint.Of(doc, frame, 4);

        Assert.NotEqual(Plain(doc, frame, 4), marked);
        // And this build still agrees with itself: its own checkpoint stays usable.
        Assert.Equal(marked, CheckpointFingerprint.Of(doc, frame, 4));
    }

    /// <summary>A stroke past the covered prefix carries nothing the pixels were made from.</summary>
    [Fact]
    public void KeptDataPastTheCoveredStrokesDoesNotMarkIt()
    {
        var doc = Drawing(out var frame);
        frame.Strokes[3].Unknown = Later();
        Assert.Equal(Plain(doc, frame, 3), CheckpointFingerprint.Of(doc, frame, 3));
    }
}
