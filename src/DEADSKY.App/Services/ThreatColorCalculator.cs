using System.Windows.Media;

namespace DEADSKY.App.Services;

/// <summary>
/// Calculates threat color coding based on range, inbound status, and weapons status.
/// Colors meet WCAG AA contrast ratio (4.5:1) against dark backgrounds.
/// </summary>
public static class ThreatColorCalculator
{
    // Color definitions (WCAG AA compliant)
    private static readonly Color CriticalRed = Color.FromRgb(0xDC, 0x35, 0x45);   // #DC3545
    private static readonly Color HighOrange = Color.FromRgb(0xFD, 0x7E, 0x14);    // #FD7E14
    private static readonly Color MediumYellow = Color.FromRgb(0xFF, 0xC1, 0x07);  // #FFC107
    private static readonly Color LowGreen = Color.FromRgb(0x28, 0xA7, 0x45);      // #28A745

    /// <summary>
    /// Determines the appropriate threat color based on range, inbound status, and weapons status.
    /// </summary>
    /// <param name="rangeNm">Range to threat in nautical miles</param>
    /// <param name="isInbound">True if threat is closing on battery (HOT aspect)</param>
    /// <param name="weaponsHot">True if threat has weapons hot/hostile classification</param>
    /// <returns>Color representing threat level</returns>
    public static Color GetThreatColor(double rangeNm, bool isInbound, bool weaponsHot)
    {
        // Critical: <20nm AND inbound AND weapons hot
        if (rangeNm < 20 && isInbound && weaponsHot)
            return CriticalRed;

        // High: 20-40nm AND inbound
        if (rangeNm >= 20 && rangeNm < 40 && isInbound)
            return HighOrange;

        // Medium: 40-80nm AND tracking (inbound)
        if (rangeNm >= 40 && rangeNm < 80 && isInbound)
            return MediumYellow;

        // Low: >80nm (or not inbound)
        return LowGreen;
    }

    /// <summary>
    /// Gets a SolidColorBrush for the specified threat parameters.
    /// </summary>
    public static SolidColorBrush GetThreatBrush(double rangeNm, bool isInbound, bool weaponsHot)
    {
        return new SolidColorBrush(GetThreatColor(rangeNm, isInbound, weaponsHot));
    }
}
