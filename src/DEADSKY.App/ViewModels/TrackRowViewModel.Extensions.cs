using System;
using System.Collections.Generic;
namespace DEADSKY.App.ViewModels;

public partial class TrackRowViewModel
{
    public string Callout => IsMissileDesignation(Designation)
        ? "VAMPIRE"
        : Classification switch
        {
            "HOSTILE" => "BANDIT",
            "ASSUMEDHOSTILE" => "SUSPECT",
            "FRIENDLY" => "FRIENDLY",
            "NEUTRAL" => "NEUTRAL",
            "CIVILIAN" => "CIVILIAN",
            _ => "BOGEY"
        };

    public string Identity => Classification switch
    {
        "HOSTILE" => "CONFIRMED HOSTILE",
        "ASSUMEDHOSTILE" => "ASSUMED HOSTILE",
        "FRIENDLY" => "FRIENDLY IFF",
        "NEUTRAL" => "NEUTRAL",
        "CIVILIAN" => "CIVIL TRAFFIC",
        _ => "UNKNOWN / UNDECLARED"
    };

    public string StateDisplay => Callout == "VAMPIRE"
        ? "MISSILE INBOUND"
        : IsBeingEngaged
            ? "SAM INBOUND"
            : QualityLabel == "FADING"
                ? "FADE / COAST"
                : QualityLabel == "LOST"
                    ? "TRACK LOST"
        : Aspect.Equals("HOT", StringComparison.OrdinalIgnoreCase)
            ? ThreatText == "HIGH" ? "INBOUND / PRIORITY" : "CLOSING"
            : "MANEUVER / COLD";

    public string TimeToThreatDisplay => Aspect.Equals("HOT", StringComparison.OrdinalIgnoreCase)
        ? ThreatText switch
        {
            "HIGH" => "IMMINENT",
            "MED" => "SOON",
            _ => "TRACKING"
        }
        : "---";

    public string TagsDisplay
    {
        get
        {
            var tags = new List<string>
            {
                PackageLabel,
                Aspect.ToUpperInvariant(),
                ThreatText
            };

            if (HasNoIff)
                tags.Add("NO-IFF");

            if (IsBeingEngaged)
                tags.Add("ENGAGED");

            if (QualityLabel is not "FIRM")
                tags.Add(QualityLabel);

            return string.Join(" | ", tags);
        }
    }

    partial void OnDesignationChanged(string value) => RaiseDerivedBindings();
    partial void OnClassificationChanged(string value) => RaiseDerivedBindings();
    partial void OnThreatTextChanged(string value) => RaiseDerivedBindings();
    partial void OnAspectChanged(string value) => RaiseDerivedBindings();
    partial void OnPackageLabelChanged(string value) => RaiseDerivedBindings();
    partial void OnIsBeingEngagedChanged(bool value) => RaiseDerivedBindings();
    partial void OnHasNoIffChanged(bool value) => RaiseDerivedBindings();
    partial void OnQualityLabelChanged(string value) => RaiseDerivedBindings();

    private void RaiseDerivedBindings()
    {
        OnPropertyChanged(nameof(Callout));
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(StateDisplay));
        OnPropertyChanged(nameof(TimeToThreatDisplay));
        OnPropertyChanged(nameof(TagsDisplay));
    }

    private static bool IsMissileDesignation(string designation)
    {
        string normalized = designation.ToUpperInvariant();
        return normalized.Contains("MISSILE", StringComparison.Ordinal) ||
               normalized.Contains("CRUISE", StringComparison.Ordinal) ||
               normalized.Contains("KH-", StringComparison.Ordinal) ||
               normalized.Contains("AGM-", StringComparison.Ordinal) ||
               normalized.Contains("9M", StringComparison.Ordinal);
    }
}
