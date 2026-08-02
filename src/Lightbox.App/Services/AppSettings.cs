using System.Text.Json;

namespace Lightbox.App.Services;

/// <summary>
/// The preferences that are not about pixels.
/// </summary>
/// <remarks>
/// Deliberately small, and deliberately separate from anything a document
/// carries. Invariant 4 says a setting that reaches pixels lives on the stroke,
/// so that an artist returning to a scene after a month finds it exactly as
/// they left it. What is left over — how often to autosave, where the last
/// project was — is about the person and belongs here.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// Minutes between autosaves. Zero turns it off.
    /// </summary>
    /// <remarks>
    /// A minute by default. Autosave writes the whole document, and at this
    /// app's sizes that is milliseconds, so the cost of being generous is
    /// nothing next to the cost of losing a drawing.
    /// </remarks>
    public double AutosaveMinutes { get; set; } = 1;

    /// <summary>
    /// Whether to autosave over the document's own file once it has one,
    /// rather than only to the recovery copy.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is not timidity: silently rewriting the file
    /// someone opened takes away the ability to close without saving, which is
    /// a real editing move. Someone who wants it can say so.
    /// </remarks>
    public bool AutosaveInPlace { get; set; }

    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lightbox", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static AppSettings Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(Path) ? Deserialize(File.ReadAllText(Path)) : new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Write the settings. Failures are swallowed: a preference that will not
    /// persist is an annoyance, and must never be the reason anything else
    /// fails.
    /// </summary>
    public void Save()
    {
        try
        {
            Lightbox.Core.Serialization.DocJson.WriteAtomic(Path, Serialize());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The autosave interval as a timespan, or null when it is off.</summary>
    public TimeSpan? AutosaveInterval =>
        AutosaveMinutes <= 0 ? null : TimeSpan.FromMinutes(Math.Clamp(AutosaveMinutes, 0.25, 60));
}
