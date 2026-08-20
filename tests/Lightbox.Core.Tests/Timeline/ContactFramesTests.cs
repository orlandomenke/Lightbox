using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// Contact detection (Q135): footfalls read off the lowest ink, linearly —
/// a shot is not a cycle, so frame 0 planted IS a footfall and nothing
/// wraps — and the band rule is the walk analyser's, shared, so the two can
/// never disagree about what "standing on the ground" means.
/// </summary>
public class ContactFramesTests
{
    /// <summary>A layer of 20×50 drawings whose lowest ink is bottoms[i].</summary>
    private static Layer Row(params double[] bottoms)
    {
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        foreach (var bottom in bottoms)
        {
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes =
                    [
                        new Stroke
                        {
                            Points =
                            [
                                new StrokePoint(30, bottom - 50, 1),
                                new StrokePoint(50, bottom, 1),
                            ],
                        },
                    ],
                },
            });
        }
        return layer;
    }

    [Fact]
    public void AFootfallStartsWhereAPlantedDrawingFollowsAnAirborneOne()
    {
        var reading = ContactFrames.Detect(Row(100, 92, 90, 100, 100, 92, 90, 100));

        Assert.NotNull(reading);
        Assert.Equal([0, 3, 7], reading.Starts);
        Assert.Equal([0, 3, 4, 7], reading.PlantedFrames);
    }

    [Fact]
    public void AShotIsNotACycleSoNothingWraps()
    {
        // Planted at both ends: a cycle would read them as one footfall
        // across the seam; a shot reads two — the landing is not the takeoff.
        var reading = ContactFrames.Detect(Row(100, 90, 88, 90, 100));

        Assert.NotNull(reading);
        Assert.Equal([0, 4], reading.Starts);
    }

    [Fact]
    public void TheBandScalesWithTheDrawing()
    {
        // 1.5 px above the ground on a 50 px figure is planted (band is 4%
        // of ink height = 2 px); 4 px above is airborne.
        var reading = ContactFrames.Detect(Row(100, 98.5, 96, 100));

        Assert.NotNull(reading);
        Assert.Equal([0, 3], reading.Starts);
        Assert.Contains(1, reading.PlantedFrames);
        Assert.DoesNotContain(2, reading.PlantedFrames);
    }

    [Fact]
    public void TooFewDrawingsIsNotAReading()
    {
        Assert.Null(ContactFrames.Detect(Row(100, 92, 100)));
    }

    [Fact]
    public void MarkedInFindsContactLabelsWhateverTheirCase()
    {
        var scene = new Scene();
        scene.Markers.Add(new FrameMarker { Frame = 2, Label = "Contact" });
        scene.Markers.Add(new FrameMarker { Frame = 5, Label = "contact " });
        scene.Markers.Add(new FrameMarker { Frame = 7, Label = "check the hand" });
        scene.Markers.Add(new FrameMarker { Frame = 9, Label = "contact" });

        Assert.Equal([2, 5], ContactFrames.MarkedIn(scene, 0, 8));
    }
}
