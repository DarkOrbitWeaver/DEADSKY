using DEADSKY.Core.Comms;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Radar;
using DEADSKY.Core.Simulation;

namespace DEADSKY.Core.Campaign;

/// <summary>
/// Phase 4: Coordinates multiple SAM batteries in a networked air defense system.
/// Responsibilities:
/// - Manage network of SAM batteries
/// - Assign engagement priorities to prevent duplicate fires
/// - Share track data between batteries
/// - Coordinate radar modes (search vs track)
/// - Handle network failover if coordinator is destroyed
/// </summary>
public class BatteryCoordinator
{
    private readonly EntityManager _entityManager;
    private readonly CommManager _comms;
    private readonly List<SAMBattery> _networkBatteries = new();
    private readonly Dictionary<string, EngagementAssignment> _engagementAssignments = new();
    private double _coordinationTimer;
    private const double CoordinationTickInterval = 2.0; // Coordinate every 2 seconds

    public BatteryCoordinator(EntityManager entityManager, CommManager comms)
    {
        _entityManager = entityManager;
        _comms = comms;
    }

    /// <summary>
    /// Register a battery with the coordination network.
    /// </summary>
    public void RegisterBattery(SAMBattery battery)
    {
        if (!_networkBatteries.Contains(battery))
        {
            _networkBatteries.Add(battery);
            
            // Set network ID if not already set
            if (string.IsNullOrEmpty(battery.BatteryNetworkId))
            {
                battery.BatteryNetworkId = $"NETWORK-{_networkBatteries.Count}";
            }

            // First battery becomes coordinator by default
            if (_networkBatteries.Count == 1)
            {
                battery.IsNetworkCoordinator = true;
            }

            // Link batteries together
            LinkBatteries(battery);
        }
    }

    /// <summary>
    /// Unregister a battery from the network (e.g., when destroyed).
    /// </summary>
    public void UnregisterBattery(string batteryId)
    {
        var battery = _networkBatteries.FirstOrDefault(b => b.Id == batteryId);
        if (battery != null)
        {
            _networkBatteries.Remove(battery);
            
            // If coordinator was destroyed, elect new coordinator
            if (battery.IsNetworkCoordinator && _networkBatteries.Count > 0)
            {
                ElectNewCoordinator();
            }

            // Remove from engagement assignments
            _engagementAssignments.Remove(batteryId);
        }
    }

    /// <summary>
    /// Assign a target track to a specific battery for engagement.
    /// </summary>
    public void AssignTarget(string batteryId, string trackId, bool isPrimary = true)
    {
        var battery = _networkBatteries.FirstOrDefault(b => b.Id == batteryId);
        if (battery == null) return;

        // Clear previous assignment
        if (_engagementAssignments.TryGetValue(batteryId, out var existing))
        {
            existing.Battery.AssignedEngagementTrackId = null;
            existing.Battery.IsInTrackOnlyMode = false;
        }

        // Create new assignment
        _engagementAssignments[batteryId] = new EngagementAssignment
        {
            Battery = battery,
            TrackId = trackId,
            IsPrimary = isPrimary,
            AssignedAt = DateTime.UtcNow
        };

        battery.AssignedEngagementTrackId = trackId;
        battery.IsInTrackOnlyMode = !isPrimary;

        // Radio announcement for coordination
        if (isPrimary)
        {
            BroadcastEngagementAssignment(battery, trackId);
        }
    }

    /// <summary>
    /// Share track data from one battery to all others in the network.
    /// </summary>
    public void ShareTrackData(SAMBattery sourceBattery, TrackFile track)
    {
        if (track == null || string.IsNullOrEmpty(track.TrackId)) return;

        // Update source battery's shared data
        sourceBattery.SharedTrackData[track.TrackId] = DateTime.UtcNow;

        // Share with all other batteries
        foreach (var battery in _networkBatteries.Where(b => b.Id != sourceBattery.Id))
        {
            battery.SharedTrackData[track.TrackId] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Coordinate engagements across all batteries.
    /// Called periodically to optimize target assignments.
    /// </summary>
    public void CoordinateEngagements(IReadOnlyList<TrackFile> hostileTracks)
    {
        // Find best battery for each track
        var assignments = new Dictionary<string, (SAMBattery Battery, double Quality)>();

        foreach (var track in hostileTracks.Where(t => t.ThreatLevel > 0.3))
        {
            var bestBattery = SelectBestBatteryForTrack(track);
            if (bestBattery != null)
            {
                var quality = CalculateEngagementQuality(bestBattery, track);
                assignments[track.TrackId] = (bestBattery, quality);
            }
        }

        // Apply assignments
        foreach (var (trackId, assignment) in assignments)
        {
            AssignTarget(assignment.Battery.Id, trackId, isPrimary: true);
        }

        // Set remaining batteries to track-only mode
        var assignedBatteryIds = assignments.Values.Select(v => v.Battery.Id).ToHashSet();
        foreach (var battery in _networkBatteries.Where(b => !assignedBatteryIds.Contains(b.Id)))
        {
            battery.IsInTrackOnlyMode = true;
        }
    }

    /// <summary>
    /// Select the best battery to engage a specific track.
    /// </summary>
    private SAMBattery? SelectBestBatteryForTrack(TrackFile track)
    {
        var candidates = _networkBatteries
            .Where(b => b.CanEngage(track.RangeNm, track.AltitudeFt) && 
                       !b.IsInSilentMode && 
                       b.ReadyLaunchers > 0)
            .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        // Select based on engagement quality
        return candidates
            .Select(b => new { Battery = b, Quality = CalculateEngagementQuality(b, track) })
            .OrderByDescending(x => x.Quality)
            .First()
            .Battery;
    }

    /// <summary>
    /// Calculate engagement quality score for a battery-track pair.
    /// </summary>
    private double CalculateEngagementQuality(SAMBattery battery, TrackFile track)
    {
        // Factors: range, altitude, launcher availability, battery health
        double rangeScore = 1.0 - Math.Abs(track.RangeNm - (battery.MissileMaxRangeNm * 0.6)) / battery.MissileMaxRangeNm;
        double altitudeScore = Math.Clamp(track.AltitudeFt / battery.MissileMaxAltFt, 0, 1);
        double launcherScore = (double)battery.ReadyLaunchers / battery.MaxLaunchers;
        double healthScore = battery.RadarHealthPct;

        return (rangeScore * 0.3 + altitudeScore * 0.2 + launcherScore * 0.3 + healthScore * 0.2);
    }

    /// <summary>
    /// Link batteries together for network coordination.
    /// </summary>
    private void LinkBatteries(SAMBattery newBattery)
    {
        foreach (var existing in _networkBatteries.Where(b => b.Id != newBattery.Id))
        {
            if (!existing.LinkedBatteryIds.Contains(newBattery.Id))
            {
                existing.LinkedBatteryIds.Add(newBattery.Id);
            }
            if (!newBattery.LinkedBatteryIds.Contains(existing.Id))
            {
                newBattery.LinkedBatteryIds.Add(existing.Id);
            }
        }
    }

    /// <summary>
    /// Elect a new coordinator when the current one is destroyed.
    /// </summary>
    private void ElectNewCoordinator()
    {
        if (_networkBatteries.Count == 0) return;

        // Elect battery with highest health and most launchers
        var newCoordinator = _networkBatteries
            .OrderByDescending(b => b.RadarHealthPct)
            .ThenByDescending(b => b.ReadyLaunchers)
            .First();

        // Clear old coordinator flag
        foreach (var battery in _networkBatteries)
        {
            battery.IsNetworkCoordinator = false;
        }

        newCoordinator.IsNetworkCoordinator = true;

        // Announce new coordinator
        var speaker = RadioRules.CreateFriendlySupportProfile(
            "BATTERY NETWORK", "COORDINATOR", newCoordinator.Callsign, "SAM BATTERY");
        
        _comms.Queue(CommManager.CreateMessage(
            speaker,
            RadioChannel.AirDefenseNet,
            $"{newCoordinator.Callsign} assuming network coordination. All batteries, check in.",
            MessagePriority.Priority,
            MessageType.StatusReport,
            recipient: "ALL BATTERIES",
            canReply: true,
            staticLevel: 0.1));
    }

    /// <summary>
    /// Broadcast engagement assignment over radio network.
    /// </summary>
    private void BroadcastEngagementAssignment(SAMBattery battery, string trackId)
    {
        var speaker = RadioRules.CreateFriendlySupportProfile(
            "BATTERY NETWORK", "COORDINATOR", battery.Callsign, "SAM BATTERY");
        
        _comms.Queue(CommManager.CreateMessage(
            speaker,
            RadioChannel.AirDefenseNet,
            $"{battery.Callsign} engaging {trackId}. All other batteries hold fire.",
            MessagePriority.Immediate,
            MessageType.StatusReport,
            recipient: "ALL BATTERIES",
            canReply: false,
            staticLevel: 0.1));
    }

    /// <summary>
    /// Main tick method - called by SimulationEngine.
    /// </summary>
    public void Tick(double deltaTime, SimulationSnapshot snapshot)
    {
        _coordinationTimer += deltaTime;

        // Periodic coordination
        if (_coordinationTimer >= CoordinationTickInterval)
        {
            _coordinationTimer = 0;
            CoordinateEngagements(snapshot.HostileTracks);
        }

        // Check for destroyed batteries
        var destroyedBatteries = _networkBatteries
            .Where(b => b.Status == EntityStatus.Destroyed)
            .ToList();

        foreach (var battery in destroyedBatteries)
        {
            UnregisterBattery(battery.Id);
        }
    }

    /// <summary>
    /// Get all batteries in the network.
    /// </summary>
    public IReadOnlyList<SAMBattery> GetNetworkBatteries() => _networkBatteries.AsReadOnly();

    /// <summary>
    /// Get network status summary.
    /// </summary>
    public string GetNetworkStatus()
    {
        if (_networkBatteries.Count == 0)
            return "BATTERY NETWORK: OFFLINE";

        var coordinator = _networkBatteries.FirstOrDefault(b => b.IsNetworkCoordinator);
        var activeCount = _networkBatteries.Count(b => b.Status == EntityStatus.Active);
        var silentCount = _networkBatteries.Count(b => b.IsInSilentMode);
        var armThreatCount = _networkBatteries.Count(b => b.IsBeingTargetedByARM);

        return $"BATTERY NETWORK: {activeCount}/{_networkBatteries.Count} ACTIVE | " +
               $"COORDINATOR: {coordinator?.Callsign ?? "NONE"} | " +
               $"SILENT: {silentCount} | ARM THREAT: {armThreatCount}";
    }
}

/// <summary>
/// Tracks engagement assignments for batteries.
/// </summary>
public class EngagementAssignment
{
    public SAMBattery Battery { get; init; } = null!;
    public string TrackId { get; init; } = "";
    public bool IsPrimary { get; init; }
    public DateTime AssignedAt { get; init; }
}
