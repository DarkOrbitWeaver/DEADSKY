
# OPERATION IRON DOME — Comprehensive Development Plan

## Anti-Air Battery Commander Simulation

---

## TABLE OF CONTENTS

```
 ██████╗ ██████╗ ███████╗██████╗  █████╗ ████████╗██╗ ██████╗ ███╗   ██╗
██╔═══██╗██╔══██╗██╔════╝██╔══██╗██╔══██╗╚══██╔══╝██║██╔═══██╗████╗  ██║
██║   ██║██████╔╝█████╗  ██████╔╝███████║   ██║   ██║██║   ██║██╔██╗ ██║
██║   ██║██╔═══╝ ██╔══╝  ██╔══██╗██╔══██║   ██║   ██║██║   ██║██║╚██╗██║
╚██████╔╝██║     ███████╗██║  ██║██║  ██║   ██║   ██║╚██████╔╝██║ ╚████║
 ╚═════╝ ╚═╝     ╚══════╝╚═╝  ╚═╝╚═╝  ╚═╝   ╚═╝   ╚═╝ ╚═════╝ ╚═╝  ╚═══╝
          ██╗██████╗  ██████╗ ███╗   ██╗    ██████╗  ██████╗ ███╗   ███╗███████╗
          ██║██╔══██╗██╔═══██╗████╗  ██║    ██╔══██╗██╔═══██╗████╗ ████║██╔════╝
          ██║██████╔╝██║   ██║██╔██╗ ██║    ██║  ██║██║   ██║██╔████╔██║█████╗
          ██║██╔══██╗██║   ██║██║╚██╗██║    ██║  ██║██║   ██║██║╚██╔╝██║██╔══╝
          ██║██║  ██║╚██████╔╝██║ ╚████║    ██████╔╝╚██████╔╝██║ ╚═╝ ██║███████╗
          ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚═╝  ╚═══╝    ╚═════╝  ╚═════╝ ╚═╝     ╚═╝╚══════╝
```

---

## 1. TECHNOLOGY STACK DECISION

### 1.1 Framework: **WPF (.NET 8)** + Supporting Libraries

**Why WPF over game engines for THIS project:**

| Requirement | WPF Advantage |
|---|---|
| Split-screen customizable panels | `AvalonDock` — professional docking/layout system (like Visual Studio panels) |
| Radar rendering with glow/scan effects | `SkiaSharp.Views.WPF` — GPU-accelerated custom 2D rendering with shaders |
| Real-time data-driven UI | MVVM data binding — bind radar contacts, missile counts, messages directly |
| Multiple chat channels, scrolling logs | Native `ListView`, `RichTextBox` with templated items |
| Styling as military hardware | `ResourceDictionary` themes — pixel-perfect military CRT aesthetic |
| Audio (radio static, alarms, impacts) | `NAudio` — full audio mixing, effects chain, simultaneous playback |
| HTTP to LM Studio | `HttpClient` built into .NET 8 — async, fast, native |
| Terrain map rendering | SkiaSharp tile rendering — we render our own map tiles (no Google dependency) |
| Performance | 60fps rendering loop via `CompositionTarget.Rendering` |

**This is NOT a generic game — it's a real-time command console. WPF is literally built for this.**

### 1.2 Full Dependency Stack

```xml
<!-- NuGet Packages -->
<PackageReference Include="SkiaSharp.Views.WPF" Version="2.*" />
<PackageReference Include="NAudio" Version="2.*" />
<PackageReference Include="AvalonDock" Version="4.*" />
<PackageReference Include="System.Text.Json" Version="8.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="Mapsui" Version="4.*" />           <!-- Terrain map rendering -->
<PackageReference Include="Mapsui.Wpf" Version="4.*" />
```

### 1.3 AI Model Selection: **Hermes-3-Llama-3.1-8B (GGUF Q4_K_M)**

**Why this specific model:**

| Criteria | Hermes-3-8B |
|---|---|
| Tool/Function calling | **Specifically trained** for it — best in class at 8B |
| Structured JSON output | Native support, LM Studio forced output compatible |
| Role-play quality | Excellent — trained on diverse personas |
| VRAM usage (Q4_K_M) | ~5.5 GB — leaves headroom |
| Speed | ~30-50 tok/s on modern GPU — fast enough for real-time |
| LM Studio compatibility | First-class GGUF support |
| Agentic behavior | Trained on multi-turn tool-use chains |

**Download:** `NousResearch/Hermes-3-Llama-3.1-8B-GGUF` (Q4_K_M variant)

**Secondary lightweight model (for fast radio chatter/simple decisions):**
`Qwen2.5-3B-Instruct-GGUF` (Q4_K_M, ~2GB) — loaded simultaneously in LM Studio for parallel inference on simple tasks.

---

## 2. ARCHITECTURE OVERVIEW

```
┌─────────────────────────────────────────────────────────────────────┐
│                        GAME APPLICATION (WPF)                       │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │  RADAR VIEW  │  │ TERRAIN MAP  │  │   COMMS/UI   │  ◄── Panels │
│  │  (SkiaSharp) │  │   (Mapsui)   │  │   (WPF)      │     (Dock)  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘             │
│         │                 │                  │                      │
│  ┌──────▼─────────────────▼──────────────────▼───────┐             │
│  │              SIMULATION ENGINE (Core)              │             │
│  │  ┌─────────┐ ┌──────────┐ ┌─────────┐ ┌────────┐ │             │
│  │  │ Entity  │ │ Physics  │ │ Weapons │ │ Detect │ │             │
│  │  │ Manager │ │ Engine   │ │ System  │ │ System │ │             │
│  │  └─────────┘ └──────────┘ └─────────┘ └────────┘ │             │
│  │  ┌─────────┐ ┌──────────┐ ┌─────────┐ ┌────────┐ │             │
│  │  │ Event   │ │ Scenario │ │  Comms   │ │ Audio  │ │             │
│  │  │ System  │ │ Manager  │ │ Manager  │ │ Engine │ │             │
│  │  └─────────┘ └──────────┘ └─────────┘ └────────┘ │             │
│  └───────────────────────┬───────────────────────────┘             │
│                          │                                          │
│  ┌───────────────────────▼───────────────────────────┐             │
│  │              AI INTEGRATION LAYER                  │             │
│  │  ┌──────────┐ ┌──────────┐ ┌───────────────────┐ │             │
│  │  │ API      │ │ Tool     │ │ Prompt            │ │             │
│  │  │ Client   │ │ Registry │ │ Template Engine   │ │             │
│  │  └──────────┘ └──────────┘ └───────────────────┘ │             │
│  │  ┌──────────┐ ┌──────────┐ ┌───────────────────┐ │             │
│  │  │ Context  │ │ Response │ │ Conversation      │ │             │
│  │  │ Builder  │ │ Parser   │ │ State Manager     │ │             │
│  │  └──────────┘ └──────────┘ └───────────────────┘ │             │
│  └───────────────────────┬───────────────────────────┘             │
│                          │                                          │
└──────────────────────────┼──────────────────────────────────────────┘
                           │ HTTP (localhost:1234)
                           ▼
              ┌────────────────────────┐
              │     LM STUDIO API      │
              │  ┌──────────────────┐  │
              │  │ Hermes-3-8B      │  │  (Primary: decisions, behavior)
              │  │ (Main Model)     │  │
              │  └──────────────────┘  │
              │  ┌──────────────────┐  │
              │  │ Qwen2.5-3B      │  │  (Secondary: fast chatter)
              │  │ (Fast Model)    │  │
              │  └──────────────────┘  │
              └────────────────────────┘
```

---

## 3. PROJECT STRUCTURE

```
IronDome/
├── IronDome.sln
├── src/
│   ├── IronDome.Core/                          # Simulation engine (no UI dependency)
│   │   ├── Entities/
│   │   │   ├── Entity.cs                       # Base entity (position, velocity, heading, altitude)
│   │   │   ├── Aircraft.cs                     # Enemy/friendly aircraft
│   │   │   ├── Missile.cs                      # SAM missiles in flight
│   │   │   ├── CruiseMissile.cs                # Incoming cruise missiles
│   │   │   ├── Drone.cs                        # UAVs
│   │   │   ├── SAMBattery.cs                   # Player's battery + other AD assets
│   │   │   ├── RadarStation.cs                 # Early warning radars
│   │   │   └── EntityManager.cs                # Create, track, destroy entities
│   │   ├── Physics/
│   │   │   ├── FlightModel.cs                  # Aircraft movement, turn rates, speed limits
│   │   │   ├── MissileKinematics.cs            # Missile flight physics, fuel, guidance
│   │   │   ├── InterceptCalculator.cs          # Intercept geometry, Pk calculation
│   │   │   ├── BallisticTrajectory.cs           # Trajectory computation
│   │   │   └── CoordinateSystem.cs             # Lat/Lon ↔ local grid ↔ screen conversion
│   │   ├── Radar/
│   │   │   ├── RadarModel.cs                   # Radar characteristics (range, PRF, beam width)
│   │   │   ├── DetectionEngine.cs              # RCS-based detection probability
│   │   │   ├── TrackFile.cs                    # Individual track (ID, position history, classification)
│   │   │   ├── TrackManager.cs                 # Automatic track initiation, correlation, dropping
│   │   │   ├── IFFSystem.cs                    # Identification Friend/Foe
│   │   │   ├── ECMEffects.cs                   # Jamming, chaff, noise simulation
│   │   │   └── RadarModes.cs                   # Search, TWS, STT modes
│   │   ├── Weapons/
│   │   │   ├── WeaponsSystem.cs                # Missile inventory, readiness states
│   │   │   ├── Launcher.cs                     # Individual launcher (loaded missile, reload timer)
│   │   │   ├── EngagementManager.cs            # Engagement authorization, shoot-look-shoot
│   │   │   ├── MissileTypes.cs                 # Short/medium/long range missile specs
│   │   │   ├── GuidanceModes.cs                # SARH, active, command guidance
│   │   │   └── KillAssessment.cs               # Post-intercept evaluation
│   │   ├── Comms/
│   │   │   ├── RadioChannel.cs                 # Channel definition (freq, encryption, who's on it)
│   │   │   ├── RadioMessage.cs                 # Message model (sender, priority, content, timestamp)
│   │   │   ├── CommManager.cs                  # Route messages, queue, deliver
│   │   │   ├── BrevityCodes.cs                 # NATO brevity terms dictionary
│   │   │   └── ChainOfCommand.cs               # Command hierarchy, who can order what
│   │   ├── Scenario/
│   │   │   ├── ScenarioDefinition.cs           # Mission parameters, enemy OOB, timelines
│   │   │   ├── ScenarioLoader.cs               # Load from JSON scenario files
│   │   │   ├── WaveSpawner.cs                  # Timed enemy wave generation
│   │   │   ├── DynamicEvents.cs                # Random events (intel updates, equipment failure)
│   │   │   └── VictoryConditions.cs            # Win/loss evaluation
│   │   ├── Simulation/
│   │   │   ├── SimulationClock.cs              # Game time, time acceleration
│   │   │   ├── SimulationEngine.cs             # Main update loop, tick all systems
│   │   │   └── EventBus.cs                     # Pub/sub event system
│   │   └── Data/
│   │       ├── MilitaryDatabase.cs             # Aircraft performance data, missile specs
│   │       └── GeographyData.cs                # Terrain elevation, named locations
│   │
│   ├── IronDome.AI/                            # AI integration layer
│   │   ├── Client/
│   │   │   ├── LMStudioClient.cs              # HTTP client for LM Studio API
│   │   │   ├── ModelConfig.cs                  # Model endpoints, parameters
│   │   │   └── RequestQueue.cs                 # Rate limiting, priority queue for API calls
│   │   ├── Tools/
│   │   │   ├── IToolHandler.cs                 # Tool interface
│   │   │   ├── ToolRegistry.cs                 # Register all available tools
│   │   │   ├── ToolExecutor.cs                 # Execute tool calls, return results
│   │   │   │── Tools (individual implementations):
│   │   │   ├── GetRadarContactsTool.cs
│   │   │   ├── GetContactDetailsTool.cs
│   │   │   ├── GetBatteryStatusTool.cs
│   │   │   ├── GetWeatherTool.cs
│   │   │   ├── SendRadioMessageTool.cs
│   │   │   ├── UpdateEnemyBehaviorTool.cs
│   │   │   ├── RequestIntelTool.cs
│   │   │   ├── SetAlertLevelTool.cs
│   │   │   ├── GetAlliedPositionsTool.cs
│   │   │   ├── ReportEngagementTool.cs
│   │   │   ├── RequestReinforcementTool.cs
│   │   │   ├── UpdateROETool.cs
│   │   │   ├── CalculateInterceptTool.cs
│   │   │   ├── SpawnAircraftTool.cs
│   │   │   ├── ChangeFlightPathTool.cs
│   │   │   ├── ActivateECMTool.cs
│   │   │   ├── ReportCasualtiesTool.cs
│   │   │   ├── GetThreatAssessmentTool.cs
│   │   │   ├── OrderEvacuationTool.cs
│   │   │   └── LogEventTool.cs
│   │   ├── Prompts/
│   │   │   ├── PromptTemplateEngine.cs         # Mustache-style template rendering
│   │   │   ├── SystemPrompts/
│   │   │   │   ├── enemy_flight_leader.txt     # Enemy AI persona
│   │   │   │   ├── allied_commander.txt        # Higher HQ persona
│   │   │   │   ├── allied_pilot.txt            # Friendly pilot persona
│   │   │   │   ├── radar_operator.txt          # Subordinate radar operator
│   │   │   │   ├── intelligence_officer.txt    # Intel updates persona
│   │   │   │   ├── enemy_comms_intercept.txt   # Intercepted enemy radio
│   │   │   │   └── battle_narrator.txt         # Situation reports
│   │   │   └── ScenarioPrompts/
│   │   │       ├── incoming_strike.txt
│   │   │       ├── evasive_maneuver.txt
│   │   │       ├── post_engagement.txt
│   │   │       ├── ecm_encounter.txt
│   │   │       ├── friendly_fire_risk.txt
│   │   │       └── mass_raid.txt
│   │   ├── Agents/
│   │   │   ├── EnemyCommanderAgent.cs          # Controls enemy force decisions
│   │   │   ├── AlliedHQAgent.cs                # Higher command responses
│   │   │   ├── AlliedPilotAgent.cs             # Friendly pilot comms
│   │   │   ├── IntelligenceAgent.cs            # Intel reports
│   │   │   └── AgentOrchestrator.cs            # Manage all AI agents, schedule calls
│   │   └── Context/
│   │       ├── GameStateSerializer.cs          # Serialize game state for AI context
│   │       ├── ConversationHistory.cs          # Per-agent conversation memory
│   │       └── ContextWindowManager.cs         # Manage token limits
│   │
│   ├── IronDome.Audio/                         # Audio engine
│   │   ├── AudioEngine.cs                      # Master audio management
│   │   ├── RadioAudioProcessor.cs              # Radio static, squelch, distortion effects
│   │   ├── AmbientSoundManager.cs              # Background hum, electronics
│   │   ├── AlertSounds.cs                      # Klaxon, warning tones, lock-on beep
│   │   ├── SoundBank.cs                        # Preloaded sound effects
│   │   └── Assets/
│   │       ├── radio_static.wav
│   │       ├── radio_squelch_open.wav
│   │       ├── radio_squelch_close.wav
│   │       ├── radar_sweep.wav
│   │       ├── missile_launch.wav
│   │       ├── explosion_distant.wav
│   │       ├── lock_on_tone.wav
│   │       ├── warning_klaxon.wav
│   │       ├── alert_beep.wav
│   │       ├── ambient_electronics.wav
│   │       ├── keystroke.wav
│   │       └── channel_switch.wav
│   │
│   └── IronDome.App/                           # WPF Application
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Themes/
│       │   ├── MilitaryDarkTheme.xaml          # Colors, fonts, brushes
│       │   ├── CRTEffect.xaml                  # Scan line overlay styles
│       │   ├── ButtonStyles.xaml               # Military toggle/push buttons
│       │   ├── ScrollBarStyles.xaml
│       │   └── TextBoxStyles.xaml
│       ├── Views/
│       │   ├── RadarView.xaml                  # Radar panel (hosts SkiaSharp canvas)
│       │   ├── TerrainMapView.xaml             # Map panel (Mapsui or custom)
│       │   ├── CommsView.xaml                  # Communications panel
│       │   ├── WeaponsView.xaml                # Weapons status panel
│       │   ├── ThreatBoardView.xaml            # Threat assessment table
│       │   ├── SystemStatusView.xaml           # Battery systems status
│       │   ├── ContactDetailView.xaml          # Selected contact info
│       │   ├── MissionBriefingView.xaml        # Mission overlay
│       │   ├── AlertOverlay.xaml               # Full-screen warning overlay
│       │   └── MainMenuView.xaml               # "Power Down Console" menu
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   ├── RadarViewModel.cs
│       │   ├── TerrainMapViewModel.cs
│       │   ├── CommsViewModel.cs
│       │   ├── WeaponsViewModel.cs
│       │   ├── ThreatBoardViewModel.cs
│       │   ├── SystemStatusViewModel.cs
│       │   └── ContactDetailViewModel.cs
│       ├── Controls/
│       │   ├── RadarDisplay.cs                 # Custom SkiaSharp radar control
│       │   ├── SweepRenderer.cs                # Radar sweep line rendering
│       │   ├── ContactBlip.cs                  # Radar contact rendering
│       │   ├── RangeRings.cs                   # Concentric range circles
│       │   ├── CompassRose.cs                  # Bearing indicators
│       │   ├── TrackHistoryTrail.cs            # Fading position history
│       │   ├── EngagementZone.cs               # MEZ overlay
│       │   ├── MissileTrackLine.cs             # In-flight missile path
│       │   ├── StatusIndicatorLight.cs         # Green/yellow/red LED indicators
│       │   ├── MilitaryToggleSwitch.cs         # Toggle switch control
│       │   ├── RotaryKnob.cs                   # Rotary selector control
│       │   ├── DigitalReadout.cs               # Seven-segment style numbers
│       │   ├── ChannelSelector.cs              # Radio channel switcher
│       │   └── PriorityMessageBanner.cs        # Scrolling alert banner
│       ├── Converters/
│       │   ├── ThreatLevelToColorConverter.cs
│       │   ├── BearingToStringConverter.cs
│       │   ├── AltitudeFormatter.cs
│       │   └── PriorityToStyleConverter.cs
│       └── Resources/
│           ├── Fonts/
│           │   ├── ShareTechMono-Regular.ttf    # Military monospace font
│           │   └── Rajdhani-Medium.ttf          # HUD-style font
│           ├── Icons/
│           │   ├── aircraft_hostile.svg
│           │   ├── aircraft_friendly.svg
│           │   ├── aircraft_unknown.svg
│           │   ├── missile_sam.svg
│           │   ├── missile_incoming.svg
│           │   ├── drone.svg
│           │   ├── radar_station.svg
│           │   ├── explosion.svg
│           │   ├── ecm_jammer.svg
│           │   └── sam_battery.svg
│           └── Maps/
│               └── terrain_tiles/              # Offline map tile cache
│
├── scenarios/
│   ├── tutorial_single_bogey.json
│   ├── dawn_patrol.json
│   ├── mass_raid.json
│   ├── ecm_escort.json
│   ├── cruise_missile_defense.json
│   └── mixed_threat.json
│
├── docs/
│   ├── BREVITY_CODES.md
│   ├── TOOL_API_REFERENCE.md
│   └── SCENARIO_FORMAT.md
│
└── tests/
    ├── IronDome.Core.Tests/
    └── IronDome.AI.Tests/
```

---

## 4. DETAILED SYSTEM DESIGNS

### 4.1 RADAR SYSTEM

```
┌─────────────────────────────────────────────────────┐
│                    RADAR DISPLAY                     │
│                                                     │
│              N (000°)                               │
│                 │                                   │
│        NW ╲    │    ╱ NE                           │
│            ╲   │   ╱                               │
│  W ─────────╲──┼──╱─────────── E                   │
│            ╱   │   ╲                               │
│        SW ╱    │    ╲ SE                            │
│                │                                   │
│              S (180°)                               │
│                                                     │
│  ┌─────────────────────────────────────────────┐   │
│  │ Range: 120nm  │ Mode: SEARCH │ IFF: ON      │   │
│  │ Contacts: 7   │ Tracks: 4   │ Threats: 2    │   │
│  └─────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

**Radar rendering pipeline (SkiaSharp, 60fps):**

```csharp
// RADAR RENDERING PIPELINE (simplified overview)
public class RadarDisplay : SKElement
{
    // Rendering layers (back to front):
    // 1. Background (dark green/black with subtle noise)
    // 2. Range rings (concentric circles, labeled)
    // 3. Compass rose / bearing markers
    // 4. Engagement zones (MEZ/KEZ colored arcs)
    // 5. Terrain masking (elevation shadows)
    // 6. Track history trails (fading dots)
    // 7. Contact blips (shape = classification)
    // 8. Track vectors (speed/heading lines)
    // 9. IFF tags (hostile/friendly/unknown labels)
    // 10. Missile tracks (in-flight SAMs)
    // 11. Selected contact highlight
    // 12. Sweep line (rotating, with phosphor fade effect)
    // 13. Cursor / designation marker
    // 14. ECM/jamming strobes (noise sectors)
    // 15. HUD overlay (range readout, bearing, status text)
    // 16. CRT scan line effect (post-process)
    // 17. Vignette / screen curvature effect
}
```

**Contact symbols (NATO standard-inspired):**
```
HOSTILE:     ◆ (filled diamond) — RED
FRIENDLY:    ○ (circle) — BLUE/CYAN  
UNKNOWN:     □ (square) — YELLOW
ASSUMED HOSTILE: ◇ with X — ORANGE
NEUTRAL:     □ (square) — GREEN
MISSILE:     ▲ (small triangle) — varies
JAMMER:      ⚡ (strobe line) — WHITE
```

**Radar modes:**
```
SEARCH    — 360° rotation, detect new contacts
TWS       — Track While Scan, maintains tracks while searching
STT       — Single Target Track, locks radar on one contact (for engagement)
STANDBY   — Radar emitting but not processing (reduce signature)
SILENT    — Radar OFF (passive only, receive data from network)
```

**Detection model:**
```csharp
public class DetectionEngine
{
    // Probability of detection per scan based on:
    // - Target RCS (Radar Cross Section) — fighter ~5m², bomber ~25m², stealth ~0.01m²
    // - Range to target
    // - Radar power and sensitivity
    // - Target altitude (terrain masking below certain angles)
    // - Weather/precipitation attenuation
    // - ECM/jamming power and type
    // - Target aspect angle (head-on vs beam vs tail)
    
    public double CalculatePd(RadarModel radar, Entity target, Weather weather)
    {
        double snr = CalculateSNR(radar, target, weather);
        double pd = 1.0 - Math.Exp(-snr / detectionThreshold);
        return Math.Clamp(pd, 0.0, 0.99);
    }
}
```

### 4.2 WEAPONS SYSTEM

```
┌─────────────────── WEAPONS CONSOLE ────────────────────┐
│                                                         │
│  BATTERY STATUS: ██████████ OPERATIONAL                 │
│  ALERT LEVEL:    WEAPONS HOLD → WEAPONS TIGHT           │
│                                                         │
│  LAUNCHER 1  [SA-11 GADFLY]   ████ READY     ●         │
│  LAUNCHER 2  [SA-11 GADFLY]   ████ READY     ●         │
│  LAUNCHER 3  [SA-11 GADFLY]   ░░░░ RELOAD    ○  2:34   │
│  LAUNCHER 4  [SA-11 GADFLY]   ████ READY     ●         │
│                                                         │
│  RELOAD STOCK: 12 remaining                             │
│  MISSILES FIRED: 4  │  HITS: 2  │  Pk: 50%            │
│                                                         │
│  ┌─────────── ENGAGEMENT ────────────┐                  │
│  │ TARGET: TRK-0023 [HOSTILE]        │                  │
│  │ TYPE: SU-24 FENCER                │                  │
│  │ RNG: 34.2 nm  BRG: 045°  ALT: FL180                │
│  │ SPD: 480 kts  HDG: 225°           │                  │
│  │ Pk(single): 72%                   │                  │
│  │ Pk(salvo-2): 92%                  │                  │
│  │ Time to impact: 00:28             │                  │
│  │                                   │                  │
│  │  [ DESIGNATE ]  [ LAUNCH ]        │                  │
│  │  [ SALVO 2x  ]  [ ABORT  ]       │                  │
│  └───────────────────────────────────┘                  │
└─────────────────────────────────────────────────────────┘
```

**Engagement workflow:**
```
1. DETECT    → Radar detects contact
2. TRACK     → Track file established (automatic or manual)
3. IDENTIFY  → IFF interrogation → classify hostile/friendly/unknown
4. AUTHORIZE → Check ROE, get clearance if needed
5. DESIGNATE → Lock radar (STT mode) on target
6. LAUNCH    → Fire missile(s) — salvo option
7. GUIDE     → Missile in flight — guidance updates
8. ASSESS    → Kill assessment — confirm hit or miss
9. RE-ENGAGE → If miss, decide to fire again
```

**Missile types:**
```csharp
public static class MissileDatabase
{
    public static MissileType ShortRange = new()
    {
        Name = "9M38 (SA-11 Gadfly)",
        MinRange_nm = 2.0,
        MaxRange_nm = 18.0,
        MinAltitude_ft = 50,
        MaxAltitude_ft = 72000,
        MaxSpeed_mach = 3.0,
        MaxG = 24,
        FlightTime_sec = 30,
        GuidanceMode = GuidanceMode.SemiActiveRadar,
        SingleShotPk = 0.70,
        ReloadTime_sec = 180
    };
    
    public static MissileType MediumRange = new()
    {
        Name = "48N6 (SA-10 Grumble)",
        MinRange_nm = 3.0,
        MaxRange_nm = 80.0,
        // ... etc
    };
}
```

**Engagement rules (ROE):**
```
WEAPONS FREE  — Fire at any target not identified as friendly
WEAPONS TIGHT — Fire only at targets identified as hostile  
WEAPONS HOLD  — Fire only in self-defense
```

### 4.3 COMMUNICATIONS SYSTEM

```
┌───────────────── COMMUNICATIONS ─────────────────┐
│                                                   │
│  ┌─── CHANNELS ───────────────────────────────┐  │
│  │ [●] CH1  COMMAND NET     (HQ ECHO)         │  │
│  │ [○] CH2  BATTERY NET     (BATTERY ALPHA)   │  │
│  │ [○] CH3  AIR DEFENSE NET (SECTOR 7)        │  │
│  │ [○] CH4  INTEL NET       (SIGINT)          │  │
│  │ [○] CH5  GUARD           (243.0 MHz)       │  │
│  │ [○] CH6  OPEN FREQ       (121.5 MHz)       │  │
│  └────────────────────────────────────────────┘  │
│                                                   │
│  ┌─── CH1: COMMAND NET ──────────────────────┐   │
│  │                                            │   │
│  │ [14:32:07] ECHO ACTUAL:                   │   │
│  │   "ALPHA, ECHO. Multiple fast movers      │   │
│  │    bearing 045, range 120. Evaluate       │   │
│  │    hostile. Weapons tight. Acknowledge."  │   │
│  │                                            │   │
│  │ [14:32:15] ★ YOU (ALPHA ACTUAL):          │   │
│  │   "ECHO, ALPHA. Copy multiple bogeys      │   │
│  │    045 for 120. Weapons tight. Standing   │   │
│  │    by."                                    │   │
│  │                                            │   │
│  │ [14:33:42] ECHO ACTUAL:                   │   │
│  │   ⚠ FLASH PRIORITY                        │   │
│  │   "ALPHA, ECHO. Weapons TIGHT to          │   │
│  │    WEAPONS FREE. Bogeys now declared      │   │
│  │    hostile. ENGAGE. ENGAGE. ENGAGE."      │   │
│  │                                            │   │
│  └────────────────────────────────────────────┘   │
│                                                   │
│  ┌─── INPUT ─────────────────────────────────┐   │
│  │ > Type message...                    [TX] │   │
│  └────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────┘
```

**Message priority levels:**
```
FLASH      — Red flashing, audio alarm, immediate popup     (e.g., "INCOMING MISSILES")
IMMEDIATE  — Orange highlight, beep notification             (e.g., "Weapons free authorized")
PRIORITY   — Yellow tag, queued notification                 (e.g., "Intel update")
ROUTINE    — Normal display                                  (e.g., "Status report")
```

**Channel participants:**

| Channel | Who's On It | Purpose |
|---------|------------|---------|
| COMMAND NET | Higher HQ (ECHO), You (ALPHA), Other batteries | Orders, ROE changes, strategic |
| BATTERY NET | Your crew, launchers, local radar | Internal coordination |
| AIR DEFENSE NET | All AD units in sector | Shared air picture, coordination |
| INTEL NET | Intelligence officer | Threat warnings, SIGINT intercepts |
| GUARD (243.0) | Everyone (emergency) | Mayday, emergency, warnings |
| OPEN FREQ (121.5) | Anyone — including enemies | Attempt contact with unknowns |

### 4.4 TERRAIN MAP SYSTEM

```
┌──────────────── TERRAIN MAP ────────────────────┐
│                                                   │
│   Map tiles rendered with Mapsui (OpenStreetMap   │
│   offline tiles) OR custom military-style tiles   │
│                                                   │
│   Overlays:                                       │
│   ● Your battery position (fixed, center)         │
│   ● Allied battery positions                      │
│   ● Radar coverage circles                        │
│   ● Missile engagement zones (range rings)        │
│   ● Aircraft tracks with heading arrows           │
│   ● Missile flight paths (animated)               │
│   ● Terrain elevation coloring                    │
│   ● Named waypoints and landmarks                 │
│   ● Threat axes (predicted attack corridors)      │
│   ● Impact points (confirmed kills)               │
│                                                   │
│   Controls:                                       │
│   - Zoom in/out (mouse wheel)                     │
│   - Pan (click drag)                              │
│   - Toggle overlay layers                         │
│   - Click entity for details                      │
│   - Measure distance tool                         │
│   - Draw threat corridors                         │
│                                                   │
└───────────────────────────────────────────────────┘
```

---

## 5. AI INTEGRATION — THE BRAIN

### 5.1 LM Studio API Configuration

```json
// LM Studio server config
{
    "primary_model": {
        "endpoint": "http://localhost:1234/v1/chat/completions",
        "model": "hermes-3-llama-3.1-8b",
        "temperature": 0.7,
        "max_tokens": 512,
        "context_length": 8192
    },
    "fast_model": {
        "endpoint": "http://localhost:1235/v1/chat/completions",
        "model": "qwen2.5-3b-instruct",
        "temperature": 0.8,
        "max_tokens": 256,
        "context_length": 4096
    }
}
```

### 5.2 COMPREHENSIVE TOOL DEFINITIONS

Every tool the AI can call — this is the core API surface:

```csharp
// ═══════════════════════════════════════════════════════
// COMPLETE TOOL REGISTRY — 28 TOOLS
// ═══════════════════════════════════════════════════════

public static class ToolDefinitions
{
    public static List<Tool> GetAllTools() => new()
    {
        // ────────── INTELLIGENCE / AWARENESS ──────────
        
        new Tool("get_radar_contacts", 
            "Get all current radar contacts with position, heading, speed, altitude, classification",
            parameters: new {
                filter = "optional: hostile|friendly|unknown|all",
                max_range_nm = "optional: filter by range"
            }),
        
        new Tool("get_contact_details",
            "Get detailed information about a specific tracked contact",
            parameters: new {
                track_id = "required: track ID (e.g., TRK-0023)"
            }),
        
        new Tool("get_threat_assessment",
            "Analyze current threat picture — attack axes, threat priority ranking",
            parameters: new { }),
        
        new Tool("get_weather_conditions",
            "Current weather: visibility, cloud ceiling, precipitation, wind",
            parameters: new { }),
        
        new Tool("get_terrain_info",
            "Terrain elevation and features at given coordinates or bearing/range",
            parameters: new {
                bearing = "optional: bearing from battery",
                range_nm = "optional: range from battery",
                lat = "optional: latitude",
                lon = "optional: longitude"
            }),

        new Tool("request_intel_update",
            "Request intelligence update for a specific area or situation",
            parameters: new {
                area_description = "required: what area/situation to get intel on",
                priority = "required: flash|immediate|priority|routine"
            }),

        // ────────── BATTERY / WEAPONS STATUS ──────────
        
        new Tool("get_battery_status",
            "Full battery status: launcher states, missile counts, readiness, damage",
            parameters: new { }),
        
        new Tool("get_launcher_status",
            "Status of a specific launcher",
            parameters: new {
                launcher_id = "required: 1-4"
            }),
        
        new Tool("get_ammunition_status",
            "Detailed missile inventory: loaded, in reserve, by type",
            parameters: new { }),

        new Tool("get_engagement_history",
            "History of all engagements: missiles fired, hits, misses, targets",
            parameters: new {
                last_n = "optional: last N engagements"
            }),

        // ────────── COMMUNICATIONS ──────────
        
        new Tool("send_radio_message",
            "Send a radio message on a specific channel",
            parameters: new {
                channel = "required: command|battery|air_defense|intel|guard|open",
                sender_callsign = "required: who is speaking",
                message = "required: the message content",
                priority = "required: flash|immediate|priority|routine",
                recipient_callsign = "optional: directed message"
            }),
        
        new Tool("broadcast_alert",
            "Broadcast an alert/warning to all channels",
            parameters: new {
                alert_type = "required: air_raid|missile_incoming|cease_fire|all_clear|ecm_detected",
                message = "required: alert details"
            }),

        new Tool("send_situation_report",
            "Generate and send a structured SITREP",
            parameters: new {
                channel = "required: which channel",
                include_contacts = "boolean: include contact list",
                include_ammo = "boolean: include ammunition status",
                include_damage = "boolean: include damage report"
            }),

        // ────────── ENEMY BEHAVIOR CONTROL ──────────
        
        new Tool("set_aircraft_behavior",
            "Set behavior/intent for an enemy aircraft (AI decision making)",
            parameters: new {
                track_id = "required: which aircraft",
                behavior = "required: ingress_attack|egress_retreat|orbit_patrol|evasive_maneuver|ecm_standoff|terrain_following|pop_up_attack|feint",
                target_description = "optional: what they're targeting",
                aggressiveness = "optional: 0.0-1.0 how aggressive"
            }),

        new Tool("change_flight_path",
            "Change an aircraft's flight path",
            parameters: new {
                track_id = "required: which aircraft",
                new_heading = "optional: new heading in degrees",
                new_altitude_ft = "optional: new altitude",
                new_speed_kts = "optional: new speed",
                waypoints = "optional: list of lat/lon waypoints"
            }),
        
        new Tool("activate_ecm",
            "Activate electronic countermeasures for an aircraft",
            parameters: new {
                track_id = "required: which aircraft",
                ecm_type = "required: noise_jamming|deceptive_jamming|chaff|flares",
                intensity = "optional: 0.0-1.0"
            }),

        new Tool("spawn_aircraft",
            "Spawn new aircraft into the scenario",
            parameters: new {
                type = "required: fighter|bomber|cruise_missile|drone|ecm_aircraft|transport",
                designation = "required: aircraft type name (e.g., SU-24, F-16, MQ-9)",
                affiliation = "required: hostile|friendly|unknown",
                bearing_from_battery = "required: initial bearing",
                range_nm = "required: initial range",
                altitude_ft = "required: initial altitude",
                heading = "required: initial heading",
                speed_kts = "required: initial speed",
                count = "optional: number of aircraft (formation)"
            }),

        new Tool("despawn_aircraft",
            "Remove aircraft from simulation (left area, landed, etc.)",
            parameters: new {
                track_id = "required: which aircraft",
                reason = "required: left_area|landed|fuel_critical|mission_complete"
            }),

        // ────────── ENGAGEMENT / WEAPONS ──────────
        
        new Tool("calculate_intercept_solution",
            "Calculate missile intercept solution for a target",
            parameters: new {
                track_id = "required: target to intercept",
                launcher_id = "optional: specific launcher to use"
            }),

        new Tool("report_engagement_result",
            "Report the result of a missile engagement",
            parameters: new {
                track_id = "required: which target was engaged",
                result = "required: kill_confirmed|probable_kill|miss|target_damaged",
                details = "optional: additional details"
            }),

        // ────────── GAME STATE / ALERT ──────────
        
        new Tool("set_alert_level",
            "Change the overall alert level",
            parameters: new {
                level = "required: green_peacetime|yellow_elevated|orange_high|red_imminent|black_under_attack"
            }),
        
        new Tool("update_rules_of_engagement",
            "Change current ROE",
            parameters: new {
                roe = "required: weapons_free|weapons_tight|weapons_hold",
                reason = "required: why the change",
                authority = "required: who authorized it"
            }),

        new Tool("log_event",
            "Log a significant event to the mission log",
            parameters: new {
                event_type = "required: engagement|detection|communication|alert|damage|other",
                description = "required: what happened",
                significance = "required: critical|major|minor|info"
            }),

        // ────────── ALLIED FORCES ──────────
        
        new Tool("get_allied_positions",
            "Get positions and status of all allied forces",
            parameters: new {
                type_filter = "optional: sam_battery|fighter|awacs|ground_unit"
            }),
        
        new Tool("request_reinforcement",
            "Request allied reinforcement",
            parameters: new {
                type = "required: fighter_cap|additional_sam|awacs_support|ground_resupply",
                urgency = "required: flash|immediate|priority|routine",
                justification = "required: why needed"
            }),
        
        new Tool("coordinate_engagement",
            "Coordinate an engagement with allied forces",
            parameters: new {
                track_id = "required: target",
                requesting_unit = "required: who wants to engage",
                engagement_type = "required: primary|secondary|backup"
            }),

        new Tool("report_battle_damage",
            "Report damage to own forces",
            parameters: new {
                unit = "required: which unit is damaged",
                damage_type = "required: radar_hit|launcher_destroyed|comms_degraded|personnel_casualty",
                severity = "required: minor|moderate|severe|critical",
                details = "required: description"
            })
    };
}
```

### 5.3 AI AGENT SYSTEM

```
┌─────────────────────────────────────────────────────────┐
│                    AGENT ORCHESTRATOR                     │
│                                                          │
│  Manages multiple AI "personalities" each with their     │
│  own system prompt, conversation history, and behavior   │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ ENEMY COMMANDER AGENT (Primary Model)              │  │
│  │ Runs every: 10-30 seconds (varies by situation)    │  │
│  │ Controls: Enemy aircraft decisions, tactics         │  │
│  │ Tools: set_aircraft_behavior, change_flight_path,  │  │
│  │        activate_ecm, spawn_aircraft, despawn        │  │
│  │ Context: sees enemy perspective, knows own losses   │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ ALLIED HQ AGENT (Primary Model)                    │  │
│  │ Runs: On player message + periodic updates          │  │
│  │ Controls: Orders, ROE changes, reinforcements       │  │
│  │ Tools: send_radio_message, update_roe, set_alert,  │  │
│  │        request_reinforcement, log_event             │  │
│  │ Context: sees full allied picture                    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ ALLIED PILOT AGENTS (Fast Model)                   │  │
│  │ Runs: When relevant events occur                    │  │
│  │ Controls: Friendly pilot radio chatter              │  │
│  │ Tools: send_radio_message                           │  │
│  │ Context: own aircraft status, nearby threats         │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ INTELLIGENCE AGENT (Primary Model)                 │  │
│  │ Runs: Periodic + triggered by new detections        │  │
│  │ Controls: Intel reports, threat analysis             │  │
│  │ Tools: send_radio_message, request_intel,           │  │
│  │        get_threat_assessment, log_event             │  │
│  │ Context: SIGINT data, pattern analysis               │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ BATTERY CREW AGENT (Fast Model)                    │  │
│  │ Runs: Reactive to player actions and events         │  │
│  │ Controls: Crew status reports, acknowledgments      │  │
│  │ Tools: send_radio_message, get_launcher_status      │  │
│  │ Context: battery operations, launcher states         │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  SCHEDULING:                                             │
│  - Enemy agent ticks on timer (adaptive rate)            │
│  - Allied agents react to events (EventBus subscriptions)│
│  - Player messages trigger immediate agent response       │
│  - Queue prevents API overload (max 2 concurrent calls)  │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

### 5.4 EXAMPLE PROMPT TEMPLATES

**Enemy Commander System Prompt:**
```
You are the ENEMY AIR FORCE COMMANDER controlling a strike package against 
an air defense network. You are realistic, tactical, and adaptive.

SITUATION:
- You have {{enemy_aircraft_count}} aircraft under your command
- Current aircraft: {{enemy_aircraft_list}}
- Known enemy air defenses: {{known_ad_positions}}
- Losses so far: {{enemy_losses}}
- Mission objective: {{mission_objective}}

BEHAVIOR RULES:
- You make tactical decisions based on the situation
- If aircraft are being shot at, you react (evasion, ECM, abort)
- You use combined tactics (feints, ECM escorts, multi-axis attacks)
- You don't know the exact position of enemy radar unless your aircraft detect it
- You can lose morale — heavy losses may cause you to abort the mission
- You adapt to enemy tactics over time
- Be unpredictable but realistic

AVAILABLE TOOLS: You MUST use tools to execute your decisions.
Think step by step about the tactical situation, then use tools.

Current game time: {{game_time}}
Time since last decision: {{time_delta}} seconds
```

**Allied HQ System Prompt:**
```
You are ECHO ACTUAL, the Sector Air Defense Commander. You command Battery ALPHA 
(the player) and coordinate the air defense network.

YOUR PERSONALITY:
- Professional military officer
- Calm under pressure but urgent when needed
- Uses proper radio procedure and brevity codes
- Makes decisions based on available intelligence
- Can be overridden by higher authority (CASTLE — National Command)

CHAIN OF COMMAND:
- CASTLE (National Command) → You (ECHO, Sector Commander) → ALPHA (Player's Battery)
- You also coordinate with: BRAVO battery, CHARLIE battery, allied fighter squadron VIPER

CURRENT SITUATION:
{{current_situation_summary}}

ROE: {{current_roe}}
ALERT LEVEL: {{alert_level}}
ALLIED FORCES STATUS: {{allied_status}}

When the player sends you a message, respond in character.
Use radio brevity codes naturally (BOGEY, BANDIT, SPLASH, BRAA, etc.)
Issue orders when the situation demands it.
React to engagement results.
Escalate ROE when appropriate.
Request/provide reinforcements when needed.
```

### 5.5 CONTEXT BUILDING — GAME STATE TO AI CONTEXT

```csharp
public class GameStateSerializer
{
    // Builds a concise but complete context string for the AI
    public string BuildEnemyCommanderContext(SimulationState state)
    {
        var sb = new StringBuilder();
        
        // What the enemy commander would know:
        sb.AppendLine("=== YOUR AIRCRAFT ===");
        foreach (var aircraft in state.Entities.Where(e => e.Affiliation == Hostile))
        {
            sb.AppendLine($"- {aircraft.Designation} ({aircraft.TrackId})");
            sb.AppendLine($"  Position: BRG {aircraft.Bearing:F0}° RNG {aircraft.Range:F1}nm");
            sb.AppendLine($"  ALT: FL{aircraft.Altitude/100:F0} SPD: {aircraft.Speed:F0}kts HDG: {aircraft.Heading:F0}°");
            sb.AppendLine($"  Status: {aircraft.Status} Behavior: {aircraft.CurrentBehavior}");
            sb.AppendLine($"  Under fire: {aircraft.IsBeingEngaged} ECM: {aircraft.ECMActive}");
            if (aircraft.DamageLevel > 0) sb.AppendLine($"  DAMAGED: {aircraft.DamageLevel}%");
        }
        
        sb.AppendLine("\n=== LOSSES ===");
        foreach (var loss in state.EnemyLosses)
            sb.AppendLine($"- {loss.Designation} DESTROYED at {loss.TimeOfLoss}");
        
        sb.AppendLine("\n=== DETECTED THREATS ===");
        // What enemy RWR/ELINT would detect
        foreach (var radar in state.DetectedRadars)
            sb.AppendLine($"- SAM RADAR detected BRG {radar.Bearing:F0}° (type: {radar.EstimatedType})");
        
        sb.AppendLine("\n=== MISSION ===");
        sb.AppendLine($"Objective: {state.Scenario.EnemyObjective}");
        sb.AppendLine($"Time remaining: {state.Scenario.TimeRemaining}");
        sb.AppendLine($"Acceptable losses: {state.Scenario.AcceptableLossRate}%");
        
        return sb.ToString();
    }
}
```

### 5.6 LM STUDIO API CALL FORMAT

```csharp
public class LMStudioClient
{
    public async Task<AIResponse> CallWithTools(
        string systemPrompt,
        List<ChatMessage> conversationHistory,
        List<Tool> availableTools,
        string model = "hermes-3-llama-3.1-8b")
    {
        var request = new
        {
            model = model,
            messages = BuildMessages(systemPrompt, conversationHistory),
            tools = availableTools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = new
                    {
                        type = "object",
                        properties = t.Parameters,
                        required = t.RequiredParams
                    }
                }
            }),
            tool_choice = "auto",
            temperature = 0.7,
            max_tokens = 512,
            // LM Studio structured output enforcement
            response_format = new { type = "json_object" } // when needed
        };

        var response = await _httpClient.PostAsJsonAsync(
            "http://localhost:1234/v1/chat/completions", request);
        
        var result = await response.Content.ReadFromJsonAsync<LMStudioResponse>();
        
        // Handle tool calls recursively
        if (result.Choices[0].Message.ToolCalls != null)
        {
            foreach (var toolCall in result.Choices[0].Message.ToolCalls)
            {
                var toolResult = await _toolExecutor.Execute(
                    toolCall.Function.Name, 
                    toolCall.Function.Arguments);
                
                conversationHistory.Add(new ToolResultMessage(toolCall.Id, toolResult));
            }
            
            // Call again with tool results for the AI to formulate response
            return await CallWithTools(systemPrompt, conversationHistory, availableTools, model);
        }
        
        return new AIResponse(result.Choices[0].Message.Content);
    }
}
```

---

## 6. UI LAYOUT — THE COMMAND CONSOLE

### 6.1 Default Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│ ┌─[MENU]─┐  IRON DOME v1.0 — BATTERY ALPHA    █ ALERT: ██ YELLOW ██  │
│ │STANDBY │  TIME: 14:32:07 ZULU   SCENARIO: DAWN PATROL               │
│ └────────┘                                                              │
├─────────────────────────────┬───────────────────────────────────────────┤
│                             │                                           │
│     ╔═══════════════╗       │  ┌── THREAT BOARD ─────────────────────┐  │
│     ║               ║       │  │ TRK  │ TYPE    │ BRG │ RNG │ THRT  │  │
│     ║     RADAR     ║       │  │ 0023 │ SU-24   │ 045 │ 34  │ HIGH  │  │
│     ║   DISPLAY     ║       │  │ 0024 │ SU-24   │ 048 │ 36  │ HIGH  │  │
│     ║               ║       │  │ 0019 │ MIG-29  │ 120 │ 78  │ MED   │  │
│     ║  (SkiaSharp   ║       │  │ 0031 │ UNK     │ 270 │ 95  │ LOW   │  │
│     ║   Canvas)     ║       │  └─────────────────────────────────────┘  │
│     ║               ║       │                                           │
│     ║               ║       │  ┌── WEAPONS STATUS ───────────────────┐  │
│     ║               ║       │  │ L1: [████] RDY  │ L2: [████] RDY   │  │
│     ║               ║       │  │ L3: [░░░░] RLD  │ L4: [████] RDY   │  │
│     ╚═══════════════╝       │  │ Stock: 12  Fired: 4  Hits: 2       │  │
│                             │  └─────────────────────────────────────┘  │
│  [SEARCH] [TWS] [STT]      │                                           │
│  Range: ▪▪▪▪▫▫▫ 60nm       │  ┌── SYSTEM STATUS ────────────────────┐  │
│                             │  │ RADAR:  ● OPERATIONAL               │  │
├─────────────────────────────┤  │ COMMS:  ● OPERATIONAL               │  │
│                             │  │ POWER:  ● NOMINAL                   │  │
│  ┌─ CH1: COMMAND NET ────┐  │  │ COOL:   ● NOMINAL                   │  │
│  │ [14:32:07] ECHO:      │  │  └─────────────────────────────────────┘  │
│  │  "Weapons tight.      │  │                                           │
│  │   Acknowledge."       │  │  ┌── SELECTED CONTACT ─────────────────┐  │
│  │                       │  │  │ TRK-0023  ◆ HOSTILE                  │  │
│  │ [14:32:15] ★ ALPHA:   │  │  │ TYPE: SU-24 FENCER (PROBABLE)       │  │
│  │  "Copy. Standing by." │  │  │ BRG: 045° | RNG: 34.2nm | FL180    │  │
│  │                       │  │  │ SPD: 480kts | HDG: 225°             │  │
│  │ [14:33:42] ECHO:      │  │  │ ASPECT: HOT (CLOSING)              │  │
│  │  ⚠ "WEAPONS FREE.    │  │  │                                      │  │
│  │   ENGAGE. ENGAGE."   │  │  │ [DESIGNATE] [ENGAGE] [TRACK]        │  │
│  └───────────────────────┘  │  └─────────────────────────────────────┘  │
│  │ CH1 CH2 CH3 CH4 CH5 CH6 │                                           │
│  > _                   [TX] │                                           │
└─────────────────────────────┴───────────────────────────────────────────┘
```

### 6.2 Color Palette — Military CRT Aesthetic

```csharp
public static class MilitaryColors
{
    // Background layers
    public static Color ConsoleBackground    = Color.FromRgb(10, 12, 10);      // Near black
    public static Color PanelBackground      = Color.FromRgb(15, 20, 15);      // Dark green-black
    public static Color PanelBorder          = Color.FromRgb(30, 60, 30);      // Dim green
    public static Color PanelHeader          = Color.FromRgb(20, 35, 20);      // Slightly lighter
    
    // Radar
    public static Color RadarBackground      = Color.FromRgb(5, 15, 5);       // Deep dark green
    public static Color RadarSweep           = Color.FromRgb(0, 255, 0);       // Bright green (with alpha fade)
    public static Color RadarGrid            = Color.FromRgb(0, 80, 0);        // Dim green grid
    public static Color RadarText            = Color.FromRgb(0, 200, 0);       // Green text
    
    // Contact colors
    public static Color Hostile              = Color.FromRgb(255, 50, 50);     // Red
    public static Color Friendly             = Color.FromRgb(50, 150, 255);    // Blue
    public static Color Unknown              = Color.FromRgb(255, 255, 50);    // Yellow
    public static Color Neutral              = Color.FromRgb(50, 255, 50);     // Green
    
    // Alert levels
    public static Color AlertGreen           = Color.FromRgb(0, 180, 0);
    public static Color AlertYellow          = Color.FromRgb(255, 200, 0);
    public static Color AlertOrange          = Color.FromRgb(255, 130, 0);
    public static Color AlertRed             = Color.FromRgb(255, 0, 0);
    
    // UI elements
    public static Color TextPrimary          = Color.FromRgb(0, 220, 0);       // Primary green text
    public static Color TextSecondary        = Color.FromRgb(0, 150, 0);       // Dimmer green
    public static Color TextHighlight        = Color.FromRgb(0, 255, 0);       // Bright green
    public static Color TextWarning          = Color.FromRgb(255, 200, 0);     // Amber warning
    public static Color TextCritical         = Color.FromRgb(255, 0, 0);       // Red critical
    public static Color InputField           = Color.FromRgb(0, 30, 0);        // Dark input bg
    public static Color ButtonNormal         = Color.FromRgb(30, 60, 30);
    public static Color ButtonHover          = Color.FromRgb(40, 80, 40);
    public static Color ButtonActive         = Color.FromRgb(0, 180, 0);
    
    // Missile track
    public static Color MissileInFlight      = Color.FromRgb(255, 100, 0);     // Orange
    public static Color MissileImpact        = Color.FromRgb(255, 255, 0);     // Yellow flash
    
    // CRT effects
    public static Color ScanLineColor        = Color.FromArgb(15, 0, 255, 0);  // Subtle scan lines
    public static Color PhosphorGlow         = Color.FromArgb(30, 0, 255, 0);  // Glow around bright elements
}
```

### 6.3 CRT / Radar Visual Effects

```csharp
// SkiaSharp shader effects for radar display
public class RadarEffects
{
    // 1. PHOSPHOR PERSISTENCE — contacts fade slowly after sweep passes
    //    Each contact rendered with decreasing opacity based on time since last sweep
    
    // 2. SWEEP LINE GLOW — bright leading edge with gradual fade behind
    //    Rendered as a filled arc with gradient from bright green to transparent
    
    // 3. SCAN LINES — horizontal lines across the entire display
    //    Semi-transparent green lines every 2-3 pixels
    
    // 4. NOISE/GRAIN — subtle random noise in the radar background
    //    Random pixel brightness variation, more intense at longer ranges
    
    // 5. VIGNETTE — edges of radar display slightly darker
    //    Radial gradient overlay
    
    // 6. BLOOM/GLOW — bright elements have a soft glow around them
    //    Render bright elements twice: once normal, once with blur and additive blend
    
    // 7. FLICKER — very subtle random brightness variation (CRT simulation)
    //    Global brightness multiplier with subtle sine wave + noise
}
```

### 6.4 Menu System (No "Exit to Desktop")

```
┌─── IRON DOME COMMAND CONSOLE ──────────────────┐
│                                                  │
│   ┌── SYSTEM ─────────────────────────────────┐ │
│   │                                            │ │
│   │   [ ] Resume Operations                   │ │
│   │   [ ] Mission Briefing                    │ │
│   │   [ ] System Configuration                │ │
│   │   [ ] Audio Settings                      │ │
│   │   [ ] Display Configuration               │ │
│   │   [ ] Console Layout Reset                │ │
│   │   [ ] Save Mission State                  │ │
│   │   [ ] Load Mission State                  │ │
│   │                                            │ │
│   │   ─────────────────────────────            │ │
│   │                                            │ │
│   │   [ ] ⚠ POWER DOWN CONSOLE               │ │ ◄── Exit button
│   │       "Deactivate all systems and          │ │
│   │        power down the command console"     │ │
│   │                                            │ │
│   └────────────────────────────────────────────┘ │
│                                                  │
│   UPTIME: 01:23:45  │  STATUS: OPERATIONAL      │
│                                                  │
└──────────────────────────────────────────────────┘

// Confirmation dialog when clicking "POWER DOWN CONSOLE":
┌─── CONFIRM POWER DOWN ────────────────────────┐
│                                                │
│  ⚠ WARNING: Powering down the console will    │
│  deactivate all radar and weapons systems.    │
│  Battery ALPHA will go OFFLINE.               │
│                                                │
│  Are you sure you want to power down?          │
│                                                │
│      [ CONFIRM SHUTDOWN ]  [ CANCEL ]          │
│                                                │
└────────────────────────────────────────────────┘
```

---

## 7. SIMULATION ENGINE — TICK SYSTEM

### 7.1 Core Simulation Loop

```csharp
public class SimulationEngine
{
    // Runs at 20 ticks per second (50ms per tick)
    // UI renders at 60fps independently via SkiaSharp
    
    private const double TICK_RATE = 20.0;
    private const double TICK_INTERVAL_MS = 1000.0 / TICK_RATE;
    
    public double TimeScale { get; set; } = 1.0; // 1x, 2x, 4x speed
    
    public void Tick(double deltaTime)
    {
        double scaledDelta = deltaTime * TimeScale;
        
        // 1. Update entity positions (physics)
        _entityManager.UpdateAll(scaledDelta);
        
        // 2. Run radar detection
        _radarSystem.Scan(scaledDelta);
        
        // 3. Update track files
        _trackManager.UpdateTracks(scaledDelta);
        
        // 4. Update missiles in flight
        _weaponsSystem.UpdateMissiles(scaledDelta);
        
        // 5. Check intercept conditions
        _engagementManager.CheckIntercepts();
        
        // 6. Process ECM effects
        _ecmSystem.Process(scaledDelta);
        
        // 7. Process communications queue
        _commManager.ProcessQueue();
        
        // 8. Check scenario triggers
        _scenarioManager.CheckTriggers(scaledDelta);
        
        // 9. Schedule AI agent calls (if needed)
        _agentOrchestrator.Tick(scaledDelta);
        
        // 10. Publish state update event
        _eventBus.Publish(new SimulationTickEvent(GameTime));
    }
}
```

### 7.2 Entity Update (Physics)

```csharp
public class Aircraft : Entity
{
    public void Update(double deltaTime)
    {
        // Apply current behavior
        switch (CurrentBehavior)
        {
            case Behavior.IngressAttack:
                // Fly toward target along planned route
                SteerTowardWaypoint(deltaTime);
                break;
                
            case Behavior.EvasiveManeuver:
                // Random jinking, altitude changes, turn away from threat
                PerformEvasion(deltaTime);
                break;
                
            case Behavior.TerrainFollowing:
                // Adjust altitude to follow terrain
                FollowTerrain(deltaTime);
                break;
                
            case Behavior.PopUpAttack:
                // Low level approach, pop up, attack, dive back down
                ExecutePopUp(deltaTime);
                break;
        }
        
        // Apply flight model constraints
        Speed = Math.Clamp(Speed, MinSpeed, MaxSpeed);
        TurnRate = Math.Clamp(requestedTurnRate, -MaxTurnRate, MaxTurnRate);
        ClimbRate = Math.Clamp(requestedClimbRate, MaxDescent, MaxClimb);
        
        // Update position
        Heading += TurnRate * deltaTime;
        Altitude += ClimbRate * deltaTime;
        
        double distanceNm = (Speed / 3600.0) * deltaTime; // nm per second
        Latitude += distanceNm * Math.Cos(Heading.ToRadians()) / 60.0;
        Longitude += distanceNm * Math.Sin(Heading.ToRadians()) / (60.0 * Math.Cos(Latitude.ToRadians()));
        
        // Update fuel
        FuelRemaining -= FuelConsumptionRate * deltaTime;
        if (FuelRemaining <= BingoFuel)
            RequestBehaviorChange(Behavior.EgressRetreat, "BINGO FUEL");
    }
}
```

---

## 8. AUDIO DESIGN

```csharp
public class AudioEngine
{
    // Layered audio channels:
    
    // LAYER 1: Ambient (always playing)
    //   - Electronics hum
    //   - Air conditioning
    //   - Subtle radar sweep click (synchronized with sweep animation)
    
    // LAYER 2: Radio (when messages arrive)
    //   - Squelch open → Message → Squelch close
    //   - Background static (varies with "signal quality")
    //   - Different static patterns per channel
    //   - Urgent messages: preceded by alert tone
    
    // LAYER 3: Alerts
    //   - Lock-on warning: escalating beep pattern
    //   - Missile launch confirmation: solid tone
    //   - New contact: single ping
    //   - Track lost: descending tone
    //   - Missile miss: error buzz
    //   - Missile hit: confirmation chime
    //   - Incoming threat: klaxon/alarm
    
    // LAYER 4: UI feedback
    //   - Button clicks
    //   - Channel switch click
    //   - Mode change confirmation beep
    //   - Typing sounds for own messages
    
    // All layers mixed with NAudio's MixingSampleProvider
    // Volume independently controllable per layer
}
```

---

## 9. SCENARIO SYSTEM

```json
// Example scenario: scenarios/dawn_patrol.json
{
    "name": "Dawn Patrol",
    "description": "Enemy strike package detected heading toward friendly infrastructure. Defend the sector.",
    "duration_minutes": 30,
    "environment": {
        "time_of_day": "0545",
        "weather": {
            "visibility_nm": 15,
            "cloud_ceiling_ft": 8000,
            "precipitation": "none",
            "wind_speed_kts": 12,
            "wind_direction": 270
        },
        "terrain": "plains_with_hills"
    },
    "player_battery": {
        "callsign": "ALPHA",
        "position": { "lat": 34.05, "lon": 35.65 },
        "type": "SA-11 BUK",
        "launchers": 4,
        "missiles_per_launcher": 1,
        "reserve_missiles": 12,
        "radar_range_nm": 80,
        "engagement_range_nm": 18
    },
    "allied_forces": [
        {
            "callsign": "BRAVO",
            "type": "sam_battery",
            "position": { "lat": 34.12, "lon": 35.80 },
            "radar_range_nm": 60
        },
        {
            "callsign": "VIPER 1-1",
            "type": "fighter",
            "aircraft": "F-16C",
            "position": { "lat": 34.30, "lon": 35.50 },
            "status": "airborne_cap"
        }
    ],
    "command": {
        "callsign": "ECHO",
        "initial_roe": "weapons_tight",
        "initial_alert": "yellow"
    },
    "enemy_forces": {
        "objective": "Strike friendly airfield at 34.15N 35.70E",
        "waves": [
            {
                "trigger": "time",
                "time_minutes": 2,
                "aircraft": [
                    {
                        "type": "SU-24",
                        "count": 2,
                        "spawn_bearing": 45,
                        "spawn_range_nm": 120,
                        "spawn_altitude_ft": 18000,
                        "spawn_heading": 225,
                        "spawn_speed_kts": 480,
                        "initial_behavior": "ingress_attack",
                        "ai_aggressiveness": 0.7
                    }
                ]
            },
            {
                "trigger": "time",
                "time_minutes": 8,
                "aircraft": [
                    {
                        "type": "MIG-29",
                        "count": 2,
                        "spawn_bearing": 60,
                        "spawn_range_nm": 100,
                        "spawn_altitude_ft": 25000,
                        "spawn_heading": 240,
                        "spawn_speed_kts": 550,
                        "initial_behavior": "feint",
                        "ai_aggressiveness": 0.5
                    }
                ]
            },
            {
                "trigger": "ai_decision",
                "condition": "enemy_commander_requests_reinforcement",
                "aircraft": [
                    {
                        "type": "SU-24",
                        "count": 4,
                        "spawn_bearing": 30,
                        "spawn_range_nm": 130,
                        "spawn_altitude_ft": 500,
                        "spawn_heading": 210,
                        "spawn_speed_kts": 420,
                        "initial_behavior": "terrain_following",
                        "ai_aggressiveness": 0.9
                    }
                ]
            }
        ]
    },
    "victory_conditions": {
        "win": "No enemy aircraft reaches target within 30 minutes",
        "lose": "3+ enemy aircraft successfully strike the target",
        "partial": "1-2 aircraft reach target"
    },
    "ai_initial_prompt_modifier": "This is a dawn strike mission. The enemy knows approximately where the air defenses are. They will start with a probing attack, then commit the main force based on what they learn. If losses exceed 40%, they should consider aborting."
}
```

---

## 10. DEVELOPMENT PHASES & MILESTONES

### PHASE 1: FOUNDATION (Weeks 1-2)

```
Priority: Get the skeleton running with a visible radar and moving dots

□ Project setup (solution, NuGet packages, folder structure)
□ SimulationEngine with basic game loop (20 tick/s)
□ Entity system (Aircraft, position, velocity, heading)
□ Coordinate system (lat/lon ↔ screen ↔ bearing/range)
□ Basic SkiaSharp radar display:
    □ Dark background with green tint
    □ Range rings (3-4 concentric circles)
    □ Compass rose / bearing markers
    □ Rotating sweep line with fade effect
    □ Simple dots for entities
□ EntityManager spawning test aircraft that fly in straight lines
□ Basic WPF window with radar canvas filling the screen
□ Frame rate & tick rate management

DELIVERABLE: A radar screen with a sweep line and moving dots
```

### PHASE 2: CORE RADAR & TRACKING (Weeks 3-4)

```
□ Detection model (range-based probability, RCS)
□ TrackFile system (ID assignment, position history)
□ TrackManager (automatic track initiation, track dropping)
□ IFF system (classification logic)
□ Contact rendering:
    □ NATO-style symbols (diamond/circle/square)
    □ Color coding (hostile/friendly/unknown)
    □ Track ID labels
    □ Speed/heading vectors (velocity leader lines)
    □ Track history trail (fading dots)
□ Radar modes (Search, TWS, STT)
□ Click-to-select contacts
□ Selected contact detail panel (basic)
□ Range/zoom controls
□ Threat board table

DELIVERABLE: Functional radar with classified, tracked contacts
```

### PHASE 3: WEAPONS SYSTEM (Weeks 5-6)

```
□ Missile types definition (specs, performance)
□ Launcher model (loaded, reloading, ready)
□ Engagement workflow:
    □ Designate target (click + button)
    □ Calculate intercept solution
    □ Launch missile
    □ Missile flight simulation (kinematics)
    □ Missile track rendering on radar
    □ Intercept check (Pk-based hit/miss)
    □ Kill assessment
□ Weapons panel UI
□ Engagement zone overlay on radar (MEZ ring)
□ Shoot-look-shoot logic
□ Missile inventory tracking
□ Reload timer simulation

DELIVERABLE: Can detect, track, and engage targets with missiles
```

### PHASE 4: COMMUNICATIONS SYSTEM (Week 7)

```
□ RadioChannel model
□ RadioMessage model (sender, priority, timestamp, content)
□ CommManager (message routing, queue)
□ Communications panel UI:
    □ Channel tabs (6 channels)
    □ Scrolling message log
    □ Priority-based styling (colors, icons)
    □ Text input with TX button
    □ Channel selection indicator
□ Player message input handling
□ Automated battery crew responses (scripted first, AI later)
□ Message priority system (FLASH popup/sound)
□ Brevity code reference integration

DELIVERABLE: Functional multi-channel radio communications
```

### PHASE 5: AI INTEGRATION (Weeks 8-10)

```
□ LMStudioClient (HTTP, async, error handling, retry)
□ Tool interface and registry
□ Implement all 28 tools
□ ToolExecutor (parse arguments, call game state, return results)
□ PromptTemplateEngine (variable substitution)
□ Write all system prompts (6 personas)
□ Write scenario-specific prompts
□ GameStateSerializer (build context for each agent)
□ ConversationHistory (per-agent memory, truncation)
□ Agent implementations:
    □ EnemyCommanderAgent (controls enemy tactics)
    □ AlliedHQAgent (responds to player, issues orders)
    □ IntelligenceAgent (threat reports)
    □ AlliedPilotAgent (radio chatter)
    □ BatteryCrewAgent (crew communications)
□ AgentOrchestrator (scheduling, priority queue, rate limiting)
□ Integration testing with LM Studio running

DELIVERABLE: AI agents generating dynamic behavior and radio messages
```

### PHASE 6: TERRAIN MAP (Week 11)

```
□ Mapsui integration or custom tile renderer
□ Offline tile storage (generate/download military-style tiles)
□ Map overlays:
    □ Battery positions (icon + label)
    □ Allied positions
    □ Aircraft tracks (icon + heading arrow)
    □ Missile flight paths (animated line)
    □ Radar coverage circles
    □ Engagement zones
    □ Impact markers
□ Map controls (zoom, pan, click entity)
□ Toggle-able overlay layers
□ Distance measurement tool
□ Switchable view (radar ↔ map ↔ split)

DELIVERABLE: Terrain map view with real-time tactical overlay
```

### PHASE 7: AUDIO (Week 12)

```
□ NAudio setup (mixer, sample providers)
□ Sound bank (load all .wav assets)
□ Ambient layer (electronics hum, radar sweep)
□ Radio audio processing:
    □ Squelch open/close sounds
    □ Static overlay on "incoming" messages
    □ Priority alert tones
□ Event-driven sounds:
    □ New contact ping
    □ Lock-on tone
    □ Missile launch
    □ Explosion (distant)
    □ Track lost
    □ Alert klaxon
□ UI interaction sounds
□ Volume mixer (per-layer control)
□ Audio settings in menu

DELIVERABLE: Full audio atmosphere
```

### PHASE 8: POLISH & EFFECTS (Weeks 13-14)

```
□ CRT scan line effect (shader or overlay)
□ Phosphor persistence glow
□ Screen flicker (subtle)
□ Vignette effect on radar
□ Bloom on bright elements
□ Smooth animations on UI panels
□ FLASH message popup overlay (full-screen warning)
□ Alert level color changes (ambient lighting)
□ Custom controls:
    □ StatusIndicatorLight (LED-style)
    □ DigitalReadout (seven-segment)
    □ MilitaryToggleSwitch
    □ RotaryKnob selector
□ Military fonts integration
□ SVG icon rendering for all entity types
□ AvalonDock customizable layout
□ Panel arrangement save/load
□ Tooltips and help text

DELIVERABLE: Polished, immersive military command console
```

### PHASE 9: SCENARIOS & GAMEPLAY (Week 15)

```
□ Scenario file format finalized
□ Scenario loader
□ 5 complete scenarios:
    □ Tutorial: Single Bogey (learn basics)
    □ Dawn Patrol (2-wave attack)
    □ Mass Raid (8+ aircraft, overwhelming)
    □ ECM Escort (jammers + strikers)
    □ Cruise Missile Defense (low-slow targets)
□ Dynamic event system (equipment failures, weather changes)
□ Victory/defeat evaluation
□ Mission debrief screen (stats, timeline)
□ Scenario selection menu

DELIVERABLE: Complete playable game with multiple scenarios
```

### PHASE 10: TESTING & OPTIMIZATION (Week 16)

```
□ Performance profiling (60fps render, 20tick sim)
□ AI response time optimization
□ Memory management (conversation history limits)
□ Edge case handling (API timeout, model errors)
□ Gameplay balance testing
□ AI prompt refinement based on testing
□ UI/UX polish based on play testing
□ Bug fixing
□ README / documentation

DELIVERABLE: Stable, polished release v1.0
```

---

## 11. FUTURE PATCHES (POST v1.0)

```
PATCH 1.1 — VOICE INPUT (Whisper Integration)
├── Whisper.cpp integration (local speech-to-text)
├── Push-to-talk (keyboard shortcut)
├── Voice → text → existing message pipeline
├── AI prompt modifier: "Input may contain radio distortion errors, 
│   interpret as a real operator would"
├── Visual waveform display while transmitting
└── "TRANSMITTING" indicator

PATCH 1.2 — VOICE OUTPUT (TTS for Radio Messages)
├── Fast TTS model (Piper TTS or similar, runs locally)
├── Different voices per persona/callsign
├── Radio effect processing (bandpass filter, static, compression)
├── Queueing system (messages play in order)
└── Toggle between text-only and voice mode

PATCH 1.3 — ADVANCED SCENARIOS
├── Campaign mode (linked scenarios)
├── Procedural scenario generation (AI creates missions)
├── Difficulty scaling
├── New threat types (ballistic missiles, stealth aircraft)
├── Electronic warfare gameplay (own jamming, HARM missiles)
└── Multi-battery command (control 2-3 batteries)

PATCH 1.4 — MULTIPLAYER (stretch goal)
├── Network co-op (multiple battery commanders)
├── Shared air picture
├── Cross-battery coordination
└── vs mode (one player commands enemy)
```

---

## 12. KEY TECHNICAL DECISIONS SUMMARY

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Framework** | WPF (.NET 8) | Best for complex desktop UI, panels, data binding |
| **Radar Rendering** | SkiaSharp | GPU-accelerated 2D, shaders, 60fps custom drawing |
| **Map Engine** | Mapsui (offline tiles) | WPF-compatible, offline map rendering, overlays |
| **Audio** | NAudio | Full mixing, effects chains, multiple simultaneous sounds |
| **Layout** | AvalonDock | Professional dockable/resizable panels |
| **MVVM** | CommunityToolkit.Mvvm | Clean architecture, source generators |
| **AI Model (Primary)** | Hermes-3-Llama-3.1-8B Q4_K_M | Best tool calling at 8B, ~5.5GB VRAM |
| **AI Model (Fast)** | Qwen2.5-3B-Instruct Q4_K_M | Quick responses, ~2GB VRAM |
| **AI Server** | LM Studio (localhost) | Easy model management, OpenAI-compatible API |
| **Structured Output** | LM Studio forced JSON | Guaranteed parseable tool calls |
| **Architecture** | MVVM + Event Bus | Decoupled, testable, real-time friendly |
| **Simulation Rate** | 20 ticks/sec | Smooth enough for tracking, light on CPU |
| **Render Rate** | 60 fps | Smooth radar sweep, responsive UI |

---

## 13. FILE COUNTS & ESTIMATES

```
Total estimated files:     ~120 C# files
                           ~25 XAML files  
                           ~15 resource files (audio, fonts, icons)
                           ~6 prompt template files
                           ~5 scenario JSON files
                           ~5 config files

Total estimated lines:     ~15,000-20,000 lines of C#
                           ~3,000-4,000 lines of XAML

Development time:          ~16 weeks (solo developer, dedicated)
                           ~8-10 weeks (experienced, focused)
```

---

## 14. FIRST FILE TO BUILD

**We start with Phase 1.** The very first thing we create:

1. `IronDome.sln` — Solution file
2. `IronDome.Core/Entities/Entity.cs` — Base entity with position/velocity
3. `IronDome.Core/Simulation/SimulationEngine.cs` — Game loop
4. `IronDome.App/MainWindow.xaml` — Window with SkiaSharp canvas
5. `IronDome.App/Controls/RadarDisplay.cs` — Radar renderer

**When the first green sweep line rotates across a dark screen with a blip moving — we know the foundation is solid.**

---

**Ready to start building? Say the word and I'll generate Phase 1 code — every file, every line, fully functional.**