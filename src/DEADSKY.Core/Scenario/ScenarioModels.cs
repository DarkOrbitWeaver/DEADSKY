using System.Text.Json;
using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Scenario;

public sealed class ScenarioDefinition
{
    public string Name { get; set; } = "Untitled Scenario";
    public string Description { get; set; } = "";
    public double DurationMinutes { get; set; } = 10;
    public PlayerBatteryConfig PlayerBattery { get; set; } = new();
    public CommandConfig Command { get; set; } = new();
    public EnemyForcesConfig EnemyForces { get; set; } = new();
    public VictoryConditionsConfig VictoryConditions { get; set; } = new();
}

public sealed class PlayerBatteryConfig
{
    public string Callsign { get; set; } = "ALPHA";
    public string Type { get; set; } = "SA-11 BUK";
    public int Launchers { get; set; } = 4;
    public int ReserveMissiles { get; set; } = 12;
    public double RadarRangeNm { get; set; } = 80;
    public double EngagementRangeNm { get; set; } = 18;
    public string MissileType { get; set; } = "9M38";
    public double MissilePk { get; set; } = 0.7;
    public string InitialROE { get; set; } = "weapons_tight";
    public string InitialAlert { get; set; } = "yellow";
}

public sealed class CommandConfig
{
    public string Callsign { get; set; } = "ECHO";
    public string InitialROE { get; set; } = "weapons_tight";
    public string InitialAlert { get; set; } = "yellow";
    public string OpeningMessage { get; set; } = "";
}

public sealed class EnemyForcesConfig
{
    public string Objective { get; set; } = "";
    public List<WaveConfig> Waves { get; set; } = new();
}

public sealed class WaveConfig
{
    public string Trigger { get; set; } = "time";
    public double TimeMinutes { get; set; }
    public List<AircraftSpawnConfig> Aircraft { get; set; } = new();
}

public sealed class AircraftSpawnConfig
{
    public string Type { get; set; } = "Fighter";
    public int Count { get; set; } = 1;
    public double SpawnBearingDeg { get; set; } = 45;
    public double SpawnRangeNm { get; set; } = 80;
    public double SpawnAltitudeFt { get; set; } = 15000;
    public double SpawnHeadingDeg { get; set; } = 225;
    public double SpawnSpeedKts { get; set; } = 420;
    public string InitialBehavior { get; set; } = "ingress_attack";
    public double Aggressiveness { get; set; } = 0.6;
}

public sealed class VictoryConditionsConfig
{
    public string Win { get; set; } = "";
    public string Lose { get; set; } = "";
    public int MaxEnemyBreakthroughs { get; set; }
}

public sealed class ScenarioLoader
{
    public ScenarioDefinition Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ScenarioDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ScenarioDefinition();
    }
}

public sealed class ScenarioManager
{
    public enum MissionOutcome
    {
        Victory,
        PartialVictory,
        Defeat
    }

    private readonly SimulationEngine _sim;
    private readonly HashSet<int> _spawnedWaveIndices = new();
    private readonly HashSet<string> _countedBreakthroughs = new();
    private bool _missionResolved;

    public ScenarioDefinition? CurrentScenario { get; private set; }
    public int EnemyBreakthroughs { get; private set; }

    public event Action<string>? OnWaveSpawned;
    public event Action<MissionOutcome, string>? OnMissionComplete;

    public ScenarioManager(SimulationEngine sim)
    {
        _sim = sim;
    }

    public void LoadScenario(ScenarioDefinition scenario)
    {
        CurrentScenario = scenario;
        EnemyBreakthroughs = 0;
        _spawnedWaveIndices.Clear();
        _countedBreakthroughs.Clear();
        _missionResolved = false;

        _sim.LoadScenario(scenario);

        if (!string.IsNullOrWhiteSpace(scenario.Command.OpeningMessage))
        {
            _sim.Comms.Queue(CommManager.CreateAlliedHQMessage(
                scenario.Command.OpeningMessage,
                MessagePriority.Priority));
        }
    }

    public void Update(double gameTimeSec)
    {
        if (CurrentScenario == null || _missionResolved)
            return;

        for (int i = 0; i < CurrentScenario.EnemyForces.Waves.Count; i++)
        {
            var wave = CurrentScenario.EnemyForces.Waves[i];
            if (_spawnedWaveIndices.Contains(i))
                continue;

            if (wave.Trigger.Equals("time", StringComparison.OrdinalIgnoreCase) &&
                gameTimeSec >= wave.TimeMinutes * 60)
            {
                SpawnWave(i, wave);
            }
        }

        foreach (var aircraft in _sim.Entities.GetHostileAircraft().OfType<Aircraft>())
        {
            if (aircraft.Position.Length > 3500 || _countedBreakthroughs.Contains(aircraft.Id))
                continue;

            _countedBreakthroughs.Add(aircraft.Id);
            EnemyBreakthroughs++;
        }

        if (EnemyBreakthroughs > CurrentScenario.VictoryConditions.MaxEnemyBreakthroughs)
        {
            ResolveMission(MissionOutcome.Defeat, CurrentScenario.VictoryConditions.Lose);
            return;
        }

        bool allWavesSpawned = _spawnedWaveIndices.Count == CurrentScenario.EnemyForces.Waves.Count;
        bool anyHostilesRemain = _sim.Entities.GetHostileAircraft().Count > 0;
        bool timeExpired = gameTimeSec >= CurrentScenario.DurationMinutes * 60;

        if (allWavesSpawned && !anyHostilesRemain)
            ResolveMission(MissionOutcome.Victory, CurrentScenario.VictoryConditions.Win);
        else if (timeExpired)
            ResolveMission(anyHostilesRemain ? MissionOutcome.PartialVictory : MissionOutcome.Victory,
                anyHostilesRemain ? "Mission timer expired with residual threats." : CurrentScenario.VictoryConditions.Win);
    }

    private void SpawnWave(int index, WaveConfig wave)
    {
        string waveName = $"WAVE-{index + 1}";
        int spawned = 0;

        foreach (var spawn in wave.Aircraft)
        {
            for (int i = 0; i < Math.Max(1, spawn.Count); i++)
            {
                var aircraft = _sim.Entities.SpawnAircraftAtBearingRange(
                    spawn.Type,
                    Affiliation.Hostile,
                    spawn.SpawnBearingDeg + i * 2,
                    spawn.SpawnRangeNm,
                    spawn.SpawnAltitudeFt,
                    spawn.SpawnHeadingDeg,
                    spawn.SpawnSpeedKts);

                aircraft.AggressivenessLevel = spawn.Aggressiveness;
                aircraft.GroupId = waveName;
                spawned++;
            }
        }

        _spawnedWaveIndices.Add(index);
        OnWaveSpawned?.Invoke($"{waveName} ({spawned} hostile aircraft)");
    }

    private void ResolveMission(MissionOutcome outcome, string reason)
    {
        _missionResolved = true;
        OnMissionComplete?.Invoke(outcome, reason);
    }
}
