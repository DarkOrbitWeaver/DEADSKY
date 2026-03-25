namespace DEADSKY.Core.Scenario;

public static class ScenarioContractNormalizer
{
    public static ScenarioContract Normalize(ScenarioContract contract)
    {
        var normalized = new ScenarioContract
        {
            Name = string.IsNullOrWhiteSpace(contract.Name) ? "Realistic Mode // Generated Shift" : contract.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(contract.Description)
                ? "AI-generated realistic mode scenario."
                : contract.Description.Trim(),
            DurationMinutes = Math.Max(10, contract.DurationMinutes),
            Objectives = contract.Objectives.Select(NormalizeObjective).ToList(),
            Packages = contract.Packages.Select(NormalizePackage).ToList(),
            Weather = new ScenarioWeatherContract
            {
                Description = string.IsNullOrWhiteSpace(contract.Weather.Description) ? "Clear" : contract.Weather.Description.Trim(),
                VisibilityNm = contract.Weather.VisibilityNm <= 0 ? 60 : contract.Weather.VisibilityNm,
                CloudCeilingFt = contract.Weather.CloudCeilingFt <= 0 ? 15000 : contract.Weather.CloudCeilingFt
            },
            Theater = new ScenarioTheaterContract
            {
                TheaterName = string.IsNullOrWhiteSpace(contract.Theater.TheaterName)
                    ? "Kovran Generated Theater"
                    : contract.Theater.TheaterName.Trim(),
                ObjectiveCount = contract.Objectives.Count,
                OpenFrequencyTraffic = contract.Theater.OpenFrequencyTraffic
            },
            Support = contract.Support.Select(NormalizeSupport).ToList(),
            RewardFactors = NormalizeRewardFactors(contract.RewardFactors)
        };

        if (normalized.Objectives.Count == 0)
        {
            normalized.Objectives.Add(new ScenarioObjectiveContract
            {
                Id = "primary-objective",
                Name = "Primary Sector Asset",
                Description = "Generated defended objective.",
                BearingDeg = 120,
                RangeNm = 48,
                Importance = "primary"
            });
        }

        for (int i = 0; i < normalized.Objectives.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(normalized.Objectives[i].Id))
                normalized.Objectives[i].Id = $"objective-{i + 1}";
        }

        foreach (var package in normalized.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.TargetObjectiveId) ||
                normalized.Objectives.All(objective => !objective.Id.Equals(package.TargetObjectiveId, StringComparison.OrdinalIgnoreCase)))
            {
                package.TargetObjectiveId = ResolveNearestObjectiveId(package, normalized.Objectives);
            }

            if (string.IsNullOrWhiteSpace(package.EntryLabel))
                package.EntryLabel = BuildEntryLabel(package.BearingDeg);
        }

        if (normalized.Support.Count == 0)
        {
            normalized.Support.Add(new ScenarioSupportContract { SupportType = "picture_relay", Availability = "ready" });
        }

        return normalized;
    }

    private static ScenarioObjectiveContract NormalizeObjective(ScenarioObjectiveContract objective) => new()
    {
        Id = Slugify(string.IsNullOrWhiteSpace(objective.Id) ? objective.Name : objective.Id),
        Name = string.IsNullOrWhiteSpace(objective.Name) ? "Unnamed Objective" : objective.Name.Trim(),
        Description = string.IsNullOrWhiteSpace(objective.Description) ? "Generated defended objective." : objective.Description.Trim(),
        BearingDeg = NormalizeBearing(objective.BearingDeg),
        RangeNm = objective.RangeNm <= 0 ? 48 : Math.Max(12, objective.RangeNm),
        Importance = string.IsNullOrWhiteSpace(objective.Importance) ? "primary" : objective.Importance.Trim().ToLowerInvariant()
    };

    private static ScenarioPackageContract NormalizePackage(ScenarioPackageContract package)
    {
        string normalizedRole = NormalizeRole(package.Role, package.DoctrineTags);
        var doctrineTags = NormalizeDoctrineTags(package.DoctrineTags, normalizedRole);

        return new ScenarioPackageContract
        {
            PackageName = string.IsNullOrWhiteSpace(package.PackageName) ? "UNNAMED PACKAGE" : package.PackageName.Trim(),
            Role = normalizedRole,
            Designation = string.IsNullOrWhiteSpace(package.Designation) ? "MiG-29" : package.Designation.Trim(),
            DoctrineTags = doctrineTags,
            AircraftCount = Math.Max(1, package.AircraftCount),
            TriggerMinutes = Math.Max(0, package.TriggerMinutes),
            EntryLabel = package.EntryLabel?.Trim() ?? string.Empty,
            BearingDeg = NormalizeBearing(package.BearingDeg),
            RangeNm = package.RangeNm <= 0 ? 120 : Math.Max(20, package.RangeNm),
            AltitudeFt = package.AltitudeFt <= 0 ? 18000 : Math.Max(300, package.AltitudeFt),
            HeadingDeg = NormalizeBearing(package.HeadingDeg),
            SpeedKts = package.SpeedKts <= 0 ? 420 : Math.Max(160, package.SpeedKts),
            Aggressiveness = Math.Clamp(package.Aggressiveness <= 0 ? 0.55 : package.Aggressiveness, 0.2, 0.95),
            TargetObjectiveId = Slugify(package.TargetObjectiveId),
            UsesTerrainMasking = package.UsesTerrainMasking || doctrineTags.Contains("terrain", StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ScenarioSupportContract NormalizeSupport(ScenarioSupportContract support) => new()
    {
        SupportType = NormalizeSupportType(support.SupportType),
        Availability = NormalizeAvailability(support.Availability)
    };

    private static List<string> NormalizeDoctrineTags(IEnumerable<string> doctrineTags, string role)
    {
        var tags = new HashSet<string>(doctrineTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant()));

        if (role.Contains("jam", StringComparison.OrdinalIgnoreCase))
            tags.Add("jammer");
        if (role.Contains("escort", StringComparison.OrdinalIgnoreCase) || role.Contains("fighter", StringComparison.OrdinalIgnoreCase))
            tags.Add("escort");
        if (role.Contains("decoy", StringComparison.OrdinalIgnoreCase) || role.Contains("feint", StringComparison.OrdinalIgnoreCase))
            tags.Add("decoy");
        if (role.Contains("terrain", StringComparison.OrdinalIgnoreCase) || role.Contains("low", StringComparison.OrdinalIgnoreCase))
            tags.Add("terrain");
        if (tags.Count == 0)
            tags.Add("strike");

        return tags.ToList();
    }

    private static List<string> NormalizeRewardFactors(IEnumerable<string> rewardFactors)
    {
        var normalized = rewardFactors
            .Where(factor => !string.IsNullOrWhiteSpace(factor))
            .Select(factor => factor.Trim().ToLowerInvariant().Replace(' ', '_'))
            .Select(factor => factor switch
            {
                "objective_defended" => "theater_survival",
                "enemy_package_neutralized" => "intercept_success",
                "low-altitude_missile_survival" => "theater_survival",
                "low_altitude_missile_survival" => "theater_survival",
                "discipline" => "command_discipline",
                _ => factor
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.AddRange(new[]
            {
                "theater_survival",
                "command_discipline",
                "intercept_success"
            });
        }

        return normalized;
    }

    private static string NormalizeRole(string role, IEnumerable<string> doctrineTags)
    {
        string value = string.IsNullOrWhiteSpace(role)
            ? string.Join(' ', doctrineTags)
            : role.Trim().ToLowerInvariant();

        if (value.Contains("jam", StringComparison.OrdinalIgnoreCase))
            return "jammer push";
        if (value.Contains("escort", StringComparison.OrdinalIgnoreCase) || value.Contains("fighter", StringComparison.OrdinalIgnoreCase))
            return "fighter screen";
        if (value.Contains("decoy", StringComparison.OrdinalIgnoreCase) || value.Contains("feint", StringComparison.OrdinalIgnoreCase))
            return "feint and strike";
        if (value.Contains("sead", StringComparison.OrdinalIgnoreCase))
            return "sead";
        if (value.Contains("terrain", StringComparison.OrdinalIgnoreCase) || value.Contains("low", StringComparison.OrdinalIgnoreCase))
            return "terrain-masked strike";

        return string.IsNullOrWhiteSpace(value) ? "strike" : value;
    }

    private static string NormalizeSupportType(string supportType)
    {
        string value = supportType?.Trim().ToLowerInvariant() ?? string.Empty;
        return value switch
        {
            "network_coverage" => "picture_relay",
            "network" => "picture_relay",
            "picture" => "picture_relay",
            "stocks" => "battery",
            "battery_support" => "battery",
            "declaration" => "declare",
            _ => string.IsNullOrWhiteSpace(value) ? "picture_relay" : value
        };
    }

    private static string NormalizeAvailability(string availability)
    {
        string value = availability?.Trim().ToLowerInvariant() ?? string.Empty;
        return value switch
        {
            "full" => "ready",
            "available" => "ready",
            "limited" => "conditional",
            _ => string.IsNullOrWhiteSpace(value) ? "ready" : value
        };
    }

    private static string ResolveNearestObjectiveId(ScenarioPackageContract package, IReadOnlyList<ScenarioObjectiveContract> objectives)
    {
        return objectives
            .OrderBy(objective =>
                Math.Abs(NormalizeBearing(package.BearingDeg) - NormalizeBearing(objective.BearingDeg)) +
                Math.Abs(package.RangeNm - objective.RangeNm) * 0.5)
            .First()
            .Id;
    }

    private static string BuildEntryLabel(double bearingDeg) => NormalizeBearing(bearingDeg) switch
    {
        >= 315 or < 45 => "NORTHERN CORRIDOR",
        >= 45 and < 135 => "EASTERN CORRIDOR",
        >= 135 and < 225 => "SOUTHERN CORRIDOR",
        _ => "WESTERN CORRIDOR"
    };

    private static double NormalizeBearing(double bearingDeg)
    {
        double normalized = bearingDeg % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
