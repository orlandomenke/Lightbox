using Lightbox.Core.Export;
using Lightbox.Import.Unity;
using Xunit.Abstractions;
using static Lightbox.Raster.Tests.UnityFixture;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The less expected ways a real project differs from a tidy fixture: an editor
/// that saved with a byte-order mark, Windows line endings, a copied asset whose
/// guid now appears twice, art deleted from under its <c>.meta</c>, names with
/// spaces and colons, ids at the edge of 64 bits. Plus three checks that do not
/// depend on guessing inputs at all: a seeded mutation fuzz, invariants over
/// random timings, and agreement with the exporter's own coordinate conversion.
/// </summary>
public class UnityLinkEdgeTests(ITestOutputHelper output)
{
    private static readonly string Sheet = UnityFixture.Guid(1);
    private static readonly string Atlas = UnityFixture.Guid(2);

    private static string RunClip(string guid = "", long id = 101) =>
        Anim("Run", 12, true, 2 / 12.0, [new Track("", new Key(0, guid.Length == 0 ? Sheet : guid, id), new Key(1 / 12.0, guid.Length == 0 ? Sheet : guid, id))]);

    private static string RunMeta(string guid) =>
        MultipleMeta(guid, 100, new Sprite("Run_000", 101, 0, 0, 16, 16));

    // ---- what editors and tools do to the bytes ------------------------------

    [Fact]
    public void AByteOrderMarkDoesNotHideTheFirstLine()
    {
        // Unity never writes one; a text editor or a merge tool sometimes does.
        var bom = "﻿";
        Assert.Equal("Run", UnityAnimReader.Read(bom + RunClip(), "Run.anim").Name);
        Assert.Single(UnityTextureMetaReader.Read(bom + RunMeta(Sheet), "m", "Run", 16, 16).Slices);
    }

    [Fact]
    public void AByteOrderMarkOnDiskIsReadThroughTheScanToo()
    {
        using var p = new Project();
        p.Texture("Assets/Run.png", 16, 16, "﻿" + RunMeta(Sheet));
        p.Clip("Assets/Run.anim", "﻿" + RunClip(), 1);

        var index = UnityProjectScanner.Scan(p.Root);

        var clip = Assert.Single(index.Clips);
        Assert.True(index.Resolve(clip.Clip.Tracks[0].Keys[0].Sprite!.Value).Resolved);
    }

    [Fact]
    public void WindowsLineEndingsReadTheSameAsUnixOnes()
    {
        var unix = UnityAnimReader.Read(RunClip(), "a");
        var windows = UnityAnimReader.Read(RunClip().Replace("\n", "\r\n"), "a");
        Assert.Equal(unix.Tracks[0].Keys, windows.Tracks[0].Keys);
        Assert.Equal(
            UnityTextureMetaReader.Read(RunMeta(Sheet), "m", "R", 16, 16).Slices,
            UnityTextureMetaReader.Read(RunMeta(Sheet).Replace("\n", "\r\n"), "m", "R", 16, 16).Slices);
    }

    [Fact]
    public void AFileWithoutAFinalNewlineLosesNothing()
    {
        var clip = UnityAnimReader.Read(RunClip().TrimEnd('\n'), "a");
        Assert.Equal(2, clip.Tracks[0].Keys.Count);
    }

    [Fact]
    public void AnEmptyFileIsRefusedNotCrashedOn()
    {
        Assert.Throws<FormatException>(() => UnityAnimReader.Read("", "empty.anim"));
        Assert.Throws<FormatException>(() => UnityTextureMetaReader.Read("", "empty.meta", "x", 1, 1));
        Assert.Throws<FormatException>(() => UnityAnimReader.Read("%YAML 1.1\n--- !u!74 &7400000\n", "header.anim"));
    }

    // ---- names, ids and numbers at their edges --------------------------------

    [Fact]
    public void ASpriteNameWithAColonOrQuotesSurvivesUnitysQuoting()
    {
        var meta = MultipleMeta(Atlas, 100, new Sprite("PLACEHOLDER", 1, 0, 0, 4, 4))
            .Replace("name: PLACEHOLDER", "name: 'Run: it''s 1'");
        Assert.Equal("Run: it's 1", UnityTextureMetaReader.Read(meta, "m", "A", 4, 4).Slices.Single().Name);
    }

    [Theory]
    [InlineData(8504722987209535885L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(-1911110885L)]
    public void AnIdAnywhereInSixtyFourBitsResolves(long id)
    {
        // Unity's ids are hashes: large, negative, and never small and tidy.
        using var p = new Project();
        p.Texture("Assets/Run.png", 16, 16, MultipleMeta(Sheet, 100, new Sprite("Run_000", id, 0, 0, 16, 16)));
        p.Clip("Assets/Run.anim", RunClip(Sheet, id), 1);

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(id, index.Resolve(new UnitySpriteRef(Sheet, id)).Slice!.InternalId);
    }

    [Fact]
    public void AnUppercaseGuidInAClipFindsItsLowercaseTexture()
    {
        using var p = new Project();
        p.Texture("Assets/Run.png", 16, 16, RunMeta(Sheet));
        p.Clip("Assets/Run.anim", RunClip(Sheet.ToUpperInvariant()), 1);

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.True(index.Resolve(index.Clips[0].Clip.Tracks[0].Keys[0].Sprite!.Value).Resolved);
    }

    [Fact]
    public void ANonIntegerFrameRateStillLandsEveryKeyOnItsFrame()
    {
        const double rate = 29.97;
        var keys = Enumerable.Range(0, 30).Select(i => new UnitySpriteKey((float)(i / rate), new UnitySpriteRef(Sheet, i + 1))).ToList();

        // Unity stores times as 32-bit floats; that rounding must not move a key.
        var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), rate, 30 / rate);

        Assert.Equal(30, sheet.Exposures.Count);
        Assert.All(sheet.Exposures, e => Assert.Equal(1, e.Length));
        Assert.Empty(sheet.Notes);
    }

    [Fact]
    public void FoldersWithSpacesAndAccentsKeepTheirNames()
    {
        using var p = new Project();
        p.Texture("Assets/Ani mation/Héro Ø/Run.png", 16, 16, RunMeta(Sheet));
        p.Clip("Assets/Ani mation/Héro Ø/Run.anim", RunClip(), 1);

        var clip = Assert.Single(UnityProjectScanner.Scan(p.Root).Clips);

        Assert.Equal("Assets/Ani mation/Héro Ø", clip.Folder);
    }

    // ---- a project that is not quite consistent --------------------------------

    [Fact]
    public void ADuplicatedGuidIsAProblemAndResolvesTheSameWayEveryTime()
    {
        // Copying an asset folder outside Unity copies its .meta, guid and all.
        using var p = new Project();
        p.Texture("Assets/B/Run.png", 16, 16, RunMeta(Sheet));
        p.Texture("Assets/A/Run.png", 16, 16, RunMeta(Sheet));
        p.Clip("Assets/Run.anim", RunClip(), 1);

        var first = UnityProjectScanner.Scan(p.Root);
        var second = UnityProjectScanner.Scan(p.Root);

        Assert.Contains(first.Problems, q => q.Reason.Contains("Assets/A/Run.png") && q.Reason.Contains("Assets/B/Run.png"));
        Assert.Equal("Assets/A/Run.png", first.AssetPath(Sheet));
        Assert.Equal(first.AssetPath(Sheet), second.AssetPath(Sheet));
    }

    [Fact]
    public void ArtDeletedFromUnderItsMetaIsAProblemNotACrash()
    {
        using var p = new Project();
        p.Texture("Assets/Run.png", 16, 16, RunMeta(Sheet));
        p.Clip("Assets/Run.anim", RunClip(), 1);
        var index = UnityProjectScanner.Scan(p.Root);
        File.Delete(Path.Combine(p.Root, "Assets", "Run.png"));

        var r = index.Resolve(new UnitySpriteRef(Sheet, 101));

        Assert.False(r.Resolved);
        Assert.NotNull(r.Problem);
    }

    [Fact]
    public void ASpriteInAPhotoshopFileSaysWhyItCannotBeUsed()
    {
        using var p = new Project();
        p.Write("Assets/Hero.psb", "not really a psb");
        p.Write("Assets/Hero.psb.meta", $"fileFormatVersion: 2\nguid: {Sheet}\nScriptedImporter:\n  x: 1\n");
        p.Clip("Assets/Run.anim", RunClip(), 1);

        var r = UnityProjectScanner.Scan(p.Root).Resolve(new UnitySpriteRef(Sheet, 101));

        Assert.Contains("not a PNG", r.Problem);
    }

    [Fact]
    public void AnImageCalledPngThatIsNotOneIsAProblem()
    {
        using var p = new Project();
        p.Write("Assets/Run.png", "<html>this was a download error page</html>"u8.ToArray());
        p.Write("Assets/Run.png.meta", RunMeta(Sheet));
        p.Clip("Assets/Run.anim", RunClip(), 1);

        var r = UnityProjectScanner.Scan(p.Root).Resolve(new UnitySpriteRef(Sheet, 101));

        Assert.Contains("not a PNG", r.Problem);
    }

    [Fact]
    public void AScanIsTheSameEveryTime()
    {
        using var p = new Project();
        for (var i = 0; i < 12; i++)
        {
            var g = UnityFixture.Guid(500 + i);
            p.Texture($"Assets/C{i % 3}/S{i}.png", 16, 16, RunMeta(g));
            p.Clip($"Assets/C{i % 3}/Clip{i}.anim", RunClip(g), 100 + i);
        }

        var a = UnityProjectScanner.Scan(p.Root);
        var b = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(a.Clips.Select(c => c.RelativePath), b.Clips.Select(c => c.RelativePath));
        Assert.Equal(a.Clips.Select(c => c.RelativePath).Order(StringComparer.OrdinalIgnoreCase), a.Clips.Select(c => c.RelativePath));
    }

    // ---- checks that do not depend on guessing the input -----------------------

    [Fact]
    public void ThousandsOfSeededMutationsOfRealShapedFilesNeverThrowAnythingElse()
    {
        // Each run makes the same mutations (fixed seed), so a failure reproduces.
        var anim = Anim("Run", 12, true, 0.5,
            [new Track("", new Key(0, Sheet, 1), new Key(0.25, Sheet, 2)), new Track("Fx", new Key(0, Atlas, 3), new Key(0.1, null, 0))],
            "  m_PositionCurves:\n  - curve:\n      m_Curve: []\n    path: Body\n", events: 2);
        var meta = MultipleMeta(Atlas, 100, new Sprite("A", 1, 0, 0, 8, 8), new Sprite("B", 2, 8, 0, 8, 8, 9, 0.5, 0.1))
            .Replace("      physicsShape: []\n", "      physicsShape:\n      - - {x: 4, y: 4}\n        - {x: -4, y: 4}\n");
        const string junk = "{}[]-:'\"\t\0 #&*!|>,\\﻿\r";
        var rng = new Random(20261127);
        var refused = 0;
        var read = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (var round = 0; round < 3000; round++)
        {
            var lines = (round % 2 == 0 ? anim : meta).Split('\n').ToList();
            for (var m = rng.Next(1, 4); m > 0; m--)
            {
                var at = rng.Next(lines.Count);
                switch (rng.Next(6))
                {
                    case 0:
                        lines.RemoveAt(at);
                        if (lines.Count == 0) lines.Add("");
                        break;
                    case 1: lines.Insert(at, lines[at]); break;
                    case 2: lines[at] = new string(' ', rng.Next(0, 3)) + lines[at].TrimStart(); break;
                    case 3:
                        var pos = rng.Next(lines[at].Length + 1);
                        lines[at] = lines[at].Insert(pos, junk[rng.Next(junk.Length)].ToString());
                        break;
                    case 4:
                        var other = rng.Next(lines.Count);
                        (lines[at], lines[other]) = (lines[other], lines[at]);
                        break;
                    default:
                        lines = lines.Take(at).ToList();
                        if (lines.Count == 0) lines.Add("");
                        break;
                }
            }
            var text = string.Join('\n', lines);
            var e = Record.Exception(() =>
            {
                if (round % 2 == 0) UnityAnimReader.Read(text, "fuzz.anim");
                else UnityTextureMetaReader.Read(text, "fuzz.meta", "A", 16, 8);
            });
            Assert.True(e is null or FormatException, $"round {round}: {e?.GetType().Name}: {e?.Message}\n{text}");
            if (e is null) read++; else refused++;
        }

        output.WriteLine($"{read} read, {refused} refused, {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "a mutation made the parser slow");
    }

    [Fact]
    public void AnyKeysAtAnyRateMakeExposuresThatTileTheClip()
    {
        var rng = new Random(7);
        for (var round = 0; round < 2000; round++)
        {
            var rate = new[] { 8.0, 12, 24, 29.97, 30, 60 }[rng.Next(6)];
            var keys = Enumerable.Range(0, rng.Next(1, 20))
                .Select(i => new UnitySpriteKey(Math.Round(rng.NextDouble() * 2, 3), rng.Next(5) == 0 ? null : new UnitySpriteRef(Sheet, i)))
                .OrderBy(k => k.Time).ToList();
            var stop = rng.NextDouble() * 2.5;

            var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), rate, stop);

            Assert.NotEmpty(sheet.Exposures);
            for (var k = 0; k < sheet.Exposures.Count; k++)
            {
                var e = sheet.Exposures[k];
                Assert.True(e.Length >= 1, $"round {round}: an exposure of {e.Length}");
                if (k > 0) Assert.Equal(sheet.Exposures[k - 1].Start + sheet.Exposures[k - 1].Length, e.Start);
            }
            // The last key's drawing is the last one shown.
            Assert.Equal(keys[^1].Sprite, sheet.Exposures[^1].Sprite);
        }
    }

    [Fact]
    public void ReadingAPivotUndoesTheExportersConversionForAnyCell()
    {
        // Import and export are two halves of one convention; if they disagree,
        // a redrawn frame lands shifted against the one it replaced. Each cell
        // gets a pivot through the exporter's UnityConvert, and reading it back
        // must give the same pixel.
        var rng = new Random(11);
        for (var round = 0; round < 1000; round++)
        {
            var texW = rng.Next(16, 4096);
            var texH = rng.Next(16, 4096);
            var w = rng.Next(1, Math.Min(texW, 900));
            var h = rng.Next(1, Math.Min(texH, 900));
            var left = rng.Next(0, texW - w + 1);
            var top = rng.Next(0, texH - h + 1);
            var px = left + rng.NextDouble() * w * 1.4 - w * 0.2; // pivots outside the cell are legal
            var py = top + rng.NextDouble() * h * 1.4 - h * 0.2;

            var (nx, ny) = UnityConvert.Pivot(px, py, left, top, w, h);
            var meta = MultipleMeta(Atlas, 100,
                new Sprite("S", 1, left, texH - top - h, w, h, Alignment: 9, PivotX: nx, PivotY: ny));
            var slice = UnityTextureMetaReader.Read(meta, "m", "A", texW, texH).Slices.Single();

            Assert.Equal(top, slice.Top);
            // Unity stores a pivot as a 32-bit float; a hundredth of a pixel is
            // well inside what that keeps, and far below a visible shift.
            Assert.Equal(px, slice.PivotPixel.X, 2);
            Assert.Equal(py, slice.PivotPixel.Y, 2);
        }
    }
}
