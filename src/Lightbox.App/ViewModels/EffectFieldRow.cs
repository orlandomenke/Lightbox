using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>How a field is edited.</summary>
public enum EffectFieldKind
{
    /// <summary>A number with a range and a step.</summary>
    Number,

    /// <summary>One of <see cref="EffectFieldRow.Choices"/>; the value is the index.</summary>
    Choice,

    /// <summary>On or off; the value is 0 or 1.</summary>
    Toggle,
}

/// <summary>
/// One tunable number on an effect, and — where it is part of the line
/// treatment — whether this element states it or inherits it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One row type for the physics and for the style</b>, because the window is
/// otherwise four hand-written panels that drift apart. Adding a solver
/// parameter is a line in a builder rather than a control in XAML, and the
/// artist gets the same widget, the same range clamping and the same revert
/// wherever the number lives.
/// </para>
/// <para>
/// <b><see cref="IsOverridden"/> is the cascade's one unavoidable cost, paid</b>
/// (Q118, Q123). An overridden field has to be visible as overridden and
/// revertible in one action, or "two places to look" reads to an artist as the
/// application being inconsistent rather than as a cascade. Fields that are not
/// part of a cascade — everything the solver reads — report
/// <see cref="IsOverridable"/> false and the window hides the marker.
/// </para>
/// <para>
/// <b><see cref="Resolves"/> is what tells the window whether this edit costs a
/// redraw or a re-simulation.</b> It is set by the builder that made the row
/// rather than inferred, for the same reason <c>SolveFingerprint</c> is written
/// out in full: a guess that goes stale shows up as a slider that appears to do
/// nothing.
/// </para>
/// </remarks>
public sealed partial class EffectFieldRow : ObservableObject
{
    private readonly Func<double> _effective;
    private readonly Func<bool>? _stated;
    private readonly Action<double?> _write;
    private readonly Action<EffectFieldRow> _changed;
    private bool _quiet;

    internal EffectFieldRow(
        string name, EffectFieldKind kind,
        Func<double> effective, Func<bool>? stated, Action<double?> write, Action<EffectFieldRow> changed,
        double min = 0, double max = 1, double step = 0.05,
        IReadOnlyList<string>? choices = null, bool resolves = false, string? hint = null)
    {
        Name = name;
        Kind = kind;
        Minimum = min;
        Maximum = max;
        Step = step;
        Choices = choices;
        Resolves = resolves;
        Hint = hint;
        _effective = effective;
        _stated = stated;
        _write = write;
        _changed = changed;
        _value = effective();
    }

    public string Name { get; }

    public EffectFieldKind Kind { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    /// <summary>Options for a <see cref="EffectFieldKind.Choice"/>; the value is the index.</summary>
    public IReadOnlyList<string>? Choices { get; }

    /// <summary>Whether changing this forces the element to be simulated again.</summary>
    public bool Resolves { get; }

    /// <summary>A sentence for the tooltip, or null.</summary>
    public string? Hint { get; }

    public bool IsNumber => Kind == EffectFieldKind.Number;

    public bool IsChoice => Kind == EffectFieldKind.Choice;

    public bool IsToggle => Kind == EffectFieldKind.Toggle;

    [ObservableProperty]
    private double _value;

    /// <summary>Whether this field can be inherited at all.</summary>
    public bool IsOverridable => _stated is not null;

    /// <summary>Whether this element states this field rather than inheriting it.</summary>
    public bool IsOverridden => _stated?.Invoke() ?? false;

    /// <summary>The toggle's state, for a two-way binding that wants a bool.</summary>
    public bool IsOn
    {
        get => Value >= 0.5;
        set => Value = value ? 1 : 0;
    }

    /// <summary>The choice's index, for a two-way binding that wants an int.</summary>
    public int ChoiceIndex
    {
        get => (int)Math.Round(Value);
        set => Value = value;
    }

    /// <summary>Put the field back to whatever the shared treatment or the default says.</summary>
    public void Revert()
    {
        if (!IsOverridable) return;
        _write(null);
        Refresh();
        _changed(this);
    }

    /// <summary>Re-read from the record — after a revert, or after the element changed.</summary>
    public void Refresh()
    {
        _quiet = true;
        Value = _effective();
        _quiet = false;
        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(ChoiceIndex));
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(ChoiceIndex));
        if (_quiet) return;
        _write(value);
        OnPropertyChanged(nameof(IsOverridden));
        _changed(this);
    }
}

/// <summary>One effects element in the list.</summary>
/// <remarks>
/// <b>It holds an id, not the element.</b> Undo does not edit the document in
/// place — it swaps a whole <see cref="Doc"/> back in — so a row that held the
/// object would go on editing a document nobody is looking at, silently, with
/// every slider still moving. Resolving by id each time costs a dictionary
/// lookup and cannot do that.
/// </remarks>
public sealed partial class SimElementRow : ObservableObject
{
    private readonly Func<string, SimElement?> _resolve;

    internal SimElementRow(string id, Func<string, SimElement?> resolve)
    {
        Id = id;
        _resolve = resolve;
    }

    public string Id { get; }

    /// <summary>The element this row stands for, or null once it has been deleted.</summary>
    public SimElement? Element => _resolve(Id);

    public string Label
    {
        get
        {
            if (Element is not { } e) return "(missing)";
            return string.IsNullOrWhiteSpace(e.Name)
                ? $"{e.Kind} · {e.FirstFrame}–{e.FirstFrame + e.FrameCount - 1}"
                : e.Name!;
        }
    }

    /// <summary>Nudge the list when something the label reads has changed.</summary>
    public void Relabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>One emitter in the selected element, by id for the same reason.</summary>
public sealed partial class EmitterRow : ObservableObject
{
    private readonly Func<string, Emitter?> _resolve;

    internal EmitterRow(string id, Func<string, Emitter?> resolve)
    {
        Id = id;
        _resolve = resolve;
    }

    public string Id { get; }

    public Emitter? Emitter => _resolve(Id);

    public string Label => Emitter is { } e
        ? $"{e.Shape} · {e.X:F0},{e.Y:F0}{(e.Travels ? " ▸" : string.Empty)}{(e.MaskLayerId is null ? string.Empty : " ▧")}"
        : "(missing)";

    public void Relabel() => OnPropertyChanged(nameof(Label));
}
