using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// One row of the track timeline, as plain data: the view model projects the
/// exposure sheet (and the camera) into these, and <see cref="TrackView"/>
/// draws them. A fresh list arrives on every timeline change, which is what
/// tells the control to re-render.
/// </summary>
/// <param name="Name">The label in the gutter.</param>
/// <param name="Keys">Frames carrying a drawing (or a camera key).</param>
/// <param name="HoldEnds">
/// For each entry in <paramref name="Keys"/>, the last frame its drawing is
/// exposed — the bar the reference draws behind the dot.
/// </param>
/// <param name="Breakdowns">Which of <paramref name="Keys"/> are breakdowns (hollow dots).</param>
/// <param name="Kind">What this row stands for — see <see cref="TrackKind"/>.</param>
public sealed record TrackRow(
    string Name,
    IReadOnlyList<int> Keys,
    IReadOnlyList<int> HoldEnds,
    IReadOnlyList<bool> Breakdowns,
    TrackKind Kind = TrackKind.Layer)
{
    /// <summary>The camera track wears the fixed camera colour.</summary>
    public bool IsCamera => Kind is TrackKind.Camera;

    /// <summary>A pose track: the armature's own keys, or one bone's.</summary>
    public bool IsArmature => Kind is TrackKind.Armature or TrackKind.Bone;
}

/// <summary>What a track row stands for.</summary>
/// <remarks>
/// An enum rather than the two booleans this would otherwise have grown, for
/// Q90's reason one type along: three states of one question read better than
/// a pair of flags whose illegal combination nothing forbids. Only the painter
/// and the host's routing branch on it.
/// </remarks>
public enum TrackKind
{
    /// <summary>A drawing layer's exposure.</summary>
    Layer,

    /// <summary>The camera's keys.</summary>
    Camera,

    /// <summary>The armature summary: every frame where any bone is keyed.</summary>
    Armature,

    /// <summary>One bone's keys, shown when the armature row is expanded.</summary>
    Bone,
}

/// <summary>
/// One clip's span on the timeline (Q57): footage or sound as a bar the hand
/// can take hold of. <paramref name="StripIndex"/> names the reference strip
/// it stands for; -1 for the audio track.
/// </summary>
public sealed record ClipBar(string Name, int Start, int End, int StripIndex);

/// <summary>What a drag on a clip bar means: the body slides, an edge trims.</summary>
public enum ClipEditKind
{
    Slide,
    TrimIn,
    TrimOut,
}

/// <summary>
/// The reference's timeline: one coloured track per layer, drawings as dots,
/// holds as bars, the camera as its own track, a ruler and a playhead. Dots
/// drag to retime; the host answers <see cref="KeyDragged"/> because moving a
/// cel is the document's business, not a view's.
/// </summary>
public class TrackView : Control
{
    public static readonly StyledProperty<IReadOnlyList<TrackRow>?> TracksProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlyList<TrackRow>?>(nameof(Tracks));

    public IReadOnlyList<TrackRow>? Tracks
    {
        get => GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    /// <summary>
    /// The dots that are selected, as (row, frame) pairs.
    /// </summary>
    /// <remarks>
    /// Rows and frames rather than anything richer, because this control is
    /// deliberately ignorant of what a key <em>means</em> — the same reason its
    /// menu event hands the host a row index and lets it decide. The host knows
    /// which row is the camera and which is a bone; all this needs to know is
    /// which dots to light.
    /// </remarks>
    public static readonly StyledProperty<IReadOnlySet<(int Row, int Frame)>?> SelectedDotsProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlySet<(int Row, int Frame)>?>(nameof(SelectedDots));

    public IReadOnlySet<(int Row, int Frame)>? SelectedDots
    {
        get => GetValue(SelectedDotsProperty);
        set => SetValue(SelectedDotsProperty, value);
    }

    /// <summary>Pixels per frame; bound to the same slider the X-sheet uses.</summary>
    public static readonly StyledProperty<double> FrameWidthProperty =
        AvaloniaProperty.Register<TrackView, double>(nameof(FrameWidth), 14);

    public double FrameWidth
    {
        get => GetValue(FrameWidthProperty);
        set => SetValue(FrameWidthProperty, value);
    }

    public static readonly StyledProperty<int> CurrentFrameProperty =
        AvaloniaProperty.Register<TrackView, int>(
            nameof(CurrentFrame), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public int CurrentFrame
    {
        get => GetValue(CurrentFrameProperty);
        set => SetValue(CurrentFrameProperty, value);
    }

    public static readonly StyledProperty<int> FrameCountProperty =
        AvaloniaProperty.Register<TrackView, int>(nameof(FrameCount), 1);

    public int FrameCount
    {
        get => GetValue(FrameCountProperty);
        set => SetValue(FrameCountProperty, value);
    }

    /// <summary>
    /// A key dot was dragged from one frame to another on the given track
    /// (index into <see cref="Tracks"/>). The host retimes the document.
    /// </summary>
    public event Action<int, int, int>? KeyDragged;

    /// <summary>
    /// A right-click landed on a key: the row, the frame, and where to put the
    /// menu. The host decides whether that row has a menu at all — the control
    /// knows where the dots are and nothing about what they mean.
    /// </summary>
    public event Action<int, int, Point>? KeyMenuRequested;

    /// <summary>
    /// A key was clicked with a modifier: (row, frame, toggle, range) — Ctrl
    /// and Shift respectively. The host owns what a key is and therefore what
    /// selecting one means.
    /// </summary>
    public event Action<int, int, bool, bool>? KeySelectRequested;

    /// <summary>
    /// The scratch track's waveform, one min/max pair per frame, or null for
    /// no audio band at all — optional means absent (Q59). Drawn as its own
    /// band under the tracks, where the reference strips put sound.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Core.Audio.AudioPeaks.Peak>?> AudioPeaksProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlyList<Core.Audio.AudioPeaks.Peak>?>(nameof(AudioPeaks));

    public IReadOnlyList<Core.Audio.AudioPeaks.Peak>? AudioPeaks
    {
        get => GetValue(AudioPeaksProperty);
        set => SetValue(AudioPeaksProperty, value);
    }

    /// <summary>
    /// One frame's ink volume, normalised against the shot's largest frame.
    /// A negative <paramref name="Level"/> is a frame with no ink at all.
    /// </summary>
    public readonly record struct VolumeBar(double Level, bool Flagged);

    /// <summary>
    /// The volume checker's per-frame band, one bar per frame, or null for no
    /// band at all — optional means absent, the audio band's rule. Squash and
    /// stretch preserves volume, so a level bar-line is on-model and a step in
    /// it is the drift the checker exists to catch.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<VolumeBar>?> VolumeBarsProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlyList<VolumeBar>?>(nameof(VolumeBars));

    public IReadOnlyList<VolumeBar>? VolumeBars
    {
        get => GetValue(VolumeBarsProperty);
        set => SetValue(VolumeBarsProperty, value);
    }

    public static readonly StyledProperty<string> AudioLabelProperty =
        AvaloniaProperty.Register<TrackView, string>(nameof(AudioLabel), "Audio");

    public string AudioLabel
    {
        get => GetValue(AudioLabelProperty);
        set => SetValue(AudioLabelProperty, value);
    }

    /// <summary>Footage clips shown as draggable bars, one row each (Q57).</summary>
    public static readonly StyledProperty<IReadOnlyList<ClipBar>?> VideoClipsProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlyList<ClipBar>?>(nameof(VideoClips));

    public IReadOnlyList<ClipBar>? VideoClips
    {
        get => GetValue(VideoClipsProperty);
        set => SetValue(VideoClipsProperty, value);
    }

    /// <summary>
    /// The scratch track's sections, drawn as bars around the waveform (Q57):
    /// one for an unsplit clip, one per section after splitting. For these
    /// bars <see cref="ClipBar.StripIndex"/> is the section index.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<ClipBar>?> AudioClipsProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlyList<ClipBar>?>(nameof(AudioClips));

    public IReadOnlyList<ClipBar>? AudioClips
    {
        get => GetValue(AudioClipsProperty);
        set => SetValue(AudioClipsProperty, value);
    }

    /// <summary>A footage bar finished a drag: which bar, what kind, how many frames.</summary>
    public event Action<ClipBar, ClipEditKind, int>? VideoClipEdited;

    /// <summary>An audio bar finished a drag.</summary>
    public event Action<ClipBar, ClipEditKind, int>? AudioClipEdited;

    /// <summary>
    /// A clip bar was right-clicked (Q57): the host opens the section menu —
    /// split at playhead — at the given position. The bool says whether the
    /// bar is audio.
    /// </summary>
    public event Action<ClipBar, bool, Point>? ClipMenuRequested;

    // The reference's geometry, measured off its timeline strip.
    internal const double Gutter = 118;   // track names
    internal const double RulerHeight = 20;
    internal const double RowPitch = 22;
    private const double DotRadius = 4.5;

    /// <summary>The narrowest a pair of ruler numbers may sit, in pixels.</summary>
    private const double MinLabelGap = 40;

    /// <summary>
    /// Ruler numbers, kept between renders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, because the adaptive step made this worth measuring.</b>
    /// Building a <see cref="FormattedText"/> costs about 35 µs, which nobody
    /// notices 42 times — the old fixed-twelve ruler on a 500-frame scene — and
    /// everybody notices 2000 times: 1.07 ms against 78 ms, on a strip that
    /// repaints every time the playhead moves. At 12 fps the whole frame budget
    /// is 83 ms, so a long scene at the widest zoom would have spent it on text
    /// alone.
    /// </para>
    /// <para>
    /// The cache is bounded by the scene's length, since a frame number is only
    /// built when it is drawn — and cleared outright past a ceiling, so a
    /// pathological document cannot turn a drawing aid into a retained heap.
    /// It also has to notice the style changing underneath it: a
    /// <c>FormattedText</c> holds its brush, so a theme change with a live cache
    /// would leave the ruler painted in the old colour.
    /// </para>
    /// </remarks>
    private readonly Dictionary<int, FormattedText> _labels = [];

    private (Typeface Face, double Size, IBrush Brush)? _labelStyle;

    private const int MaxCachedLabels = 4096;

    /// <summary>The label cache, reachable by its test — see TimelineRulerTests.</summary>
    internal FormattedText LabelForTests(int frame, Typeface face, double size, IBrush brush) =>
        Label(frame, face, size, brush);

    private FormattedText Label(int frame, Typeface face, double size, IBrush brush)
    {
        if (_labelStyle is not { } style
            || !style.Face.Equals(face) || style.Size != size || !ReferenceEquals(style.Brush, brush)
            || _labels.Count > MaxCachedLabels)
        {
            _labels.Clear();
            _labelStyle = (face, size, brush);
        }
        if (!_labels.TryGetValue(frame, out var label))
        {
            label = new FormattedText(
                (frame + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, face, size, brush);
            _labels[frame] = label;
        }
        return label;
    }

    /// <summary>
    /// How many frames apart the ruler's numbers are, given how wide a frame is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It used to be twelve, always.</b> That reads well at the narrowest
    /// frame width and falls apart at the widest, where twelve frames is over
    /// eight hundred pixels — a ruler with one number on it. The playhead
    /// carries its own number, so the practical effect was that the only frame
    /// you could read was the one you were standing on, which is precisely what
    /// a ruler is for avoiding.
    /// </para>
    /// <para>
    /// <b>Every step divides or multiplies twelve</b>, so frame 1, 13, 25 are
    /// numbered at every zoom. The reference's cadence is the second, and a
    /// ladder of, say, fives would put the numbers somewhere an animator does
    /// not count from.
    /// </para>
    /// </remarks>
    internal static int LabelStep(double frameWidth)
    {
        foreach (var step in (int[])[1, 2, 3, 4, 6, 12, 24, 48, 96])
        {
            if (step * frameWidth >= MinLabelGap) return step;
        }
        return 192;
    }

    static TrackView()
    {
        AffectsMeasure<TrackView>(
            TracksProperty, FrameWidthProperty, FrameCountProperty, AudioPeaksProperty,
            VideoClipsProperty, VolumeBarsProperty);
        AffectsRender<TrackView>(
            TracksProperty, FrameWidthProperty, CurrentFrameProperty, FrameCountProperty,
            AudioPeaksProperty, VideoClipsProperty, AudioClipsProperty, VolumeBarsProperty,
            SelectedDotsProperty);
    }

    // ---- geometry, static so the tests can hold it still ---------------------

    internal static double XAtFrame(int frame, double frameWidth) =>
        Gutter + (frame + 0.5) * frameWidth;

    internal static int FrameAtX(double x, double frameWidth, int frameCount) =>
        Math.Clamp((int)Math.Floor((x - Gutter) / frameWidth), 0, Math.Max(0, frameCount - 1));

    internal static double YAtRow(int row) => RulerHeight + (row + 0.5) * RowPitch;

    internal static int RowAtY(double y, int rows) =>
        Math.Clamp((int)Math.Floor((y - RulerHeight) / RowPitch), 0, Math.Max(0, rows - 1));

    // ---- colours --------------------------------------------------------------

    /// <summary>
    /// The per-track palette, cycling — the reference gives every track its
    /// own hue so a row is findable by colour before it is read by name. The
    /// camera always takes the orange, matching the reference.
    /// </summary>
    private static readonly Color[] TrackColours =
    [
        Color.Parse("#7B61FF"), // violet
        Color.Parse("#4FA3FF"), // blue
        Color.Parse("#E85FBE"), // magenta
        Color.Parse("#2FD1B9"), // teal
        Color.Parse("#7BD156"), // green
        Color.Parse("#E8C55F"), // gold
    ];

    /// <summary>
    /// The camera's one hue, wherever it shows — the timeline track, the
    /// ruler's key dots, the graph editor's X curve, the Scene panel's path.
    /// Internal so the code-side consumers share this definition; the XAML
    /// side reads its twin, <c>CameraTrack</c> in <c>Palette.axaml</c>.
    /// </summary>
    internal static readonly Color CameraColour = Color.Parse("#FF9F45");

    /// <summary>The rig's fixed colour — the bone chrome's, so the two read as one system.</summary>
    private static readonly Color ArmatureColour = Color.Parse("#7EC8E3");

    internal static Color ColourOf(int row, bool isCamera) =>
        isCamera ? CameraColour : TrackColours[row % TrackColours.Length];

    /// <summary>
    /// The colour a track's dots wear: the camera's and the armature's are
    /// fixed, a layer's rotates through the palette by position.
    /// </summary>
    /// <remarks>
    /// The armature has one colour for the same reason the camera does — it is
    /// one thing wherever it appears, and a rig whose row changed colour when a
    /// layer was added above it would read as a different track.
    /// </remarks>
    internal static Color ColourOf(int row, TrackKind kind) => kind switch
    {
        TrackKind.Camera => CameraColour,
        TrackKind.Armature or TrackKind.Bone => ArmatureColour,
        _ => TrackColours[row % TrackColours.Length],
    };

    // ---- layout ---------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Tracks?.Count ?? 0;
        var clipRows = VideoClips?.Count ?? 0;
        var audioBand = AudioPeaks is { Count: > 0 } ? RowPitch : 0;
        var volumeBand = VolumeBars is { Count: > 0 } ? RowPitch : 0;
        return new Size(
            Gutter + FrameCount * FrameWidth + 24,
            RulerHeight + (rows + clipRows) * RowPitch + audioBand + volumeBand + 6);
    }

    // ---- painting -------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        var tracks = Tracks;
        if (tracks is null || tracks.Count == 0) return;

        var text = new SolidColorBrush(Color.Parse("#A6ABB8"));
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        var typeface = new Typeface(Avalonia.Media.FontFamily.Default);

        // The light grid: a per-frame vertical when the frames are wide enough
        // to want one, and a hairline under each track row. Barely-there — it
        // exists so a dot's frame can be read without counting.
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), 1);
        if (FrameWidth >= 8)
        {
            for (var f = 0; f < FrameCount; f++)
            {
                if (f % 12 == 0) continue;   // the ruler line below already covers it
                var gx = XAtFrame(f, FrameWidth);
                context.DrawLine(grid, new Point(gx, RulerHeight), new Point(gx, Bounds.Height));
            }
        }
        for (var r = 0; r <= tracks.Count; r++)
        {
            var gy = RulerHeight + r * RowPitch;
            context.DrawLine(grid,
                new Point(Gutter, gy), new Point(Gutter + FrameCount * FrameWidth, gy));
        }

        // The structure lines stay on the second's cadence whatever the
        // numbers do: they are the rhythm an animator counts against, and
        // drawing one per frame at a wide zoom would be a grid rather than a
        // ruler.
        for (var f = 0; f < FrameCount; f += 12)
        {
            var x = XAtFrame(f, FrameWidth);
            context.DrawLine(faint, new Point(x, RulerHeight), new Point(x, Bounds.Height));
        }

        // The numbers, as close together as they will legibly go.
        var playhead = XAtFrame(CurrentFrame, FrameWidth);
        for (var f = 0; f < FrameCount; f += LabelStep(FrameWidth))
        {
            var x = XAtFrame(f, FrameWidth);
            // The playhead's chip is drawn last, over everything, and carries
            // its own number. A ruler number underneath it is not hidden so
            // much as half-visible around the edges, which reads as a smudge.
            if (Math.Abs(x - playhead) < 11) continue;
            var label = Label(f, typeface, 10, text);
            context.DrawText(label, new Point(x - label.Width / 2, 2));
        }

        for (var r = 0; r < tracks.Count; r++)
        {
            var track = tracks[r];
            var colour = ColourOf(r, track.Kind);
            var y = YAtRow(r);

            var name = new FormattedText(
                track.Name, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, text);
            context.DrawText(name, new Point(10, y - name.Height / 2));

            // The track line, faint, full width — the rail the dots ride.
            var rail = new Pen(new SolidColorBrush(Color.FromArgb(0x30, colour.R, colour.G, colour.B)), 2);
            context.DrawLine(rail,
                new Point(Gutter, y), new Point(Gutter + FrameCount * FrameWidth, y));

            for (var i = 0; i < track.Keys.Count; i++)
            {
                var key = track.Keys[i];
                // The hold bar first, under its dot.
                var holdEnd = i < track.HoldEnds.Count ? track.HoldEnds[i] : key;
                if (holdEnd > key)
                {
                    var bar = new SolidColorBrush(Color.FromArgb(0x66, colour.R, colour.G, colour.B));
                    context.DrawRectangle(bar, null, new RoundedRect(new Rect(
                        XAtFrame(key, FrameWidth), y - 2.5,
                        (holdEnd - key) * FrameWidth, 5), 2.5));
                }

                var at = new Point(XAtFrame(key, FrameWidth), y);
                var isBreakdown = i < track.Breakdowns.Count && track.Breakdowns[i];
                if (isBreakdown)
                {
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 2), at, DotRadius - 1, DotRadius - 1);
                }
                else
                {
                    context.DrawEllipse(new SolidColorBrush(colour), null, at, DotRadius, DotRadius);
                }

                // A ring around the dot rather than a different fill: a key
                // already says three things by colour and fill — which track it
                // is, and whether it is a breakdown — and a fourth would have to
                // take one of them away. The ring sits outside all of it.
                if (SelectedDots?.Contains((r, key)) == true)
                {
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.White), 1.5),
                        at, DotRadius + 2.5, DotRadius + 2.5);
                }
            }
        }

        // Footage clip bars (Q57): one row per clip, the bar the hand takes
        // hold of — body slides, edges trim.
        var clipColour = Color.Parse("#6F9BE8");
        if (VideoClips is { Count: > 0 } clips)
        {
            for (var j = 0; j < clips.Count; j++)
            {
                var clip = clips[j];
                var y = RulerHeight + (tracks.Count + j) * RowPitch;
                var (start, end) = DraggedSpan(clip, isAudio: false);

                var clipName = new FormattedText(
                    clip.Name, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 11, text);
                context.DrawText(clipName, new Point(10, y + RowPitch / 2 - clipName.Height / 2));

                DrawClipBar(context, clipColour, start, end, y);
            }
        }

        // The scratch track's waveform, its own band under the drawing
        // tracks — where the reference strips put sound. One bar per frame:
        // the band answers "what is under frame 12", not "what does the
        // pressure wave look like", so it is addressed the way frames are.
        if (AudioPeaks is { Count: > 0 } audio)
        {
            var top = AudioBandTop(tracks.Count);
            var mid = top + RowPitch / 2;
            var half = RowPitch / 2 - 3;
            var audioColour = Color.Parse("#63C7A6");
            var wave = new SolidColorBrush(Color.FromArgb(0xB4, audioColour.R, audioColour.G, audioColour.B));

            var audioName = new FormattedText(
                AudioLabel, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, text);
            context.DrawText(audioName, new Point(10, mid - audioName.Height / 2));

            // The clip bars around the waveform (Q57) — one per section —
            // drawn first so the sound reads on top of its own handles.
            if (AudioClips is { Count: > 0 } audioClips)
            {
                foreach (var audioClip in audioClips)
                {
                    var (start, end) = DraggedSpan(audioClip, isAudio: true);
                    DrawClipBar(context, audioColour, start, end, top);
                }
            }

            var gap = FrameWidth >= 4 ? 1.0 : 0.0;
            for (var f = 0; f < Math.Min(audio.Count, FrameCount); f++)
            {
                var p = audio[f];
                var y0 = mid - p.Max * half;
                var y1 = mid - p.Min * half;
                if (y1 - y0 < 1) { y0 = mid - 0.5; y1 = mid + 0.5; }
                context.DrawRectangle(wave, null,
                    new Rect(Gutter + f * FrameWidth, y0, Math.Max(1, FrameWidth - gap), y1 - y0));
            }
        }

        // The volume checker's band, under whatever sound there is: one bar
        // per frame, so drift reads as a step in an otherwise level skyline.
        // Flagged frames are warm — they are the answer, not the data.
        if (VolumeBars is { Count: > 0 } volume)
        {
            var top = VolumeBandTop(tracks.Count);
            var floor = top + RowPitch - 3;
            var span = RowPitch - 6;

            var volumeName = new FormattedText(
                "Volume", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, text);
            context.DrawText(volumeName, new Point(10, top + RowPitch / 2 - volumeName.Height / 2));

            var gap = FrameWidth >= 4 ? 1.0 : 0.0;
            for (var f = 0; f < Math.Min(volume.Count, FrameCount); f++)
            {
                var bar = volume[f];
                if (bar.Level < 0) continue;   // no ink is no bar, not a zero bar
                var height = Math.Max(1, bar.Level * span);
                context.DrawRectangle(bar.Flagged ? VolumeFlaggedBrush : VolumeSteadyBrush, null,
                    new Rect(
                        Gutter + f * FrameWidth, floor - height,
                        Math.Max(1, FrameWidth - gap), height));
            }
        }

        // A dragged dot's ghost, so the hand sees where the drawing will land.
        if (_drag is { } d)
        {
            var colour = ColourOf(d.Row, tracks[d.Row].Kind);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(0x88, colour.R, colour.G, colour.B)), null,
                new Point(XAtFrame(d.ToFrame, FrameWidth), YAtRow(d.Row)), DotRadius, DotRadius);
        }

        // The playhead, over everything.
        var playX = XAtFrame(CurrentFrame, FrameWidth);
        var play = new Pen(new SolidColorBrush(Color.Parse("#B49CFF")), 1.5);
        context.DrawLine(play, new Point(playX, 0), new Point(playX, Bounds.Height));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#7B61FF")), null,
            new RoundedRect(new Rect(playX - 9, 2, 18, 13), 3));
        var frameLabel = new FormattedText(
            (CurrentFrame + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 9, Brushes.White);
        context.DrawText(frameLabel, new Point(playX - frameLabel.Width / 2, 4));
    }

    // ---- clip bars (Q57) --------------------------------------------------------

    private (ClipBar Bar, bool IsAudio, ClipEditKind Kind, int PressFrame, int Delta)? _clipDrag;

    private double AudioBandTop(int trackCount) =>
        RulerHeight + (trackCount + (VideoClips?.Count ?? 0)) * RowPitch;

    /// <summary>Below the audio band when there is one, in its place when not.</summary>
    private double VolumeBandTop(int trackCount) =>
        AudioBandTop(trackCount) + (AudioPeaks is { Count: > 0 } ? RowPitch : 0);

    // Static, because Render runs per frame during playback and a brush per
    // call is an allocation the band does not need. Fixed colours, not theme
    // resources: the bars are data, and data should not change reading with
    // the chrome.
    //
    // IMMUTABLE, and that is load-bearing rather than style: a
    // SolidColorBrush is a mutable AvaloniaObject with thread affinity, so a
    // static one shared by two compositors throws "the calling thread cannot
    // access this object" from whichever renders second — which is exactly
    // how TheReadingsReachBothSurfaces flaked in CI while every local run of
    // the same test passed. The immutable brush has no owner thread and is
    // still one allocation for the process.
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush VolumeSteadyBrush =
        new(Color.Parse("#B44DC4FF"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush VolumeFlaggedBrush =
        new(Color.Parse("#DCFF6A3D"));

    /// <summary>The bar's span with the live drag applied — the ghost the hand follows.</summary>
    private (int Start, int End) DraggedSpan(ClipBar bar, bool isAudio)
    {
        if (_clipDrag is not { } cd || cd.IsAudio != isAudio || cd.Bar != bar)
        {
            return (bar.Start, bar.End);
        }
        return cd.Kind switch
        {
            ClipEditKind.Slide => (bar.Start + cd.Delta, bar.End + cd.Delta),
            ClipEditKind.TrimIn => (Math.Min(bar.Start + cd.Delta, bar.End), bar.End),
            _ => (bar.Start, Math.Max(bar.End + cd.Delta, bar.Start)),
        };
    }

    private void DrawClipBar(DrawingContext context, Color colour, int start, int end, double y)
    {
        var x0 = Gutter + start * FrameWidth;
        var width = Math.Max(FrameWidth, (end - start + 1) * FrameWidth);
        var rect = new Rect(x0, y + 2, width, RowPitch - 4);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(0x30, colour.R, colour.G, colour.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(0xA0, colour.R, colour.G, colour.B)), 1),
            new RoundedRect(rect, 3));
        // Brighter edge handles, so trimming has somewhere to aim.
        var handle = new SolidColorBrush(Color.FromArgb(0xE0, colour.R, colour.G, colour.B));
        context.DrawRectangle(handle, null, new Rect(rect.X, rect.Y, 3, rect.Height));
        context.DrawRectangle(handle, null, new Rect(rect.Right - 3, rect.Y, 3, rect.Height));
    }

    /// <summary>
    /// The frame under x with no clamping — clip drags may aim past the end
    /// of the document (the timeline grows to follow) or left of frame zero.
    /// </summary>
    private int RawFrameAtX(double x) => (int)Math.Floor((x - Gutter) / Math.Max(0.01, FrameWidth));

    private (ClipBar Bar, bool IsAudio, ClipEditKind Kind)? ClipHitAt(Point p, int trackCount)
    {
        // The edge zone grows with the frames so trimming stays hittable
        // zoomed out, and never eats the whole bar zoomed in.
        var edge = Math.Max(5.0, Math.Min(FrameWidth, 12.0));
        if (VideoClips is { Count: > 0 } clips)
        {
            var rowsTop = RulerHeight + trackCount * RowPitch;
            var j = (int)Math.Floor((p.Y - rowsTop) / RowPitch);
            if (j >= 0 && j < clips.Count && SpanHit(p.X, clips[j].Start, clips[j].End, edge) is { } kind)
            {
                return (clips[j], false, kind);
            }
        }
        if (AudioClips is { Count: > 0 } audio && AudioPeaks is { Count: > 0 })
        {
            var top = AudioBandTop(trackCount);
            if (p.Y >= top && p.Y < top + RowPitch)
            {
                // The sections share one band, so the hit is decided by x.
                foreach (var bar in audio)
                {
                    if (SpanHit(p.X, bar.Start, bar.End, edge) is { } kind)
                    {
                        return (bar, true, kind);
                    }
                }
            }
        }
        return null;
    }

    private ClipEditKind? SpanHit(double x, int start, int end, double edge)
    {
        var x0 = Gutter + start * FrameWidth;
        var x1 = Gutter + (end + 1) * FrameWidth;
        if (x < x0 - 2 || x > x1 + 2) return null;
        if (x <= x0 + edge) return ClipEditKind.TrimIn;
        if (x >= x1 - edge) return ClipEditKind.TrimOut;
        return ClipEditKind.Slide;
    }

    // ---- interaction -----------------------------------------------------------

    private (int Row, int FromFrame, int ToFrame)? _drag;
    private bool _scrubbing;

    /// <summary>The key index under the pointer, or null when it missed every dot.</summary>
    internal static int? KeyHit(TrackRow track, int frame, double x, double frameWidth)
    {
        for (var i = 0; i < track.Keys.Count; i++)
        {
            if (Math.Abs(XAtFrame(track.Keys[i], frameWidth) - x) <= 6) return track.Keys[i];
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var tracks = Tracks;
        if (tracks is null || tracks.Count == 0) return;
        var p = e.GetPosition(this);
        if (p.X < Gutter) return;

        if (p.Y > RulerHeight)
        {
            var row = RowAtY(p.Y, tracks.Count);
            if (Math.Abs(p.Y - YAtRow(row)) <= RowPitch / 2 &&
                KeyHit(tracks[row], 0, p.X, FrameWidth) is { } grabbed)
            {
                // Right-click asks for the menu instead of starting a drag.
                // Offered on every row; the host answers on the ones that have
                // something to put in it, which keeps this control ignorant of
                // what a key means.
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                {
                    KeyMenuRequested?.Invoke(row, grabbed, p);
                    e.Handled = true;
                    return;
                }

                // Ctrl and Shift pick rather than drag. A modified click that
                // also armed a drag would retime the key the artist was only
                // adding to a selection, and a stray two-pixel wobble is all
                // that would take.
                var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (ctrl || shift)
                {
                    KeySelectRequested?.Invoke(row, grabbed, ctrl, shift);
                    e.Handled = true;
                    return;
                }

                // A plain press on a key outside the selection makes it the
                // selection, so a drag that follows moves what is highlighted
                // rather than something else. On a key already in it, the
                // selection stands and the drag takes the lot.
                KeySelectRequested?.Invoke(row, grabbed, false, false);
                _drag = (row, grabbed, grabbed);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        // A clip bar under the pointer takes the drag before scrubbing does
        // (Q57) — body slides, edges trim, right-click opens the section menu.
        if (ClipHitAt(p, tracks.Count) is { } hit)
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                ClipMenuRequested?.Invoke(hit.Bar, hit.IsAudio, p);
                e.Handled = true;
                return;
            }
            _clipDrag = (hit.Bar, hit.IsAudio, hit.Kind, RawFrameAtX(p.X), 0);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Anywhere else in the frame area scrubs, ruler included.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _scrubbing = true;
        e.Pointer.Capture(this);
        CurrentFrame = FrameAtX(p.X, FrameWidth, FrameCount);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_clipDrag is { } cd)
        {
            var delta = RawFrameAtX(p.X) - cd.PressFrame;
            if (delta != cd.Delta)
            {
                _clipDrag = cd with { Delta = delta };
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }
        if (_drag is { } d)
        {
            var to = FrameAtX(p.X, FrameWidth, FrameCount);
            if (to != d.ToFrame)
            {
                _drag = (d.Row, d.FromFrame, to);
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            CurrentFrame = FrameAtX(p.X, FrameWidth, FrameCount);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_clipDrag is { } cd)
        {
            _clipDrag = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (cd.Delta != 0)
            {
                if (cd.IsAudio) AudioClipEdited?.Invoke(cd.Bar, cd.Kind, cd.Delta);
                else VideoClipEdited?.Invoke(cd.Bar, cd.Kind, cd.Delta);
            }
            e.Handled = true;
            return;
        }
        if (_drag is { } d)
        {
            _drag = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (d.ToFrame != d.FromFrame) KeyDragged?.Invoke(d.Row, d.FromFrame, d.ToFrame);
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            _scrubbing = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
