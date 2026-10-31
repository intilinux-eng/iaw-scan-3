using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace IES_2.Avalonia.Controls
{
    /// <summary>True when the bound page key is the one given as parameter, for highlighting the sidebar.</summary>
    public sealed class EqualsConverter : IValueConverter
    {
        public static readonly EqualsConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Equals(value, parameter);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>True for a count above zero, for showing badges only when there's something to count.</summary>
    public sealed class IsPositiveConverter : IValueConverter
    {
        public static readonly IsPositiveConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int count && count > 0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
