using DEADSKY.Core.Physics;

namespace DEADSKY.Core.Entities;

public class EntityAddedEvent { public Entity Entity { get; init; } = null!; }
public class EntityRemovedEvent { public Entity Entity { get; init; } = null!; public string Reason { get; init; } = ""; }
public class EntityStatusChangedEvent { public Entity Entity { get; init; } = null!; public EntityStatus OldStatus { get; init; } public EntityStatus NewStatus { get; init; } }

/// <summary>
/// The central registry for all simulated entities.
/// Thread-safe reads via snapshot. Writes happen only in simulation tick.
/// </summary>
public class EntityManager
{
    private readonly Dictionary<string, Entity> _entities = new();
    private readonly object _lock = new();

    // Events — raised on simulation thread, subscribers must dispatch to UI thread
    public event Action<EntityAddedEvent>? EntityAdded;
    public event Action<EntityRemovedEvent>? EntityRemoved;
    public event Action<EntityStatusChangedEvent>? EntityStatusChanged;

    // ── CRUD ──────────────────────────────────────────────────────────

    public void Add(Entity entity)
    {
        lock (_lock) { _entities[entity.Id] = entity; }
        EntityAdded?.Invoke(new EntityAddedEvent { Entity = entity });
    }

    public Entity? Get(string id)
    {
        lock (_lock) { return _entities.TryGetValue(id, out var e) ? e : null; }
    }

    public T? GetAs<T>(string id) where T : Entity => Get(id) as T;

    public void Remove(string id, string reason = "Removed")
    {
        Entity? entity;
        lock (_lock) { _entities.TryGetValue(id, out entity); _entities.Remove(id); }
        if (entity != null)
            EntityRemoved?.Invoke(new EntityRemovedEvent { Entity = entity, Reason = reason });
    }

    /// <summary>Mark entity destroyed and raise status change event</summary>
    public void Destroy(string id, string reason = "Destroyed")
    {
        Entity? entity;
        lock (_lock) { _entities.TryGetValue(id, out entity); }
        if (entity == null) return;

        var oldStatus = entity.Status;
        entity.Status = EntityStatus.Destroyed;
        EntityStatusChanged?.Invoke(new EntityStatusChangedEvent
        {
            Entity = entity, OldStatus = oldStatus, NewStatus = EntityStatus.Destroyed
        });

        // Keep in list for a few seconds for death animations, then remove
        // (removal happens in cleanup pass in SimulationEngine)
    }

    // ── Queries ───────────────────────────────────────────────────────

    /// <summary>Get a snapshot (copy) of all active entities — safe to enumerate on any thread</summary>
    public IReadOnlyList<Entity> GetSnapshot()
    {
        lock (_lock) { return _entities.Values.ToList(); }
    }

    public IReadOnlyList<Entity> GetActiveSnapshot()
    {
        lock (_lock) { return _entities.Values.Where(e => e.IsActive).ToList(); }
    }

    public IReadOnlyList<T> GetByType<T>() where T : Entity
    {
        lock (_lock) { return _entities.Values.OfType<T>().ToList(); }
    }

    public IReadOnlyList<Entity> GetByAffiliation(Affiliation affiliation)
    {
        lock (_lock) { return _entities.Values.Where(e => e.Affiliation == affiliation && e.IsActive).ToList(); }
    }

    public IReadOnlyList<Entity> GetHostileAircraft()
    {
        lock (_lock)
        {
            return _entities.Values
                .Where(e => e.IsActive &&
                            e.Affiliation == Affiliation.Hostile &&
                            (e.Type == EntityType.Aircraft || e.Type == EntityType.Drone ||
                             e.Type == EntityType.CruiseMissile || e.Type == EntityType.Helicopter))
                .ToList();
        }
    }

    public IReadOnlyList<SAMMissile> GetActiveMissiles()
    {
        lock (_lock)
        {
            return _entities.Values
                .OfType<SAMMissile>()
                .Where(m => m.IsActive && !m.HasDetonated)
                .ToList();
        }
    }

    public SAMBattery? GetPlayerBattery()
    {
        lock (_lock)
        {
            return _entities.Values.OfType<SAMBattery>()
                .FirstOrDefault(b => b.Affiliation == Affiliation.Friendly && b.Callsign == "ALPHA");
        }
    }

    public int Count
    {
        get { lock (_lock) { return _entities.Count; } }
    }

    public int ActiveCount
    {
        get { lock (_lock) { return _entities.Values.Count(e => e.IsActive); } }
    }

    // ── Update all ────────────────────────────────────────────────────

    /// <summary>Update all active entities. Called by SimulationEngine each tick.</summary>
    public void UpdateAll(double deltaTime)
    {
        // Get snapshot to avoid modification during iteration
        List<Entity> active;
        lock (_lock) { active = _entities.Values.Where(e => e.IsActive).ToList(); }

        foreach (var entity in active)
        {
            entity.Update(deltaTime);
        }

        // Cleanup destroyed entities that have been dead for >5 seconds
        var toRemove = new List<string>();
        lock (_lock)
        {
            foreach (var (id, entity) in _entities)
            {
                if (entity.Status == EntityStatus.Destroyed &&
                    (DateTime.UtcNow - entity.StatusChangedUtc).TotalSeconds > 3.0 && // wait for death FX
                    entity.Type != EntityType.SAMBattery) // never remove the player battery
                {
                    toRemove.Add(id);
                }
            }
        }
        foreach (var id in toRemove)
            Remove(id, "Cleanup");
    }

    public void Clear()
    {
        lock (_lock) { _entities.Clear(); }
    }

    // ── Spawn helpers ─────────────────────────────────────────────────

    /// <summary>Spawn an aircraft at the given bearing/range from origin</summary>
    public Aircraft SpawnAircraftAtBearingRange(
        string designation, Affiliation affiliation,
        double bearingDeg, double rangeNm, double altitudeFt,
        double headingDeg, double speedKts)
    {
        var aircraft = Aircraft.CreateFromType(designation, affiliation);
        aircraft.Position = CoordinateSystem.FromBearingRange(bearingDeg, rangeNm);
        aircraft.AltitudeM = CoordinateSystem.FtToM(altitudeFt);
        aircraft.HeadingDeg = headingDeg;
        aircraft.SpeedMps = CoordinateSystem.KtsToMps(speedKts);
        aircraft.SpawnTime = DateTime.UtcNow;
        aircraft.SyncPhysicsState();
        Add(aircraft);
        return aircraft;
    }

    /// <summary>Fire a SAM missile from a launcher toward a target</summary>
    public SAMMissile LaunchMissile(
        SAMBattery battery, Launcher launcher, Entity target,
        string missileType, double singleShotPk)
    {
        var missile = new SAMMissile
        {
            MissileTypeName = missileType,
            TargetEntityId = target.Id,
            LaunchedByBatteryId = battery.Id,
            LauncherId = launcher.Id,
            SingleShotPk = singleShotPk,
            Position = battery.Position,
            HeadingDeg = battery.Position.HeadingTo(target.Position),
            AltitudeM = battery.AltitudeM + 10, // launch height
            SpeedMps = CoordinateSystem.KtsToMps(200), // initial speed
            Affiliation = Affiliation.Friendly,
            SpawnTime = DateTime.UtcNow
        };

        missile.SyncPhysicsState();
        missile.RequestedSpeedMps = missile.FlightModel.MaxSpeedMps;
        missile.RequestedHeadingDeg = missile.HeadingDeg;
        missile.RequestedAltitudeM = target.AltitudeM;

        launcher.State = LauncherState.Reloading;
        launcher.ReloadProgress = 0;
        launcher.ActiveMissileId = missile.Id;

        battery.MissilesFired++;

        Add(missile);
        return missile;
    }
}
