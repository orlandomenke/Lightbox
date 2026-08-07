using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Ai.Tests;

/// <summary>
/// The taxonomy half of a subject reading: what comes back from a model, what
/// is kept, and what it costs.
/// </summary>
/// <remarks>
/// The reading is an input to <em>authoring</em> and never to rendering, so
/// none of these touch a pixel. The test that guards that line lives in the
/// Raster suite, where a render is available to compare.
/// </remarks>
public class SubjectReadingTests(ITestOutputHelper output)
{
    private static AiResult<string> Reply(object payload) =>
        AiResult<string>.Success(JsonSerializer.Serialize(payload));

    private static AiResult<SubjectTaxonomy> Read(object payload) =>
        StrokeParsing.Subject(Reply(payload), "The model");

    [Fact]
    public void AnOrdinaryReadingSurvivesIntact()
    {
        var result = Read(new
        {
            kind = "Biped",
            parts = new[]
            {
                new { name = "Torso", parent = (string?)null, depth = 0 },
                new { name = "near-arm", parent = (string?)"torso", depth = 2 },
                new { name = "far-arm", parent = (string?)"torso", depth = -1 },
            },
        });

        Assert.Equal(AiOutcome.Success, result.Outcome);
        var taxonomy = result.Value!;
        Assert.Equal("biped", taxonomy.Kind);           // normalised
        Assert.Equal(3, taxonomy.Parts.Count);
        var near = taxonomy.Parts.Single(p => p.Name == "near-arm");
        Assert.Equal("torso", near.Parent);
        Assert.Equal(2, near.Depth);
        // Nothing has reviewed it yet, which is what lets a re-read replace it.
        Assert.False(taxonomy.Reviewed);
    }

    [Fact]
    public void APartNamingAParentNobodyDeclaredIsOrphanedRatherThanKept()
    {
        // A dangling parent is not harmless: the taxonomy block prints it into
        // the next inbetween prompt as a fact about the character.
        var result = Read(new
        {
            kind = "biped",
            parts = new[] { new { name = "hand", parent = "arm", depth = 1 } },
        });

        Assert.Equal(AiOutcome.Success, result.Outcome);
        Assert.Null(Assert.Single(result.Value!.Parts).Parent);
    }

    [Fact]
    public void APartCannotBeItsOwnAncestor()
    {
        // Two parts each claiming the other. Left alone this is a hang in every
        // later walk of the tree, not a wrong answer.
        var result = Read(new
        {
            kind = "biped",
            parts = new[]
            {
                new { name = "a", parent = "b", depth = 0 },
                new { name = "b", parent = "a", depth = 0 },
            },
        });

        Assert.Equal(AiOutcome.Success, result.Outcome);
        Assert.Contains(result.Value!.Parts, p => p.Parent is null);

        // And the tree terminates from every part, which is the property that
        // actually matters — asserted by walking it rather than by inspection.
        foreach (var part in result.Value!.Parts)
        {
            var seen = new HashSet<string> { part.Name };
            var walker = part;
            while (walker.Parent is { } parent)
            {
                Assert.True(seen.Add(parent), "the parent chain loops");
                walker = result.Value!.Parts.Single(p => p.Name == parent);
            }
        }
    }

    [Fact]
    public void TheSameNameTwiceIsOnePart()
    {
        var result = Read(new
        {
            kind = "biped",
            parts = new[]
            {
                new { name = "head", parent = (string?)null, depth = 1 },
                new { name = "Head", parent = (string?)null, depth = 5 },
            },
        });

        Assert.Single(result.Value!.Parts);
    }

    [Fact]
    public void AReadingWithNoPartsIsAFailureRatherThanAnEmptyAnswer()
    {
        // "biped, no parts" is worse than no reading: it would be stored, look
        // like an answer, and add a useless block to every later request.
        var result = Read(new { kind = "biped", parts = Array.Empty<object>() });

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("no parts", result.Message);
        Assert.True(result.Retryable);
    }

    [Fact]
    public void TheSchemaCannotCarryGeometry()
    {
        // The two-halves split only holds if the wire cannot express placement.
        // A schema with a points array would be filled in, and the taxonomy
        // would be stale the first time the character turned round.
        Assert.DoesNotContain("points", StrokeSchemas.SubjectResult);
        Assert.DoesNotContain("\"x\"", StrokeSchemas.SubjectResult);
        Assert.DoesNotContain("region", StrokeSchemas.SubjectResult);
    }

    // ---- what it does to a request ------------------------------------------

    private static InbetweenRequest Request(SubjectTaxonomy? taxonomy) => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Label = "arm", Points = [new(0, 0, 0.5), new(10, 0, 0.5)] }],
        [new Stroke { Label = "arm", Points = [new(0, 50, 0.5), new(10, 50, 0.5)] }],
        [0.5],
        Easing.Linear,
        Taxonomy: taxonomy);

    private static SubjectTaxonomy Knight() => new()
    {
        Kind = "biped",
        Parts =
        [
            new SubjectPart { Name = "torso", Depth = 0 },
            new SubjectPart { Name = "head", Parent = "torso", Depth = 1 },
            new SubjectPart { Name = "near-arm", Parent = "torso", Depth = 2 },
            new SubjectPart { Name = "far-arm", Parent = "torso", Depth = -1 },
        ],
    };

    [Fact]
    public void WithoutATaxonomyTheRequestIsTheOneLightboxAlwaysSent()
    {
        // The absence test, on the wire rather than in the file: a project that
        // never read a subject must pay nothing at all for the feature.
        Assert.DoesNotContain("subject", Prompts.InbetweenUser(Request(null)));
    }

    [Fact]
    public void TheTaxonomyGoesAtTheFrontWhereACachePrefixCanCoverIt()
    {
        // Prompt caching covers a prefix. The taxonomy is the one part of an
        // inbetween request that is identical across every frame of every
        // animation of a character — after the frame data it saves nothing.
        var prompt = Prompts.InbetweenUser(Request(Knight()));

        var subject = prompt.IndexOf("The subject", StringComparison.Ordinal);
        var frames = prompt.IndexOf("keyframeA", StringComparison.Ordinal);

        output.WriteLine($"taxonomy at {subject}, keyframes at {frames}");
        Assert.InRange(subject, 0, frames - 1);
    }

    /// <summary>
    /// A realistic frame pair, so the share below is measured against a request
    /// somebody would actually send.
    /// </summary>
    private static InbetweenRequest RealisticRequest(SubjectTaxonomy? taxonomy)
    {
        static List<Stroke> Frame(double offset) => Enumerable.Range(0, 40)
            .Select(i => new Stroke
            {
                Label = $"part-{i}",
                Points = Enumerable.Range(0, 24)
                    .Select(k => new StrokePoint(20 + i * 7 + k, 40 + offset + k * 1.5, 0.55))
                    .ToList(),
            })
            .ToList();

        return new InbetweenRequest(
            new SceneInfo(1920, 1080, 24), Frame(0), Frame(120), [0.5], Easing.Linear,
            Taxonomy: taxonomy);
    }

    [Fact]
    public void ATaxonomyIsCheapAgainstTheFrameDataItRidesOn()
    {
        // The block is small, so sending it on every inbetween is affordable —
        // it is the READING that had to stop being per-frame, not the block.
        //
        // Measured against BOTH a toy pair and a realistic one, on purpose. The
        // toy makes the taxonomy look like nearly half the request, which is
        // true and useless: the denominator is two two-point strokes. The share
        // that decides anything is the one against a frame somebody would draw.
        var toyWithout = Prompts.InbetweenUser(Request(null)).Length;
        var toyWith = Prompts.InbetweenUser(Request(Knight())).Length;
        var realWithout = Prompts.InbetweenUser(RealisticRequest(null)).Length;
        var realWith = Prompts.InbetweenUser(RealisticRequest(Knight())).Length;

        var block = toyWith - toyWithout;
        output.WriteLine($"taxonomy block {block} B");
        output.WriteLine($"  two-stroke pair:  {toyWithout} B -> {toyWith} B "
                       + $"({100.0 * block / toyWith:F0}% of the request)");
        output.WriteLine($"  40-stroke pair:   {realWithout / 1024.0:F1} KB -> {realWith / 1024.0:F1} KB "
                       + $"({100.0 * (realWith - realWithout) / realWith:F1}% of the request)");

        Assert.True(block < 512, $"the taxonomy block grew to {block} B");
        // The number that matters: on a real frame pair it disappears.
        Assert.True(realWith - realWithout < realWithout / 20,
            "the taxonomy is more than 5% of a realistic request");
    }

    [Fact]
    public void ATaxonomyRoundTripsThroughTheProjectFile()
    {
        var manifest = new ProjectManifest();
        var character = new Character { Name = "Knight", Taxonomy = Knight() };
        manifest.Characters.Add(character);

        var json = JsonSerializer.Serialize(manifest, DocJson.Options);
        var back = JsonSerializer.Deserialize<ProjectManifest>(json, DocJson.Options)!;

        var parts = back.Characters[0].Taxonomy!.Parts;
        Assert.Equal("biped", back.Characters[0].Taxonomy!.Kind);
        Assert.Equal(4, parts.Count);
        Assert.Equal("torso", parts.Single(p => p.Name == "near-arm").Parent);
        Assert.Equal(-1, parts.Single(p => p.Name == "far-arm").Depth);
    }

    [Fact]
    public void ACharacterThatWasNeverReadWritesNoKey()
    {
        // "Optional means absent." Serialize and look, rather than trusting the
        // model — the two ways this has gone wrong before were both invisible
        // from the type.
        var manifest = new ProjectManifest();
        manifest.Characters.Add(new Character { Name = "Knight" });

        var json = JsonSerializer.Serialize(manifest, DocJson.Options);

        Assert.DoesNotContain("\"taxonomy\"", json);
    }

    [Fact]
    public void AnAnimationFindsTheCharacterThatOwnsIt()
    {
        // Getting this wrong reads the WRONG subject, which is worse than
        // reading none: it would look like it worked.
        var walk = new DocumentRef { Name = "walk" };
        var run = new DocumentRef { Name = "run" };
        var knight = new Character { Name = "Knight", Animations = [walk] };
        var goblin = new Character { Name = "Goblin", Animations = [run] };
        var manifest = new ProjectManifest { Characters = [knight, goblin] };

        Assert.Same(knight, manifest.CharacterOwning(walk.Id));
        Assert.Same(goblin, manifest.CharacterOwning(run.Id));
        Assert.Null(manifest.CharacterOwning("no-such-document"));
        Assert.Null(manifest.CharacterOwning(null));
    }

    [Fact]
    public void AnAnimationReachedThroughAVariantStillBelongsToItsCharacter()
    {
        var walk = new DocumentRef { Name = "walk" };
        var armoured = new DocumentRef { Name = "walk-armoured" };
        var knight = new Character
        {
            Name = "Knight",
            Animations = [walk],
            Variants =
            [
                new CharacterVariant
                {
                    Name = "Armoured",
                    AnimationOverrides = { [walk.Id] = armoured },
                },
            ],
        };
        var manifest = new ProjectManifest { Characters = [knight] };

        Assert.Same(knight, manifest.CharacterOwning(armoured.Id));
    }
}
