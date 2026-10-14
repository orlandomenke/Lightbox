using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// A brush that rewets: the paint it passes over bleeds out into the wet
/// area and thins, and what the water pushed gathers where the water stopped
/// (Q255). On a <see cref="BrushKind.Blur"/> brush, which already reads the
/// layer as it was before the stroke and lays no paint of its own.
/// </summary>
/// <remarks>
/// <para>
/// The owner's brief, with a reference of a rewetted wash: <em>"the rewetting
/// is more a bleed/spread and diluting the pigment"</em>, and of the look
/// chosen from a sheet, <em>"the pooling should be more dynamic and
/// irregular"</em>. No wet or dry state is kept anywhere (Q245): this is what
/// one stroke does to the pixels under it, a function of those pixels and of
/// where the brush went, so it replays like any other mark.
/// </para>
/// <para>
/// Optional the way <see cref="ColourMixing"/> is: <see cref="BrushSettings.Wet"/>
/// is null for a brush that does not rewet, and null writes nothing to the
/// file. A block on the existing blur kind and not a new kind of brush, so a
/// build that predates it reads the stroke as a plain blur and not as a file
/// it cannot open.
/// </para>
/// <para>
/// How wet a touch is comes from the brush's flow and pressure; how far paint
/// can travel, from its size. The paper the wash settles into is the brush's
/// own texture surface and <see cref="BrushSettings.TextureSettle"/>.
/// </para>
/// </remarks>
public sealed class WetSettings
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched so a save here does not drop them (Q230).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>0..1: how far the paint bleeds, as a fraction of the brush's size.</summary>
    public double Spread { get; set; } = 0.6;

    /// <summary>0..1: how much pigment the water carries off where it is wettest.</summary>
    public double Thin { get; set; } = 0.2;

    /// <summary>
    /// 0..1: how uneven the wash is - water pools in some places and not in
    /// others, pushing pigment out of the pools. At 0 the bleed is even.
    /// </summary>
    public double Cloud { get; set; } = 0.9;

    /// <summary>0..1: how much of the pigment the water pushed gathers in a line where the water stopped.</summary>
    public double Gather { get; set; } = 0.5;

    public WetSettings Clone() => new() { Unknown = Unknown, Spread = Spread, Thin = Thin, Cloud = Cloud, Gather = Gather };
}
