using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// B414. A baked sample is shared by every copy of its stroke on purpose
/// (<c>DocCloneTests.DeliberatelyShared</c>), so it must never be changed in
/// place: a change would reach every copy, including the ones undo holds.
/// </summary>
public class SharedSampleTests
{
    [Fact]
    public void MovingADrawingLeavesACopyTakenBeforeItWhereItWas()
    {
        var frame = new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Points = [new StrokePoint(10, 10, 1), new StrokePoint(20, 20, 1)],
                    Baked = new BakedSample { PngBase64 = "iVBORw0KGgo=", X = 10, Y = 10 },
                },
            ],
        };
        var before = frame.Clone();

        Assert.True(FrameTranslate.Apply(frame, 5, 5));

        Assert.Equal(15, frame.Strokes[0].Baked!.X);
        Assert.Equal(10, before.Strokes[0].Baked!.X); // the copy undo restores from
        Assert.Equal(10, before.Strokes[0].Baked!.Y);
    }
}
