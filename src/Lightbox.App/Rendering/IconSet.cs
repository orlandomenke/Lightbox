using Avalonia;
using Avalonia.Media;

namespace Lightbox.App.Rendering;

/// <summary>
/// Every icon the application has, named once.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the registry half of "Lightbox draws its own icons", and it is
/// separable from the artwork on purpose.</b> The set was previously a pile of
/// <c>StaticResource</c> keys typed into XAML by hand, which fails in the two
/// ways an unregistered capability always fails: a typo shows a blank button
/// rather than an error, and a tool that wants a glyph nobody drew reaches for
/// whatever the system font has — which is how eleven Unicode characters and
/// one emoji ended up sitting beside twenty-one hand-drawn monoline paths.
/// </para>
/// <para>
/// So this type exists to be <em>enumerated</em>. <c>IconSetTests</c> walks
/// <see cref="All"/> and requires every name to resolve to a real geometry, and
/// walks the XAML the other way to require every icon it references to be
/// named here. Redrawing the set later is then a single swap rather than a
/// hunt, which is the other thing the roadmap asks of it.
/// </para>
/// <para>
/// The names are the resource keys, deliberately: one string, not a mapping
/// that can disagree with itself.
/// </para>
/// </remarks>
public static class IconSet
{
    // ---- state pairs: the glyph carries the state ---------------------------

    public const string EyeOpen = "IconEyeOpen";
    public const string EyeClosed = "IconEyeClosed";
    public const string LockOpen = "IconLockOpen";
    public const string LockClosed = "IconLockClosed";
    public const string AlphaLock = "IconAlphaLock";
    public const string OnionOn = "IconOnionOn";
    public const string OnionOff = "IconOnionOff";

    // ---- the tool rail ------------------------------------------------------

    public const string Brush = "IconBrush";
    public const string Eraser = "IconEraser";
    public const string Fill = "IconFill";
    public const string Picker = "IconPicker";
    public const string Gradient = "IconGradient";
    public const string Move = "IconMove";
    public const string Crop = "IconCrop";
    public const string Bone = "IconBone";
    public const string Pen = "IconPen";
    public const string Width = "IconWidth";
    public const string Arrow = "IconArrow";
    public const string Points = "IconPoints";

    // ---- shape variants -----------------------------------------------------

    public const string ShapeLine = "IconShapeLine";
    public const string ShapeRect = "IconShapeRect";
    public const string ShapeEllipse = "IconShapeEllipse";
    public const string ShapePolygon = "IconShapePolygon";

    // ---- select variants ----------------------------------------------------

    public const string SelectLasso = "IconSelectLasso";
    public const string SelectBox = "IconSelectBox";
    public const string SelectEllipse = "IconSelectEllipse";
    public const string SelectPolygon = "IconSelectPolygon";
    public const string SelectWand = "IconSelectWand";

    // ---- everyday verbs -----------------------------------------------------

    public const string Close = "IconClose";
    public const string Plus = "IconPlus";
    public const string Minus = "IconMinus";
    public const string Trash = "IconTrash";
    public const string Folder = "IconFolder";
    public const string Eject = "IconEject";
    public const string ChevronDown = "IconChevronDown";
    public const string ChevronUp = "IconChevronUp";
    public const string ChevronRight = "IconChevronRight";
    public const string Gear = "IconGear";
    public const string Kebab = "IconKebab";
    public const string Pencil = "IconPencil";
    public const string Duplicate = "IconDuplicate";
    public const string Undo = "IconUndo";
    public const string Redo = "IconRedo";
    public const string ArrowUp = "IconArrowUp";
    public const string ArrowDown = "IconArrowDown";
    public const string ArrowUpToBar = "IconArrowUpToBar";
    public const string ArrowDownToBar = "IconArrowDownToBar";
    public const string SwapHorizontal = "IconSwapHorizontal";
    public const string SwapVertical = "IconSwapVertical";
    public const string Refresh = "IconRefresh";
    public const string Key = "IconKey";
    public const string Grip = "IconGrip";

    // ---- transforms of the drawing or the view ------------------------------

    public const string MirrorHorizontal = "IconMirrorHorizontal";
    public const string MirrorVertical = "IconMirrorVertical";
    public const string RotateCw = "IconRotateCw";
    public const string RotateCcw = "IconRotateCcw";
    public const string Home = "IconHome";

    // ---- the shortcut bar's toggles -----------------------------------------

    public const string Camera = "IconCamera";
    public const string Snap = "IconSnap";
    public const string Guides = "IconGuides";
    public const string ReferenceGrid = "IconReferenceGrid";

    // ---- panels and windows -------------------------------------------------

    public const string Window = "IconWindow";
    public const string Board = "IconBoard";
    public const string Float = "IconFloat";
    public const string Dock = "IconDock";

    // ---- the transport ------------------------------------------------------

    public const string Play = "IconPlay";
    public const string PlayReverse = "IconPlayReverse";
    public const string Pause = "IconPause";
    public const string SkipToStart = "IconSkipToStart";
    public const string SkipToEnd = "IconSkipToEnd";
    public const string FastRewind = "IconFastRewind";
    public const string FastForward = "IconFastForward";
    public const string Loop = "IconLoop";

    /// <summary>The refusal, worn by the pointer rather than by a button.</summary>
    public const string Forbidden = "IconForbidden";

    /// <summary>Every name above, for the tests that walk the set.</summary>
    /// <remarks>
    /// Written out rather than reflected over the constants. Reflection would
    /// keep itself up to date and would also make the list unreadable at the
    /// one moment it matters — when somebody is working out whether the icon
    /// they need already exists.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        EyeOpen, EyeClosed, LockOpen, LockClosed, AlphaLock, OnionOn, OnionOff,
        Brush, Eraser, Fill, Picker, Gradient, Move, Crop, Bone, Pen, Width, Arrow, Points,
        ShapeLine, ShapeRect, ShapeEllipse, ShapePolygon,
        SelectLasso, SelectBox, SelectEllipse, SelectPolygon, SelectWand,
        Close, Plus, Minus, Trash, Folder, Eject, ChevronDown, ChevronUp, ChevronRight,
        Gear, Kebab, Pencil, Duplicate, Undo, Redo,
        ArrowUp, ArrowDown, ArrowUpToBar, ArrowDownToBar,
        SwapHorizontal, SwapVertical, Refresh, Key, Grip,
        MirrorHorizontal, MirrorVertical, RotateCw, RotateCcw, Home,
        Camera, Snap, Guides, ReferenceGrid,
        Window, Board, Float, Dock,
        Play, PlayReverse, Pause, SkipToStart, SkipToEnd, FastRewind, FastForward, Loop,
        Forbidden,
    ];

    /// <summary>The geometry for a name, or null when nothing is registered.</summary>
    /// <remarks>
    /// Null rather than a throw, because the callers are view models building a
    /// binding: a missing icon should leave a button blank and fail a test, not
    /// take the window down the way <c>ProjectWindow</c>'s data format did.
    /// </remarks>
    public static Geometry? Resolve(string name)
    {
        if (Application.Current is not { } app) return null;
        return app.Resources.TryGetResource(name, null, out var found) ? found as Geometry : null;
    }

    /// <summary>The icon for a shape the shape tool is set to draw.</summary>
    public static string ForShape(Core.Geometry.ShapeKind shape) => shape switch
    {
        Core.Geometry.ShapeKind.Line => ShapeLine,
        Core.Geometry.ShapeKind.Ellipse => ShapeEllipse,
        Core.Geometry.ShapeKind.Polygon => ShapePolygon,
        _ => ShapeRect,
    };

    /// <summary>The icon a tool wears wherever the UI shows one.</summary>
    /// <remarks>
    /// One mapping, so the rail, the Quick options bar and anything later
    /// cannot disagree about what a tool looks like. The two tools whose
    /// button changes with their setting fall back to a representative here —
    /// the bar's icon says which <em>tool</em> is in hand, not which variant.
    /// </remarks>
    public static string ForTool(ViewModels.ToolId tool) => tool switch
    {
        ViewModels.ToolId.Brush => Brush,
        ViewModels.ToolId.Eraser => Eraser,
        ViewModels.ToolId.Fill => Fill,
        ViewModels.ToolId.Picker => Picker,
        ViewModels.ToolId.Gradient => Gradient,
        ViewModels.ToolId.Move => Move,
        ViewModels.ToolId.Crop => Crop,
        ViewModels.ToolId.Pen => Pen,
        ViewModels.ToolId.Width => Width,
        ViewModels.ToolId.Arrow => Arrow,
        ViewModels.ToolId.DirectSelect => Points,
        ViewModels.ToolId.Shape => ShapeRect,
        _ => SelectLasso,
    };

    /// <summary>The icon for the select tool's current variant.</summary>
    public static string ForSelect(ViewModels.SelectVariant variant) => variant switch
    {
        ViewModels.SelectVariant.Box => SelectBox,
        ViewModels.SelectVariant.Ellipse => SelectEllipse,
        ViewModels.SelectVariant.Polygon => SelectPolygon,
        ViewModels.SelectVariant.Wand => SelectWand,
        _ => SelectLasso,
    };
}
