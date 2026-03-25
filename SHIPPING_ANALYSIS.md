# DEADSKY - What's Missing to Ship

## Executive Summary

You have **~60% of the core systems implemented**. The simulation engine is solid, but you're missing critical integration layers and several complete subsystems. Here's what you need to ship a playable v1.0.

---

## ✅ WHAT YOU HAVE (Working)

### Core Simulation (SOLID)
- ✅ Entity system with proper physics
- ✅ Flight models for aircraft/missiles
- ✅ Coordinate system (Vec2, bearing/range conversions)
- ✅ Simulation clock with time scaling
- ✅ Entity manager with lifecycle
- ✅ Event bus for pub/sub

### Radar & Tracking (GOOD)
- ✅ Radar detection engine with RCS/range/ECM
- ✅ Track manager with correlation
- ✅ Track quality (firm/fading/lost)
- ✅ IFF system
- ✅ Radar modes (Search/TWS/STT/Silent)

### Weapons (FUNCTIONAL)
- ✅ SAM missile physics
- ✅ Proportional navigation guidance
- ✅ Launcher states & reload
- ✅ Engagement manager
- ✅ Pk calculation
- ✅ Kill assessment

### Aircraft AI (BASIC)
- ✅ Multiple behaviors (ingress/egress/evasion/pop-up)
- ✅ Waypoint navigation
- ✅ Fuel management
- ✅ ECM capability
- ✅ Formation slots

### Communications (COMPLETE)
- ✅ Radio channels
- ✅ Message priority
- ✅ Message history
- ✅ Brevity codes dictionary

### UI Foundation (STARTED)
- ✅ Radar display with SkiaSharp (beautiful!)
- ✅ MainViewModel structure
- ✅ MVVM bindings setup
- ✅ Track selection

---

## ❌ WHAT'S MISSING (Critical for v1.0)

### 1. **SimulationEngine.cs** - MISSING ENTIRELY ⚠️
**Priority: CRITICAL**

You have all the pieces but no main loop! You need:

```csharp
public class SimulationEngine
{
    public EntityManager Entities { get; }
    public RadarSystem Radar { get; }
    public WeaponsSystem Weapons { get; }
    public CommManager Comms { get; }
    public SimulationClock Clock { get; }
    public EventBus Events { get; }
    
    private Thread? _simThread;
    private bool _running;
    
    public void Start() { /* Start sim thread */ }
    public void Pause() { /* Pause */ }
    public void Tick() 
    {
        double dt = Clock.GetDeltaTime();
        
        // Update all systems
        Entities.UpdateAll(dt);
        Radar.Update(dt, Entities.GetActiveSnapshot(), ...);
        Weapons.Update(dt);
        Comms.ProcessQueue();
        
        // Publish tick event
        Events.Publish(new SimulationTickEvent(...));
        
        // Create snapshot for UI
        LatestSnapshot = CreateSnapshot();
    }
}
```

**Without this, nothing runs.**

---

### 2. **SimulationSnapshot.cs** - MISSING ⚠️
**Priority: CRITICAL**

The UI needs a thread-safe snapshot of game state:

```csharp
public record SimulationSnapshot
{
    public double GameTimeSec { get; init; }
    public string GameTimeString { get; init; }
    public double RadarSweepAngle { get; init; }
    public double RadarRangeNm { get; init; }
    public RadarMode RadarMode { get; init; }
    public SAMBattery? Battery { get; init; }
    public IReadOnlyList<TrackFile> AllTracks { get; init; }
    public IReadOnlyList<TrackFile> FirmTracks { get; init; }
    public IReadOnlyList<TrackFile> HostileTracks { get; init; }
    public IReadOnlyList<SAMMissile> ActiveMissiles { get; init; }
    public IReadOnlyList<EcmEffect> ActiveEcmEffects { get; init; }
}
```

---

### 3. **Player Command Interface** - MISSING
**Priority: CRITICAL**

SimulationEngine needs player action methods:

```csharp
public bool PlayerDesignate(string trackId) { ... }
public bool PlayerFire(string trackId) { ... }
public List<EngagementResult> PlayerSalvo(string trackId, int count) { ... }
public void PlayerSetRadarMode(RadarMode mode) { ... }
public void PlayerSetRadarRange(double rangeNm) { ... }
public RadioMessage PlayerSendMessage(RadioChannel ch, string content) { ... }
```

---

### 4. **UI Panels** - MOSTLY MISSING
**Priority: HIGH**

You have RadarDisplay but need:

- ❌ **WeaponsPanel** - Launcher states, fire buttons, Pk display
- ❌ **CommsPanel** - Channel tabs, message list, input box
- ❌ **ThreatBoard** - Sortable track table
- ❌ **SystemStatus** - Battery health, alert level, ROE
- ❌ **ContactDetail** - Selected track info panel

**MainWindow.xaml is empty** - needs layout with all panels.

---

### 5. **Scenario System** - PARTIALLY MISSING
**Priority: HIGH**

You reference it in MainViewModel but it doesn't exist:

```csharp
// MISSING FILES:
- ScenarioDefinition.cs
- ScenarioLoader.cs  
- ScenarioManager.cs
- WaveSpawner.cs
- VictoryConditions.cs
```

Need to spawn waves, check win/loss, manage mission flow.

---

### 6. **Audio System** - STUB ONLY
**Priority: MEDIUM**

MainViewModel references `AudioEngine` but it doesn't exist. Need:

```csharp
public class AudioEngine
{
    public void Initialize() { }
    public void Play(SoundEvent evt) { }
}

public enum SoundEvent
{
    MissileLaunch, MissileImpact, MissileMiss,
    NewContact, LockOnWarning, AlertKlaxon,
    RadioSquelchOpen, RadioSquelchClose,
    ChannelSwitch, SystemOnline, SystemOffline,
    FlashMessageAlert
}
```

Can use NAudio or just placeholder beeps for v1.0.

---

### 7. **AI Integration** - REFERENCED BUT INCOMPLETE
**Priority: MEDIUM (can ship without)**

MainViewModel tries to use:
- `AgentOrchestrator` ✅ (exists in AI/)
- `AIModelClient` ✅ (exists)
- `ToolRegistry` ✅ (exists)

But missing:
- ❌ Tool implementations (28 tools referenced in plan)
- ❌ Prompt templates
- ❌ Agent classes (EnemyCommanderAgent, AlliedHQAgent, etc.)

**Can ship v1.0 without AI** - just disable it and use scripted behaviors.

---

### 8. **Progression Systems** - REFERENCED BUT MISSING
**Priority: LOW (v1.1)**

MainViewModel references but don't exist:
- `CrewRoster` / `Soldier` classes
- `BudgetSystem`
- `PlayerProfile` / rank progression
- `EventEngine` / dynamic events
- `GroupTacticManager`
- `MissionRewardCalculator`

**These are DEADSKY addendum features** - not needed for core gameplay.

---

### 9. **Missing Core Files**

Referenced but don't exist:
- ❌ `MissileKinematics.cs` - **Wait, this IS in FlightModel.cs!** ✅
- ❌ `IncomingMissile.cs` - **Wait, this IS in Missile.cs!** ✅
- ❌ `SAMBattery.cs` - **EXISTS!** ✅

Actually you have more than I thought! Good.

---

## 🎯 MINIMUM VIABLE PRODUCT (MVP) Checklist

To ship a **playable v1.0**, you need:

### Phase 1: Core Loop (1-2 days)
- [ ] Create `SimulationEngine.cs` with main loop
- [ ] Create `SimulationSnapshot.cs`
- [ ] Add player command methods to SimulationEngine
- [ ] Wire up simulation thread in MainViewModel
- [ ] Test: Can start sim, entities move, radar sweeps

### Phase 2: Basic Scenario (1 day)
- [ ] Create `ScenarioDefinition.cs` (data model)
- [ ] Create `ScenarioLoader.cs` (load from JSON)
- [ ] Create `ScenarioManager.cs` (spawn waves, check victory)
- [ ] Create 1 tutorial scenario JSON file
- [ ] Test: Can load scenario, spawn aircraft, detect win/loss

### Phase 3: UI Completion (2-3 days)
- [ ] Build `WeaponsPanel.xaml` - launchers + fire buttons
- [ ] Build `CommsPanel.xaml` - messages + input
- [ ] Build `ThreatBoard.xaml` - track table
- [ ] Build `SystemStatus.xaml` - battery info
- [ ] Layout `MainWindow.xaml` with all panels
- [ ] Test: Can see everything, click buttons

### Phase 4: Player Actions (1 day)
- [ ] Wire up Designate button → SimulationEngine
- [ ] Wire up Fire button → SimulationEngine
- [ ] Wire up Radar mode buttons
- [ ] Wire up Comms send button
- [ ] Test: Can actually play the game!

### Phase 5: Audio Placeholder (0.5 days)
- [ ] Create `AudioEngine.cs` stub
- [ ] Add `SoundEvent` enum
- [ ] Play system beeps for events
- [ ] Test: Hear something when stuff happens

### Phase 6: Polish (1 day)
- [ ] Add mission briefing screen
- [ ] Add mission end screen (victory/defeat)
- [ ] Add pause menu
- [ ] Fix any crashes
- [ ] Test: Play full mission start to finish

---

## 📊 Estimated Time to Ship

**With focused work:**
- **MVP (playable):** 6-8 days
- **With basic AI:** +3-4 days
- **With progression systems:** +5-7 days
- **Full DEADSKY features:** +10-15 days

**Total for complete v1.0:** ~3-4 weeks

---

## 🚀 Recommended Build Order

### Week 1: Make it Run
1. SimulationEngine + Snapshot
2. Basic scenario system
3. Player commands
4. Test with console output (no UI)

### Week 2: Make it Playable
1. Complete UI panels
2. Wire up all buttons
3. Audio stubs
4. End-to-end playthrough

### Week 3: Make it Fun
1. Add 3-5 scenarios
2. Basic AI (enemy behaviors)
3. Polish UI
4. Playtesting

### Week 4: Make it Shippable
1. Progression systems
2. Shop/upgrades
3. Campaign structure
4. Bug fixes

---

## 🔥 Critical Path (Must Have)

1. **SimulationEngine** - Nothing works without this
2. **SimulationSnapshot** - UI can't display anything
3. **Player commands** - Can't interact
4. **Scenario system** - No missions to play
5. **UI panels** - Can't see what's happening
6. **Basic audio** - Feels dead without it

Everything else is **nice to have** for v1.0.

---

## 💡 What You Can Skip for v1.0

- ❌ Crew morale system
- ❌ Shop/economy
- ❌ Rank progression
- ❌ Dynamic events
- ❌ Enemy commander AI (use scripted)
- ❌ Terrain map (radar-only is fine)
- ❌ Multiple missile types (just SA-11)
- ❌ Upgrades
- ❌ Campaign persistence

**Ship the core loop first. Add depth later.**

---

## 🎮 What Makes It "Shippable"

A player should be able to:
1. ✅ Start a mission
2. ✅ See radar contacts appear
3. ✅ Select a track
4. ✅ Designate target
5. ✅ Fire missile
6. ✅ Watch missile fly
7. ✅ See hit/miss result
8. ✅ Receive radio messages
9. ✅ Win or lose the mission
10. ✅ Play again

**That's it. That's v1.0.**

Everything else is v1.1+.

---

## 📝 Final Verdict

**You're 60% done with the hard part (simulation).**
**You're 20% done with the easy part (UI/glue).**

**The good news:** Your architecture is solid. No major refactoring needed.

**The bad news:** You have ~2 weeks of focused work to make it playable.

**The great news:** Once the core loop works, adding content (scenarios, aircraft types, etc.) is trivial.

---

## 🎯 Next Steps

1. Read this document
2. Decide: MVP only, or full v1.0?
3. Start with SimulationEngine.cs
4. Work through the checklist
5. Ship it!

You're closer than you think. The foundation is excellent.
