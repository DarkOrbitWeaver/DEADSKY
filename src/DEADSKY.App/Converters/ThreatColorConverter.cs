using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DEADSKY.App.Services;
using DEADSKY.App.ViewModels;

namespace DEADSKY.App.Converters;

/// <summary>
/// Converts a TrackRowViewModel to a SolidColorBrush based on threat level.
/// Uses ThreatColorCalculator to determine appropriate color coding.
/// </summary>
public class ThreatColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TrackRowViewModel trackRow || trackRow.TrackFile == null)
            return new SolidColorBrush(Colors.Gray);

        var track = trackRow.TrackFile;

        // Determine if weapons hot (hostile or assumed hostile)
        bool weaponsHot = track.Classification == Core.Radar.TrackClassification.Hostile ||
                          track.Classification == Core.Radar.TrackClassification.AssumedHostile;

        // Use IsHot property which indicates closing on battery
        bool isInbound = track.IsHot;

        // Get range in nautical miles
        double rangeNm = track.RangeNm;

        return ThreatColorCalculator.GetThreatBrush(rangeNm, isInbound, weaponsHot);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ThreatColorConverter does not support ConvertBack");
    }
}
