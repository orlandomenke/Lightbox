using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Services;

/// <summary>
/// An agent suggesting line matches (Q270's "the model matches, the engine
/// draws", Q271's matches): it sees both keys with every piece numbered, and
/// answers with numbers. The matches it sends are saved exactly as the
/// artist's clicks are, so the artist reviews and corrects them in the Match
/// lines mode.
/// </summary>
public sealed partial class IpcDocumentApi
{
    /// <summary>What an agent's suggestions are marked with — the provider name MCP provenance already uses.</summary>
    internal const string AgentName = "MCP agent";

    private sealed class MatchPiecesRef : FrameRef
    {
        /// <summary>Null means the default panel size.</summary>
        public int? LongEdge { get; set; }
    }

    private MainViewModel.PiecedGap GapFor(int frameIndex, string? layerId)
    {
        var layer = ResolveLayer(layerId);
        if (frameIndex < 0 || frameIndex >= Vm.Doc.Scene.FrameCount)
            throw new ArgumentException($"frameIndex must be 0..{Vm.Doc.Scene.FrameCount - 1}.");
        return Vm.PiecedGapAt(layer, frameIndex)
               ?? throw new ArgumentException(
                   $"No gap at frame {frameIndex} on layer \"{layer.Name}\": it needs a key or breakdown at or before it and another after it.");
    }

    private IpcProtocol.Response RenderMatchPieces(IpcProtocol.Request request)
    {
        var p = Payload<MatchPiecesRef>(request);
        var gap = GapFor(p.FrameIndex, p.LayerId);
        var a = MatchPiecesPicture.Number(gap.PiecesA, 'A');
        var b = MatchPiecesPicture.Number(gap.PiecesB, 'B');
        var matches = gap.Layer.Cels[gap.A].Frame!.Matches;
        // Capped so the composite stays inside what a model is shown at full
        // size (~1568px wide): pixels past that are bytes paid for and downscaled
        // away before a token is spent on them (DESIGN-ai-payload.md).
        var panel = p.LongEdge is int e and > 0 ? Math.Min(e, MatchPiecesPicture.MaxPanelLongEdge) : MatchPiecesPicture.PanelLongEdge;
        var png = MatchPiecesPicture.Render(a, b, matches,
            $"Key A - frame {gap.A}", $"Key B - frame {gap.B}", panel);

        string NameOf(IReadOnlyList<MatchPiecesPicture.Numbered> side, PieceRef r) =>
            side.FirstOrDefault(n => n.Ref == r)?.Name ?? "?";
        return IpcProtocol.Response.Success(new
        {
            LayerId = gap.Layer.Id,
            KeyA = gap.A,
            KeyB = gap.B,
            gap.Cut,
            // Box and length, not geometry: enough to sanity-check a reading of
            // the picture without paying for the points (Q177).
            Pieces = a.Concat(b).Select(n => new
            {
                Id = n.Name,
                Box = MatchPiecesPicture.Box(n.Piece),
                Length = MatchPiecesPicture.Length(n.Piece),
            }),
            Matches = (matches ?? []).Select(m => new
            {
                A = m.A.Select(r => NameOf(a, r)),
                B = m.B.Select(r => NameOf(b, r)),
                m.Solid,
                // The artist's matches are fixed: set_line_matches keeps them and
                // refuses their pieces. The agent's own are what it replaces.
                By = m.SuggestedBy is null ? "artist" : "agent",
            }),
            PngBase64 = Convert.ToBase64String(png),
        });
    }

    private sealed class SetMatchesPayload : FrameRef
    {
        public string? Cut { get; set; }
        public List<MatchDto> Matches { get; set; } = [];
    }

    private sealed class MatchDto
    {
        public List<string> A { get; set; } = [];
        public List<string> B { get; set; } = [];
        public bool? Solid { get; set; }
    }

    /// <summary>
    /// Save an agent's matches on a gap, marked as its suggestions. They replace
    /// the agent's earlier suggestions there and never the artist's matches,
    /// whose pieces it may not name (the art-director, G12). Refused, with
    /// nothing changed, when the keys were cut differently from the picture the
    /// agent read, or a name is unknown, used twice or the artist's.
    /// </summary>
    private IpcProtocol.Response SetLineMatches(IpcProtocol.Request request)
    {
        var p = Payload<SetMatchesPayload>(request);
        // An empty answer is far likelier a model that produced nothing than a
        // request to withdraw every suggestion (the ai-engineer, G12).
        if (p.Matches.Count == 0)
            return IpcProtocol.Response.Fail("No matches given. To leave the gap as it is, do not call set_line_matches.");
        var gap = GapFor(p.FrameIndex, p.LayerId);
        if (string.IsNullOrEmpty(p.Cut) || p.Cut != gap.Cut)
            return IpcProtocol.Response.Fail(
                "The keys are not the ones you read: pass the cut from render_match_pieces, and call it again if a key changed since.");
        var artists = (gap.Layer.Cels[gap.A].Frame!.Matches ?? []).Where(m => m.SuggestedBy is null).ToList();
        var theirs = artists.SelectMany(m => m.A).Concat(artists.SelectMany(m => m.B)).ToHashSet();

        var names = MatchPiecesPicture.Number(gap.PiecesA, 'A')
            .Concat(MatchPiecesPicture.Number(gap.PiecesB, 'B'))
            .ToDictionary(n => n.Name, n => n.Ref, StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<LineMatch>();
        foreach (var m in p.Matches)
        {
            if (m.A.Count == 0 || m.B.Count == 0)
                return IpcProtocol.Response.Fail(
                    "Every match needs at least one piece of key A and one of key B. Leave a piece that appears or goes out of every match.");
            var match = new LineMatch { Solid = m.Solid, SuggestedBy = AgentName };
            foreach (var (side, list, into) in new[] { ('A', m.A, match.A), ('B', m.B, match.B) })
            {
                foreach (var name in list)
                {
                    var key = name.Trim();
                    if (key.Length == 0 || char.ToUpperInvariant(key[0]) != side || !names.TryGetValue(key, out var r))
                        return IpcProtocol.Response.Fail($"\"{name}\" is not a piece of key {side} in this gap.");
                    if (!used.Add(key))
                        return IpcProtocol.Response.Fail($"\"{key}\" is in more than one match.");
                    if (theirs.Contains(r))
                        return IpcProtocol.Response.Fail($"\"{key}\" is in a match the artist made; leave it out.");
                    into.Add(r);
                }
            }
            matches.Add(match);
        }

        var (redrawn, kept) = Vm.SaveMatchesFromAgent(gap, [.. artists.Select(m => m.Clone()), .. matches]);
        return IpcProtocol.Response.Success(new
        {
            Saved = matches.Count,
            // Named, so the agent can see what it left out without rendering again.
            Unmatched = names.Where(n => !used.Contains(n.Key) && !theirs.Contains(n.Value)).Select(n => n.Key).ToList(),
            Redrawn = redrawn,
            Kept = kept,
        });
    }
}
