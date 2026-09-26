using System.Globalization;
using System.Windows.Data;
using LocalRack.Models;

namespace LocalRack.Converters;

public sealed class ServiceStatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ServiceStatus.Running ? "Stop" : "Start";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
