using DEADSKY.Core.Scenario;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    private static readonly IReadOnlyDictionary<string, Func<ScenarioDefinition>> ScriptedScenarioFactories =
        new Dictionary<string, Func<ScenarioDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["tutorial"] = BuildTutorialScenarioCanonical,
            ["single_strike"] = BuildSingleStrikeScenarioCanonical,
            ["escort_strike"] = BuildEscortStrikeScenarioCanonical,
            ["decoy_raid"] = BuildDecoyRaidScenarioCanonical,
            ["jammer_wave"] = BuildJammerPressureScenarioCanonical,
            ["operation"] = BuildOperationDeadskyCanonical,
            ["deadsky"] = BuildOperationDeadskyCanonical,
            ["multi_axis_raid"] = BuildOperationDeadskyCanonical
        };

    private static readonly string[] StartupScenarioRotation =
    [
        "single_strike",
        "escort_strike",
        "decoy_raid",
        "jammer_wave",
        "operation"
    ];

    public string CurrentScenarioArchetypeText => CurrentScenarioDefinition == null
        ? "ARCHETYPE: STANDBY"
        : $"ARCHETYPE: {CurrentScenarioDefinition.Archetype.Replace('_', ' ').ToUpperInvariant()}";

    public string CurrentScenarioSaveTruthText => "SAVE SCOPE: PROFILE/CAMPAIGN/LOGISTICS/THEATER ONLY. LIVE SORTIE RESUME: NOT AVAILABLE.";

    private static IReadOnlyList<ScenarioDefinition> BuildScriptedScenarioLibrary() =>
        StartupScenarioRotation
            .Select(key => ResolveScenarioDefinition(key))
            .ToList();

    private static ScenarioDefinition ResolveScenarioDefinition(string? scenarioKey)
    {
        string normalized = string.IsNullOrWhiteSpace(scenarioKey)
            ? "tutorial"
            : scenarioKey.Trim().ToLowerInvariant();

        if (ScriptedScenarioFactories.TryGetValue(normalized, out var factory))
            return factory();

        return BuildTutorialScenarioCanonical();
    }

    private string ChooseStartupScenarioKey()
    {
        string? lastScenarioKey = _loadedCampaignState?.LastScenarioKey;
        string? lastArchetype = _loadedCampaignState?.LastScenarioArchetype;

        var candidates = BuildScriptedScenarioLibrary()
            .Where(scenario => !scenario.IsTutorial)
            .ToList();

        if (candidates.Count == 0)
            return "operation";

        int seed = DateTime.UtcNow.Date.DayOfYear + Profile.MissionsCompleted;
        int index = Math.Abs(seed) % candidates.Count;

        if (!string.IsNullOrWhiteSpace(lastScenarioKey) &&
            string.Equals(candidates[index].Key, lastScenarioKey, StringComparison.OrdinalIgnoreCase))
        {
            index = (index + 1) % candidates.Count;
        }
        else if (!string.IsNullOrWhiteSpace(lastArchetype) &&
                 string.Equals(candidates[index].Archetype, lastArchetype, StringComparison.OrdinalIgnoreCase))
        {
            index = (index + 1) % candidates.Count;
        }

        return candidates[index].Key;
    }

    internal void LoadStartupScenario()
    {
        string scenarioKey = ChooseStartupScenarioKey();
        LoadScenario(scenarioKey);
    }

    private static ScenarioDefinition BuildTutorialScenarioCanonical() => new()
    {
        Key = "tutorial",
        Name = "Tutorial - Single Bogey",
        Archetype = "training_single_bogey",
        Category = "training",
        IsTutorial = true,
        Description = "A single enemy aircraft is approaching. Track, designate, and engage.",
        DurationMinutes = 10,
        PlayerBattery = BuildBatteryConfig(launchers: 4, reserveMissiles: 12, radarRangeNm: 120, engagementRangeNm: 18, missilePk: 0.70),
        Command = new CommandConfig
        {
            Callsign = "ECHO",
            InitialROE = "weapons_tight",
            InitialAlert = "yellow",
            OpeningMessage = "ALPHA, ECHO. Single BOGEY detected bearing 045, range 095. Track and classify. WEAPONS TIGHT - do not engage until cleared. Acknowledge."
        },
        SectorMap = BuildSectorMap(
            ("airfield", "Kovran Airfield", "Friendly runway and fuel park.", 132, 62, "primary"),
            landmarks:
            [
                ("Sable Ridge", 350, 56, "ridge"),
                ("Vanta River", 48, 42, "river"),
                ("Ashen Farms", 220, 70, "terrain")
            ]),
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Strike friendly airfield",
            Waves =
            [
                CreateWave(
                    "RAVEN-1",
                    "single striker",
                    "NORTHEAST CORRIDOR",
                    "airfield",
                    0.25,
                    CreateAircraftSpawn("SU-24", 1, 45, 95, 18000, 225, 450, "ingress_attack", 0.5))
            ]
        },
        VictoryConditions = BuildVictory("Destroy the enemy aircraft", "Enemy aircraft reaches target", 0)
    };

    private static ScenarioDefinition BuildSingleStrikeScenarioCanonical() => new()
    {
        Key = "single_strike",
        Name = "Operation Iron Lantern",
        Archetype = "single_strike",
        Category = "operation",
        Description = "A clean single-package strike probes the sector and tests your timing.",
        DurationMinutes = 14,
        PlayerBattery = BuildBatteryConfig(launchers: 4, reserveMissiles: 14, radarRangeNm: 120, engagementRangeNm: 20, missilePk: 0.7),
        Command = BuildCommand("ALPHA, ECHO. SINGLE STRIKE PACKAGE EXPECTED FROM THE EAST. HOLD WEAPONS TIGHT UNTIL HOSTILE ACT OR DECLARE."),
        SectorMap = BuildSectorMap(
            ("depot", "Kovran Depot", "Fuel and munitions storage complex.", 128, 58, "primary"),
            landmarks:
            [
                ("Sable Ridge", 350, 56, "ridge"),
                ("Needle Pass", 92, 84, "terrain"),
                ("Ashen Farms", 220, 70, "terrain")
            ]),
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Single hostile strike on depot complex",
            Waves =
            [
                CreateWave(
                    "LANCER-1",
                    "single strike",
                    "EASTERN CORRIDOR",
                    "depot",
                    0.55,
                    CreateAircraftSpawn("SU-24", 2, 78, 118, 18500, 242, 435, "ingress_attack", 0.68))
            ]
        },
        VictoryConditions = BuildVictory("Break up the strike package before weapon release.", "Strike package reaches the depot complex.", 0)
    };

    private static ScenarioDefinition BuildEscortStrikeScenarioCanonical() => new()
    {
        Key = "escort_strike",
        Name = "Operation Cold Relay",
        Archetype = "escort_plus_striker",
        Category = "operation",
        Description = "An escorted strike package uses fighters to screen the main attack run.",
        DurationMinutes = 18,
        PlayerBattery = BuildBatteryConfig(launchers: 4, reserveMissiles: 16, radarRangeNm: 120, engagementRangeNm: 20, missilePk: 0.68),
        Command = BuildCommand("ALPHA, ECHO. ESCORT PLUS STRIKER PACKAGE EXPECTED. FIGHTER SCREEN MAY TRY TO PULL YOUR ATTENTION HIGH."),
        SectorMap = BuildSectorMap(
            ("relay", "Dunewatch Relay", "Regional command relay station.", 78, 88, "primary"),
            landmarks:
            [
                ("Sable Ridge", 350, 56, "ridge"),
                ("Vanta River", 48, 42, "river"),
                ("Needle Pass", 92, 84, "terrain")
            ]),
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Escort a strike package into the relay site",
            Waves =
            [
                CreateWave(
                    "VIPER-1",
                    "fighter escort",
                    "NORTH CAP",
                    "relay",
                    0.35,
                    CreateAircraftSpawn("MiG-29", 2, 26, 124, 23000, 214, 485, "ingress_attack", 0.66)),
                CreateWave(
                    "HAMMER-2",
                    "strike package",
                    "RIVER CORRIDOR",
                    "relay",
                    1.25,
                    CreateAircraftSpawn("SU-24", 2, 62, 116, 17500, 236, 440, "ingress_attack", 0.72))
            ]
        },
        VictoryConditions = BuildVictory("Stop the escort and striker package before the relay is hit.", "The escorted strike package reaches the relay station.", 1)
    };

    private static ScenarioDefinition BuildDecoyRaidScenarioCanonical() => new()
    {
        Key = "decoy_raid",
        Name = "Operation Glass Spear",
        Archetype = "decoy_plus_main_raid",
        Category = "operation",
        Description = "Decoys pull your radar picture wide while the real raid follows behind.",
        DurationMinutes = 20,
        PlayerBattery = BuildBatteryConfig(launchers: 4, reserveMissiles: 16, radarRangeNm: 120, engagementRangeNm: 20, missilePk: 0.68),
        Command = BuildCommand("ALPHA, ECHO. EXPECT A FEINT. DO NOT CHASE THE FIRST NOISE PACKAGE BLIND."),
        SectorMap = BuildSectorMap(
            ("depot", "Kovran Depot", "Fuel and munitions storage complex.", 128, 58, "primary"),
            ("airfield", "Kovran Airfield", "Friendly runway and fuel park.", 132, 62, "secondary"),
            landmarks:
            [
                ("Vanta River", 48, 42, "river"),
                ("Ashen Farms", 220, 70, "terrain"),
                ("Needle Pass", 92, 84, "terrain")
            ]),
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Use decoys to open a lane for the main raid",
            Waves =
            [
                CreateWave(
                    "SHADE-1",
                    "decoy screen",
                    "RIVER CORRIDOR",
                    "airfield",
                    0.4,
                    CreateAircraftSpawn("MQ-9", 2, 72, 120, 14000, 238, 250, "feint", 0.32)),
                CreateWave(
                    "RAVEN-2",
                    "main raid",
                    "EASTERN CORRIDOR",
                    "depot",
                    1.8,
                    CreateAircraftSpawn("SU-24", 2, 96, 122, 19500, 248, 440, "ingress_attack", 0.75),
                    CreateAircraftSpawn("MiG-29", 1, 88, 126, 21000, 246, 495, "ingress_attack", 0.7))
            ]
        },
        VictoryConditions = BuildVictory("Ignore the feint and break up the main raid.", "The main raid penetrates to the defended assets.", 1)
    };

    private static ScenarioDefinition BuildJammerPressureScenarioCanonical() => new()
    {
        Key = "jammer_wave",
        Name = "Operation Sable Static",
        Archetype = "jammer_pressure_wave",
        Category = "operation",
        Description = "A jammer pressures the scope while strikers try to ride the degraded picture.",
        DurationMinutes = 19,
        PlayerBattery = BuildBatteryConfig(launchers: 4, reserveMissiles: 16, radarRangeNm: 120, engagementRangeNm: 20, missilePk: 0.67),
        Command = BuildCommand("ALPHA, ECHO. JAMMER PRESSURE WAVE EXPECTED. MAINTAIN TRACK DISCIPLINE THROUGH DEGRADED PICTURE."),
        SectorMap = BuildSectorMap(
            ("relay", "Dunewatch Relay", "Regional command relay station.", 78, 88, "primary"),
            ("depot", "Kovran Depot", "Fuel and munitions storage complex.", 128, 58, "secondary"),
            landmarks:
            [
                ("Sable Ridge", 350, 56, "ridge"),
                ("Needle Pass", 92, 84, "terrain"),
                ("Outer Approach", 84, 70, "approach")
            ]),
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Pressure command relay under ECM cover",
            Waves =
            [
                CreateWave(
                    "SPECTER-3",
                    "jammer push",
                    "EASTERN LOW ROUTE",
                    "relay",
                    0.55,
                    CreateAircraftSpawn("EA-6", 1, 102, 132, 14000, 250, 410, "ecm_standoff", 0.52)),
                CreateWave(
                    "LANCER-4",
                    "strike package",
                    "EASTERN CORRIDOR",
                    "relay",
                    1.4,
                    CreateAircraftSpawn("SU-24", 2, 94, 118, 16000, 244, 432, "ingress_attack", 0.73))
            ]
        },
        VictoryConditions = BuildVictory("Hold the relay under jammer pressure.", "The ECM-backed strike reaches the relay station.", 1)
    };

    private static ScenarioDefinition BuildOperationDeadskyCanonical() => new()
    {
        Key = "operation",
        Name = "Operation DEADSKY",
        Archetype = "multi_axis_timed_raid",
        Category = "operation",
        Description = "Layered hostile strike with escorts, decoys, and a late jammer push.",
        DurationMinutes = 24,
        PlayerBattery = BuildBatteryConfig(launchers: 4, reserveMissiles: 16, radarRangeNm: 120, engagementRangeNm: 20, missilePk: 0.68),
        Command = BuildCommand("ALPHA, ECHO. EXPECT MULTI-AXIS STRIKE PACKAGE. INITIAL ROE WEAPONS TIGHT. HOLD FIRE UNTIL DECLARE OR HOSTILE ACT CONFIRMED."),
        SectorMap = BuildSectorMap(
            ("depot", "Kovran Depot", "Fuel and munitions storage complex.", 128, 58, "primary"),
            ("relay", "Dunewatch Relay", "Regional command relay station.", 78, 88, "secondary"),
            landmarks:
            [
                ("Sable Ridge", 350, 56, "ridge"),
                ("Vanta River", 48, 42, "river"),
                ("Ashen Farms", 220, 70, "terrain"),
                ("Needle Pass", 92, 84, "terrain")
            ]),
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Penetrate sector air defenses and strike depot complex",
            Waves =
            [
                CreateWave(
                    "LANCER-1",
                    "fighter screen",
                    "NORTH CAP",
                    "depot",
                    0.4,
                    CreateAircraftSpawn("MiG-29", 2, 30, 125, 22000, 215, 480, "ingress_attack", 0.65)),
                CreateWave(
                    "SHADE-2",
                    "feint and strike",
                    "RIVER CORRIDOR",
                    "depot",
                    2.1,
                    CreateAircraftSpawn("MQ-9", 2, 75, 118, 14000, 240, 260, "feint", 0.35),
                    CreateAircraftSpawn("SU-24", 2, 58, 122, 19000, 235, 440, "ingress_attack", 0.72)),
                CreateWave(
                    "SPECTER-3",
                    "jammer push",
                    "EASTERN LOW ROUTE",
                    "relay",
                    5.8,
                    CreateAircraftSpawn("EA-6", 1, 110, 135, 9000, 250, 430, "ecm_standoff", 0.55),
                    CreateAircraftSpawn("MiG-29", 2, 102, 128, 17000, 248, 500, "terrain_following", 0.76))
            ]
        },
        VictoryConditions = BuildVictory("Break up the raid and keep the depot intact.", "Strike package breaks through to the depot complex.", 1)
    };

    private static PlayerBatteryConfig BuildBatteryConfig(int launchers, int reserveMissiles, double radarRangeNm, double engagementRangeNm, double missilePk) => new()
    {
        Callsign = "ALPHA",
        Type = "SA-11 BUK",
        Launchers = launchers,
        ReserveMissiles = reserveMissiles,
        RadarRangeNm = radarRangeNm,
        EngagementRangeNm = engagementRangeNm,
        MissileType = "9M38",
        MissilePk = missilePk,
        InitialROE = "weapons_tight",
        InitialAlert = "yellow"
    };

    private static CommandConfig BuildCommand(string openingMessage) => new()
    {
        Callsign = "ECHO",
        InitialROE = "weapons_tight",
        InitialAlert = "yellow",
        OpeningMessage = openingMessage
    };

    private static SectorMapConfig BuildSectorMap(
        (string Id, string Name, string Description, double BearingDeg, double RangeNm, string Importance) primaryObjective,
        (string Id, string Name, string Description, double BearingDeg, double RangeNm, string Importance)? secondaryObjective = null,
        params (string Name, double BearingDeg, double RangeNm, string Category)[] landmarks)
    {
        var objectives = new List<MapObjectiveConfig>
        {
            new()
            {
                Id = primaryObjective.Id,
                Name = primaryObjective.Name,
                Description = primaryObjective.Description,
                BearingDeg = primaryObjective.BearingDeg,
                RangeNm = primaryObjective.RangeNm,
                Importance = primaryObjective.Importance
            }
        };

        if (secondaryObjective.HasValue)
        {
            var second = secondaryObjective.Value;
            objectives.Add(new MapObjectiveConfig
            {
                Id = second.Id,
                Name = second.Name,
                Description = second.Description,
                BearingDeg = second.BearingDeg,
                RangeNm = second.RangeNm,
                Importance = second.Importance
            });
        }

        return new SectorMapConfig
        {
            TheaterName = "Kovran Lowlands",
            Objectives = objectives,
            Landmarks = landmarks.Select(landmark => new MapLandmarkConfig
            {
                Name = landmark.Name,
                BearingDeg = landmark.BearingDeg,
                RangeNm = landmark.RangeNm,
                Category = landmark.Category
            }).ToList()
        };
    }

    private static WaveConfig CreateWave(
        string packageName,
        string packageRole,
        string entryLabel,
        string targetObjectiveId,
        double timeMinutes,
        params AircraftSpawnConfig[] aircraft) => new()
    {
        Trigger = "time",
        TimeMinutes = timeMinutes,
        PackageName = packageName,
        PackageRole = packageRole,
        EntryLabel = entryLabel,
        TargetObjectiveId = targetObjectiveId,
        Aircraft = aircraft.ToList()
    };

    private static AircraftSpawnConfig CreateAircraftSpawn(
        string type,
        int count,
        double spawnBearingDeg,
        double spawnRangeNm,
        double spawnAltitudeFt,
        double spawnHeadingDeg,
        double spawnSpeedKts,
        string initialBehavior,
        double aggressiveness) => new()
    {
        Type = type,
        Count = count,
        SpawnBearingDeg = spawnBearingDeg,
        SpawnRangeNm = spawnRangeNm,
        SpawnAltitudeFt = spawnAltitudeFt,
        SpawnHeadingDeg = spawnHeadingDeg,
        SpawnSpeedKts = spawnSpeedKts,
        InitialBehavior = initialBehavior,
        Aggressiveness = aggressiveness
    };

    private static VictoryConditionsConfig BuildVictory(string win, string lose, int maxEnemyBreakthroughs) => new()
    {
        Win = win,
        Lose = lose,
        MaxEnemyBreakthroughs = maxEnemyBreakthroughs
    };
}
