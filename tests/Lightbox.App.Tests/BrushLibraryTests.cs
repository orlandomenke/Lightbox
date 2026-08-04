using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Importing brushes without stalling the window, and being able to get rid of them again.
/// </summary>
/// <remarks>
/// <para>
/// Two complaints, one cause. Importing a fifty-six brush collection made the main window "go
/// transparent as if about to crash" — nothing was crashing, the UI thread was inside a parser
/// for several seconds and had stopped answering the compositor. And once the brushes were in
/// there was no way to remove them: deleting from the brush options is one at a time through a
/// flyout that closes on each pick, and renaming an import was not possible at all.
/// </para>
/// <para>
/// The reading is now a pure function over bytes, which is what makes most of this testable
/// without a window at all. The window tests cover the wiring the pure part cannot: that
/// progress reaches the bar, that a shipped brush is refused before the click rather than
/// after, and that stopping keeps what was already read.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class BrushLibraryTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A minimal GBR v2 brush — a solid square tip, the smallest real import.</summary>
    private static byte[] Gbr(int size, string name = "fixture")
    {
        using var ms = new MemoryStream();
        void BE(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, v);
            ms.Write(b);
        }
        var bytes = System.Text.Encoding.ASCII.GetBytes(name);
        BE((uint)(28 + bytes.Length + 1));
        BE(2);
        BE((uint)size);
        BE((uint)size);
        BE(1);
        BE(0x47494D50);
        BE(20);
        ms.Write(bytes);
        ms.WriteByte(0);
        for (var i = 0; i < size * size; i++) ms.WriteByte(255);
        return ms.ToArray();
    }

    private static List<(string, byte[])> Collection(int count) =>
        Enumerable.Range(0, count).Select(i => ($"brush{i}.gbr", Gbr(16 + i % 8, $"pack {i}"))).ToList();

    // ---- the reading, with no window in it -------------------------------------

    [Fact]
    public void ReadingBrushesReportsProgressPerFile()
    {
        // What the bar is bound to. Progress is per *file* rather than per brush because one
        // .abr can hold dozens and a bar that jumps from 0 to 40 tells you nothing about how
        // long the rest will take.
        var files = Collection(6);
        var seen = new List<BrushImportProgress>();

        var outcome = BrushImportJob.Read(files, new SyncProgress(seen.Add), TestContext.Current.CancellationToken);

        Assert.Equal(6, outcome.Presets.Count);
        output.WriteLine(string.Join(", ", seen.Select(p => $"{p.Done}/{p.Total}")));
        // One report before anything is read, so the bar appears at zero rather than at 1/6.
        Assert.Equal(0, seen[0].Done);
        Assert.Equal(6, seen[^1].Done);
        Assert.Equal(1, seen[^1].Fraction);
        Assert.Equal(6, seen[^1].Found);
        // Monotone: a bar that goes backwards reads as a fault in the import.
        Assert.Equal(seen.Select(p => p.Done).OrderBy(d => d), seen.Select(p => p.Done));
    }

    [Fact]
    public void ABadFileIsNamedRatherThanCountedAndDoesNotStopTheRest()
    {
        // An artist picks a folder, not fifty individual files. Refusing the lot because one
        // is corrupt is the wrong answer, and "3 files could not be read" out of fifty-six is
        // unactionable — the names are what tell you which to look at.
        var files = new List<(string, byte[])>
        {
            ("good.gbr", Gbr(20, "good one")),
            ("broken.gbr", [1, 2, 3, 4]),
            ("alsogood.gbr", Gbr(24, "second")),
        };

        var outcome = BrushImportJob.Read(files, cancel: TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Presets.Count);
        Assert.Equal(["broken.gbr"], outcome.Unreadable);
        var summary = BrushImportJob.Summarise(outcome);
        output.WriteLine(summary);
        Assert.Contains("broken.gbr", summary);
    }

    [Fact]
    public void ManyBadFilesDoNotProduceAParagraph()
    {
        var outcome = new BrushImportOutcome([], ["a", "b", "c", "d", "e", "f"]);

        var summary = BrushImportJob.Summarise(outcome);
        output.WriteLine(summary);

        Assert.Contains("and 3 more", summary);
        Assert.DoesNotContain("\"f\"", summary);
    }

    [Fact]
    public void GivingUpKeepsWhatWasAlreadyRead()
    {
        // Cancelling must save work, not cost it. The brushes already parsed are exactly what
        // the artist would have got by picking fewer files, so throwing them away would make
        // the Stop button a punishment.
        var files = Collection(10);
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.Done >= 3) cancel.Cancel();
        });

        var outcome = BrushImportJob.Read(files, progress, cancel.Token);

        output.WriteLine($"kept {outcome.Presets.Count} of 10");
        Assert.InRange(outcome.Presets.Count, 3, 9);
        Assert.Empty(outcome.Unreadable);
    }

    [Fact]
    public void NothingToImportSaysSoRatherThanClaimingSuccess()
    {
        var summary = BrushImportJob.Summarise(BrushImportOutcome.Empty);
        Assert.Contains("Nothing to import", summary);
    }

    // ---- the view model -------------------------------------------------------

    [AvaloniaFact]
    public async Task ImportingOffTheThreadStillLandsEveryBrushInTheList()
    {
        var vm = new MainViewModel(null);
        var before = vm.BrushPresetChoices.Count;

        var (added, outcome) = await vm.ImportBrushFilesAsync(Collection(12));

        Assert.Equal(12, added);
        Assert.Empty(outcome.Unreadable);
        Assert.Equal(before + 12, vm.BrushPresetChoices.Count);
    }

    [AvaloniaFact]
    public void RemovingACollectionSavesOnceRatherThanOncePerBrush()
    {
        // The reason DeletePresets exists rather than a loop. Each single delete rewrites the
        // whole store, so clearing fifty-six imports that way writes the file fifty-six times,
        // each write bigger than the last — the same stall as the import that made them.
        var vm = new MainViewModel(null);
        vm.ImportBrushFiles(Collection(20));
        var mine = vm.BrushPresetChoices.Where(p => !p.IsBuiltIn).ToList();
        var builtIns = vm.BrushPresetChoices.Count(p => p.IsBuiltIn);

        var before = PresetStore.Writes;
        var removed = vm.DeletePresets(mine);
        var writes = PresetStore.Writes - before;

        output.WriteLine($"removed {removed} brushes with {writes} store write(s)");
        Assert.Equal(20, removed);
        Assert.Equal(builtIns, vm.BrushPresetChoices.Count);
        Assert.Equal(1, writes);
    }

    [AvaloniaFact]
    public void RemovingASelectionLeavesTheShippedBrushesAlone()
    {
        // Inside a multi-selection, "delete" on a shipped brush must not quietly revert it to
        // factory settings on the way past. On a single delete that reading is obvious; here
        // it is not, and somebody clearing a folder of imports did not ask for it.
        var vm = new MainViewModel(null);
        vm.ImportBrushFiles(Collection(3));
        var everything = vm.BrushPresetChoices.ToList();
        var shipped = everything.Count(p => p.IsBuiltIn);

        var removed = vm.DeletePresets(everything);

        Assert.Equal(3, removed);
        Assert.Equal(shipped, vm.BrushPresetChoices.Count);
        Assert.All(vm.BrushPresetChoices, p => Assert.True(p.IsBuiltIn));
    }

    [AvaloniaFact]
    public void RemovingTheBrushInYourHandLetsGoOfIt()
    {
        var vm = new MainViewModel(null);
        vm.ImportBrushFiles(Collection(2));
        var mine = vm.BrushPresetChoices.Where(p => !p.IsBuiltIn).ToList();
        vm.ApplyPreset(mine[0]);

        vm.DeletePresets(mine);

        Assert.Null(vm.SelectedBrushPreset);
    }

    // ---- the window -----------------------------------------------------------

    private static (BrushLibraryWindow Window, MainViewModel Vm) OpenLibrary()
    {
        var vm = new MainViewModel(null);
        var window = new BrushLibraryWindow(vm);
        window.Show();
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static List<BrushChoice> Rows(BrushLibraryWindow window) =>
        (window.FindControl<ListBox>("BrushList")!.ItemsSource as IEnumerable<BrushChoice>)?.ToList() ?? [];

    [AvaloniaFact]
    public void TheLibraryListsEveryBrushWithItsMark()
    {
        var (window, vm) = OpenLibrary();

        var rows = Rows(window);

        Assert.Equal(vm.BrushPresetChoices.Count, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.Preview));
    }

    [AvaloniaFact]
    public void ARowSaysWhereTheBrushCameFromBecauseThatDecidesWhatYouMayDoToIt()
    {
        var (window, vm) = OpenLibrary();
        vm.ImportBrushFiles([("pack.gbr", Gbr(20, "imported one"))]);
        window.FindControl<TextBox>("SearchBox")!.Text = "imported";
        Pump();

        var row = Assert.Single(Rows(window));
        Assert.Equal("Imported", row.Origin);
        Assert.Contains("20 px", row.SizeAndTags);
    }

    [AvaloniaFact]
    public void SelectingAShippedBrushOffersNoRenameAndSaysWhy()
    {
        // Told beforehand rather than refused after the click — the same rule the forbidden
        // cursor is for. A disabled button with no explanation is the version of this that
        // reads as the app being broken.
        var (window, _) = OpenLibrary();
        var list = window.FindControl<ListBox>("BrushList")!;
        var shipped = Rows(window).First(r => r.Preset.IsBuiltIn);

        list.SelectedItems!.Clear();
        list.SelectedItems.Add(shipped);
        Pump();

        Assert.False(window.FindControl<Control>("RenameRow")!.IsVisible);
        Assert.True(window.FindControl<Control>("ShippedNote")!.IsVisible);
        Assert.False(window.FindControl<Button>("RemoveButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void RenamingAnImportedBrushSticks()
    {
        var (window, vm) = OpenLibrary();
        vm.ImportBrushFiles([("pack.gbr", Gbr(20, "pack name nobody chose"))]);
        window.FindControl<TextBox>("SearchBox")!.Text = "nobody";
        Pump();

        var list = window.FindControl<ListBox>("BrushList")!;
        list.SelectedItems!.Clear();
        list.SelectedItems.Add(Rows(window).Single());
        Pump();

        window.FindControl<TextBox>("RenameBox")!.Text = "Rough ink";
        window.FindControl<Button>("RenameButton")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.Contains(vm.BrushPresetChoices, p => p.Name == "Rough ink");
        Assert.DoesNotContain(vm.BrushPresetChoices, p => p.Name == "pack name nobody chose");
    }

    [AvaloniaFact]
    public void RemovingFromTheLibraryTakesThemOutOfThePickerToo()
    {
        // The two lists are the same collection, which is the point — a brush removed here
        // must not still be pickable, and nothing should need telling about it.
        var (window, vm) = OpenLibrary();
        vm.ImportBrushFiles(Collection(4));
        window.FindControl<TextBox>("SearchBox")!.Text = "pack";
        Pump();

        var list = window.FindControl<ListBox>("BrushList")!;
        list.SelectedItems!.Clear();
        foreach (var row in Rows(window)) list.SelectedItems.Add(row);
        Pump();

        window.FindControl<Button>("RemoveButton")!.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.DoesNotContain(vm.BrushPresetChoices, p => p.Name.StartsWith("pack ", StringComparison.Ordinal));
        Assert.Empty(Rows(window));
    }

    [AvaloniaFact]
    public async Task TheProgressBarIsGoneWhenTheImportIs()
    {
        // A window left with a dead bar on it is indistinguishable from the stall this whole
        // change exists to remove.
        var (window, _) = OpenLibrary();

        await window.RunImport(Collection(5));
        Pump();

        Assert.False(window.FindControl<Control>("ProgressRow")!.IsVisible);
        Assert.True(window.FindControl<Button>("ImportButton")!.IsEnabled);
        Assert.Equal(5, Rows(window).Count(r => r.Name.StartsWith("pack ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ReadingACollectionSizedImportCostsRealTime()
    {
        // What the async path and the progress bar are actually for, measured at a tip size a
        // real collection uses. **Both numbers, because the small one is misleading:** 56
        // brushes with 24 px tips parse in well under a tenth of a second, so a test built on
        // toy fixtures would have "proved" there was nothing to fix. The cost is in the tip —
        // decoding it and re-encoding it as PNG — and it grows with its area.
        var small = Collection(56);
        var clockSmall = Stopwatch.StartNew();
        var readSmall = BrushImportJob.Read(small, cancel: TestContext.Current.CancellationToken);
        var msSmall = clockSmall.Elapsed.TotalMilliseconds;

        // Eight rather than fifty-six, because the whole point is that each one is
        // expensive: 56 at this size takes **11.5 seconds**, which is the reported stall
        // exactly and far too long to spend in the normal suite. Eight and a multiplication
        // says the same thing.
        const int sample = 8;
        var big = Enumerable.Range(0, sample).Select(i => ($"big{i}.gbr", Gbr(300, $"big {i}"))).ToList();
        var clockBig = Stopwatch.StartNew();
        var readBig = BrushImportJob.Read(big, cancel: TestContext.Current.CancellationToken);
        var msBig = clockBig.Elapsed.TotalMilliseconds;
        var each = msBig / sample;

        output.WriteLine(
            $"56 × 24 px tips: {msSmall:F0} ms total. "
            + $"{sample} × 300 px tips: {msBig:F0} ms ({each:F0} ms each) — "
            + $"a 56-brush collection at this size is about {each * 56 / 1000:F0} s.");
        Assert.Equal(56, readSmall.Presets.Count);
        Assert.Equal(sample, readBig.Presets.Count);

        // The claim worth guarding is the shape, not the absolute: tip area dominates, so a
        // real collection is seconds of work and belongs off the UI thread. A per-brush cost
        // this size is also why the bar reports per file and offers a way out.
        Assert.True(
            each > msSmall / 56,
            $"a 300 px tip ({each:F1} ms) was not dearer than a 24 px one ({msSmall / 56:F1} ms) — "
            + "the cost model in this file is wrong");
    }

    /// <summary>An <see cref="IProgress{T}"/> that reports on the calling thread.</summary>
    /// <remarks>
    /// <c>Progress&lt;T&gt;</c> posts to a synchronization context, so in a test its callbacks
    /// arrive after the assertion that was waiting for them. This one is deliberately
    /// synchronous — the job under test does not care, and the test can then read what was
    /// reported.
    /// </remarks>
    private sealed class SyncProgress(Action<BrushImportProgress> onReport) : IProgress<BrushImportProgress>
    {
        public void Report(BrushImportProgress value) => onReport(value);
    }

}
