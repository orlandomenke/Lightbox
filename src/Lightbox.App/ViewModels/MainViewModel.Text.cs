using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using Lightbox.Raster.Text;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// <para>
/// The text tool. Click to put a caret on the canvas, type, and the words are
/// shaped and drawn as you go; pressing Escape or clicking away bakes them into
/// ordinary contour strokes (<see cref="TextBaker"/>) in one undoable edit.
/// </para>
/// <para>
/// <b>The session holds a <see cref="TextElement"/> and nothing else.</b> There
/// is no half-committed state in the document while somebody is typing — the
/// glyphs on screen are a preview on the live scratch, exactly as the shape
/// tool's rectangle is, and the document learns about the text once. That is
/// what makes cancelling free and undo a single step.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    // ---- text tool ----------------------------------------------------------

    public bool IsTextTool => ActiveTool == ToolId.Text;

    /// <summary>The element being typed, or null when nobody is typing.</summary>
    private TextElement? _liveText;

    /// <summary>
    /// The colour, brush and clip every glyph of the live text will be painted
    /// with.
    /// </summary>
    /// <remarks>
    /// Captured when the session starts rather than read at commit time, for
    /// invariant 4's reason: what an artist saw while typing is what gets
    /// recorded, even if they change the colour swatch on the way past.
    /// </remarks>
    private Stroke? _liveTextPaint;

    /// <summary>Where the caret sits, as an index into the element's text.</summary>
    private int _textCaret;

    /// <summary>
    /// The other end of the selection, or the caret's own position when nothing
    /// is selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An anchor and a caret, not a start and an end.</b> Which end moves is
    /// what Shift+arrow and a drag both need to know, and a normalised pair
    /// forgets it — extend left past the anchor and a start/end pair would
    /// silently swap which end the next keystroke moves. <see cref="TextSelection"/>
    /// is the ordered view for everything that only wants the range.
    /// </para>
    /// <para>
    /// Not in the record, and it must not be: a selection is where the artist is
    /// looking, not something the document says. It lives exactly as long as the
    /// typing session.
    /// </para>
    /// </remarks>
    private int _textAnchor;

    private SKTypeface? _liveTextFace;

    /// <summary>The strokes a retype will replace, and the frame they are on.</summary>
    private List<Stroke>? _retyping;
    private string? _retypingFrameId;

    /// <summary>
    /// The element as the document has it, kept so undoing a retype restores
    /// the words that were there rather than the ones being typed over them.
    /// </summary>
    private TextElement? _retypingOriginal;

    internal TextElement? LiveText => _liveText;

    internal int TextCaret => _textCaret;

    internal int TextAnchor => _textAnchor;

    /// <summary>The selected range, low end first, or an empty range at the caret.</summary>
    internal (int Start, int End) TextSelection =>
        _textCaret <= _textAnchor ? (_textCaret, _textAnchor) : (_textAnchor, _textCaret);

    /// <summary>Whether any characters are selected.</summary>
    internal bool HasTextSelection => _textCaret != _textAnchor;

    internal Stroke? LiveTextPaint => _liveTextPaint;

    /// <summary>
    /// Whether a typeface was resolved for the type being set.
    /// </summary>
    /// <remarks>
    /// A seam rather than a state anybody acts on: without one there is nothing
    /// to shape with, and the failure is silent — a caret that types and commits
    /// nothing. Worth being able to assert.
    /// </remarks>
    internal bool HasTextFace => _liveTextFace is not null;

    /// <summary>Whether an artist is typing on the canvas right now.</summary>
    /// <remarks>
    /// The window asks so it can stop treating letters as tool shortcuts: while
    /// this is true, <c>B</c> is a letter rather than the brush.
    /// </remarks>
    public bool TextSessionActive => _liveText is not null;

    // ---- what the next piece of type will look like -------------------------

    [ObservableProperty]
    private double _textSize = 48;

    /// <summary>Letter-spacing in thousandths of an em.</summary>
    [ObservableProperty]
    private double _textTracking;

    /// <summary>Baseline-to-baseline distance, or 0 for the font's own.</summary>
    /// <remarks>
    /// Zero rather than null here because this is a number in a spin box, and
    /// "empty" is not a state a spin box has. It becomes
    /// <see cref="TextElement.LineHeight"/> null on the way into the record,
    /// which is where the difference matters — see that property.
    /// </remarks>
    [ObservableProperty]
    private double _textLineHeight;

    [ObservableProperty]
    private TextAlign _textAlign;

    /// <summary>The face new type is set in.</summary>
    [ObservableProperty]
    private FontFace? _selectedFont;

    /// <summary>What the font button says.</summary>
    public string SelectedFontName => SelectedFont?.ToString() ?? "Loading…";

    /// <summary>The filter over the font list, as typed into the browser.</summary>
    [ObservableProperty]
    private string _fontFilter = "";

    /// <summary>What the font browser is showing.</summary>
    public ObservableCollection<FontFace> FontChoices { get; } = [];

    /// <summary>Why the Google list is short or missing, when it is.</summary>
    [ObservableProperty]
    private string? _fontTrouble;

    private FontLibrary? _fonts;
    private IReadOnlyList<FontFace> _allFonts = [];

    /// <summary>
    /// The fonts this application can reach.
    /// </summary>
    /// <remarks>
    /// Built on first use rather than at startup, and built without the Google
    /// source at all when the artist has turned it off — which is what makes
    /// "off" mean no network rather than an ignored result.
    /// </remarks>
    public FontLibrary Fonts => _fonts ??= new FontLibrary(
        Settings.Fonts.UseGoogleFonts ? new GoogleFontSource() : null);

    /// <summary>
    /// Drop the built library so the next request builds it against the
    /// settings as they are now.
    /// </summary>
    /// <remarks>
    /// Whether there is a Google source at all is decided when the library is
    /// constructed — see <see cref="Fonts"/> — so turning the preference off
    /// has to reach this or the old library goes on being able to fetch.
    /// </remarks>
    public void ForgetFontLibrary()
    {
        _fonts?.Dispose();
        _fonts = null;
        _allFonts = [];
        FontChoices.Clear();
    }

    public IReadOnlyList<TextAlign> TextAlignChoices { get; } =
        [TextAlign.Left, TextAlign.Centre, TextAlign.Right];

    partial void OnTextSizeChanged(double value) => ReshapeLiveText(t => t.Size = Math.Max(1, value));

    partial void OnTextTrackingChanged(double value) => ReshapeLiveText(t => t.Tracking = value);

    partial void OnTextLineHeightChanged(double value) =>
        ReshapeLiveText(t => t.LineHeight = value > 0 ? value : null);

    partial void OnTextAlignChanged(TextAlign value) => ReshapeLiveText(t => t.Align = value);

    partial void OnSelectedFontChanged(FontFace? value)
    {
        OnPropertyChanged(nameof(SelectedFontName));
        if (value is null) return;
        _ = AdoptFontAsync(value);
    }

    partial void OnFontFilterChanged(string value) => ShowFonts();

    /// <summary>
    /// Load the face and, if type is being typed right now, reshape it into the
    /// new font under the artist's hands.
    /// </summary>
    private async Task AdoptFontAsync(FontFace face)
    {
        SKTypeface? typeface;
        try
        {
            typeface = await Fonts.LoadAsync(face).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Started from a property setter and nobody is awaiting it, so an
            // escape here is an unobserved task exception rather than an error
            // anybody sees. A font that will not load is a line of status text.
            AiStatus = $"Could not load {face}.";
            return;
        }

        FontTrouble = Fonts.Trouble;
        if (typeface is null)
        {
            AiStatus = $"Could not load {face}.";
            return;
        }

        // A face the system will name but this cannot draw is refused here
        // rather than at the commit, where the only symptom is a title that
        // vanishes — see TextBaker.CanSetType.
        if (!TextBaker.CanSetType(typeface))
        {
            AiStatus = $"“{face.Family}” has no outlines this can set type with.";
            return;
        }

        _liveTextFace = typeface;
        if (_liveText is not null)
        {
            _liveText.Font = new FontRef
            {
                Family = face.Family, Weight = face.Weight, Italic = face.Italic,
            };
            RenderTextPreview();
        }
    }

    /// <summary>
    /// Fill the font list once, on reaching for the tool.
    /// </summary>
    /// <remarks>
    /// Idempotent and fire-and-forget: picking the text tool is not a moment to
    /// wait on a network, and picking it a second time must not refetch. The
    /// installed faces are in hand synchronously, so the list is never empty
    /// even when Google is unreachable.
    /// </remarks>
    public void EnsureFontsLoaded()
    {
        if (_allFonts.Count > 0) return;
        _ = LoadFontsCommand.ExecuteAsync(null);
    }

    /// <summary>Fill the browser: installed at once, Google when it answers.</summary>
    [RelayCommand]
    private async Task LoadFonts()
    {
        _allFonts = FontLibrary.Installed();
        ShowFonts();
        SelectedFont ??= DefaultFace();

        if (!Settings.Fonts.UseGoogleFonts) return;

        try
        {
            _allFonts = await Fonts.FacesAsync().ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The source already turns being offline into a line of text; this
            // is for everything nobody thought of. The installed fonts are
            // already listed, so the browser still works.
            FontTrouble = "Could not reach Google Fonts.";
            return;
        }

        FontTrouble = Fonts.Trouble;
        ShowFonts();
    }

    /// <summary>
    /// A sensible face to start in: the one this machine already considers its
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a hardcoded family. "Arial" is not installed on a Linux
    /// machine and "DejaVu Sans" is not installed on a Mac, so a named default
    /// is a default that is missing somewhere — and a text tool whose first
    /// click reports a missing font is one nobody tries twice.
    /// </para>
    /// <para>
    /// <b>Also deliberately not "the first one alphabetically", which is what
    /// this was.</b> On the machine it was first run on that is Bitstream
    /// Charter, a Type 1 face with no outlines Skia will hand back — so the tool
    /// opened in a font that silently set nothing. Skia's own default is
    /// whatever fontconfig resolves for the system, which is by definition a
    /// face the platform can draw with.
    /// </para>
    /// </remarks>
    private static FontFace? DefaultFace()
    {
        var installed = FontLibrary.Installed();
        var preferred = SKTypeface.Default.FamilyName;

        return installed.FirstOrDefault(f =>
                f.Family == preferred && f is { Weight: 400, Italic: false })
            ?? installed.FirstOrDefault(f => f.Family == preferred)
            ?? installed.FirstOrDefault(f => f is { Weight: 400, Italic: false })
            ?? installed.FirstOrDefault();
    }

    private void ShowFonts()
    {
        var filter = FontFilter.Trim();
        FontChoices.Clear();
        foreach (var face in _allFonts)
        {
            if (filter.Length > 0
                && !face.Family.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            FontChoices.Add(face);
            // A browser is for browsing, not for scrolling four thousand rows.
            // The filter is how you reach the rest, and it is right there.
            if (FontChoices.Count >= 300) break;
        }
    }

    // ---- the typing session -------------------------------------------------

    /// <summary>
    /// Put a caret on the canvas — on the type already there, or on nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Clicking type that is already set retypes it rather than starting a
    /// second block on top, which is the behaviour of every application that has
    /// a text tool and the reason <see cref="Stroke.TextId"/> exists.
    /// </para>
    /// <para>
    /// The point is snapped like any other, so type dropped on a grid or a guide
    /// lands on it — a title lined up to the same guide as the artwork is most
    /// of what anybody wants from a baseline.
    /// </para>
    /// </remarks>
    public void BeginText(double x, double y)
    {
        if (ActiveTool != ToolId.Text || IsPlaying) return;
        if (TextSessionActive) CommitText();
        if (!CanEdit(ActiveLayer, "type on it") || PaintTargetOrKey() is null) return;
        CommitSwatchEdit();

        if (PaintTarget() is not { } target) return;

        (x, y) = SnappedPoint(x, y);

        if (TypeAt(target, x, y) is { } existing)
        {
            BeginRetyping(target, existing);
            // Where you clicked, not the end of the block. Picking type up
            // always landed the caret after the last character, so editing the
            // first word of a caption meant arrowing back through all of it.
            PlaceTextCaretAt(x, y);
            return;
        }

        _liveText = new TextElement
        {
            Text = "",
            Size = Math.Max(1, TextSize),
            Tracking = TextTracking,
            LineHeight = TextLineHeight > 0 ? TextLineHeight : null,
            Align = TextAlign,
            X = x,
            Y = y,
            Font = SelectedFont is { } face
                ? new FontRef { Family = face.Family, Weight = face.Weight, Italic = face.Italic }
                : new FontRef(),
        };
        _textCaret = 0;
        _textAnchor = 0;
        _retyping = null;
        _retypingFrameId = null;
        _liveTextFace ??= SKTypeface.Default;
        // Whatever face is actually in hand is the one the element names, so an
        // artist who has not opened the font list yet still writes a document
        // that can be retyped. Recording an empty family would leave type nobody
        // could ever pick up again.
        if (_liveText.Font.Family.Length == 0)
        {
            _liveText.Font.Family = _liveTextFace.FamilyName;
        }
        _liveTextPaint = TextPaintPrototype();

        _live.EnsureScratch(Scene.Width, Scene.Height);
        RenderTextPreview();
        OnPropertyChanged(nameof(TextSessionActive));
        PublishSnapshot();
        AiStatus = "Type. Esc or a click elsewhere sets it.";
    }

    /// <summary>
    /// Open the type under a point for editing, switching to the Text tool.
    /// </summary>
    /// <returns>Whether there was any type there to open.</returns>
    /// <remarks>
    /// <b>The Arrow's double-click, and the second way in.</b> Photoshop's
    /// gesture: the arrow is what you are holding while you arrange a page, and
    /// having to go back to the tool rail to fix a typo is the interruption
    /// worth removing. It switches the tool rather than typing behind the
    /// arrow's back, so what the artist is holding always matches what the next
    /// keystroke will do.
    /// </remarks>
    public bool EnterTypeAt(double x, double y)
    {
        if (IsPlaying) return false;
        if (ActiveLayer is not { } layer) return false;
        if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } exposed) return false;
        if (TypeAt(exposed, x, y) is null) return false;
        if (!CanEdit(layer, "type on it")) return false;

        ActiveTool = ToolId.Text;
        BeginText(x, y);
        return TextSessionActive;
    }

    /// <summary>Pick up type already on the canvas and put the caret at its end.</summary>
    private void BeginRetyping(Frame target, TextElement element)
    {
        var glyphs = StrokesOf(target).Where(s => s.TextId == element.Id).ToList();

        _liveText = element.Clone();
        // The caller places the caret when it knows where the click landed;
        // the end of the block is the answer for everything that does not.
        _textCaret = _liveText.Text.Length;
        _textAnchor = _textCaret;
        _retyping = glyphs;
        _retypingFrameId = target.Id;
        _retypingOriginal = element;
        _liveTextPaint = glyphs.Count > 0 ? glyphs[0].Clone() : TextPaintPrototype();
        _liveTextPaint.TextId = null;

        // The bar shows what was picked up. Also what keeps the commit honest:
        // it only records a font when the chosen face is the one the words were
        // actually shaped in — see CommitText.
        if (_allFonts.FirstOrDefault(f =>
                f.Family == element.Font.Family
                && f.Weight == element.Font.Weight
                && f.Italic == element.Font.Italic) is { } picked)
        {
            SelectedFont = picked;
        }

        TextSize = _liveText.Size;
        TextTracking = _liveText.Tracking;
        TextLineHeight = _liveText.LineHeight ?? 0;
        TextAlign = _liveText.Align;

        // Resolving the face can want a download, so the preview goes up with
        // whatever is already to hand and improves when the font arrives. The
        // alternative is a click that freezes until a network answers.
        _liveTextFace = FontRegistry.Resolve(_liveText.Font) ?? _liveTextFace ?? SKTypeface.Default;
        _ = ResolveRetypedFaceAsync(_liveText.Font);

        _live.EnsureScratch(Scene.Width, Scene.Height);
        RenderTextPreview();
        OnPropertyChanged(nameof(TextSessionActive));
        PublishSnapshot();
    }

    private async Task ResolveRetypedFaceAsync(FontRef font)
    {
        var resolved = await Fonts.ResolveAsync(font).ConfigureAwait(true);
        if (resolved is null)
        {
            AiStatus =
                $"“{font.Family}” is not on this machine — the words are still a drawing, "
                + "but retyping them will use another face.";
            return;
        }
        if (_liveText is null || !_liveText.Font.SameFace(font)) return;
        _liveTextFace = resolved;
        RenderTextPreview();
    }

    /// <summary>The type under a point, if any.</summary>
    /// <remarks>
    /// Even-odd containment on the glyph's own contours, which is the rule it
    /// was painted under — so clicking the hole in an "o" misses it, exactly as
    /// clicking the hole in a flood fill does.
    /// </remarks>
    /// <summary>
    /// How far outside a block's box still counts as pointing at it.
    /// </summary>
    /// <remarks>
    /// The box is the em box, so a descender or an accent can sit a hair
    /// outside it, and a caret aimed at the last letter of a line lands on the
    /// far edge. Small enough that two blocks a line apart do not overlap.
    /// </remarks>
    private const double TypeBoxSlack = 4;

    /// <summary>
    /// The type a point is pointing at, topmost first — <b>by its box</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to hit-test glyph outlines, and that is what made the tool
    /// feel limited.</b> Clicking the gap between two letters, or the inside of
    /// an "o", missed every contour and started a <em>second</em> block on top
    /// of the first — so the way to retype a word was to aim at the ink of one
    /// of its letters. It also meant there was nothing to show on hover: the
    /// shape that responded was forty separate glyph outlines.
    /// </para>
    /// <para>
    /// The box is what an artist sees and aims at, so the box is what answers.
    /// Its extent comes from <c>TextLayout</c> rather than from the strokes,
    /// because a block whose glyphs are all spaces has no contours at all and
    /// still has somewhere to put a caret.
    /// </para>
    /// </remarks>
    internal TextElement? TypeAt(Frame target, double x, double y)
    {
        if (_editor.Doc.Texts is not { } texts) return null;

        var strokes = StrokesOf(target);
        var seen = new HashSet<string>();
        for (var i = strokes.Count - 1; i >= 0; i--)
        {
            var stroke = strokes[i];
            if (stroke.TextId is not { } id || !texts.TryGetValue(id, out var element)) continue;
            if (!seen.Add(id)) continue;   // one block, many glyph strokes
            if (BoxOf(element) is { } box
                && x >= box.Left - TypeBoxSlack && x <= box.Right + TypeBoxSlack
                && y >= box.Top - TypeBoxSlack && y <= box.Bottom + TypeBoxSlack)
            {
                return element;
            }
        }
        return null;
    }

    /// <summary>An element's em box in document coordinates, or null with no face.</summary>
    internal SKRect? BoxOf(TextElement element) =>
        TextLayout.Of(element, FaceFor(element)).Box;

    /// <summary>
    /// The typeface to measure an element with.
    /// </summary>
    /// <remarks>
    /// The live face when it is the element being typed, and the registry's
    /// answer otherwise — a block set in a font this session has not loaded
    /// still has to be findable, or the artist cannot click on it to fix it.
    /// </remarks>
    private SKTypeface FaceFor(TextElement element) =>
        _liveText is not null && _liveText.Id == element.Id && _liveTextFace is not null
            ? _liveTextFace
            : FontRegistry.Resolve(element.Font) ?? _liveTextFace ?? SKTypeface.Default;

    /// <summary>
    /// The box the Text tool would enter if it were clicked here, or null.
    /// </summary>
    /// <remarks>
    /// What the canvas outlines on hover. Asked of the exposed frame read-only:
    /// a hover must never key a held cel, which is the same rule the fill
    /// preview follows.
    /// </remarks>
    internal SKRect? TypeBoxUnder(double x, double y)
    {
        if (ActiveLayer is not { } layer) return null;
        if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame) return null;
        return TypeAt(frame, x, y) is { } element ? BoxOf(element) : null;
    }

    private Stroke TextPaintPrototype() => new()
    {
        Tool = ToolKind.Text,
        Color = ColorHex,
        SwatchId = ActiveSwatchId,
        PaletteId = ActivePaletteId,
        // A glyph is a filled contour, so the only brush settings that reach it
        // are the two a fill uses. Everything else — spacing, scatter, texture —
        // describes a path being walked, which this is not.
        Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
        AlphaLocked = ActiveLayer?.AlphaLocked ?? false,
        Label = "text",
    };

    /// <summary>Type characters in at the caret, replacing any selection.</summary>
    public void TypeIntoText(string characters)
    {
        if (_liveText is not { } text || characters.Length == 0) return;
        // Control characters arrive here on some platforms and are not letters.
        var typed = new string([.. characters.Where(c => !char.IsControl(c))]);
        if (typed.Length == 0) return;
        TypeControl(typed);
    }

    /// <summary>Break the line at the caret.</summary>
    public void TextNewline() => TypeControl("\n");

    /// <remarks>
    /// <b>Typing over a selection replaces it</b>, which is the behaviour that
    /// makes a selection worth having: select a word, type, and the word is
    /// gone. Every insertion goes through here so none of them can forget —
    /// there is no second path that inserts at the caret and leaves the
    /// highlighted text sitting behind the new letters.
    /// </remarks>
    private void TypeControl(string characters)
    {
        if (_liveText is not { } text) return;
        DeleteTextSelection();
        var at = Math.Clamp(_textCaret, 0, text.Text.Length);
        text.Text = text.Text.Insert(at, characters);
        PutCaret(at + characters.Length);
        RenderTextPreview();
    }

    /// <summary>Take back the selection, or the character before the caret.</summary>
    public void TextBackspace()
    {
        if (_liveText is not { } text) return;
        if (DeleteTextSelection())
        {
            RenderTextPreview();
            return;
        }
        if (_textCaret <= 0 || text.Text.Length == 0) return;
        var at = Math.Clamp(_textCaret, 1, text.Text.Length);
        text.Text = text.Text.Remove(at - 1, 1);
        PutCaret(at - 1);
        RenderTextPreview();
    }

    /// <summary>Take out the selection, or the character after the caret.</summary>
    public void TextDeleteForward()
    {
        if (_liveText is not { } text) return;
        if (DeleteTextSelection())
        {
            RenderTextPreview();
            return;
        }
        if (_textCaret >= text.Text.Length) return;
        text.Text = text.Text.Remove(_textCaret, 1);
        RenderTextPreview();
    }

    /// <summary>
    /// Take out whatever is selected, leaving the caret where it was.
    /// </summary>
    /// <returns>Whether anything was removed.</returns>
    private bool DeleteTextSelection()
    {
        if (_liveText is not { } text || !HasTextSelection) return false;
        var (start, end) = TextSelection;
        start = Math.Clamp(start, 0, text.Text.Length);
        end = Math.Clamp(end, start, text.Text.Length);
        if (end == start) return false;
        text.Text = text.Text.Remove(start, end - start);
        PutCaret(start);
        return true;
    }

    /// <summary>Move the caret and drop the selection with it.</summary>
    private void PutCaret(int index)
    {
        var length = _liveText?.Text.Length ?? 0;
        _textCaret = Math.Clamp(index, 0, length);
        _textAnchor = _textCaret;
    }

    /// <param name="by">Negative to go back, positive to go on.</param>
    /// <param name="extend">
    /// Shift is down: move the caret and leave the anchor, so the selection
    /// grows or shrinks from the end being moved.
    /// </param>
    /// <remarks>
    /// <b>Without Shift, an arrow collapses a selection to its edge rather than
    /// stepping from the caret.</b> Select a word, press Right, and the caret
    /// goes to the end of the word — not one character past whichever end
    /// happened to be the caret. Every text field behaves this way and the
    /// difference is only ever noticed when it is wrong.
    /// </remarks>
    public void MoveTextCaret(int by, bool extend = false)
    {
        if (_liveText is not { } text) return;
        if (!extend && HasTextSelection)
        {
            var (start, end) = TextSelection;
            PutCaret(by < 0 ? start : end);
            RenderTextPreview();
            return;
        }
        _textCaret = Math.Clamp(_textCaret + by, 0, text.Text.Length);
        if (!extend) _textAnchor = _textCaret;
        RenderTextPreview();
    }

    /// <summary>To the start or the end of the whole block.</summary>
    public void TextCaretToEdge(bool end, bool extend = false)
    {
        if (_liveText is not { } text) return;
        _textCaret = end ? text.Text.Length : 0;
        if (!extend) _textAnchor = _textCaret;
        RenderTextPreview();
    }

    // ---- selecting ----------------------------------------------------------

    /// <summary>Select everything in the block being typed.</summary>
    public void SelectAllText()
    {
        if (_liveText is not { } text) return;
        _textAnchor = 0;
        _textCaret = text.Text.Length;
        RenderTextPreview();
    }

    /// <summary>
    /// Put the caret where a point lands, and start a selection there.
    /// </summary>
    /// <param name="extend">
    /// Shift is down, so this is the far end of a selection from wherever the
    /// caret already was rather than a fresh one.
    /// </param>
    public void PlaceTextCaretAt(double x, double y, bool extend = false)
    {
        if (_liveText is not { } text) return;
        var at = TextLayout.Of(text, _liveTextFace ?? SKTypeface.Default).IndexAt(x, y);
        _textCaret = Math.Clamp(at, 0, text.Text.Length);
        if (!extend) _textAnchor = _textCaret;
        RenderTextPreview();
    }

    /// <summary>Drag the far end of a selection to a point.</summary>
    public void DragTextSelectionTo(double x, double y) => PlaceTextCaretAt(x, y, extend: true);

    /// <summary>Take the word a point lands in — the double-click.</summary>
    public void SelectTextWordAt(double x, double y)
    {
        if (_liveText is not { } text) return;
        var at = TextLayout.Of(text, _liveTextFace ?? SKTypeface.Default).IndexAt(x, y);
        var (start, end) = TextLayout.WordAt(text.Text, at);
        _textAnchor = start;
        _textCaret = end;
        RenderTextPreview();
    }

    /// <summary>
    /// Set the type: bake it to strokes and record it, in one undoable edit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Everything the document learns happens here.</b> The glyph strokes,
    /// the element that lets them be retyped, and — when the licence allows it —
    /// the font itself all go in inside a single delta, so one Ctrl+Z takes the
    /// whole caption back out and leaves the document exactly as heavy as it was.
    /// </para>
    /// <para>
    /// Committing empty text is a deletion when retyping and nothing at all
    /// otherwise: clearing a word and pressing Escape is how type is removed,
    /// and a click that started a caret and typed nothing must leave no trace.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void CommitText()
    {
        if (_liveText is not { } text) return;

        var replacing = _retyping;
        var replacingFrameId = _retypingFrameId;
        var original = _retypingOriginal;
        var paint = _liveTextPaint ?? TextPaintPrototype();
        var typed = text.Text.Trim().Length > 0;

        ClearTextSession();

        if (!typed && replacing is null) return;

        var target = replacingFrameId is not null
            ? FrameById(_editor.Doc, replacingFrameId)
            : PaintTarget();
        if (target is null) return;

        var glyphs = typed && _liveTextFace is { } face
            ? TextBaker.Bake(text, face, paint)
            : [];

        // Only record a font when the face in the bar is the one these words
        // were actually shaped in. Picking type up and pressing Escape without
        // touching anything would otherwise silently re-font it to whatever the
        // tool happened to be set to — and, worse, carry that font into the
        // document for text that is not set in it.
        var chosen = SelectedFont;
        var shapedInChosen = typed
            && chosen is not null
            && text.Font.SameFace(new FontRef
            {
                Family = chosen.Family, Weight = chosen.Weight, Italic = chosen.Italic,
            });
        var choice = shapedInChosen && chosen is not null
            ? Fonts.Reference(chosen, _editor.Doc, Settings.Fonts.EmbedOpenFonts)
            : default;
        if (choice.Reference is { Family.Length: > 0 }) text.Font = choice.Reference;

        var frameId = target.Id;
        var removed = replacing ?? [];
        var indices = new List<int>();

        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    var list = StrokeListIn(doc, frameId);
                    if (list is null) return;

                    // Retyping takes the old letters out where they were, so
                    // redoing puts them back in the same order — a caption that
                    // jumped above the artwork on every edit would be a stacking
                    // bug nobody could explain.
                    indices.Clear();
                    foreach (var old in removed)
                    {
                        var at = list.FindIndex(s => s.Id == old.Id);
                        if (at < 0) continue;
                        indices.Add(at);
                        list.RemoveAt(at);
                    }

                    if (glyphs.Count == 0)
                    {
                        if (removed.Count > 0) doc.Texts?.Remove(text.Id);
                        return;
                    }

                    doc.Texts ??= [];
                    doc.Texts[text.Id] = text;
                    choice.RecordInto(doc);
                    list.AddRange(glyphs);
                },
                revert: doc =>
                {
                    var list = StrokeListIn(doc, frameId);
                    if (list is null) return;

                    foreach (var glyph in glyphs) list.RemoveAll(s => s.Id == glyph.Id);
                    if (glyphs.Count > 0)
                    {
                        doc.Texts?.Remove(text.Id);
                        choice.RemoveFrom(doc);
                        if (doc.Texts is { Count: 0 }) doc.Texts = null;
                    }

                    for (var i = removed.Count - 1; i >= 0; i--)
                    {
                        var at = i < indices.Count ? Math.Min(indices[i], list.Count) : list.Count;
                        list.Insert(at, removed[i]);
                    }
                    // The element as it was before the retype, not the one being
                    // typed over it: undoing must put back the words that go
                    // with the letters being put back.
                    if (original is not null)
                    {
                        doc.Texts ??= [];
                        doc.Texts[original.Id] = original;
                    }
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }

        // Retyping changes stroke order, so the frame is re-rendered rather than
        // added to — the same call the fill tool makes when it tucks under.
        InvalidateFrameRender(frameId);
        _dirtyThumbIds.Add(frameId);
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        AiStatus = glyphs.Count > 0
            ? $"Set {glyphs.Count} letters in {text.Font.Family}."
            : typed
                // Words that shaped to nothing are not words that were deleted,
                // and saying so is the difference between a font to change and a
                // title that mysteriously disappeared.
                ? $"“{text.Font.Family}” set no letters — try another font."
                : "Type removed.";
    }

    /// <summary>Drop the type being typed. Nothing was in the document yet.</summary>
    [RelayCommand]
    public void CancelText()
    {
        if (_liveText is null) return;
        ClearTextSession();
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
    }

    private void ClearTextSession()
    {
        _liveText = null;
        _liveTextPaint = null;
        _retyping = null;
        _retypingFrameId = null;
        _retypingOriginal = null;
        _textCaret = 0;
        _live.ClearScratch();
        OnPropertyChanged(nameof(TextSessionActive));
    }

    private void ReshapeLiveText(Action<TextElement> change)
    {
        if (_liveText is not { } text) return;
        change(text);
        RenderTextPreview();
    }

    /// <summary>
    /// Draw the type as it will be, plus the caret.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The preview is the real bake.</b> The glyph strokes drawn here are the
    /// ones the commit will record, stamped through the same engine — so what an
    /// artist sees while typing is not an approximation of the result, it is the
    /// result. That is the same bargain the shape and gradient previews make.
    /// </para>
    /// <para>
    /// Whole-canvas rather than incremental, because a keystroke can move every
    /// glyph on the line: type is centred, tracked and re-flowed as a unit, so
    /// there is nothing from the previous frame worth keeping.
    /// </para>
    /// </remarks>
    private void RenderTextPreview()
    {
        if (_liveText is not { } text || _live.ScratchCanvas is null) return;

        _live.ClearScratch();
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var face = _liveTextFace ?? SKTypeface.Default;
        var paint = (_liveTextPaint ?? TextPaintPrototype()).Clone();
        paint.Brush.Opacity = 1;
        var layout = TextLayout.Of(text, face);

        // Under the glyphs, which is the whole reason it is drawn first: a
        // highlight over the letters would hide what is selected.
        DrawTextSelection(layout);
        foreach (var glyph in TextBaker.Bake(text, face, paint))
        {
            BrushEngine.StampStroke(_live.ScratchCanvas, glyph, info);
        }

        DrawTextBox(layout);
        DrawTextCaret(text, layout);
        _live.ScratchCanvas.Flush();
        _live.ScratchUsed = new SKRectI(0, 0, Scene.Width, Scene.Height);
        _publish.InvalidateWholeCanvas();
        // And ask for the frame to be published, which is the half that was
        // missing: invalidating says the canvas is stale, publishing is what
        // puts new pixels on it. Without this the composite an artist sees
        // stays the one taken when the caret went down — so every letter
        // reached the record and the scratch, and the screen showed an empty
        // caret sitting still. The shape tool's MoveShape has always paired
        // the two; text did not, and nothing was red because no test looks at
        // what was published.
        RequestSnapshot();
    }

    /// <summary>
    /// The caret: a bar on the baseline where the next letter will go.
    /// </summary>
    /// <remarks>
    /// Drawn straight onto the scratch rather than as a stroke, because it is
    /// chrome — it must never be able to reach a document, and a caret that was
    /// a stroke is one bad commit away from being drawn into somebody's frame.
    /// Sized from the type so it stays visible at any point size.
    /// </remarks>
    /// <summary>The bar behind the selected characters.</summary>
    /// <remarks>
    /// Painted into the live scratch alongside the caret rather than drawn by
    /// the canvas as chrome, because it has to sit <em>under</em> the glyphs and
    /// the glyphs are in the scratch. It is not in the record and never reaches
    /// a commit — <c>CommitText</c> bakes from the element, not from these
    /// pixels.
    /// </remarks>
    private void DrawTextSelection(TextLayout layout)
    {
        if (_live.ScratchCanvas is not { } canvas || !HasTextSelection) return;
        var (start, end) = TextSelection;
        using var paint = new SKPaint
        {
            Color = new SKColor(0x4A, 0x6E, 0xA9, 0x8C),
            IsAntialias = false,
        };
        foreach (var rect in layout.SelectionRects(start, end)) canvas.DrawRect(rect, paint);
    }

    /// <summary>
    /// The block's box, dashed, while it is being typed in.
    /// </summary>
    /// <remarks>
    /// <b>The box you can see is the box that responds.</b> Hit-testing moved
    /// from glyph outlines to this rectangle, so drawing it is what makes the
    /// new rule legible — without it an artist would be aiming at a target the
    /// application knows about and they do not. Dashed and thin so it reads as
    /// chrome rather than as a rule somebody drew.
    /// </remarks>
    private void DrawTextBox(TextLayout layout)
    {
        if (_live.ScratchCanvas is not { } canvas) return;
        var box = layout.Box;
        if (box.Width <= 0 && box.Height <= 0) return;
        using var paint = new SKPaint
        {
            Color = new SKColor(0x4A, 0x6E, 0xA9, 0xB4),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash([4, 4], 0),
        };
        canvas.DrawRect(
            new SKRect(
                box.Left - (float)TypeBoxSlack,
                box.Top - (float)TypeBoxSlack,
                box.Right + (float)TypeBoxSlack,
                box.Bottom + (float)TypeBoxSlack),
            paint);
    }

    private void DrawTextCaret(TextElement text, TextLayout layout)
    {
        if (_live.ScratchCanvas is not { } canvas) return;
        // A caret inside a selection is noise — the highlight already says where
        // the next keystroke lands, and every text field hides it.
        if (HasTextSelection) return;

        var (x, baseline) = layout.Caret(_textCaret);
        var top = (float)(baseline + layout.Ascent);
        var bottom = (float)(baseline + layout.Descent);
        var width = (float)Math.Max(1, text.Size / 24);

        using var paint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(190),
            IsAntialias = false,
        };
        canvas.DrawRect((float)x, top, width, bottom - top, paint);
    }
}
