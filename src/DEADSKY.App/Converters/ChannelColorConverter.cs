using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DEADSKY.Core.Comms;

namespace DEADSKY.App.Converters;

/// <summary>
/// Converts a RadioChannel to a SolidColorBrush for channel-specific color coding.
/// Maps channels to distinct colors for quick visual identification.
/// </summary>
public class ChannelColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not RadioChannel channel)
            return new SolidColorBrush(Colors.Gray);

        return channel switch
        {
            RadioChannel.CommandNet => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4D8EC0")), // Blue
            RadioChannel.IntelNet => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9B59B6")),    // Purple
            RadioChannel.BatteryNet => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745")),  // Green
            RadioChannel.Guard => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545")),       // Red
            RadioChannel.AirDefenseNet => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4D8EC0")), // Blue (same as Command)
            RadioChannel.OpenFreq => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107")),    // Yellow/Amber
            _ => new SolidColorBrush(Colors.Gray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ChannelColorConverter does not support ConvertBack");
    }
}
