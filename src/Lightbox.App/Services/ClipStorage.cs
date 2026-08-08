namespace Lightbox.App.Services;

/// <summary>
/// How an imported clip lives in the document — the artist's choice at
/// import (Q57). Two purposes, each with its own storage: a reference is a
/// drawing aid and pays reference prices; production footage is material and
/// pays material prices.
/// </summary>
public enum ClipStorage
{
    /// <summary>
    /// A reference kept by path: the document stays light, the file stays
    /// where a pipeline can keep editing it, and the frames rebuild on load.
    /// The default, and what Q56 shipped.
    /// </summary>
    ReferenceByPath,

    /// <summary>
    /// A reference with the extracted frames embedded — self-contained at
    /// contact-sheet quality, no FFmpeg needed to reopen. The original
    /// footage is not recoverable from the document, which is fine: it is a
    /// reference, not an asset.
    /// </summary>
    ReferenceEmbedded,

    /// <summary>
    /// Small production (Q57): the original clip bytes embedded, full
    /// fidelity, and the footage composites into exports — the clip IS part
    /// of the shot.
    /// </summary>
    Production,
}
