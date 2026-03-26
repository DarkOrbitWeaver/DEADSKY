using System.Text.Json;
using DEADSKY.AI.Client;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Scenario;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Backend.Tests;

internal static class SimulationTestFactory
{
    public static ScenarioDefinition CreateSingleBogeyScenario() => new()
    {
        Name = "Single Bogey Test",
        Description = "Minimal hostile ingress for regression tests.",
        DurationMinutes = 10,
        PlayerBattery = new PlayerBatteryConfig
        {
            Callsign = "ALPHA",
            Launchers = 4,
            ReserveMissiles = 12,
            RadarRangeNm = 80,
            EngagementRangeNm = 20,
            MissileType = "9M38",
            MissilePk = 0.75,
            InitialROE = "weapons_tight",
            InitialAlert = "yellow"
        },
        Command = new CommandConfig
        {
            Callsign = "ECHO",
            OpeningMessage = "ALPHA, ECHO. TEST CONTROL. SINGLE BOGEY INBOUND."
        },
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Strike test target",
            Waves = new List<WaveConfig>
            {
                new()
                {
                    Trigger = "time",
                    TimeMinutes = 0.1,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new()
                        {
                            Type = "SU-24",
                            Count = 1,
                            SpawnBearingDeg = 45,
                            SpawnRangeNm = 60,
                            SpawnAltitudeFt = 18000,
                            SpawnHeadingDeg = 225,
                            SpawnSpeedKts = 420,
                            InitialBehavior = "ingress_attack",
                            Aggressiveness = 0.6
                        }
                    }
                }
            }
        },
        VictoryConditions = new VictoryConditionsConfig
        {
            Win = "Destroy the hostile aircraft.",
            Lose = "Hostile aircraft reaches target.",
            MaxEnemyBreakthroughs = 0
        }
    };

    public static ScenarioDefinition CreateMultiWaveScenario() => new()
    {
        Name = "Multi-Wave Test",
        Description = "Two-wave hostile raid used for scenario regression.",
        DurationMinutes = 20,
        PlayerBattery = new PlayerBatteryConfig
        {
            Callsign = "ALPHA",
            Launchers = 4,
            ReserveMissiles = 16,
            RadarRangeNm = 120,
            EngagementRangeNm = 20,
            MissileType = "9M38",
            MissilePk = 0.7,
            InitialROE = "weapons_tight",
            InitialAlert = "yellow"
        },
        Command = new CommandConfig
        {
            Callsign = "ECHO",
            OpeningMessage = "ALPHA, ECHO. TEST CONTROL. EXPECT LAYERED STRIKE."
        },
        EnemyForces = new EnemyForcesConfig
        {
            Objective = "Overload defenses",
            Waves = new List<WaveConfig>
            {
                new()
                {
                    Trigger = "time",
                    TimeMinutes = 0.1,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new()
                        {
                            Type = "MiG-29",
                            Count = 2,
                            SpawnBearingDeg = 20,
                            SpawnRangeNm = 90,
                            SpawnAltitudeFt = 22000,
                            SpawnHeadingDeg = 210,
                            SpawnSpeedKts = 480,
                            InitialBehavior = "ingress_attack",
                            Aggressiveness = 0.65
                        }
                    }
                },
                new()
                {
                    Trigger = "time",
                    TimeMinutes = 2.0,
                    Aircraft = new List<AircraftSpawnConfig>
                    {
                        new()
                        {
                            Type = "SU-24",
                            Count = 1,
                            SpawnBearingDeg = 75,
                            SpawnRangeNm = 100,
                            SpawnAltitudeFt = 15000,
                            SpawnHeadingDeg = 240,
                            SpawnSpeedKts = 430,
                            InitialBehavior = "ingress_attack",
                            Aggressiveness = 0.7
                        }
                    }
                }
            }
        },
        VictoryConditions = new VictoryConditionsConfig
        {
            Win = "Defeat both waves.",
            Lose = "Raid breaks through.",
            MaxEnemyBreakthroughs = 0
        }
    };

    public static ScenarioDefinition CreateOperationScenarioWithObjectives()
    {
        var scenario = CreateMultiWaveScenario();
        scenario.SectorMap = new SectorMapConfig
        {
            TheaterName = "Kovran Lowlands",
            Objectives = new List<MapObjectiveConfig>
            {
                new() { Id = "depot", Name = "Kovran Depot", Description = "Fuel and munitions storage complex.", BearingDeg = 128, RangeNm = 58, Importance = "primary" },
                new() { Id = "relay", Name = "Dunewatch Relay", Description = "Regional command relay station.", BearingDeg = 78, RangeNm = 88, Importance = "secondary" }
            }
        };
        scenario.EnemyForces.Waves[0].TargetObjectiveId = "depot";
        scenario.EnemyForces.Waves[1].TargetObjectiveId = "relay";
        return scenario;
    }

    public static SimulationEngine CreateLoadedSimulation(ScenarioDefinition? scenario = null)
    {
        var sim = new SimulationEngine();
        sim.LoadScenario(scenario ?? CreateSingleBogeyScenario());
        return sim;
    }

    public static TrackFile AddDetectedHostileTrack(
        SimulationEngine sim,
        string designation = "SU-24",
        double bearingDeg = 45,
        double rangeNm = 15,
        double altitudeFt = 18000,
        double headingDeg = 225,
        double speedKts = 420)
    {
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            designation,
            Affiliation.Hostile,
            bearingDeg,
            rangeNm,
            altitudeFt,
            headingDeg,
            speedKts);

        var track = sim.Radar.TrackManager.ProcessDetection(aircraft, bearingDeg, iffResponse: false, radarNoise: 0);
        track.Classification = TrackClassification.Hostile;
        track.UpdateThreatAssessment();
        return track;
    }

    public static TrackFile AddDetectedFriendlyTrack(
        SimulationEngine sim,
        string designation = "F-16",
        double bearingDeg = 35,
        double rangeNm = 10,
        double altitudeFt = 16000,
        double headingDeg = 180,
        double speedKts = 390)
    {
        var aircraft = sim.Entities.SpawnAircraftAtBearingRange(
            designation,
            Affiliation.Friendly,
            bearingDeg,
            rangeNm,
            altitudeFt,
            headingDeg,
            speedKts);

        var track = sim.Radar.TrackManager.ProcessDetection(aircraft, bearingDeg, iffResponse: true, radarNoise: 0);
        track.Classification = TrackClassification.Friendly;
        track.UpdateThreatAssessment();
        return track;
    }

    public static ToolCall CreateToolCall(string name, object args)
    {
        var json = JsonSerializer.Serialize(args);
        return new ToolCall
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ArgumentsJson = json,
            Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new()
        };
    }

    public static RadioMessage DrainSingleQueuedMessage(CommManager comms)
    {
        RadioMessage? received = null;
        void Handler(RadioMessage msg) => received = msg;

        comms.MessageReceived += Handler;
        try
        {
            comms.ProcessQueue();
        }
        finally
        {
            comms.MessageReceived -= Handler;
        }

        return received ?? throw new InvalidOperationException("Expected a queued radio message.");
    }
}
