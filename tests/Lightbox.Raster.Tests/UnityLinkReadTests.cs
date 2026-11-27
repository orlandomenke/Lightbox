using Lightbox.Import.Unity;
using Xunit.Abstractions;
using static Lightbox.Raster.Tests.UnityFixture;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Reading a Unity project's sprite clips (Q300, docs/DESIGN-unity-link.md):
/// the clip's keys and timing, a texture's slicing in both conventions, and a
/// scan that finds the three shapes a game holds — a grid sheet, a hand-sliced
/// atlas shared by several clips, and one PNG per frame.
/// </summary>
public class UnityLinkReadTests(ITestOutputHelper output)
{
    private static readonly string Sheet = UnityFixture.Guid(1);
    private static readonly string Atlas = UnityFixture.Guid(2);

    // ---- the YAML subset ---------------------------------------------------

    [Fact]
    public void ASequenceWhoseDashesSitAtItsKeysIndentBelongsToThatKey()
    {
        var doc = UnityYaml.Parse("Root:\n  list:\n  - a: 1\n    b: {x: 2, y: [3, 4]}\n  - a: 5\n  after: 6\n", "t").Single();
        var list = doc.Root.At("Root.list")!;

        Assert.Equal(2, list.Items.Count);
        Assert.Equal("2", list.Items[0].At("b.x")!.Text);
        Assert.Equal("4", list.Items[0].At("b.y")!.Items[1].Text);
        Assert.Equal("5", list.Items[1]["a"]!.Text);
        Assert.Equal("6", doc.Root.At("Root.after")!.Text);
    }

    [Fact]
    public void ASequenceOfSequencesIsReadAsOne()
    {
        // How Unity writes a sprite's custom physics shape or outline.
        var doc = UnityYaml.Parse("physicsShape:\n- - {x: 32, y: 16}\n  - {x: -32, y: 16}\n- - {x: 1, y: 2}\nafter: 1\n", "t").Single();
        var shapes = doc.Root["physicsShape"]!;

        Assert.Equal(2, shapes.Items.Count);
        Assert.Equal("-32", shapes.Items[0].Items[1]["x"]!.Text);
        Assert.Equal("2", shapes.Items[1].Items[0]["y"]!.Text);
        Assert.Equal("1", doc.Root["after"]!.Text);
    }

    [Fact]
    public void AQuoteEscapedByABackslashDoesNotCloseTheString()
    {
        Assert.Throws<FormatException>(() => UnityYaml.Parse("a: \"\\\"\n", "t"));
        Assert.Equal("\\", UnityYaml.Parse("a: \"\\\\\"\n", "t").Single().Root["a"]!.Text);
    }

    [Fact]
    public void AWrappedQuotedScalarIsReadWhole()
    {
        var doc = UnityYaml.Parse("m:\n  name: 'it''s a long\n    name'\n  next: 1\n", "t").Single();
        Assert.Equal("it's a long name", doc.Root.At("m.name")!.Text);
        Assert.Equal("1", doc.Root.At("m.next")!.Text);
    }

    [Fact]
    public void BracketsNestedPastTheLimitAreRefusedByName()
    {
        var deep = new string('[', 200) + new string(']', 200);
        var ex = Assert.Throws<FormatException>(() => UnityYaml.Parse("k: " + deep + "\n", "deep.anim"));
        Assert.Contains("deep.anim", ex.Message);
    }

    [Theory]
    [InlineData("a: [}]")]
    [InlineData("a: [}")]
    [InlineData("a: {k: [}]}")]
    [InlineData("a: {k: v]}")]
    [InlineData("a: [x y]]")]
    public void AStrayCloserIsRefusedRatherThanSpunOn(string line)
    {
        // A value step that consumes nothing used to loop, adding an empty item
        // each pass until memory ran out.
        var parse = Task.Run(() => Record.Exception(() => UnityYaml.Parse(line + "\n", "stray.anim")));
        Assert.True(parse.Wait(TimeSpan.FromSeconds(5)), $"'{line}' did not finish");
        Assert.IsType<FormatException>(parse.Result);
    }

    [Fact]
    public void BlockNestingPastTheLimitIsRefusedHoweverItIsReached()
    {
        // Map → dash sequence → "- k:" map → … never passed through the one place
        // depth used to be checked, and a few MB of it overflowed the stack —
        // which nothing can catch. A small stack keeps the input that would prove
        // a regression small; a regression still takes the test host down, which
        // is loud enough.
        var sb = new System.Text.StringBuilder("a:\n");
        for (var i = 0; i < 3000; i++) sb.Append(' ', 2 * i).Append("- k:\n");
        Exception? seen = null;
        var t = new Thread(() => seen = Record.Exception(() => UnityYaml.Parse(sb.ToString(), "deep.anim")), 256 * 1024);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(10)));
        var ex = Assert.IsType<FormatException>(seen);
        Assert.Contains("nested too deeply", ex.Message);
    }

    [Fact]
    public void ABinarySerializedAssetSaysHowToFixIt()
    {
        var ex = Assert.Throws<FormatException>(() => UnityYaml.Parse("\0\0\0binary", "Run.anim"));
        Assert.Contains("Force Text", ex.Message);
    }

    // ---- clips -------------------------------------------------------------

    [Fact]
    public void SpriteKeysComeBackInTimeOrderWithTheirSheetAndRate()
    {
        var text = Anim("Running", 12, loop: true, stopTime: 0.25,
            [new Track("", new Key(1.0 / 12, Sheet, -20), new Key(0, Sheet, -10), new Key(2.0 / 12, Sheet, 30))]);

        var clip = UnityAnimReader.Read(text, "Running.anim");

        Assert.Equal("Running", clip.Name);
        Assert.Equal(12, clip.SampleRate);
        Assert.True(clip.Loop);
        var track = Assert.Single(clip.Tracks);
        Assert.Equal("", track.Path);
        Assert.Equal([-10L, -20L, 30L], track.Keys.Select(k => k.Sprite!.Value.FileId));
        Assert.All(track.Keys, k => Assert.Equal(Sheet, k.Sprite!.Value.Guid));
    }

    [Fact]
    public void EachRendererPathIsItsOwnTrack()
    {
        var text = Anim("DashAttack", 60, loop: false, stopTime: 0.1,
            [new Track("", new Key(0, Atlas, 1)), new Track("Attack", new Key(0, Sheet, 2), new Key(0.05, null, 0))]);

        var clip = UnityAnimReader.Read(text, "DashAttack.anim");

        Assert.False(clip.Loop);
        Assert.Equal(["", "Attack"], clip.Tracks.Select(t => t.Path));
        Assert.Null(clip.Tracks[1].Keys[1].Sprite);
    }

    [Fact]
    public void WhatTheClipAnimatesBesidesSpritesIsNamedRatherThanDropped()
    {
        var position = "  m_PositionCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve: []\n    path: Body\n";
        var text = Anim("Hit", 12, false, 0.5, [new Track("", new Key(0, Sheet, 1))], position, events: 2);

        var clip = UnityAnimReader.Read(text, "Hit.anim");

        Assert.Contains(clip.Skipped, s => s.Contains("position"));
        Assert.Contains(clip.Skipped, s => s.Contains("2 animation event"));
        Assert.Single(clip.Tracks);
    }

    [Fact]
    public void ASpriteCurveWithNoKeysOrABrokenReferenceIsNamed()
    {
        var text = Anim("Odd", 12, false, 0.5, [new Track("Body", new Key(0, Sheet, 1)), new Track("Empty")])
            .Replace("{fileID: 1, guid: " + Sheet, "{fileID: 1, guid: not-a-guid")
            .Replace("  m_CompressedRotationCurves: []\n", "  m_CompressedRotationCurves:\n  - path: Body\n");

        var clip = UnityAnimReader.Read(text, "Odd.anim");

        Assert.Null(Assert.Single(clip.Tracks).Keys[0].Sprite);
        Assert.Contains(clip.Skipped, x => x.Contains("cannot be read"));
        Assert.Contains(clip.Skipped, x => x.Contains("'Empty' with no keys"));
        Assert.Contains(clip.Skipped, x => x.Contains("compressed rotation"));
    }

    [Fact]
    public void TwoComponentsOnOnePathAreToldApartByClass()
    {
        var text = Anim("Mask", 12, false, 0.5, [new Track("Body", new Key(0, Sheet, 1)), new Track("Body", new Key(0, Sheet, 2))]);
        var second = text.LastIndexOf("classID: 212", StringComparison.Ordinal);
        text = text[..second] + "classID: 331" + text[(second + 12)..];

        var clip = UnityAnimReader.Read(text, "Mask.anim");

        Assert.Equal([212, 331], clip.Tracks.Select(t => t.ClassId));
    }

    [Fact]
    public void AClipWithoutSpritesHasNoTracksRatherThanBeingRefused()
    {
        var clip = UnityAnimReader.Read(Anim("Bob", 30, true, 1, []), "Bob.anim");
        Assert.Empty(clip.Tracks);
    }

    [Fact]
    public void SomethingThatIsNotAClipIsRefusedByName()
    {
        var ex = Assert.Throws<FormatException>(() =>
            UnityAnimReader.Read("--- !u!1 &1\nGameObject:\n  m_Name: x\n", "Prefab.anim"));
        Assert.Contains("Prefab.anim", ex.Message);
        Assert.Contains("not an animation clip", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-12")]
    [InlineData("NaN")]
    [InlineData("1e300")]
    public void ASampleRateThatIsNotAFrameRateIsRefused(string rate)
    {
        var text = Anim("X", 12, true, 1, [new Track("", new Key(0, Sheet, 1))])
            .Replace("m_SampleRate: 12", "m_SampleRate: " + rate);
        Assert.Throws<FormatException>(() => UnityAnimReader.Read(text, "X.anim"));
    }

    [Fact]
    public void EveryPrefixOfAClipIsReadOrRefusedNeverACrash()
    {
        var whole = Anim("Running", 12, true, 0.5,
            [new Track("", new Key(0, Sheet, 1), new Key(0.25, Sheet, 2)), new Track("Fx", new Key(0, Atlas, 3))], events: 1);
        var refused = 0;
        // A half-written file is the ordinary way this happens: Unity mid-save, a sync mid-copy.
        for (var cut = 0; cut <= whole.Length; cut++)
        {
            try { UnityAnimReader.Read(whole[..cut], "cut.anim"); }
            catch (FormatException) { refused++; }
        }
        output.WriteLine($"{refused} of {whole.Length + 1} prefixes refused, the rest read");
        Assert.True(refused > 0);
    }

    [Fact]
    public void GarbageIsAFormatErrorNeverAnotherException()
    {
        string[] hostile =
        [
            "AnimationClip:\n  m_PPtrCurves:\n  - curve: {",
            "AnimationClip:\n\t m_Name: tab",
            "AnimationClip:\n  m_SampleRate: 12\n      over: indented\n",
            "--- !u!74 &x\nAnimationClip:\n  m_SampleRate: 12\n  m_PPtrCurves:\n  - curve:\n    - time: -1\n      value: {fileID: 1, guid: " + Sheet + "}\n",
            "{]}",
            "'unclosed",
            "AnimationClip: {m_SampleRate: 12, m_PPtrCurves: [{curve: [{time: x}]}]}",
        ];
        foreach (var h in hostile)
        {
            var e = Record.Exception(() => UnityAnimReader.Read(h, "h.anim"));
            Assert.True(e is null or FormatException, $"{e?.GetType().Name}: {e?.Message} for {h}");
        }
    }

    // ---- timing ------------------------------------------------------------

    [Fact]
    public void KeysOnTheGridBecomeOneFrameEachAndTheLastHoldsToTheStop()
    {
        var keys = Enumerable.Range(0, 8).Select(i => new UnitySpriteKey(i / 12.0, new UnitySpriteRef(Sheet, i + 1))).ToList();

        var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), 12, stopTime: 8 / 12.0);

        Assert.Equal(8, sheet.Exposures.Count);
        Assert.All(sheet.Exposures, e => Assert.Equal(1, e.Length));
        Assert.Equal(8, sheet.FrameCount);
        Assert.Empty(sheet.Notes);
    }

    [Fact]
    public void AHeldKeyIsOneDrawingExposedForSeveralFrames()
    {
        // Animating on 3s at 12 fps: keys every quarter second.
        var keys = new[] { 0.0, 0.25, 0.5 }.Select((t, i) => new UnitySpriteKey(t, new UnitySpriteRef(Sheet, i + 1))).ToList();

        var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), 12, stopTime: 0.75);

        Assert.Equal([(0, 3), (3, 3), (6, 3)], sheet.Exposures.Select(e => (e.Start, e.Length)));
    }

    [Fact]
    public void AKeyBetweenFramesIsPlacedOnTheNearestAndSaysSo()
    {
        var keys = new List<UnitySpriteKey> { new(0, new UnitySpriteRef(Sheet, 1)), new(0.2, new UnitySpriteRef(Sheet, 2)) };

        var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), 12, stopTime: 0.5);

        Assert.Equal(2, sheet.Exposures[1].Start); // 0.2 s × 12 = 2.4 → frame 2
        Assert.Contains(sheet.Notes, n => n.Contains("between frames"));
    }

    [Fact]
    public void TwoKeysOnOneFrameKeepTheLaterAndSaySo()
    {
        var keys = new List<UnitySpriteKey> { new(0, new UnitySpriteRef(Sheet, 1)), new(0.01, new UnitySpriteRef(Sheet, 2)) };

        var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), 12, stopTime: 0.1);

        var only = Assert.Single(sheet.Exposures);
        Assert.Equal(2, only.Sprite!.Value.FileId);
        Assert.Contains(sheet.Notes, n => n.Contains("later one is kept"));
    }

    [Fact]
    public void AOneKeyClipWithNoLengthStillHoldsAFrame()
    {
        var sheet = UnityClipTiming.Exposures(
            new UnitySpriteTrack("", [new UnitySpriteKey(0, new UnitySpriteRef(Sheet, 1))]), 60, stopTime: 0);
        Assert.Equal(1, Assert.Single(sheet.Exposures).Length);
    }

    [Fact]
    public void AClipThatStopsBeforeItsLastKeySaysSo()
    {
        var keys = new List<UnitySpriteKey> { new(0, new UnitySpriteRef(Sheet, 1)), new(0.5, new UnitySpriteRef(Sheet, 2)) };
        var sheet = UnityClipTiming.Exposures(new UnitySpriteTrack("", keys), 12, stopTime: 0.25);
        Assert.Equal(1, sheet.Exposures[^1].Length);
        Assert.Contains(sheet.Notes, n => n.Contains("stops before its last key"));
    }

    // ---- texture slicing ---------------------------------------------------

    [Fact]
    public void ACellsTopIsMeasuredFromTheTextureTopAndItsPivotLandsOnTheFeet()
    {
        // Worked example: a 100 px tall texture, a cell 20 px tall whose bottom is
        // 10 px above the texture's bottom. Its top is 100 − 10 − 20 = 70 px down.
        var meta = MultipleMeta(Atlas, 900, new Sprite("Run_1", 4242, X: 5, Y: 10, W: 30, H: 20, Alignment: 7));

        var tex = UnityTextureMetaReader.Read(meta, "Atlas.png.meta", "Atlas", 64, 100);

        var s = Assert.Single(tex.Slices);
        Assert.Equal(70, s.Top);
        Assert.Equal(4242, s.InternalId);
        Assert.Equal(900, tex.PixelsPerUnit);
        // Bottom-centre: the middle of the cell's bottom edge, in top-left pixels.
        Assert.Equal((20.0, 90.0), s.PivotPixel);
    }

    [Theory]
    [InlineData(0, 0.5, 0.5)]
    [InlineData(1, 0.0, 1.0)]
    [InlineData(6, 0.0, 0.0)]
    [InlineData(8, 1.0, 0.0)]
    public void ANamedAlignmentIsItsPivotWhateverTheCustomFieldSays(int alignment, double px, double py)
    {
        var meta = MultipleMeta(Atlas, 100, new Sprite("A", 1, 0, 0, 10, 10, alignment, PivotX: 0.123, PivotY: 0.456));
        var s = UnityTextureMetaReader.Read(meta, "m", "A", 10, 10).Slices.Single();
        Assert.Equal((px, py), (s.PivotX, s.PivotY));
    }

    [Fact]
    public void ACustomAlignmentUsesThePivotField()
    {
        var meta = MultipleMeta(Atlas, 100, new Sprite("A", 1, 0, 0, 10, 10, Alignment: 9, PivotX: 0.5, PivotY: 0.08));
        var s = UnityTextureMetaReader.Read(meta, "m", "A", 10, 10).Slices.Single();
        Assert.Equal((0.5, 0.08), (s.PivotX, s.PivotY));
    }

    [Fact]
    public void ASingleSpriteTextureIsOneSliceCoveringItWithUnitysFixedId()
    {
        var tex = UnityTextureMetaReader.Read(SingleMeta(Sheet), "Run_03.png.meta", "Run_03", 48, 64);

        var s = Assert.Single(tex.Slices);
        Assert.Equal(UnitySpriteRef.SingleSpriteFileId, s.InternalId);
        Assert.Equal("Run_03", s.Name);
        Assert.Equal((0, 0, 48, 64), (s.UnityX, s.Top, s.Width, s.Height));
    }

    [Fact]
    public void AnOlderMetaFindsItsIdsInTheNameTable()
    {
        var meta = MultipleMeta(Atlas, 100, new Sprite("Old_0", 0, 0, 0, 8, 8))
            .Replace("  internalIDToNameTable: []\n",
                "  internalIDToNameTable:\n  - first:\n      213: 21300000\n    second: Old_0\n");

        var s = UnityTextureMetaReader.Read(meta, "m", "Old", 8, 8).Slices.Single();

        Assert.Equal(21300000, s.InternalId);
    }

    [Fact]
    public void ASpriteWithACustomPhysicsShapeDoesNotBreakItsSheet()
    {
        var meta = MultipleMeta(Atlas, 100, new Sprite("Shaped", 1, 0, 0, 8, 8), new Sprite("Plain", 2, 8, 0, 8, 8))
            .Replace("      outline: []\n      physicsShape: []\n",
                "      outline: []\n      physicsShape:\n      - - {x: 4, y: 4}\n        - {x: -4, y: 4}\n        - {x: 0, y: -4}\n");

        var tex = UnityTextureMetaReader.Read(meta, "m", "A", 16, 8);

        Assert.Equal(["Shaped", "Plain"], tex.Slices.Select(x => x.Name));
    }

    [Fact]
    public void ACellOutsideTheTextureIsAProblemNotASlice()
    {
        // The texture was resized after slicing: writing into this rect would land wrong.
        var meta = MultipleMeta(Atlas, 100, new Sprite("Fits", 1, 0, 0, 8, 8), new Sprite("Spills", 2, 60, 0, 8, 8));

        var tex = UnityTextureMetaReader.Read(meta, "m", "A", 64, 64);

        Assert.Equal(["Fits"], tex.Slices.Select(s => s.Name));
        Assert.Contains(tex.Problems, p => p.Contains("Spills") && p.Contains("outside"));
    }

    [Fact]
    public void AMetaForSomethingElseIsRefused()
    {
        var ex = Assert.Throws<FormatException>(() => UnityTextureMetaReader.Read(
            $"fileFormatVersion: 2\nguid: {Sheet}\nAudioImporter:\n  x: 1\n", "a.wav.meta", "a", 1, 1));
        Assert.Contains("not a texture", ex.Message);
    }

    // ---- PNG header ----------------------------------------------------------

    [Fact]
    public void APngsSizeIsReadFromItsHeaderAlone()
    {
        using var ms = new MemoryStream(Png(37, 21));
        Assert.Equal((37, 21), PngHeader.ReadSize(ms, "p"));
    }

    [Fact]
    public void ATruncatedOrForeignImageIsRefused()
    {
        var png = Png(4, 4);
        Assert.Throws<FormatException>(() => PngHeader.ReadSize(new MemoryStream(png[..20]), "p"));
        Assert.Throws<FormatException>(() => PngHeader.ReadSize(new MemoryStream("GIF89a........................"u8.ToArray()), "p"));
    }

    // ---- the project scan ----------------------------------------------------

    /// <summary>The three shapes, side by side, as one project.</summary>
    private static Project ThreeShapes()
    {
        var p = new Project();

        // 1. A grid sheet with its sidecar: 4 equal cells in a 2×2 grid, bottom-left origin.
        p.Texture("Assets/Animation/Placeholder/Bot/Run.png", 64, 64, MultipleMeta(Sheet, 100,
            new Sprite("Run_000", 101, 0, 32, 32, 32, 9, 0.5, 0.08),
            new Sprite("Run_001", 102, 32, 32, 32, 32, 9, 0.5, 0.08),
            new Sprite("Run_002", 103, 0, 0, 32, 32, 9, 0.5, 0.08),
            new Sprite("Run_003", 104, 32, 0, 32, 32, 9, 0.5, 0.08)));
        p.Write("Assets/Animation/Placeholder/Bot/Run.json", "{\"clip\":\"Run\",\"fps\":12,\"frames\":4}");
        p.Clip("Assets/Animation/Placeholder/Bot/Run.anim", Anim("Run", 12, true, 4 / 12.0,
            [new Track("", Enumerable.Range(0, 4).Select(i => new Key(i / 12.0, Sheet, 101 + i)).ToArray())]), 1);

        // 2. A hand-sliced atlas: cells of different sizes, two clips sharing one.
        p.Texture("Assets/Animation/Hero.png", 200, 100, MultipleMeta(Atlas, 900,
            new Sprite("Idle_1", 201, 0, 0, 40, 60),
            new Sprite("Idle_2", 202, 40, 0, 44, 62),
            new Sprite("Dash_1", 203, 90, 10, 70, 50)));
        p.Clip("Assets/Animation/Player/Idle.anim", Anim("Idle", 12, true, 0.5,
            [new Track("", new Key(0, Atlas, 201), new Key(0.25, Atlas, 202))]), 2);
        p.Clip("Assets/Animation/Player/Dash.anim", Anim("Dash", 60, false, 0.1,
            [new Track("", new Key(0, Atlas, 202), new Key(0.05, Atlas, 203))]), 3);

        // 3. One PNG per frame.
        var frames = new List<Key>();
        for (var i = 0; i < 3; i++)
        {
            var g = UnityFixture.Guid(100 + i);
            p.Texture($"Assets/Animation/Goblin/Walk_{i}.png", 24, 30, SingleMeta(g));
            frames.Add(new Key(i / 8.0, g, UnitySpriteRef.SingleSpriteFileId));
        }
        p.Clip("Assets/Animation/Goblin/Walk.anim", Anim("Walk", 8, true, 3 / 8.0, [new Track("", frames.ToArray())]), 4);

        // Not a sprite clip: animates a transform only.
        p.Clip("Assets/Animation/Camera/Shake.anim", Anim("Shake", 30, false, 1, []), 5);
        return p;
    }

    [Fact]
    public void AFolderWithAssetsAndAVersionFileIsAUnityProject()
    {
        using var p = new Project();
        Assert.True(UnityProjectScanner.LooksLikeUnityProject(p.Root));
        Assert.False(UnityProjectScanner.LooksLikeUnityProject(Path.Combine(p.Root, "Assets")));
    }

    [Fact]
    public void TheScanFindsEverySpriteClipAndOnlyThose()
    {
        using var p = ThreeShapes();

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(
            ["Assets/Animation/Goblin/Walk.anim", "Assets/Animation/Placeholder/Bot/Run.anim",
             "Assets/Animation/Player/Dash.anim", "Assets/Animation/Player/Idle.anim"],
            index.Clips.Select(c => c.RelativePath));
        Assert.Equal("Assets/Animation/Player", index.Clips.Single(c => c.Clip.Name == "Idle").Folder);
        Assert.Empty(index.Problems);
    }

    [Fact]
    public void EveryFrameOfEveryShapeResolvesToItsCell()
    {
        using var p = ThreeShapes();
        var index = UnityProjectScanner.Scan(p.Root);

        foreach (var clip in index.Clips)
            foreach (var key in clip.Clip.Tracks.SelectMany(t => t.Keys))
            {
                var r = index.Resolve(key.Sprite!.Value);
                Assert.True(r.Resolved, $"{clip.RelativePath}: {r.Problem}");
            }

        var run = index.Resolve(new UnitySpriteRef(Sheet, 103));
        Assert.Equal("Assets/Animation/Placeholder/Bot/Run.png", run.TexturePath);
        Assert.Equal(("Run_002", 32), (run.Slice!.Name, run.Slice.Top)); // bottom-left cell: 64 − 0 − 32

        var walk = index.Resolve(index.Clips.Single(c => c.Clip.Name == "Walk").Clip.Tracks[0].Keys[2].Sprite!.Value);
        Assert.Equal("Walk_2", walk.Slice!.Name);
        Assert.Equal((24, 30), (walk.Slice.Width, walk.Slice.Height));
    }

    [Fact]
    public void ASpriteTwoClipsShareIsListedWithBoth()
    {
        using var p = ThreeShapes();
        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(["Assets/Animation/Player/Dash.anim", "Assets/Animation/Player/Idle.anim"],
            index.ClipsUsing(new UnitySpriteRef(Atlas, 202)));
        Assert.Single(index.ClipsUsing(new UnitySpriteRef(Atlas, 201)));
    }

    [Fact]
    public void ABrokenClipIsAProblemAndTheRestAreStillFound()
    {
        using var p = ThreeShapes();
        p.Write("Assets/Animation/Broken.anim", "AnimationClip:\n  m_Sprite: here\n  m_SampleRate: banana\n");

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(4, index.Clips.Count);
        var problem = Assert.Single(index.Problems);
        Assert.Equal("Assets/Animation/Broken.anim", problem.RelativePath);
        Assert.Contains("m_SampleRate", problem.Reason);
    }

    [Fact]
    public void AReferenceThatLeadsNowhereSaysWhichStepFailed()
    {
        using var p = ThreeShapes();
        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Contains("no asset", index.Resolve(new UnitySpriteRef(UnityFixture.Guid(77), 1)).Problem);
        Assert.Contains("no sprite with id", index.Resolve(new UnitySpriteRef(Atlas, 999)).Problem);
    }

    [Fact]
    public void FoldersUnityIgnoresAreIgnoredToo()
    {
        using var p = ThreeShapes();
        p.Clip("Assets/Animation/Old~/Run.anim", Anim("OldRun", 12, true, 0.1, [new Track("", new Key(0, Sheet, 101))]), 6);
        p.Clip("Assets/.hidden/Run.anim", Anim("HiddenRun", 12, true, 0.1, [new Track("", new Key(0, Sheet, 101))]), 7);

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.DoesNotContain(index.Clips, c => c.Clip.Name is "OldRun" or "HiddenRun");
    }

    [Fact]
    public void EverythingUnityIgnoresTheScanIgnores()
    {
        using var p = ThreeShapes();
        var clip = Anim("Ignored", 12, true, 0.1, [new Track("", new Key(0, Sheet, 101))]);
        p.Clip("Assets/cvs/Run.anim", clip, 10);
        p.Clip("Assets/Animation/.Run.anim", clip, 11);
        p.Clip("Assets/Animation/Run.anim.tmp", clip, 12);
        // The Hidden attribute exists only on Windows: elsewhere setting it is a
        // no-op and "hidden" means the leading dot, which the clip above covers.
        if (OperatingSystem.IsWindows())
        {
            p.Clip("Assets/Secret/Run.anim", clip, 13);
            var secret = new DirectoryInfo(Path.Combine(p.Root, "Assets", "Secret"));
            secret.Attributes |= FileAttributes.Hidden;
        }

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(4, index.Clips.Count);
        Assert.DoesNotContain(index.Clips, c => c.Clip.Name == "Ignored");
    }

    [Fact]
    public void AScanStopsAtItsFileLimitAndSaysSo()
    {
        using var p = ThreeShapes();
        var index = UnityProjectScanner.Scan(p.Root, new UnityScanOptions { MaxFiles = 5 });
        Assert.Contains(index.Problems, q => q.Reason.Contains("scan stopped"));
    }

    [Fact]
    public void AClipPastTheSizeLimitIsAProblemWhateverItsLengthClaims()
    {
        using var p = ThreeShapes();
        var index = UnityProjectScanner.Scan(p.Root, new UnityScanOptions { MaxFileBytes = 256 });

        // Every fixture clip is over 256 bytes, so every one is refused by the read itself.
        Assert.Empty(index.Clips);
        Assert.Contains(index.Problems, q => q.Reason.Contains("not read"));
    }

    [Fact]
    public void AMetaWithNoLineBreakIsReadOnlyAsFarAsItsHead()
    {
        using var p = ThreeShapes();
        // 8 MB on one line: the guid pass must not buffer it to find a newline.
        p.Write("Assets/Huge.png.meta", new string('x', 8 * 1024 * 1024));

        var index = UnityProjectScanner.Scan(p.Root);

        Assert.Equal(4, index.Clips.Count);
    }

    [Fact]
    public void LinksAreNotFollowedOutOfTheProject()
    {
        using var p = ThreeShapes();
        using var outside = new Project();
        outside.Clip("Assets/Elsewhere/Run.anim", Anim("Elsewhere", 12, true, 0.1, [new Track("", new Key(0, Sheet, 101))]), 8);

        // A folder inside Assets that leads outside the project.
        MakeDirectoryLink(Path.Combine(p.Root, "Assets", "Linked"), Path.Combine(outside.Root, "Assets"));
        var index = UnityProjectScanner.Scan(p.Root);
        Assert.DoesNotContain(index.Clips, c => c.Clip.Name == "Elsewhere");
        Assert.Equal(4, index.Clips.Count);

        // A project whose Assets is itself a link is refused outright.
        using var hollow = new Project();
        Directory.Delete(Path.Combine(hollow.Root, "Assets"));
        MakeDirectoryLink(Path.Combine(hollow.Root, "Assets"), Path.Combine(outside.Root, "Assets"));
        Assert.Throws<IOException>(() => UnityProjectScanner.Scan(hollow.Root));
    }

    // A junction on Windows needs no privilege, where a symbolic link does; on
    // Linux and macOS a symbolic link needs none.
    private static void MakeDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        Assert.True(Directory.Exists(link), "could not create a junction for the test");
    }

    [Fact]
    public void APathThatClimbsOutOfTheProjectIsRefused()
    {
        using var p = new Project();
        Assert.Null(UnityProjectScanner.Inside(p.Root, "../outside.png"));
        Assert.Null(UnityProjectScanner.Inside(p.Root, "Assets/../../outside.png"));
        Assert.NotNull(UnityProjectScanner.Inside(p.Root, "Assets/Hero.png"));
    }
}
