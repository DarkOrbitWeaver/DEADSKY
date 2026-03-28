# Implementation Plan: Real Support Entities

## Overview

This implementation transforms virtual support units into real, visible entities with physics, behavior, and radio communications. The architecture principle: **AI agent (LM) gives high-level orders via existing radio/tool system. AI algorithms handle low-level behavior (flight paths, maneuvers, tactics, teamwork).**

The existing system already has:
- **AgentOrchestrator**: Manages AI agents (EnemyCommander, AlliedHQ, Intel, CrewPersonality)
- **ToolRegistry**: Provides tools for AI agents to interact with simulation (get_radar_contacts, send_radio_message, set_aircraft_behavior, spawn_aircraft, request_support_action, etc.)
- **FriendlySupportDirector**: Manages friendly support availability and coordination
- **EntityManager**: Spawns and manages all entities
- **CommManager**: Routes radio communications

**DO NOT create command parsers or manual tool call extraction.** The AI agent uses tools directly through the existing ToolRegistry.

## Completed Tasks (Keep)

- [x] 1.1 Extend Aircraft class to support friendly fighter behavior ✓
- [x] 1.2 Implement CAP patrol behavior in Aircraft class ✓
- [x] 1.3 Implement friendly fighter weapon engagement logic ✓
- [x] 1.4 Add friendly fighter spawning to EntityManager ✓
- [x] 2.1 Modify FriendlySupportDirector to spawn real CAP entities ✓
- [x] 2.2 Implement CAP fighter lifecycle management ✓
- [x] 3.1 Create CAP fighter radio message generation ✓
- [x] 3.2 Implement CAP fighter engagement radio calls ✓

## Remaining Tasks

### Phase 1: CAP Fighter Integration with AI Agent

- [x] 4.1 Add CAP intercept tool to ToolRegistry
  - Create `task_cap_intercept` tool in ToolRegistry
  - Parameters: cap_callsign (required), target_track_id (required)
  - Tool validates CAP fighter exists and is active
  - Tool validates target track exists
  - Tool calls Aircraft.SetInterceptTarget(trackId)
  - Tool returns success/failure with reason
  - _Requirements: 3.1, 3.2, 3.3_
  - _Note: AI agent calls this tool directly, no parser needed_

- [x] 4.2 Implement CAP intercept behavior execution
  - Enhance Aircraft.SetInterceptTarget to send Wilco acknowledgment
  - Implement SetInterceptCourse to calculate lead pursuit
  - Update ExecuteOrbit to handle intercept mode
  - Implement ResumePatrol when target destroyed
  - _Requirements: 3.3, 3.4, 3.5_

- [x] 4.3 Add CAP weapon engagement automation
  - Implement autonomous target detection in ExecuteCapPatrol
  - Call SelectWeaponForTarget when in range
  - Call ExpendWeapon and send Fox-3 report on launch
  - Send Splash report on target destruction
  - Send Winchester/Bingo reports when appropriate
  - _Requirements: 1.6, 1.9, 2.3, 2.4, 2.5, 2.6_

- [x] 4.4 Wire CAP fighter to CommManager
  - Set CommManager reference when spawning CAP fighter
  - Ensure all radio methods work correctly
  - Test on-station, tally, fox-3, splash, bingo, winchester reports
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_

### Phase 2: AWACS Entity

- [x] 5.1 Create AWACSAircraft class extending Aircraft
  - Add racetrack orbit pattern properties
  - Add enhanced radar coverage properties
  - Set E-3 Sentry physical characteristics
  - _Requirements: 4.1, 4.8_

- [x] 5.2 Implement AWACS orbit behavior
  - Implement racetrack pattern flight logic
  - Maintain 30,000ft altitude and 80nm range
  - _Requirements: 4.2_

- [ ] 5.3 Integrate AWACS with FriendlySupportDirector
  - Spawn AWACS entity when support becomes available
  - Track AWACS entity ID in FriendlySupportPackage
  - Update availability based on AWACS state
  - _Requirements: 4.1, 4.7, 12.1, 12.2_

- [ ] 5.4 Extend RadarSystem for AWACS data fusion
  - Detect AWACS entity in RadarSystem.Update
  - Apply 30% range multiplier when AWACS active
  - Improve track classification accuracy
  - _Requirements: 4.4, 4.5, 4.7_

- [ ] 5.5 Implement AWACS radio communications
  - Create Picture message generation tool
  - Use Bullseye reference format
  - Implement 30-90 second interval timing
  - Add Defensive status broadcast
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

### Phase 3: Airbase Entities

- [ ] 6.1 Create Airbase class extending Entity
  - Add aircraft inventory tracking
  - Add fuel reserves property
  - Add munitions inventory property
  - Add launch queue
  - _Requirements: 6.1, 6.2, 6.10_

- [ ] 6.2 Implement aircraft launch delay system
  - Calculate launch delay (2-8 minutes)
  - Implement launch queue processing
  - Spawn aircraft when timer expires
  - _Requirements: 6.5, 6.6_

- [ ] 6.3 Create AirbaseManager class
  - Initialize airbases from scenario
  - Track all active airbase entities
  - Route aircraft requests to appropriate airbase
  - _Requirements: 6.1, 14.4_

- [ ] 6.4 Add aircraft request tool to ToolRegistry
  - Create `request_aircraft_launch` tool
  - Parameters: airbase_id, aircraft_type, count, sector_id
  - Validate airbase resources
  - Queue launch request
  - _Requirements: 6.3, 6.9_

- [ ] 6.5 Implement airbase radio communications
  - Generate acknowledgment with ETA
  - Report launch complete
  - Report unable with reason
  - _Requirements: 7.1, 7.2, 7.3, 7.4_

### Phase 4: Friendly SAM Battery Entities

- [ ] 7.1 Extend SAMBattery for multiple friendly batteries
  - Add battery coordination properties
  - Add engagement intent broadcasting
  - Ensure player battery code unchanged
  - _Requirements: 8.1, 8.2_

- [ ] 7.2 Create BatteryCoordinator class
  - Initialize friendly batteries from scenario
  - Track all friendly battery entities
  - Route engagement orders
  - _Requirements: 8.1, 13.1_

- [ ] 7.3 Implement engagement intent broadcasting
  - Broadcast when player battery launches
  - Broadcast when friendly battery launches
  - Include target track ID and battery ID
  - _Requirements: 13.1, 13.2_

- [ ] 7.4 Implement target deprioritization logic
  - Mark targets as engaged
  - Deprioritize engaged targets
  - Clear engaged status on miss
  - _Requirements: 13.2, 13.3, 13.4, 13.5_

- [ ] 7.5 Add battery engagement tool to ToolRegistry
  - Create `task_battery_engage` tool
  - Parameters: battery_callsign, target_track_id
  - Validate battery and target
  - Execute missile launch
  - _Requirements: 8.5, 8.6_

- [ ] 7.6 Implement friendly battery radio communications
  - Generate acknowledgment on engagement order
  - Report missile launch with target
  - Report Splash on destruction
  - Report unable with reason
  - _Requirements: 9.1, 9.2, 9.3, 9.4_

- [ ] 7.7 Implement battery vulnerability to SEAD
  - Detect SEAD threats
  - Apply damage when attacked
  - Broadcast damage report
  - _Requirements: 8.9, 8.10, 9.5, 9.6_

### Phase 5: Radar Display Integration

- [ ] 8.1 Update RadarSystem to detect all friendly entities
  - Detect CAP fighters
  - Detect AWACS
  - Detect friendly batteries
  - Apply friendly IFF
  - _Requirements: 11.1, 11.2_

- [ ] 8.2 Enhance radar display rendering
  - Render CAP fighters with F-16 icon (blue)
  - Render AWACS with E-3 icon (blue)
  - Render friendly batteries with SAM icon (blue)
  - Render friendly missiles (blue trail)
  - _Requirements: 11.3, 11.5, 11.6_

- [ ] 8.3 Implement friendly entity selection
  - Add click selection for friendly entities
  - Display entity details panel
  - Show real-time updates
  - _Requirements: 11.4_

### Phase 6: Entity Lifecycle Management

- [ ] 9.1 Add entity state transition tracking
  - Implement state enum (Spawning, Active, Returning, Despawned, Destroyed)
  - Track state transitions with timestamps
  - Raise events on state changes
  - _Requirements: 10.7_

- [ ] 9.2 Implement entity cleanup and despawning
  - Despawn entities that RTB
  - Remove destroyed entities after animation
  - Enforce maximum entity count limits
  - _Requirements: 10.2, 10.5, 10.6_

- [ ] 9.3 Update SimulationEngine integration
  - Call Update on all friendly entities
  - Apply physics calculations
  - _Requirements: 10.3, 10.4_

- [ ] 9.4 Add friendly entity initialization to scenario loading
  - Spawn initial friendly entities
  - Initialize CAP if scenario includes it
  - Initialize AWACS if scenario includes it
  - Initialize friendly batteries from scenario
  - _Requirements: 6.1, 8.1_

### Phase 7: Campaign Persistence

- [ ] 10.1 Add friendly entity state to campaign save
  - Save CAP availability and status
  - Save AWACS availability and status
  - Save airbase inventory and damage
  - Save friendly battery status
  - _Requirements: 14.1, 14.4, 14.5_

- [ ] 10.2 Implement state restoration on campaign load
  - Restore CAP availability
  - Restore airbase inventory
  - Restore friendly battery capability
  - Apply damage states
  - _Requirements: 14.2, 14.3, 14.5, 14.6_

### Phase 8: Final Integration

- [ ] 11.1 Wire all friendly entity systems together
  - Connect EntityManager with FriendlySupportDirector
  - Connect AirbaseManager with FriendlySupportDirector
  - Connect BatteryCoordinator with WeaponsSystem
  - Connect all radio communications to CommManager
  - Ensure all entity lifecycle events propagate
  - _Requirements: 1.1, 4.1, 6.1, 8.1, 12.1_

- [ ] 11.2 Update SimulationSnapshot to include friendly entity data
  - Add friendly aircraft list
  - Add AWACS entity
  - Add friendly battery list
  - Add airbase status
  - _Requirements: 11.1, 11.2_

- [ ] 11.3 Final checkpoint - Ensure all tests pass
  - Run all unit tests
  - Run integration tests
  - Verify all requirements met

## Architecture Notes

### How AI Agent Gives Orders

The AI agent (AlliedHQ) uses tools from ToolRegistry:
1. Player sends radio message: "VIPER 1-1 intercept TRACK-1234"
2. AlliedHQ agent receives message via RespondToPlayerMessage
3. Agent calls `task_cap_intercept` tool with parameters: {cap_callsign: "VIPER 1-1", target_track_id: "TRACK-1234"}
4. Tool validates and executes: Aircraft.SetInterceptTarget("TRACK-1234")
5. CAP fighter sends Wilco acknowledgment via radio
6. CAP fighter autonomously flies intercept course and engages

### No Command Parsers Needed

The existing architecture already handles:
- Radio message routing (CommManager)
- AI agent tool calls (ToolRegistry)
- Entity behavior (Aircraft behavior state machine)
- Radio communications (CommManager + RadioRules)

We simply add new tools to ToolRegistry for:
- `task_cap_intercept`: Task CAP fighter to intercept target
- `request_aircraft_launch`: Request aircraft from airbase
- `task_battery_engage`: Task friendly battery to engage target

The AI agent decides when to call these tools based on player messages and tactical situation.

## Testing Strategy

Each task should include:
1. Unit tests for new classes/methods
2. Integration tests for system interactions
3. Manual testing in-game to verify behavior

Focus on:
- Entity spawning and lifecycle
- Behavior state machines
- Radio communications
- AI agent tool calls
- Radar display rendering
