using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GalacticDiagnosis.Gui;

/// <summary>Visible when the value is true or a non-empty object; Invert flips it.</summary>
public sealed class VisibleWhen : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch
        {
            bool b => b,
            string s => s.Length > 0,
            int n => n > 0,
            null => false,
            _ => true,
        };
        return on != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
