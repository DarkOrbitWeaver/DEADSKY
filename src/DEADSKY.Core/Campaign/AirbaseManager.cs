using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Campaign;

public class ScrambleRequest
{
    public string AirbaseId { get; init; } = "";
    public AircraftRole Role { get; init; }
    public int Count { get; init; }
    public double DelayRemainingSec { get; set; }
    public string PackageName { get; init; } = "";
}

public class ScrambleResult
{
    public bool Accepted { get; init; }
    public string Reason { get; init; } = "";
    public double WaitTimeSec { get; init; }
}

/// <summary>
/// Task 6.3: Handles launch queues, scrambles, and airbase resource consumption.
/// </summary>
public class AirbaseManager
{
    private readonly EntityManager _entityManager;
    private readonly CommManager _comms;
    private readonly List<ScrambleRequest> _queue = new();
    private int _scrambleCounter = 1;

    public AirbaseManager(EntityManager entityManager, CommManager comms)
    {
        _entityManager = entityManager;
        _comms = comms;
    }

    public IReadOnlyList<ScrambleRequest> GetQueue() => _queue.AsReadOnly();

    /// <summary>
    /// Requirement 6.2: Aircraft launch delay system (2-8 min).
    /// Deducts resources immediately; if successful, puts the scramble in the queue.
    /// </summary>
    public ScrambleResult RequestScramble(string airbaseId, AircraftRole role, int count)
    {
        var airbases = _entityManager.GetByType<Airbase>();
        
        // Find by precise ID or designation/callsign fallback
        var baseEntity = airbases.FirstOrDefault(a => 
            a.Id == airbaseId || 
            a.Designation.Equals(airbaseId, StringComparison.OrdinalIgnoreCase) ||
            a.CallSign.Equals(airbaseId, StringComparison.OrdinalIgnoreCase));

        if (baseEntity == null)
            return new ScrambleResult { Accepted = false, Reason = $"Airbase {airbaseId} not found or destroyed." };

        if (baseEntity.FightersAvailable < count)
            return new ScrambleResult { Accepted = false, Reason = $"Insufficient fighters available at {baseEntity.CallSign}. Has {baseEntity.FightersAvailable}." };

        // Deduct resources
        baseEntity.FightersAvailable -= count;
        baseEntity.FuelAvailableKg -= (count * 15000); // 15 tons per fighter
        baseEntity.AamAvailable -= (count * 6);        // 6 missiles per fighter

        // Calculate realism delay (2-8 minutes depends on base readiness, here simplified to random)
        double delaySec = 120 + SimulationRandom.Instance.NextDouble() * 360;

        string pkgName = $"FLIGHT-{_scrambleCounter++}";
        var req = new ScrambleRequest
        {
            AirbaseId = baseEntity.Id,
            Role = role,
            Count = count,
            DelayRemainingSec = delaySec,
            PackageName = pkgName
        };

        _queue.Add(req);

        // Radio confirmation
        var speaker = RadioRules.CreateFriendlySupportProfile("AIRFIELD COMMAND", "TOWER", baseEntity.CallSign, "AIRBASE");
        _comms.Queue(CommManager.CreateMessage(
            speaker,
            RadioChannel.CommandNet,
            $"{baseEntity.CallSign} TOWER: Scrambling {count}x fighters, callsign {pkgName}. Wheels up in {(int)(delaySec / 60)} mikes.",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALPHA",
            canReply: false,
            staticLevel: 0.1));

        return new ScrambleResult { Accepted = true, Reason = "Scramble ordered.", WaitTimeSec = delaySec };
    }

    public void Tick(double deltaTime)
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            var req = _queue[i];
            req.DelayRemainingSec -= deltaTime;

            if (req.DelayRemainingSec <= 0)
            {
                ExecuteScrambleLaunch(req);
                _queue.RemoveAt(i);
            }
        }
    }

    private void ExecuteScrambleLaunch(ScrambleRequest req)
    {
        var airbase = _entityManager.GetAs<Airbase>(req.AirbaseId);
        if (airbase == null || airbase.Status == EntityStatus.Destroyed)
        {
            // Base was destroyed before launch could execute
            var baseName = airbase?.CallSign ?? "UNKNOWN BASE";
            var speaker = RadioRules.CreateAlliedHQProfile("ECHO ACTUAL");
            _comms.Queue(CommManager.CreateMessage(
                speaker,
                RadioChannel.CommandNet,
                $"Scramble {req.PackageName} scrubbed. Base {baseName} rendered out of action.",
                MessagePriority.Immediate,
                MessageType.Alert,
                recipient: "ALPHA",
                canReply: false,
                staticLevel: 0.2));
            return;
        }

        // Spawn actual fighters
        for (int i = 0; i < req.Count; i++)
        {
            var fighter = new Aircraft
            {
                CallSign = $"{req.PackageName}-{i+1}",
                Designation = "F-16C VIPER",
                Affiliation = Affiliation.Friendly,
                Role = req.Role,
                Position = airbase.Position,
                HeadingDeg = 090,
                RequestedAltitudeM = CoordinateSystem.FtToM(25000), // Climbout profile
                AltitudeM = airbase.AltitudeM,
                SpeedMps = CoordinateSystem.KtsToMps(180), // rotation speed
                RequestedSpeedMps = CoordinateSystem.KtsToMps(450),
                CurrentBehavior = AircraftBehavior.IngressAttack,
                CommManager = _comms
            };
            
            fighter.SyncPhysicsState();
            _entityManager.Add(fighter);
        }

        var tower = RadioRules.CreateFriendlySupportProfile("AIRFIELD COMMAND", "TOWER", airbase.CallSign, "AIRBASE");
        _comms.Queue(CommManager.CreateMessage(
            tower,
            RadioChannel.CommandNet,
            $"{req.PackageName} is airborne from {airbase.CallSign}.",
            MessagePriority.Routine,
            MessageType.StatusReport,
            recipient: "ALPHA",
            canReply: false,
            staticLevel: 0.05));
    }
}
