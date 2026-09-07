using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Vestigium.Logging.Demo;

public sealed class LevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Verbose = Freeze("#64748B");
    private static readonly SolidColorBrush Debug = Freeze("#94A3B8");
    private static readonly SolidColorBrush Information = Freeze("#7DD3FC");
    private static readonly SolidColorBrush Warning = Freeze("#FBBF24");
    private static readonly SolidColorBrush Error = Freeze("#F87171");
    private static readonly SolidColorBrush Fatal = Freeze("#FB7185");
    private static readonly SolidColorBrush Fallback = Freeze("#E2E8F0");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Verbose" => Verbose,
            "Debug" => Debug,
            "Information" => Information,
            "Warning" => Warning,
            "Error" => Error,
            "Fatal" => Fatal,
            _ => Fallback
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
