using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>One scanned character, shaped for a row.</summary>
public sealed class LibraryEntryRow(LibraryEntry entry)
{
    public LibraryEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public string LibraryName => Entry.LibraryName;

    /// <summary>"3 animations · 2 variants", with the empty parts left unsaid.</summary>
    public string Summary
    {
        get
        {
            var animations = $"{Entry.DocumentCount} animation{(Entry.DocumentCount == 1 ? "" : "s")}";
            return Entry.VariantCount > 0
                ? $"{animations} · {Entry.VariantCount} variant{(Entry.VariantCount == 1 ? "" : "s")}"
                : animations;
        }
    }
}

/// <summary>
/// The character library's one view model (Q138): the picker on the project
/// browser and the library window are both views over this — the same scanned
/// entries, the same import path — so the two surfaces cannot drift apart.
/// </summary>
/// <remarks>
/// Scanning happens when a surface opens (<see cref="Rescan"/>), never at
/// startup. Importing goes through the Q35 gate: when the merge would replace
/// copies the artist has edited, <see cref="ConfirmReplaceEdited"/> is asked
/// with their names first — and when no asker is wired (or the answer is no),
/// the edited copies are kept, because the safe default is the one that
/// destroys nothing.
/// </remarks>
public sealed partial class LibraryViewModel(
    AppSettings settings, Func<Project?> project, Action<ImportResult> imported) : ObservableObject
{
    public ObservableCollection<string> Roots { get; } = [.. settings.Library.Roots];

    public ObservableCollection<LibraryEntryRow> Entries { get; } = [];

    /// <summary>The window's dialog, or null in a surface that cannot ask.</summary>
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmReplaceEdited { get; set; }

    public bool HasRoots => Roots.Count > 0;

    /// <summary>
    /// True when the roots were scanned and offered nothing — distinct from
    /// "no roots", which is the feature not set up rather than an empty shelf.
    /// </summary>
    [ObservableProperty]
    private bool _scannedEmpty;

    public void AddRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Roots.Contains(path)) return;
        Roots.Add(path);
        settings.Library.Roots.Add(path);
        settings.Save();
        OnPropertyChanged(nameof(HasRoots));
        Rescan();
    }

    public void RemoveRoot(string path)
    {
        if (!Roots.Remove(path)) return;
        settings.Library.Roots.Remove(path);
        settings.Save();
        OnPropertyChanged(nameof(HasRoots));
        Rescan();
    }

    /// <summary>Re-read the roots. Called when a surface opens, and after edits.</summary>
    public void Rescan()
    {
        Entries.Clear();
        foreach (var entry in CharacterLibrary.Scan(Roots))
        {
            Entries.Add(new LibraryEntryRow(entry));
        }
        ScannedEmpty = HasRoots && Entries.Count == 0;
    }

    /// <summary>
    /// Import one character into the open project, through the edited-copy
    /// gate. Null when no project is open — the surfaces are meant to be
    /// hidden then, but a stale flyout can outlive a project being closed.
    /// </summary>
    public async Task<ImportResult?> Import(LibraryEntryRow row)
    {
        if (project() is not { } target) return null;
        var edited = CharacterLibrary.WhatReimportWouldReplace(row.Entry, target);
        var replaceEdited = false;
        if (edited.Count > 0 && ConfirmReplaceEdited is { } ask)
        {
            replaceEdited = await ask(edited);
        }
        var result = CharacterLibrary.Import(row.Entry, target, replaceEdited);
        imported(result);
        return result;
    }
}
