using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Lightbox.App.Controls;

/// <summary>
/// Arithmetic in every numeric field: type <c>50+10</c>, <c>128/2</c> or
/// <c>12 * 4</c> and committing evaluates it, spaces or no spaces. An artist
/// nudging a value does the sum where the number already is instead of in
/// their head.
/// </summary>
/// <remarks>
/// <para>
/// Wired through <see cref="NumericUpDown.TextConverter"/>, which the control
/// consults on every commit (Enter and focus loss) — so there is no event
/// ordering to lose against the control's own commit. The converter is
/// per-control, created by the attached property a style sets on every
/// <see cref="NumericUpDown"/>, because the text-to-value direction is only
/// half the job: the value-to-text direction must keep honouring the field's
/// own <c>FormatString</c>, and a shared converter cannot see it.
/// </para>
/// <para>
/// A plain number takes the same path it always took (parsed with the
/// control's <c>NumberFormat</c>); the evaluator only engages when an
/// operator is present. Anything it cannot evaluate — including division by
/// zero — returns null, which the control treats exactly as it treats
/// unparseable input.
/// </para>
/// </remarks>
public static class FieldMath
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>("Enabled", typeof(FieldMath));

    public static bool GetEnabled(NumericUpDown host) => host.GetValue(EnabledProperty);
    public static void SetEnabled(NumericUpDown host, bool value) => host.SetValue(EnabledProperty, value);

    /// <summary>
    /// Show and read this field as 0–100 while the bound value stays 0–1.
    /// </summary>
    /// <remarks>
    /// Only the text changes scale. The value, the binding, the slider beside
    /// the field, <c>Increment</c> and the stroke record all stay 0–1, which
    /// is what keeps invariant 4 out of this entirely — a percent field is a
    /// presentation, not a second unit the document could catch. Lives here
    /// because the TextConverter is the one place both text directions pass
    /// through.
    /// </remarks>
    public static readonly AttachedProperty<bool> PercentProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>("Percent", typeof(FieldMath));

    public static bool GetPercent(NumericUpDown host) => host.GetValue(PercentProperty);
    public static void SetPercent(NumericUpDown host, bool value) => host.SetValue(PercentProperty, value);

    static FieldMath()
    {
        EnabledProperty.Changed.AddClassHandler<NumericUpDown>((host, e) =>
            host.TextConverter = e.NewValue is true ? new Converter(host) : null);
    }

    /// <summary>
    /// Evaluate <paramref name="text"/> as an arithmetic expression:
    /// <c>+ - * /</c> with the usual precedence, parentheses, unary minus,
    /// decimals with either <c>.</c> or <c>,</c>. Whitespace is ignored
    /// everywhere. Null when the text is not an expression it understands.
    /// </summary>
    public static decimal? Evaluate(string text)
    {
        var p = new Parser(text);
        var value = p.ParseExpression();
        return value is not null && p.AtEnd ? value : null;
    }

    /// <summary>
    /// Recursive descent over: expr := term (('+'|'-') term)*;
    /// term := factor (('*'|'/') factor)*; factor := '-' factor | number |
    /// '(' expr ')'.
    /// </summary>
    private ref struct Parser(string text)
    {
        private readonly string _s = text;
        private int _i;

        public bool AtEnd
        {
            get
            {
                SkipSpace();
                return _i >= _s.Length;
            }
        }

        private void SkipSpace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private bool Take(char c)
        {
            SkipSpace();
            if (_i < _s.Length && _s[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }

        public decimal? ParseExpression()
        {
            var left = ParseTerm();
            while (left is not null)
            {
                if (Take('+')) left += ParseTerm();
                else if (Take('-')) left -= ParseTerm();
                else break;
            }
            return left;
        }

        private decimal? ParseTerm()
        {
            var left = ParseFactor();
            while (left is not null)
            {
                if (Take('*'))
                {
                    left *= ParseFactor();
                }
                else if (Take('/'))
                {
                    var right = ParseFactor();
                    if (right is null or 0) return null;
                    left /= right;
                }
                else
                {
                    break;
                }
            }
            return left;
        }

        private decimal? ParseFactor()
        {
            if (Take('-')) return -ParseFactor();
            if (Take('('))
            {
                var inner = ParseExpression();
                return Take(')') ? inner : null;
            }
            SkipSpace();
            var start = _i;
            var seenPoint = false;
            while (_i < _s.Length && (char.IsAsciiDigit(_s[_i]) || (!seenPoint && _s[_i] is '.' or ',')))
            {
                seenPoint |= _s[_i] is '.' or ',';
                _i++;
            }
            if (_i == start) return null;
            // One token, either decimal mark: fields never show thousands
            // separators, so a comma here is someone typing their locale's
            // decimal point rather than grouping digits.
            var token = _s[start.._i].Replace(',', '.');
            return decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;
        }
    }

    private sealed class Converter(NumericUpDown host) : IValueConverter
    {
        /// <summary>Text going in: a number as before, or an expression.</summary>
        /// <remarks>
        /// Never null for text that does not read as a number: an empty or
        /// unreadable commit means <em>no change</em>, so the field's current
        /// value comes back instead. Null used to flow into the two-way
        /// binding, fail to become a double, and leave the field wearing a
        /// validation error that blocked typing until something else moved
        /// the value — clearing a field to retype it is not a request to
        /// unset it.
        /// </remarks>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Read(value) ?? host.Value;
        }

        private decimal? Read(object? value)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text)) return null;
            var scale = GetPercent(host) ? 100m : 1m;
            // The path plain numbers always took, kept byte-for-byte: the
            // evaluator only earns its keep when an operator is present.
            if (decimal.TryParse(text, host.ParsingNumberStyle, host.NumberFormat, out var plain)) return plain / scale;
            return Evaluate(text) / scale;
        }

        /// <summary>Value going out: exactly what the control would have done.</summary>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not decimal d) return null;
            // A percent field shows whole points; its FormatString was written
            // for the 0–1 scale and would render 0.5 as "50.00".
            if (GetPercent(host)) return (d * 100m).ToString("0.#", host.NumberFormat);
            return string.IsNullOrEmpty(host.FormatString)
                ? d.ToString(host.NumberFormat)
                : host.FormatString.Contains('{')
                    ? string.Format(host.NumberFormat, host.FormatString, d)
                    : d.ToString(host.FormatString, host.NumberFormat);
        }
    }
}
