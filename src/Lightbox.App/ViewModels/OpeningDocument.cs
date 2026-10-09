namespace Lightbox.App.ViewModels;

/// <summary>A document being read from disk: shown in the tab strip until it is a tab (Q229).</summary>
public sealed record OpeningDocument(string Title, string Path);
