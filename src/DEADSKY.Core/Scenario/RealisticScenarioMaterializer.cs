namespace DEADSKY.Core.Scenario;

public static class RealisticScenarioMaterializer
{
    public static ScenarioDefinition Materialize(ScenarioContract contract)
    {
        var validation = ScenarioContractValidator.Validate(contract);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Summary);

        var objectives = contract.Objectives.Select(objective => new MapObjectiveConfig
        {
            Id = objective.Id,
            Name = objective.Name,
            Description = objective.Description,
            BearingDeg = objective.BearingDeg,
            RangeNm = objective.RangeNm,
            Importance = objective.Importance
        }).ToList();

        return new ScenarioDefinition
        {
            Name = contract.Name,
            Description = string.IsNullOrWhiteSpace(contract.Description)
                ? "AI-generated realistic mode scenario."
                : contract.Description,
            DurationMinutes = Math.Max(10, contract.DurationMinutes),
            IsRealisticMode = true,
            PlayerBattery = BuildBatteryConfig(contract),
            Command = new CommandConfig
            {
                Callsign = "ECHO",
                InitialAlert = "yellow",
                InitialROE = "weapons_tight",
                OpeningMessage = BuildOpeningMessage(contract)
            },
            SectorMap = new SectorMapConfig
            {
                TheaterName = string.IsNullOrWhiteSpace(contract.Theater.TheaterName)
                    ? "Kovran Generated Theater"
                    : contract.Theater.TheaterName,
                Objectives = objectives,
                Landmarks = BuildLandmarks(objectives)
            },
            EnemyForces = new EnemyForcesConfig
            {
                Objective = BuildEnemyObjectiveLine(contract),
                Waves = contract.Packages
                    .OrderBy(package => package.TriggerMinutes)
                    .Select(BuildWave)
                    .ToList()
            },
            VictoryConditions = new VictoryConditionsConfig
            {
                Win = "Hold defended assets and break up the raid.",
                Lose = "Enemy raid breaches defended objectives.",
                MaxEnemyBreakthroughs = 1
            },
            Weather = new WeatherConfig
            {
                VisibilityNm = contract.Weather.VisibilityNm,
                CloudCeilingFt = contract.Weather.CloudCeilingFt,
                PrecipitationMmHr = 0,
                Description = contract.Weather.Description,
                WindSpeedKts = 8,
                WindDirectionDeg = 180
            }
        };
    }

    private static PlayerBatteryConfig BuildBatteryConfig(ScenarioContract contract)
    {
        int hostileCount = contract.Packages.Sum(package => Math.Max(1, package.AircraftCount));
        return new PlayerBatteryConfig
        {
            Callsign = "ALPHA",
            Type = "SA-11 BUK",
            Launchers = hostileCount >= 8 ? 4 : 3,
            ReserveMissiles = hostileCount >= 8 ? 18 : 14,
            RadarRangeNm = 120,
            EngagementRangeNm = 20,
            MissileType = "9M38",
            MissilePk = hostileCount >= 8 ? 0.66 : 0.7,
            InitialROE = "weapons_tight",
            InitialAlert = "yellow"
        };
    }

    private static WaveConfig BuildWave(ScenarioPackageContract package)
    {
        string role = NormalizeRole(package);
        return new WaveConfig
        {
            Trigger = "time",
            TimeMinutes = package.TriggerMinutes,
            PackageName = package.PackageName,
            PackageRole = role,
            EntryLabel = string.IsNullOrWhiteSpace(package.EntryLabel)
                ? BuildEntryLabel(package.BearingDeg)
                : package.EntryLabel,
            TargetObjectiveId = package.TargetObjectiveId,
            Aircraft = new List<AircraftSpawnConfig>
            {
                new()
                {
                    Type = package.Designation,
                    Count = Math.Max(1, package.AircraftCount),
                    SpawnBearingDeg = package.BearingDeg,
                    SpawnRangeNm = package.RangeNm,
                    SpawnAltitudeFt = package.UsesTerrainMasking ? Math.Min(package.AltitudeFt, 1200) : package.AltitudeFt,
                    SpawnHeadingDeg = package.HeadingDeg,
                    SpawnSpeedKts = package.SpeedKts,
                    InitialBehavior = SelectInitialBehavior(package),
                    Aggressiveness = Math.Clamp(package.Aggressiveness, 0.2, 0.95)
                }
            }
        };
    }

    private static string BuildOpeningMessage(ScenarioContract contract)
    {
        string objective = contract.Objectives.FirstOrDefault()?.Name?.ToUpperInvariant() ?? "PRIMARY ASSETS";
        return $"ALPHA, ECHO. REALISTIC MODE SHIFT ACTIVE. EXPECT {contract.Packages.Count} RAID PACKAGE(S) AIMED AT {objective}. INITIAL ROE WEAPONS TIGHT. USE SUPPORT NETS AND MAINTAIN TRACK DISCIPLINE.";
    }

    private static string BuildEnemyObjectiveLine(ScenarioContract contract)
    {
        string objectiveNames = string.Join(", ", contract.Objectives.Select(objective => objective.Name));
        return $"Penetrate sector air defenses and pressure {objectiveNames}";
    }

    private static string NormalizeRole(ScenarioPackageContract package)
    {
        if (!string.IsNullOrWhiteSpace(package.Role))
            return package.Role;
        if (package.DoctrineTags.Any(tag => tag.Equals("jammer", StringComparison.OrdinalIgnoreCase)))
            return "jammer push";
        if (package.DoctrineTags.Any(tag => tag.Equals("decoy", StringComparison.OrdinalIgnoreCase)))
            return "feint and strike";
        if (package.DoctrineTags.Any(tag => tag.Equals("escort", StringComparison.OrdinalIgnoreCase)))
            return "fighter screen";
        return "strike";
    }

    private static string SelectInitialBehavior(ScenarioPackageContract package)
    {
        if (package.DoctrineTags.Any(tag => tag.Equals("jammer", StringComparison.OrdinalIgnoreCase)))
            return "ecm_standoff";
        if (package.DoctrineTags.Any(tag => tag.Equals("decoy", StringComparison.OrdinalIgnoreCase)))
            return "feint";
        if (package.DoctrineTags.Any(tag => tag.Equals("terrain", StringComparison.OrdinalIgnoreCase)) || package.UsesTerrainMasking)
            return "terrain_following";
        if (package.DoctrineTags.Any(tag => tag.Equals("sead", StringComparison.OrdinalIgnoreCase)))
            return "sead";
        return "ingress_attack";
    }

    private static string BuildEntryLabel(double bearingDeg)
    {
        return bearingDeg switch
        {
            >= 315 or < 45 => "NORTHERN CORRIDOR",
            >= 45 and < 135 => "EASTERN CORRIDOR",
            >= 135 and < 225 => "SOUTHERN CORRIDOR",
            _ => "WESTERN CORRIDOR"
        };
    }

    private static List<MapLandmarkConfig> BuildLandmarks(IReadOnlyList<MapObjectiveConfig> objectives)
    {
        var landmarks = new List<MapLandmarkConfig>
        {
            new() { Name = "SABLE RIDGE", BearingDeg = 352, RangeNm = 48, Category = "ridge" },
            new() { Name = "VANTA RIVER", BearingDeg = 58, RangeNm = 36, Category = "river" },
            new() { Name = "ASHEN FARMS", BearingDeg = 210, RangeNm = 63, Category = "terrain" }
        };

        if (objectives.Count > 0)
        {
            landmarks.Add(new MapLandmarkConfig
            {
                Name = $"{objectives[0].Name.ToUpperInvariant()} OUTER APPROACH",
                BearingDeg = objectives[0].BearingDeg,
                RangeNm = Math.Max(12, objectives[0].RangeNm - 12),
                Category = "approach"
            });
        }

        return landmarks;
    }
}
