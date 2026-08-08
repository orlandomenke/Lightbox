using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Numeric fields evaluate arithmetic on commit: 50+10, 12*4, 128/2 —
/// with or without spaces, per the owner's request.
/// </summary>
public class FieldMathTests
{
    [Theory]
    [InlineData("50+10", 60)]
    [InlineData("50 + 10", 60)]
    [InlineData("50   +10", 60)]
    [InlineData("100-25", 75)]
    [InlineData("12*4", 48)]
    [InlineData("128/2", 64)]
    [InlineData("2+3*4", 14)]          // precedence, not left-to-right
    [InlineData("(2+3)*4", 20)]
    [InlineData("-5+10", 5)]
    [InlineData("10*-2", -20)]
    [InlineData("0.5*10", 5)]
    [InlineData("0,5*10", 5)]          // either decimal mark
    [InlineData("100/3*3", 100)]
    public void ExpressionsEvaluate(string text, decimal expected)
    {
        Assert.Equal(expected, FieldMath.Evaluate(text));
    }

    [Theory]
    [InlineData("10/0")]               // the control treats null as bad input
    [InlineData("5+")]
    [InlineData("*4")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("abc")]
    [InlineData("1..2")]
    [InlineData("")]
    public void WhatCannotBeEvaluatedIsRefused(string text)
    {
        Assert.Null(FieldMath.Evaluate(text));
    }

    [AvaloniaFact]
    public void TypingASumIntoAFieldCommitsTheResult()
    {
        // Through the real control, because the converter is only wired if
        // the style actually reaches every NumericUpDown.
        var num = new NumericUpDown { Value = 1, Minimum = 0, Maximum = 1000, FormatString = "0" };
        var window = new Window { Content = new StackPanel { Children = { num } } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        num.Text = "50+10";
        num.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(60m, num.Value);

        // The other direction: a value renders through the field's own
        // format, not the converter's idea of one. Driven by a Value change
        // because that is the one path that always re-renders the text.
        num.Value = 128;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("128", num.Text);
    }

    [AvaloniaFact]
    public void APlainNumberStillCommitsExactlyAsBefore()
    {
        var num = new NumericUpDown { Value = 1, Minimum = 0, Maximum = 1000, FormatString = "0.00" };
        var window = new Window { Content = new StackPanel { Children = { num } } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        num.Text = "42.5";
        num.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(42.5m, num.Value);

        num.Value = 7.5m;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("7.50", num.Text);
    }
}
