namespace DEADSKY.Core.Scenario;

public sealed class ScenarioContract
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double DurationMinutes { get; set; } = 18;
    public List<ScenarioPackageContract> Packages { get; set; } = new();
    public List<ScenarioObjectiveContract> Objectives { get; set; } = new();
    public ScenarioWeatherContract Weather { get; set; } = new();
    public ScenarioTheaterContract Theater { get; set; } = new();
    public List<ScenarioSupportContract> Support { get; set; } = new();
    public List<string> RewardFactors { get; set; } = new();
}

public sealed class ScenarioPackageContract
{
    public string PackageName { get; set; } = "";
    public string Role { get; set; } = "";
    public string Designation { get; set; } = "MiG-29";
    public List<string> DoctrineTags { get; set; } = new();
    public int AircraftCount { get; set; }
    public double TriggerMinutes { get; set; } = 0.5;
    public string EntryLabel { get; set; } = "";
    public double BearingDeg { get; set; } = 45;
    public double RangeNm { get; set; } = 120;
    public double AltitudeFt { get; set; } = 18000;
    public double HeadingDeg { get; set; } = 225;
    public double SpeedKts { get; set; } = 440;
    public double Aggressiveness { get; set; } = 0.65;
    public string TargetObjectiveId { get; set; } = "";
    public bool UsesTerrainMasking { get; set; }
}

public sealed class ScenarioObjectiveContract
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double BearingDeg { get; set; }
    public double RangeNm { get; set; }
    public string Importance { get; set; } = "primary";
}

public sealed class ScenarioWeatherContract
{
    public string Description { get; set; } = "Clear";
    public double VisibilityNm { get; set; } = 80;
    public double CloudCeilingFt { get; set; } = 25000;
}

public sealed class ScenarioTheaterContract
{
    public string TheaterName { get; set; } = "";
    public int ObjectiveCount { get; set; }
    public bool OpenFrequencyTraffic { get; set; }
}

public sealed class ScenarioSupportContract
{
    public string SupportType { get; set; } = "";
    public string Availability { get; set; } = "ready";
}

public sealed record ScenarioContractValidation(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public string Summary =>
        IsValid
            ? Warnings.Count == 0
                ? "REALISTIC CONTRACT: VALIDATED"
                : $"REALISTIC CONTRACT: VALID WITH {Warnings.Count} WARNING(S)"
            : $"REALISTIC CONTRACT: INVALID ({Errors.Count} ERRORS)";
}

public static class ScenarioContractValidator
{
    private static readonly HashSet<string> SupportedLandmarkCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "ridge",
        "river",
        "terrain",
        "approach",
        "urban",
        "coast",
        "forest",
        "pass"
    };

    public static ScenarioContract FromScenario(ScenarioDefinition scenario) => new()
    {
        Name = scenario.Name,
        Description = scenario.Description,
        DurationMinutes = scenario.DurationMinutes,
        Packages = scenario.EnemyForces.Waves.Select(wave => new ScenarioPackageContract
        {
            PackageName = string.IsNullOrWhiteSpace(wave.PackageName) ? "UNNAMED" : wave.PackageName,
            Role = string.IsNullOrWhiteSpace(wave.PackageRole) ? "strike" : wave.PackageRole,
            Designation = wave.Aircraft.FirstOrDefault()?.Type ?? "MiG-29",
            DoctrineTags = BuildDoctrineTags(wave),
            AircraftCount = Math.Max(1, wave.Aircraft.Sum(aircraft => Math.Max(1, aircraft.Count))),
            TriggerMinutes = wave.TimeMinutes,
            EntryLabel = wave.EntryLabel,
            BearingDeg = wave.Aircraft.FirstOrDefault()?.SpawnBearingDeg ?? 45,
            RangeNm = wave.Aircraft.FirstOrDefault()?.SpawnRangeNm ?? 120,
            AltitudeFt = wave.Aircraft.FirstOrDefault()?.SpawnAltitudeFt ?? 18000,
            HeadingDeg = wave.Aircraft.FirstOrDefault()?.SpawnHeadingDeg ?? 225,
            SpeedKts = wave.Aircraft.FirstOrDefault()?.SpawnSpeedKts ?? 440,
            Aggressiveness = wave.Aircraft.FirstOrDefault()?.Aggressiveness ?? 0.65,
            TargetObjectiveId = wave.TargetObjectiveId,
            UsesTerrainMasking = wave.PackageRole.Contains("low", StringComparison.OrdinalIgnoreCase) ||
                                 wave.PackageRole.Contains("terrain", StringComparison.OrdinalIgnoreCase)
        }).ToList(),
        Objectives = scenario.SectorMap.Objectives.Select(objective => new ScenarioObjectiveContract
        {
            Id = objective.Id,
            Name = objective.Name,
            Description = objective.Description,
            BearingDeg = objective.BearingDeg,
            RangeNm = objective.RangeNm,
            Importance = objective.Importance
        }).ToList(),
        Weather = new ScenarioWeatherContract
        {
            Description = scenario.Weather.Description,
            VisibilityNm = scenario.Weather.VisibilityNm,
            CloudCeilingFt = scenario.Weather.CloudCeilingFt
        },
        Theater = new ScenarioTheaterContract
        {
            TheaterName = scenario.SectorMap.TheaterName,
            ObjectiveCount = scenario.SectorMap.Objectives.Count,
            OpenFrequencyTraffic = true
        },
        Support = new List<ScenarioSupportContract>
        {
            new() { SupportType = "picture_relay", Availability = "ready" },
            new() { SupportType = "cap", Availability = "conditional" },
            new() { SupportType = "awacs", Availability = "ready" }
        },
        RewardFactors = new List<string>
        {
            "theater_survival",
            "command_discipline",
            "intercept_success",
            "logistics_preservation",
            "crew_condition"
        }
    };

    public static ScenarioContractValidation Validate(ScenarioContract contract)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(contract.Name))
            errors.Add("Scenario name is required.");

        if (contract.Packages.Count == 0)
            errors.Add("At least one hostile package is required.");

        if (contract.Objectives.Count == 0)
            errors.Add("At least one defended objective is required.");

        foreach (var package in contract.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageName))
                errors.Add("Each package must have a package name.");
            if (package.AircraftCount <= 0)
                errors.Add($"Package {package.PackageName} must contain aircraft.");
            if (package.TriggerMinutes < 0)
                errors.Add($"Package {package.PackageName} has an invalid trigger time.");
            if (package.RangeNm < 20)
                warnings.Add($"Package {package.PackageName} starts very close to the battery.");
            if (package.TargetObjectiveId.Length > 0 &&
                contract.Objectives.All(objective => !objective.Id.Equals(package.TargetObjectiveId, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Package {package.PackageName} targets unknown objective {package.TargetObjectiveId}.");
            }
            if (package.Role.Contains("jamm", StringComparison.OrdinalIgnoreCase) &&
                !package.DoctrineTags.Any(tag => tag.Equals("jammer", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Package {package.PackageName} looks like a jammer but is missing a jammer tag.");
            }
        }

        if (contract.Theater.ObjectiveCount == 0)
            warnings.Add("Scenario has no objectives; realistic rewards will be shallow.");

        if (contract.Support.Count == 0)
            warnings.Add("No friendly support actors declared.");

        return new ScenarioContractValidation(errors.Count == 0, errors, warnings);
    }

    public static ScenarioContractValidation ValidateScenario(ScenarioDefinition scenario)
    {
        var contractValidation = Validate(FromScenario(scenario));
        var errors = contractValidation.Errors.ToList();
        var warnings = contractValidation.Warnings.ToList();

        if (string.IsNullOrWhiteSpace(scenario.SectorMap.TheaterName))
            errors.Add("Scenario theater name is required for tactical map rendering.");

        foreach (var objective in scenario.SectorMap.Objectives)
        {
            if (string.IsNullOrWhiteSpace(objective.Id) || string.IsNullOrWhiteSpace(objective.Name))
                errors.Add("Every tactical objective needs an id and display name.");

            if (!objective.Importance.Equals("primary", StringComparison.OrdinalIgnoreCase) &&
                !objective.Importance.Equals("secondary", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Objective {objective.Name} uses unsupported importance '{objective.Importance}'.");
            }
        }

        foreach (var landmark in scenario.SectorMap.Landmarks)
        {
            if (!SupportedLandmarkCategories.Contains(landmark.Category))
            {
                errors.Add($"Landmark {landmark.Name} uses unsupported category '{landmark.Category}'.");
            }
        }

        if (scenario.SectorMap.Landmarks.Count == 0)
            warnings.Add("Scenario has no landmark notes; tactical map will rely only on objectives and tracks.");

        return new ScenarioContractValidation(errors.Count == 0, errors, warnings);
    }

    private static List<string> BuildDoctrineTags(WaveConfig wave)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string role = wave.PackageRole;
        if (role.Contains("fighter", StringComparison.OrdinalIgnoreCase)) tags.Add("escort");
        if (role.Contains("jam", StringComparison.OrdinalIgnoreCase)) tags.Add("jammer");
        if (role.Contains("feint", StringComparison.OrdinalIgnoreCase) || role.Contains("decoy", StringComparison.OrdinalIgnoreCase)) tags.Add("decoy");
        if (role.Contains("sead", StringComparison.OrdinalIgnoreCase)) tags.Add("sead");
        if (role.Contains("low", StringComparison.OrdinalIgnoreCase) || role.Contains("terrain", StringComparison.OrdinalIgnoreCase)) tags.Add("terrain");
        if (tags.Count == 0) tags.Add("strike");
        return tags.ToList();
    }
}
