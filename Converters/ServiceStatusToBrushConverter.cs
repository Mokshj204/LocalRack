using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LocalRack.Models;

namespace LocalRack.Converters;

public sealed class ServiceStatusToBrushConverter : IValueConverter
{
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x4F));
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));

    static ServiceStatusToBrushConverter()
    {
        RunningBrush.Freeze();
        StoppedBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ServiceStatus.Running ? RunningBrush : StoppedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
