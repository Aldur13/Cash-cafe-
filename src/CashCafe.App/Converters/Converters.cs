using System;

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CashCafe.Domain;

namespace CashCafe.App.Converters;

/// <summary>Formats a <see cref="Money"/> as "40,00 kr" wherever XAML binds to one.</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Money money ? money.ToString() : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && MoneyParser.Parse(text) is { Success: true } parsed
            ? parsed.Value
            : Binding.DoNothing;
}

/// <summary>
/// Colours a balance the way the wiki describes: black when comfortable, amber when running
/// low, red when in debt. The same three states the student site uses, so a student is told
/// the same thing on their phone as they are at the counter.
/// </summary>
public sealed class BalanceBrushConverter : IValueConverter
{
    public static Money LowThreshold { get; set; } = Money.FromKronor(20);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Money money) return Brushes.Black;

        if (money.IsNegative) return new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E));
        if (money <= LowThreshold) return new SolidColorBrush(Color.FromRgb(0x9A, 0x64, 0x12));

        return new SolidColorBrush(Color.FromRgb(0x17, 0x20, 0x1C));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter as string == "invert") flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
