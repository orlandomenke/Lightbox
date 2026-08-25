using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Replays a recorded input trace through the real canvas handlers, and counts
/// what the artist would have seen.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> B126, B254 and B255 all live on hardware this
/// repository has not got — a Huion pen on Windows, where the driver posts a
/// phantom mouse stream beside the pen and the two trade the canvas dozens of
/// times a second. Nothing headless can produce that stream: a synthesised
/// pointer has integer coordinates, no pressure and one device id, which is
/// precisely the shape that made the bug invisible for months. What <em>can</em>
/// be had is the reporter's own minute, recorded by <see cref="InputTrace"/> and
/// played back exactly. The capture costs one round-trip; this makes it a
/// fixture that runs forever.
/// </para>
/// <para>
/// <b>What it proves, and what it cannot.</b> It drives the events the canvas
/// received, so it holds the application's <em>reaction</em> to them — the leave
/// grace, the ring, the crossing counters, the samples a stroke is built from.
/// It cannot hold anything that happens above the canvas's handlers:
/// pointer-over is recomputed inside Avalonia's input manager, which is where
/// B255's <c>PenEchoFilter</c> attempt died, and no replay from here reaches it.
/// A green run here is not a fixed pen; it is a promise that this capture still
/// comes out the way the fix said it would.
/// </para>
/// <para>
/// <b>Time is the recorded time, not the wall clock</b> — see
/// <c>CanvasControl.HoverClock</c> for why that is not a convenience. Two
/// consequences worth knowing while reading a failure: the replay finishes as
/// fast as the events can be raised, and <c>SettleHover</c> stands in for the
/// 50 ms timer, asked once per gap with the clock already advanced to the far
/// side of it. That is at least as eager as the real timer and never less, so a
/// replay reporting no teardowns is a stronger statement than the application
/// itself has to make.
/// </para>
/// <para>
/// <b>One batch becomes several events, and that is the honest limit.</b> The
/// paint path reads a coalesced batch per move (<c>GetIntermediatePoints</c>),
/// and a synthesised event cannot carry one — so a recorded batch of four is
/// replayed as four moves of one. The <em>stroke</em> that results is the same:
/// the same samples, in the same order, with the same pressures and contact
/// flags, which is what invariants 1 and 2 are about. The <em>cost</em> is not —
/// four deliveries of one point charge four times the per-event overhead — so
/// nothing here should be read as a timing measurement.
/// </para>
/// </remarks>
internal static class InputTraceReplay
{
    /// <summary>What a capture did to the canvas.</summary>
    /// <param name="Replayed">Events actually raised at the control.</param>
    /// <param name="Skipped">
    /// Recorded entries that are not input — cursor decisions, popups, stalls,
    /// notes. Counted rather than dropped silently, because a fixture that
    /// replayed nothing would otherwise pass every assertion about churn.
    /// </param>
    /// <param name="Samples">
    /// Coalesced points delivered. Zero for a v1 capture, which did not record
    /// them, and zero for a minute of pure hover, which has none.
    /// </param>
    /// <param name="HoverTeardowns">
    /// How many times the brush ring went away. <b>The number B126 is about.</b>
    /// </param>
    /// <param name="OutsideCanvas">
    /// Replayed positions that fell outside the canvas this rig built. Nonzero
    /// means the capture was taken against a different canvas than it is being
    /// replayed on, which makes every enter and exit in it mean something else —
    /// the failure this counter exists to make loud rather than subtle.
    /// </param>
    internal readonly record struct Result(
        int Replayed,
        int Skipped,
        int Moves,
        int Samples,
        int Enters,
        int Exits,
        int HoverTeardowns,
        int Devices,
        int OutsideCanvas,
        double Seconds);

    internal static Result Replay(
        InputTraceLog.Capture capture, Window window, CanvasControl canvas) =>
        Replay(capture.Entries, window, canvas, trustRecordedContact: capture.Version >= 2);

    internal static Result Replay(
        IReadOnlyList<InputTrace.Entry> entries, Window window, CanvasControl canvas,
        bool trustRecordedContact = true)
    {
        var pointers = new Dictionary<int, Pointer>();
        var inferredContact = new HashSet<int>();
        var devices = new HashSet<(PointerType Type, int Id)>();
        var pending = new List<InputTrace.Entry>();
        var now = entries.Count > 0 ? entries[0].Seconds : 0;
        var origin = DateTime.UnixEpoch;

        // The canvas reads elapsed time from here, so a departure ages by the
        // gap the capture recorded rather than by how long the harness took.
        canvas.HoverClock = () => origin + TimeSpan.FromSeconds(now);

        int replayed = 0, skipped = 0, moves = 0, samples = 0;
        int enters = 0, exits = 0, teardowns = 0, outside = 0;
        var hadRing = canvas.HasHoverRing;

        foreach (var e in entries)
        {
            // The clock reaches this event before the event does, and the settle
            // is asked in between. That order is the whole of the fidelity here:
            // in the application a DispatcherTimer fires 50 ms into a departure,
            // which is to say *during the gap*, and a return that arrives after
            // it finds the ring already gone. Asking only at events would let a
            // sixteen-second absence be cancelled by the enter that ended it, and
            // the replay would report a strobe-free minute for a capture in which
            // the ring plainly went away.
            now = e.Seconds;
            if (e.DeviceId >= 0) devices.Add((e.Device, e.DeviceId));

            canvas.SettleHover();
            Track();

            // A sample belongs to the move that follows it, and is held until
            // then so the batch arrives in front of its own delivered event
            // rather than behind it.
            if (e.Kind == InputTrace.Kind.Sample) { pending.Add(e); continue; }

            if (e.Kind == InputTrace.Kind.Move && pending.Count > 0)
            {
                foreach (var sample in pending)
                {
                    if (Deliver(sample)) { samples++; moves++; }
                }
                pending.Clear();
                // The delivered move's own point is already in that batch — the
                // canvas read it from GetIntermediatePoints, not from
                // GetCurrentPoint — so raising it again would stamp a dab twice.
                Track();
                Dispatcher.UIThread.RunJobs();
                continue;
            }

            if (!Deliver(e)) { skipped++; continue; }

            switch (e.Kind)
            {
                case InputTrace.Kind.Move: moves++; break;
                case InputTrace.Kind.Enter: enters++; break;
                case InputTrace.Kind.Exit: exits++; break;
            }

            Track();
            if (replayed % 64 == 0) Dispatcher.UIThread.RunJobs();
        }

        // Samples with no move behind them: a capture can end mid-batch, and
        // dropping them would quietly shorten the last stroke.
        foreach (var sample in pending)
        {
            if (Deliver(sample)) { samples++; moves++; }
        }
        Dispatcher.UIThread.RunJobs();

        bool Deliver(in InputTrace.Entry entry)
        {
            if (!Raise(entry, window, canvas, pointers, inferredContact, trustRecordedContact))
            {
                return false;
            }
            replayed++;
            if (entry.DeviceId >= 0 && !canvas.Bounds.Contains(new Point(entry.X, entry.Y))) outside++;
            return true;
        }

        void Track()
        {
            var ring = canvas.HasHoverRing;
            if (hadRing && !ring) teardowns++;
            hadRing = ring;
        }

        var seconds = entries.Count > 0 ? entries[^1].Seconds - entries[0].Seconds : 0;
        return new Result(
            replayed, skipped, moves, samples, enters, exits, teardowns, devices.Count,
            outside, seconds);
    }

    /// <summary>
    /// Turn one recorded entry into the event the canvas received, or say it was
    /// not an input event at all.
    /// </summary>
    private static bool Raise(
        in InputTrace.Entry e, Window window, CanvasControl canvas,
        Dictionary<int, Pointer> pointers, HashSet<int> inferredContact, bool trustRecordedContact)
    {
        if (e.Kind is not (InputTrace.Kind.Move or InputTrace.Kind.Sample
            or InputTrace.Kind.Enter or InputTrace.Kind.Exit
            or InputTrace.Kind.Press or InputTrace.Kind.Release or InputTrace.Kind.CaptureLost))
        {
            return false;
        }

        if (!pointers.TryGetValue(e.DeviceId, out var pointer))
        {
            // Primary is decided by which device arrived first, which is the one
            // fact the trace does not record. It matters to nothing the canvas
            // does with these events, and deciding it by arrival at least keeps a
            // replayed pen and its phantom mouse distinguishable.
            pointer = new Pointer(e.DeviceId, e.Device, pointers.Count == 0);
            pointers[e.DeviceId] = pointer;
        }

        if (e.Kind == InputTrace.Kind.CaptureLost)
        {
            canvas.RaiseEvent(new PointerCaptureLostEventArgs(canvas, pointer));
            return true;
        }

        var at = canvas.TranslatePoint(new Point(e.X, e.Y), window) ?? new Point(e.X, e.Y);
        // The device's own clock where the capture kept it: the speed axis is
        // computed from this, so synthesising it from arrival time would replay
        // a stroke the artist did not draw. Falls back for a v1 capture, which
        // did not record it.
        var timestamp = e.DeviceTime != 0 ? e.DeviceTime : (ulong)Math.Max(0, e.Seconds * 1000);

        switch (e.Kind)
        {
            case InputTrace.Kind.Press:
                inferredContact.Add(e.DeviceId);
                canvas.RaiseEvent(new PointerPressedEventArgs(
                    canvas, pointer, window, at, timestamp,
                    Properties(e, true, PointerUpdateKind.LeftButtonPressed),
                    e.Modifiers));
                return true;

            case InputTrace.Kind.Release:
                canvas.RaiseEvent(new PointerReleasedEventArgs(
                    canvas, pointer, window, at, timestamp,
                    Properties(e, false, PointerUpdateKind.LeftButtonReleased),
                    e.Modifiers, MouseButton.Left));
                inferredContact.Remove(e.DeviceId);
                return true;

            default:
                var contact = trustRecordedContact ? e.InContact : inferredContact.Contains(e.DeviceId);
                canvas.RaiseEvent(new PointerEventArgs(
                    e.Kind switch
                    {
                        InputTrace.Kind.Enter => InputElement.PointerEnteredEvent,
                        InputTrace.Kind.Exit => InputElement.PointerExitedEvent,
                        _ => InputElement.PointerMovedEvent,
                    },
                    canvas, pointer, window, at, timestamp,
                    Properties(e, contact, PointerUpdateKind.Other),
                    e.Modifiers));
                return true;
        }
    }

    /// <summary>
    /// <b>Contact is the capture's evidence in a v2 trace and the harness's
    /// inference in a v1 one.</b> The paint path drops any sample not in
    /// contact, so this decides whether a replayed point joins the stroke at
    /// all — and coalesced history reaching back past the press into hover
    /// positions is exactly B185. Inferring it from the surrounding press and
    /// release was inferring the thing under test, which is why the recorder
    /// now keeps it.
    /// </summary>
    private static PointerPointProperties Properties(
        in InputTrace.Entry e, bool inContact, PointerUpdateKind kind) =>
        new(inContact ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None,
            kind, 0, e.Pressure, e.TiltX, e.TiltY);
}
