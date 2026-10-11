using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SensorPanelGlue.UI;

/// <summary>Collapses an element whose bound text is empty - an InfoBar with nothing to say.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible when a list is empty: the empty-state line. Bound to the collection's Count so it updates
/// as rows come and go (binding to the collection itself would not re-evaluate on change).
/// </summary>
public sealed class EmptyCollectionToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int count => count == 0 ? Visibility.Visible : Visibility.Collapsed,
        ICollection collection => collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
