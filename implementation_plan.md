# Implementation Plan: Phase 3 (Airbase Entities)

## Background

With AWACS telemetry and support infrastructure (Phase 2) completely integrated and verified, the DEADSKY simulator needs realistic launch delays and resource constraints for friendly aircraft. 

Currently, FriendlySupportDirector spawns aircraft instantly when the cooldown expires. Phase 3 introduces **Airbases** as physical entities on the map. Airbases will have finite pools of fighters, fuel, and missiles. When AlliedHQ requests a launch, the AirbaseManager will queue the request, apply a realistic scramble delay (2-8 minutes), consume resources, and physically spawn the aircraft at the airbase coordinates.

## User Review Required

> [!IMPORTANT]
> This phase fundamentally changes how aircraft are spawned. Previously, `FriendlySupportDirector` spawned them instantly around a patrol zone. Now, aircraft must depart from a specific `Airbase` coordinate and fly to the patrol zone. 

Is this the behavior you want, or do you want to keep the abstraction of off-map assets (i.e. aircraft "warp in" at the edge of the scenario)? My proposed plan converts airbases into physical map entities to increase realism and vulnerability (e.g. enemy can target the airbase to stop launches). 

---

## Proposed Changes

### 1. Airbase Entity
#### [NEW] [Airbase.cs](file:///c:/DarkOrbitWeaver/DEADSKY/src/DEADSKY.Core/Entities/Airbase.cs)
Create a new `Airbase` class inheriting from `Entity`:
- `EntityType.RadarStation` (or a newly minted `EntityType.Airbase` if we expand the enum)
- Properties: `FightersAvailable` (int), `FuelAvailableKg` (double), `AamAvailable` (int)
- Spawns at a fixed coordinate and cannot move (`SpeedMps = 0`).

### 2. Airbase Manager
#### [NEW] [AirbaseManager.cs](file:///c:/DarkOrbitWeaver/DEADSKY/src/DEADSKY.Core/Campaign/AirbaseManager.cs)
Create an `AirbaseManager` class to handle scramble queues and resource tracking:
- **`RequestScramble(airbaseId, aircraftType, count, priority)`**: Deducts resources from the airbase immediately. If resources are insufficient, returns a denial. Otherwise, queues the launch.
- **`Tick(deltaTime)`**: Iterates through the launch queue. Scramble delay is typically 120-480 seconds (2-8 mins). When the timer expires, it calls `EntityManager.Add` to spawn the fighters at the airbase coordinates and pushes a CommManager radio call (e.g., *"MAGIC, scrambling 2x Fighter from ALPHA"*).

### 3. AI Tool Integration
#### [MODIFY] [ToolRegistry.cs](file:///c:/DarkOrbitWeaver/DEADSKY/src/DEADSKY.AI/Tools/ToolRegistry.cs)
Register a new tool `request_aircraft_launch`:
- Accepts parameters: `{ "airbaseId": "ALPHA", "count": 2, "mission": "CAP" }`
- Maps to `AirbaseManager.RequestScramble()`
- Returns a structured result to the AI explaining the estimated launch time or reason for denial (e.g., "Insufficient fuel").

#### [MODIFY] [AgentOrchestrator.cs](file:///c:/DarkOrbitWeaver/DEADSKY/src/DEADSKY.AI/Agents/AgentOrchestrator.cs)
- Expose the current state of airbases (fuel, missiles, fighters available) in the simulation snapshot provided to AlliedHQ (ECHO ACTUAL).
- Instruct AlliedHQ in its system prompt to manage these resources over the course of the scenario.

---

## Verification Plan

### Automated Tests
Create `AirbaseManagerTests.cs` to verify:
1. **`RequestScramble_WithResources_QueuesLaunch`**: Verify resources are deducted and scramble is queued.
2. **`RequestScramble_WithoutResources_Fails`**: Verify scramble is denied if fighters/fuel are 0.
3. **`Tick_CompletesScramble_SpawnsEntity`**: Advance simulation time by 5 minutes and verify the fighter entity is injected into `EntityManager`.

### Manual Testing
- Run `dotnet run` on the simulation and observe `request_aircraft_launch` tool calls from the AI.
- Watch console logs for the radio broadcasts indicating scrambles and launches.
