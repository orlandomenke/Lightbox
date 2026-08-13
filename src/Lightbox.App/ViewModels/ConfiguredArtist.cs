using Lightbox.Ai;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Which AI provider is configured, whether it is usable, and the artist it makes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not called <c>AiConnection</c>, and that is deliberate.</b>
/// <see cref="Lightbox.Ai.AiConnection"/> already exists and is the <i>settings record</i>
/// — what is stored on disk. This is the live thing built from one. Naming both the same
/// word compiled cleanly and passed every test, because inside such a class the
/// unqualified name resolves to the enclosing type and <c>var connection =
/// AiSettings.Load()</c> hides the rest. It is exactly the wrong pair of things to make
/// one word apart.
/// </para>
/// <para>
/// Tier 1 of <c>docs/DESIGN-mainviewmodel-decomposition.md</c>, extracted under Q78.
/// Four fields that were loose on <see cref="MainViewModel"/> — the artist, the
/// provider label, the model label and the enabled flag — and the one operation that
/// sets all four together.
/// </para>
/// <para>
/// <b>Not readonly, and that is the point.</b> Edit ▸ Configure ▸ AI can swap the
/// provider while the app is running; an artist that could only be chosen at startup
/// would make "test it, then use it" a two-launch operation. So the whole of this
/// object's job is to be reloadable, and <see cref="Reload"/> is the only way its
/// state changes — the four values are read from one connection record and must never
/// disagree about which provider they describe.
/// </para>
/// <para>
/// <b>The labels are cached rather than read from disk per get.</b> They back bound
/// properties, and a binding that touches the filesystem each time it refreshes is a
/// trap waiting for someone to bind it in a list.
/// </para>
/// <para>
/// <b>What deliberately stays on the view model.</b> <c>AiBusy</c> and <c>AiStatus</c>
/// are <c>[ObservableProperty]</c> bindings, so they cannot move without changing every
/// XAML path that names them; and the <see cref="System.Threading.CancellationTokenSource"/>
/// for the call in flight belongs to the call, not to the connection — a provider swap
/// mid-request must not silently inherit the previous request's cancellation.
/// </para>
/// <para>
/// <b>Determinism is not this object's concern and must not become it.</b> Nothing here
/// touches a stroke, a seed or a render; it decides who is asked, never what comes back.
/// A future temperature or sampling setting would be a per-request value on the request
/// record, not a field here — caching it beside the provider label would make two
/// requests with the same document produce different marks depending on when the
/// settings were last loaded, which invariant 2 forbids.
/// </para>
/// </remarks>
sealed class ConfiguredArtist
{
    private IAiArtist? _artist;
    private string _providerLabel = "None";
    private string? _modelLabel;
    private bool _enabled = true;

    /// <summary>Start with an injected artist — the test seam — without reading settings.</summary>
    internal ConfiguredArtist(IAiArtist? artist = null) => _artist = artist;

    /// <summary>The configured artist, or null when there is no usable provider.</summary>
    internal IAiArtist? Artist => _artist;

    /// <summary>Whether there is an artist at all.</summary>
    internal bool IsAvailable => _artist is not null;

    /// <summary>
    /// Which provider is in use, or <c>"None"</c> when there is no artist.
    /// </summary>
    /// <remarks>
    /// Guarded on the artist rather than reported from the label alone: a connection
    /// that names a provider but failed to construct one is not in use, and saying
    /// otherwise would tell the artist their request went somewhere.
    /// </remarks>
    internal string ProviderLabel => _artist is null ? "None" : _providerLabel;

    /// <summary>
    /// The configured model name, nullable because not every provider names one.
    /// Recorded in <c>AiProvenance</c> on frames the AI drew.
    /// </summary>
    internal string? ModelLabel => _modelLabel;

    /// <summary>
    /// Whether AI assistance is switched on at all.
    /// </summary>
    /// <remarks>
    /// The AI bar binds its <i>visibility</i> here rather than its enabled state: a
    /// studio that turns AI off wants it gone, not greyed, and a permanently disabled
    /// row is a worse answer than an absent one — the camera's rule again.
    /// </remarks>
    internal bool Enabled => _enabled;

    /// <summary>
    /// Rebuild the artist from what is stored, so a provider picked at 3pm is the one
    /// that draws at 3.01 without a restart.
    /// </summary>
    /// <remarks>
    /// All four values move together and come from one <c>AiSettings.Load()</c>. Setting
    /// them from separate reads is the failure this method exists to prevent: a label
    /// describing one provider beside an artist built from another is wrong in the
    /// direction nobody checks, because both halves look right on their own.
    /// </remarks>
    internal void Reload()
    {
        (_artist as IDisposable)?.Dispose();
        var connection = AiSettings.Load();
        _providerLabel = connection.Provider.Name;
        _modelLabel = connection.Value("model");
        _enabled = connection.Enabled;
        _artist = AiArtistFactory.Create(connection);
    }
}
