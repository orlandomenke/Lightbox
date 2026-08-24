namespace Lightbox.App.Services;

/// <summary>
/// How this artist wants fonts found and carried.
/// </summary>
/// <remarks>
/// With the other preferences that are not about pixels, for the reason
/// <see cref="AppSettings"/> gives: neither of these can change a drawing. The
/// first decides whether the application ever talks to the network, the second
/// what goes into a file that is already drawn. Text already set is unaffected
/// by both — its contours are in the document.
/// </remarks>
public sealed class FontSettings
{
    /// <summary>
    /// Offer Google Fonts beside the installed ones.
    /// </summary>
    /// <remarks>
    /// <b>On, and it still makes no request until the artist opens the font
    /// browser.</b> Off means this application never contacts fonts.google.com
    /// at all — which is a real requirement in a studio with an air gap, and the
    /// sort of thing that should be one switch rather than a firewall rule.
    /// </remarks>
    public bool UseGoogleFonts { get; set; } = true;

    /// <summary>
    /// Carry an open-licensed font inside documents that use it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, because the alternative — a file somebody else cannot retype — is a
    /// worse surprise than a slightly larger one, and because the fonts this can
    /// apply to are exactly the ones licensed to be passed around.
    /// </para>
    /// <para>
    /// Turning it off never affects how anything looks. A document written with
    /// this off names its fonts and carries none; open it where they are
    /// installed or cached and the text is editable again.
    /// </para>
    /// </remarks>
    public bool EmbedOpenFonts { get; set; } = true;
}
