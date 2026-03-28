# DEADSKY Task Tracker

## Status Summary
- Build: ✅ Fixed (0 errors, warnings resolved)
- Tests: ✅ Fixed (316 tests, all passing)
- **Current Status: Phase 2 & 3 Complete, Ready for Phase 4**
- Architecture confirmed: clean, modular ECS with event-driven pub/sub

---

## Phase A: Core Implementation

### Phase 2: AWACS Entity ✅ COMPLETE
- [x] 5.1 AWACSAircraft class created ✓
- [x] 5.2 AWACS racetrack orbit behavior ✓
- [x] 5.3 Integrate AWACS with FriendlySupportDirector
  - [x] Add `SpawnedEntityId` to FriendlySupportPackage (reused for AWACS)
  - [x] Add `SpawnAwacsAircraft()` in FriendlySupportDirector (mirror SpawnCapFighter)
  - [x] Call SpawnAwacs when AWACS support cooldown expires (like CAP)
  - [x] UpdateAwacsEntityState() in Tick() — lifecycle management
  - [x] Tests verified
- [x] 5.4 Extend RadarSystem for AWACS data fusion
  - [x] Detect AWACSAircraft in RadarSystem.Update
  - [x] Apply 30% range multiplier when AWACS active
  - [x] Tests verified
- [x] 5.5 Implement AWACS radio communications ✓

### Phase 3: Airbase Entities ✅ COMPLETE
- [x] 6.1 Airbase class (aircraft inventory, fuel, munitions, launch queue) ✓
- [x] 6.2 Aircraft launch delay system (2-8 min) ✓
- [x] 6.3 AirbaseManager class ✓
- [x] 6.4 `request_aircraft_launch` tool in ToolRegistry ✓
- [x] 6.5 Airbase radio communications ✓
- [x] `get_airbase_status` tool in ToolRegistry ✓

### Phase 4: Friendly SAM Battery Entities ⏳ NEXT PRIORITY
- [ ] 7.1 SAM Battery Coordination System
- [ ] 7.2 BatteryCoordinator class
- [ ] 7.3 Engagement coordination tools
- [ ] 7.4 Battery-to-battery radio communications
- [ ] 7.5 SEAD vulnerability (ARM threats)
- [ ] 7.6 Multiple battery network management
- [ ] 7.7 Cooperative engagement protocols

### Phase 5: Radar Display Integration
- [ ] 8.1 Detect all friendly entities on radar
- [ ] 8.2 Enhanced display with selection
- [ ] 8.3 Friendly entity identification

### Phase 6: Entity Lifecycle Management
- [ ] 9.1 State transitions
- [ ] 9.2 Cleanup protocols
- [ ] 9.3 SimulationEngine integration
- [ ] 9.4 Persistence hooks

### Phase 7: Campaign Persistence
- [ ] 10.1 Save friendly entity state
- [ ] 10.2 Restore campaign state

### Phase 8: Final Integration
- [ ] 11.1 Wire all systems
- [ ] 11.2 Update snapshot
- [ ] 11.3 Verify all tests pass

---

## Phase B: Improvements (After Phase A is complete)

### B1: More AI Tools
- [ ] `get_awacs_picture` — AWACS-fused air picture for AlliedHQ
- [ ] `task_awacs_sector_clear` — task AWACS to report sector status
- [ ] `request_close_air_support` — request strike on ground coordinates
- [ ] `task_fighter_escort` — escort a specific flight
- [ ] `get_airbase_status` — query airbase resources
- [ ] `task_battery_suppress` — order nearby battery to suppress a sector

### B2: HQ Command Logic Improvements
- [ ] HQ can deny player requests (ROE, command confidence, resource constraints)
- [ ] HQ issues proactive orders based on threat picture
- [ ] HQ escalates ROE when overwhelming threat detected
- [ ] Chain of command radio: CASTLE → ECHO → ALPHA

### B3: Aircraft AI Algorithm Improvements
- [ ] Improved lead pursuit intercept (full quadratic solver, not simplified)
- [ ] Formation flight for multiple fighters (echelon, trail, wedge)
- [ ] Situational awareness: CAP fighters react to nearby SAM launches
- [ ] Coordinated multi-ship engagements (split headings, time-on-target)
- [ ] AWACS data link: AWACS pushes track data to CAP fighters

### B4: Lore & World Depth
- [ ] Named pilots with callsigns and radio personalities
- [ ] Airbase names, country, theater lore in scenario
- [ ] AWACS callsign history and distinctive voice
- [ ] Intel intercepts from enemy command net  
- [ ] Crew can comment on friendly aircraft radio calls
- [ ] Post-mission debrief narrative

### B5: Gameplay Diversity
- [ ] Multiple simultaneous CAP sectors (north/south split)
- [ ] AWACS can vector fighters proactively
- [ ] Airbase resource economy (missiles are finite, can be depleted)
- [ ] Night/weather scenarios affect AWACS coverage
- [ ] SAR mission triggered when pilot goes down
