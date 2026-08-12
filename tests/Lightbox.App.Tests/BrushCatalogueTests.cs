using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The brush picker tells you what a brush costs before you pick it.
/// </summary>
/// <remarks>
/// <para>
/// The expressive options — a simulated medium, reading the canvas back,
/// blending the layers underneath — are what this application is betting on
/// for artistic range. They are also the slow ones, and an artist who reaches
/// for one on a 4K canvas at frame 180 should have known that when they chose
/// it rather than finding out from the frame counter.
/// </para>
/// <para>
/// So: most brushes are fast, the costly ones are badged, and the list is
/// grouped so the two kinds read as two kinds.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushCatalogueTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void YouCanDrawAWholePictureWithoutTouchingAnExpressiveBrush()
    {
        // The property that matters, and it is not a count. Seven of the
        // thirteen built-ins are expressive, and that is correct: four of them
        // ARE the simulated media, and smudge, blender and blur read the
        // canvas back because that is what those tools are. Counting would
        // force deleting useful brushes to satisfy a number.
        //
        // What has to hold is that the everyday drawing brushes — the ones you
        // reach for to make a picture rather than to make an effect — cost
        // nothing unusual.
        var everyday = new[] { "Pencil", "Ink", "Soft round", "Airbrush" };
        var presets = BuiltInPresets.Create();

        foreach (var name in everyday)
        {
            var preset = Assert.Single(presets, p => p.Name == name);
            Assert.False(preset.IsExpressive, $"“{name}” is an everyday brush and should be fast");
        }
    }

    [AvaloniaFact]
    public void EverySimulatedMediumHasAFastCounterpart()
    {
        // The catalogue's answer to "I want that look but not that cost". A
        // simulated medium with no flat version leaves an artist who cannot
        // afford it with nothing, which is how a feature becomes a trap.
        var presets = BuiltInPresets.Create();

        foreach (var simulated in presets.Where(p => p.Settings.Medium.Kind != MediumKind.None))
        {
            var stem = simulated.Name.Split(' ')[0];
            Assert.Contains(presets, p =>
                !p.IsExpressive && p.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase));
        }
    }

    [AvaloniaFact]
    public void TheExpressiveOnesAreOnlyMediaAndEffectTools()
    {
        // Named rather than counted, so a plain drawing brush quietly
        // acquiring a medium shows up as a surprise rather than as a number
        // going up by one.
        var unexpected = BuiltInPresets.Create()
            .Where(p => p.IsExpressive)
            .Where(p => p.Settings.Medium.Kind == MediumKind.None
                        && p.Settings.Kind is not (BrushKind.Smudge or BrushKind.Blur))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(unexpected);
    }

    /// <summary>Put a preset in the (isolated) user store, as saving a brush would.</summary>
    private static void SaveUserPreset(BrushPreset preset) =>
        PresetStore.Save(new PresetStore.State { UserPresets = [preset] }, MainViewModel.BrushStorePath);

    [AvaloniaFact]
    public void ThePickerGroupsFastBrushesBeforeExpressiveOnes()
    {
        // The categorisation. Badging each row makes a brush legible; ordering
        // makes the two KINDS legible, so somebody scanning for something cheap
        // does not have to read every row.
        //
        // A user preset is what makes this load-bearing, and it is why the test
        // saves one. The built-in catalogue happens to be declared fast-first
        // already, so on built-ins alone the sort is a no-op and this assertion
        // passes with the sort removed — which it did, until this line.
        SaveUserPreset(new BrushPreset { Name = "My pencil", Settings = new BrushSettings { Size = 4 } });
        var vm = new MainViewModel(null);

        var costs = vm.BrushPresetChoices.Select(p => p.Cost).ToList();

        Assert.Contains(BrushCost.Expressive, costs);
        var firstExpressive = costs.IndexOf(BrushCost.Expressive);
        Assert.DoesNotContain(BrushCost.Fast, costs.Skip(firstExpressive));
    }

    [AvaloniaFact]
    public void AnExpressiveUserPresetJoinsTheExpressiveGroup()
    {
        // The other half: a brush you made yourself is categorised by what it
        // does, not by the fact that you made it.
        SaveUserPreset(new BrushPreset
        {
            Name = "My oil",
            Settings = new BrushSettings { Medium = new MediumSettings { Kind = MediumKind.Oil } },
        });
        var vm = new MainViewModel(null);

        var mine = Assert.Single(vm.BrushPresetChoices, p => p.Name == "My oil");
        Assert.True(mine.IsExpressive);
        var costs = vm.BrushPresetChoices.Select(p => p.Cost).ToList();
        Assert.DoesNotContain(BrushCost.Fast, costs.Skip(costs.IndexOf(BrushCost.Expressive)));
    }

    [AvaloniaFact]
    public void GroupingKeepsEachKindInItsDeclaredOrder()
    {
        // A stable sort, so the list an artist has learned the shape of stays
        // learnable. Reordering brushes among themselves would be a worse
        // change than not grouping at all.
        var declared = BuiltInPresets.Create();
        var vm = new MainViewModel(null);

        foreach (var cost in new[] { BrushCost.Fast, BrushCost.Expressive })
        {
            var expected = declared.Where(p => p.Cost == cost).Select(p => p.Name).ToList();
            var actual = vm.BrushPresetChoices
                .Where(p => p.Cost == cost && declared.Any(d => d.Id == p.Id))
                .Select(p => p.Name)
                .ToList();
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// The badge follows the cost tier exactly: Fast is unbadged, and the two
    /// paid tiers each carry their own glyph. Rewritten for B177, which added
    /// the Textured tier — wet edge and granulation were measured at 57–74% of
    /// the laggy presets' commit cost while badged Fast, so "only the
    /// expressive ones carry a badge" stopped being the true statement.
    /// </summary>
    [AvaloniaFact]
    public void TheBadgeFollowsTheCostTier()
    {
        var vm = new MainViewModel(null);

        Assert.All(vm.BrushPresetChoices, p => Assert.Equal(
            p.Cost switch
            {
                BrushCost.Expressive => "◈",
                BrushCost.Textured => "◇",
                _ => "",
            },
            p.CostBadge));

        // The tier is populated: the presets the artist reported as laggy are
        // the ones wearing the new glyph.
        var textured = vm.BrushPresetChoices
            .Where(p => p.Cost == BrushCost.Textured).Select(p => p.Name).ToList();
        Assert.Contains(textured, name => name.Contains("Pencil", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void ABadgedBrushSaysWhatItIsPayingFor()
    {
        // A badge with no explanation is a shrug. The tooltip has to name the
        // option, because that is the thing the artist can turn off.
        var vm = new MainViewModel(null);

        foreach (var preset in vm.BrushPresetChoices.Where(p => p.IsExpressive))
        {
            Assert.Contains("Expressive", preset.CostTip);
            Assert.NotEqual(
                "Expressive — . Slower on a large canvas; worth it when the mark matters.",
                preset.CostTip);
        }
    }

    [AvaloniaFact]
    public void CostIsNotWrittenIntoTheSavedPreset()
    {
        // Derived, never asserted. A stored answer would be wrong the first
        // time somebody edited the brush, and a badge that lies is worse than
        // no badge at all.
        var preset = new BrushPreset
        {
            Name = "Oil",
            Settings = new BrushSettings { Medium = new MediumSettings { Kind = MediumKind.Oil } },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(preset);

        Assert.DoesNotContain("cost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("badge", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expressive", json, StringComparison.OrdinalIgnoreCase);
    }
}
