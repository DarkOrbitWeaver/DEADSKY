# Phase 4 Implementation Plan: Friendly SAM Battery Entities

**Status:** Ready to Implement  
**Priority:** High  
**Estimated Complexity:** Medium-High  
**Dependencies:** Phase 2 (AWACS) ✅, Phase 3 (Airbase) ✅

---

## Overview

Phase 4 introduces **coordinated SAM battery operations** where multiple friendly SAM batteries can:
- Share track data and target information
- Coordinate engagements to prevent duplicate fires
- Communicate via battery-to-battery radio network
- Respond to SEAD (Suppression of Enemy Air Defense) threats
- Operate as an integrated air defense network

---

## Requirements

### 7.1 SAM Battery Coordination System

**Goal:** Enable multiple SAM batteries to coordinate their operations

**Implementation:**
1. Add coordination properties to `SAMBattery` class:
   - `BatteryNetworkId` - Shared network identifier
   - `IsNetworkCoordinator` - Flag for primary coordinator
   - `LinkedBatteryIds` - List of linked battery IDs
   - `SharedTrackData` - Dictionary of shared track information

2. Extend `FriendlySupportPackage` to include SAM battery packages:
   - Add `SAMBattery` support type
   - Track spawned battery entities
   - Manage battery availability states

### 7.2 BatteryCoordinator Class

**File:** `src/DEADSKY.Core/Campaign/BatteryCoordinator.cs`

**Responsibilities:**
- Manage network of SAM batteries
- Assign engagement priorities
- Prevent duplicate target engagements
- Share track data between batteries
- Coordinate radar modes (search vs track)

**Key Methods:**
```csharp
public class BatteryCoordinator
{
    private readonly EntityManager _entityManager;
    private readonly CommManager _comms;
    private readonly List<SAMBattery> _networkBatteries = new();
    
    public void RegisterBattery(SAMBattery battery);
    public void UnregisterBattery(string batteryId);
    public void AssignTarget(string batteryId, string trackId);
    public void CoordinateEngagements();
    public void ShareTrackData(SAMBattery source, TrackFile track);
    public void Tick(double deltaTime);
}
```

### 7.3 Engagement Coordination Tools

**File:** `src/DEADSKY.AI/Tools/ToolRegistry.cs`

**New AI Tools:**
1. `task_battery_engage` - Order battery to engage specific target
2. `task_battery_standby` - Put battery on standby
3. `task_battery_hold_fire` - Cease fire order
4. `get_battery_network_status` - Get status of all batteries in network
5. `share_track_data` - Share track information with network

**Tool Definitions:**
```csharp
Register("task_battery_engage",
    "Order a SAM battery to engage a specific hostile track.",
    new[] {
        ("battery_callsign", "required: battery callsign e.g. BRAVO"),
        ("track_id", "required: target track ID e.g. TRK-0023"),
        ("missile_count", "optional: number of missiles to fire (default 1)")
    },
    new[] { "battery_callsign", "track_id" },
    TaskBatteryEngage);

Register("get_battery_network_status",
    "Get the status of all SAM batteries in the defense network.",
    Array.Empty<(string, string)>(),
    Array.Empty<string>(),
    GetBatteryNetworkStatus);
```

### 7.4 Battery-to-Battery Radio Communications

**Files to Modify:**
- `src/DEADSKY.Core/Comms/RadioRules.cs`
- `src/DEADSKY.Core/Comms/CommManager.cs`

**New Radio Channel:**
```csharp
public enum RadioChannel
{
    // ... existing channels ...
    BatteryNetwork  // NEW: Battery-to-battery coordination
}
```

**Radio Message Types:**
1. **Engagement Coordination:**
   - "BRAVO, this is ALPHA. Engaging TRK-0023, recommend you hold fire."
   - "Copy ALPHA. Holding fire. Tracking TRK-0023."

2. **Track Data Sharing:**
   - "Network update: New track BRAVO-1, bearing 045, range 32, hostile."
   - "Copy BRAVO-1. Network tracking."

3. **SEAD Warnings:**
   - "SEAD WARNING! ARM detected bearing 090, range 15."
   - "All batteries, go silent! Repeat, go silent!"

### 7.5 SEAD Vulnerability (ARM Threats)

**Files to Modify:**
- `src/DEADSKY.Core/Entities/SAMBattery.cs`
- `src/DEADSKY.Core/Weapons/WeaponsSystem.cs`

**New Properties on SAMBattery:**
```csharp
public bool IsRadiating { get; private set; } = true;
public bool IsBeingTargetedByARM { get; private set; }
public double? ARMThreatBearing { get; private set; }
public double? ARMThreatRangeNm { get; private set; }
public DateTime? ARMThreatDetectedTime { get; private set; }
public bool IsInSilentMode { get; private set; }
```

**New Methods:**
```csharp
public void GoSilent()
{
    RadarOnline = false;
    RadarMode = RadarMode.Silent;
    IsRadiating = false;
    IsInSilentMode = true;
}

public void GoActive()
{
    RadarOnline = true;
    RadarMode = RadarMode.Search;
    IsRadiating = true;
    IsInSilentMode = false;
}
```

**ARM Detection Logic:**
- When enemy aircraft with ARMs (Anti-Radiation Missiles) detect radar emissions
- ARM launches toward radar source
- Battery RWR detects ARM lock
- Battery must go silent or risk destruction

### 7.6 Multiple Battery Network Management

**Goal:** Support 2-4 batteries in coordinated network

**Implementation:**
1. **Network Topology:**
   - One primary coordinator (usually player's ALPHA battery)
   - Secondary batteries (BRAVO, CHARLIE, etc.)
   - Mesh communication network

2. **Network States:**
   - `Online` - Full network operational
   - `Degraded` - One or more batteries offline
   - `Fragmented` - Coordinator lost, secondary takes over
   - `Offline` - Network collapsed

3. **Failover Logic:**
   - If coordinator destroyed, next senior battery takes over
   - Automatic reassignment of engagement priorities

### 7.7 Cooperative Engagement Protocols

**Engagement Priority Rules:**
1. **Best Geometry:** Battery with best shot geometry engages
2. **Ammo Conservation:** Battery with most missiles engages
3. **Redundancy:** Second battery tracks but doesn't fire
4. **Salvo Coordination:** Multiple batteries fire simultaneously for saturation

**Protocol Implementation:**
```csharp
public class EngagementProtocol
{
    public SAMBattery SelectEngagingBattery(
        List<SAMBattery> batteries, 
        TrackFile target)
    {
        // Calculate engagement quality for each battery
        var candidates = batteries
            .Where(b => b.CanEngage(target.RangeNm, target.AltitudeFt))
            .Select(b => new {
                Battery = b,
                Quality = CalculateEngagementQuality(b, target)
            })
            .OrderByDescending(x => x.Quality)
            .ToList();
        
        // Best battery engages, second tracks for redundancy
        if (candidates.Count >= 2)
        {
            candidates[0].Battery.AssignTarget(target);
            candidates[1].Battery.TrackOnly(target);
        }
        else if (candidates.Count == 1)
        {
            candidates[0].Battery.AssignTarget(target);
        }
        
        return candidates.FirstOrDefault()?.Battery;
    }
}
```

---

## Implementation Checklist

### Core Systems
- [ ] 7.1.1 Add coordination properties to SAMBattery
- [ ] 7.1.2 Extend FriendlySupportPackage for SAM batteries
- [ ] 7.2.1 Create BatteryCoordinator class
- [ ] 7.2.2 Implement battery registration/unregistration
- [ ] 7.2.3 Implement engagement assignment logic
- [ ] 7.2.4 Implement track data sharing
- [ ] 7.2.5 Wire BatteryCoordinator into SimulationEngine

### AI Tools
- [ ] 7.3.1 Add `task_battery_engage` tool
- [ ] 7.3.2 Add `task_battery_standby` tool
- [ ] 7.3.3 Add `task_battery_hold_fire` tool
- [ ] 7.3.4 Add `get_battery_network_status` tool
- [ ] 7.3.5 Update ToolAccessPolicy for new tools

### Communications
- [ ] 7.4.1 Add BatteryNetwork radio channel
- [ ] 7.4.2 Create battery coordination radio messages
- [ ] 7.4.3 Implement SEAD warning broadcasts
- [ ] 7.4.4 Add RadioRules for battery network traffic

### SEAD/ARM System
- [ ] 7.5.1 Add ARM detection properties to SAMBattery
- [ ] 7.5.2 Implement GoSilent() and GoActive() methods
- [ ] 7.5.3 Add ARM threat detection in WeaponsSystem
- [ ] 7.5.4 Implement ARM missile guidance toward radar sources
- [ ] 7.5.5 Add ARM vulnerability logic

### Integration
- [ ] 7.6.1 Support multiple battery spawning
- [ ] 7.6.2 Implement network failover logic
- [ ] 7.7.1 Implement engagement priority protocols
- [ ] 7.7.2 Implement salvo coordination
- [ ] 7.7.3 Wire all systems in SimulationEngine

### Testing
- [ ] Create BatteryCoordinatorTests.cs
- [ ] Create SAMBatteryNetworkTests.cs
- [ ] Create SEADVulnerabilityTests.cs
- [ ] Test multi-battery engagements
- [ ] Test ARM threat scenarios
- [ ] Test network failover

---

## Files to Create

1. `src/DEADSKY.Core/Campaign/BatteryCoordinator.cs`
2. `tests/DEADSKY.Backend.Tests/BatteryCoordinatorTests.cs`
3. `tests/DEADSKY.Backend.Tests/SAMBatteryNetworkTests.cs`
4. `tests/DEADSKY.Backend.Tests/SEADVulnerabilityTests.cs`

## Files to Modify

1. `src/DEADSKY.Core/Entities/SAMBattery.cs`
2. `src/DEADSKY.Core/Campaign/FriendlySupportDirector.cs`
3. `src/DEADSKY.Core/Simulation/SimulationEngine.cs`
4. `src/DEADSKY.AI/Tools/ToolRegistry.cs`
5. `src/DEADSKY.AI/Tools/ToolAccessPolicy.cs`
6. `src/DEADSKY.Core/Comms/RadioRules.cs`
7. `src/DEADSKY.Core/Comms/CommManager.cs`
8. `src/DEADSKY.Core/Weapons/WeaponsSystem.cs`

---

## Risk Assessment

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking existing SAM battery logic | High | Low | Extensive unit tests, regression testing |
| Coordinator complexity | Medium | Medium | Start simple, iterate with basic features first |
| AI tool integration | Medium | Low | Mirror existing tool patterns from Phase 3 |
| Radio channel complexity | Low | Low | Use existing channel infrastructure |
| ARM balance issues | Medium | Medium | Tunable parameters, playtesting |

---

## Success Criteria

1. **Functional:**
   - ✅ 2+ batteries can coordinate on same target
   - ✅ No duplicate engagements (wasted missiles)
   - ✅ Battery network shares track data
   - ✅ SEAD threats detected and responded to
   - ✅ Battery can go silent to avoid ARM

2. **AI Integration:**
   - ✅ AlliedHQ can task batteries via AI tools
   - ✅ Battery status visible in operational picture
   - ✅ Radio traffic generated for coordination

3. **Testing:**
   - ✅ All new tests pass
   - ✅ All existing tests still pass (no regressions)
   - ✅ Build succeeds with 0 errors

---

## Estimated Timeline

| Task | Estimated Hours |
|------|-----------------|
| BatteryCoordinator core | 4-6 hours |
| AI Tools | 2-3 hours |
| Radio Communications | 2-3 hours |
| SEAD/ARM System | 4-6 hours |
| Integration & Testing | 4-6 hours |
| **Total** | **16-24 hours** |

---

## Next Steps

1. **Start with BatteryCoordinator** - Core coordination logic
2. **Add AI Tools** - Enable AI to task batteries
3. **Implement Radio** - Add coordination communications
4. **Add SEAD Threat** - Implement ARM vulnerability
5. **Integration** - Wire into SimulationEngine
6. **Testing** - Comprehensive test coverage

---

**Ready to begin implementation.**
