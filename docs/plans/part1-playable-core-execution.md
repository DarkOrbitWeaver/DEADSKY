# DEADSKY Part 1 Execution Plan

This document turns the "Playable Core First, Realism Expansion Second" roadmap into an implementation-ready plan tied to the current codebase.

It is intentionally biased toward the live loop that already exists:

- WPF shell remains the active frontend
- `MainViewModel` partials remain the orchestration seam
- deterministic simulation and fire-control stay authoritative
- AI remains commander-level only
- persistence remains campaign/profile/logistics/theater only

## Current Baseline

The repo already contains useful Part 1 foundations:

- radar-first shell exists in `src/DEADSKY.App/MainWindow.xaml`
- mission lifecycle and shell state are partially split out of the main VM
- fire-control readouts already exist in `src/DEADSKY.App/ViewModels/MainViewModel.FireControl.cs`
- scenario loading already defaults to `operation` on startup in `src/DEADSKY.App/MainWindow.xaml.cs`
- scripted and generated scenarios already exist under `src/DEADSKY.App/ViewModels/MainViewModel.Scenarios.cs`, `src/DEADSKY.AI/Scenario/`, and `src/DEADSKY.Core/Scenario/`
- campaign/persistence seams already exist under `src/DEADSKY.App/Services/` and `src/DEADSKY.App/ViewModels/MainViewModel.CampaignPersistence.cs`
- commander-facing AI support seams already exist in `src/DEADSKY.AI/Agents/`, `src/DEADSKY.AI/Tools/`, and the campaign/comms directors

This means Part 1 is not a greenfield build. The main risk is shell bloat, overlapping state language, and too much truth being recomputed in multiple places.

## Part 1 Implementation Order

Ship Part 1 in five slices:

1. Shell cleanup and operator readability
2. Fire-control state unification and radar interaction clarity
3. Scenario archetypes and honest startup flow
4. Commander-layer AI and gameplay truth alignment
5. Code and UI de-bloat pass

Do not start with Part 2 realism work. If a change does not improve the live playable loop, it waits.

## Slice 1: Shell Cleanup

Goal: make the main station feel like one coherent radar operator station.

### Target files

- `src/DEADSKY.App/MainWindow.xaml`
- `src/DEADSKY.App/Themes/MilitaryDarkTheme.xaml`
- `src/DEADSKY.App/ViewModels/MainViewModel.Layout.cs`
- `src/DEADSKY.App/ViewModels/MainViewModel.cs`

### Actions

- reduce duplicated status surfaces across header, tactical summary, fire-control badges, alerts, and comms
- keep the left side dominated by radar and selected-track control
- keep the right rail focused on track board, concise summary, and scenario controls
- keep comms floating, but collapse it into a smaller, higher-signal operator drawer
- remove or hide controls that do not change live game state
- remove any disabled-looking surfaces that are only decorative
- standardize widths, spacing, badge sizing, and severity colors so the shell reads as a single station

### Definition of done

- every visible button has a meaningful outcome
- no dead panel remains visible
- operator can scan the shell and understand alert level, selected target, fire-control status, and current mission state within a few seconds

## Slice 2: Fire-Control and Radar Clarity

Goal: expose a single coherent fire-control truth model to the operator.

### Target files

- `src/DEADSKY.App/ViewModels/MainViewModel.FireControl.cs`
- `src/DEADSKY.App/ViewModels/MainViewModel.cs`
- `src/DEADSKY.App/Controls/RadarDisplay.cs`
- `src/DEADSKY.Core/Radar/TrackManager.cs`
- `src/DEADSKY.Core/Radar/RadarSystem.cs`
- `src/DEADSKY.Core/Weapons/WeaponsSystem.cs`
- `src/DEADSKY.Core/Entities/SAMBattery.cs`

### Actions

- replace overlapping badge text with a stable fire-control state model
- standardize operator language around:
  - `LOCK`
  - `IN RANGE`
  - `READY`
  - `NO SHOT / LONG`
  - `NO SHOT / ROE`
  - `MISSILE IN FLIGHT`
  - `SALVO x2`
- add a deterministic "why can't I shoot?" explanation path from the same checks that gate launch
- improve selected-track focus behavior on radar:
  - stronger brackets
  - clear designation state
  - visible in-range vs out-of-range cues
  - smart range stepping when selection changes
- audit radar modes and remove any operator-facing mode that is not actually supported by gameplay

### Definition of done

- the selected-track panel, radar, and launch buttons all reflect the same fire-control truth
- no-shot reasons always map to deterministic checks
- missile launch and in-flight state are visible without reading the log

## Slice 3: Scenario Variety and Honest Persistence

Goal: make startup varied and replayable without pretending live mission resume exists.

### Target files

- `src/DEADSKY.App/MainWindow.xaml.cs`
- `src/DEADSKY.App/ViewModels/MainViewModel.Scenarios.cs`
- `src/DEADSKY.App/ViewModels/MainViewModel.CampaignPersistence.cs`
- `src/DEADSKY.App/Services/CampaignPersistenceService.cs`
- `src/DEADSKY.Core/Scenario/ScenarioModels.cs`
- `src/DEADSKY.Core/Scenario/ScenarioContract.cs`
- `src/DEADSKY.Core/Scenario/RealisticScenarioMaterializer.cs`

### Actions

- introduce a scenario archetype/category layer instead of treating scenarios as only hardcoded names
- keep tutorial as a training mission, not the game's default identity
- expand scripted coverage to the Part 1 archetypes:
  - single strike
  - escort plus striker package
  - decoy plus main raid
  - jammer pressure wave
  - multi-axis timed raid
- ensure startup lands in a varied playable scenario selection path, not a fixed single-bogey loop
- expose persistence honestly in UI and save metadata:
  - what saves
  - what does not save
  - where it is stored
  - when it updates
- keep live mission resume out of scope

### Definition of done

- startup consistently enters a replayable playable operation flow
- tutorial remains intentionally loadable
- UI never implies the player can resume an in-progress mission after restart

## Slice 4: Commander AI and Shared Truth

Goal: keep AI important without letting it own deterministic execution.

### Target files

- `src/DEADSKY.AI/Agents/AgentOrchestrator.cs`
- `src/DEADSKY.AI/Tools/ToolRegistry.cs`
- `src/DEADSKY.Core/Campaign/`
- `src/DEADSKY.Core/Comms/`
- `src/DEADSKY.App/ViewModels/MainViewModel.RadioSupport.cs`
- `src/DEADSKY.App/ViewModels/MainViewModel.Notifications.cs`

### Actions

- constrain AI tool calls to commander-level intent and doctrine actions only
- move any low-level execution math and fail-safe behavior into deterministic systems
- make support activity more visible through comms and concise status surfaces:
  - support package state
  - nearby battery reports
  - CAP/AWACS/relay updates
  - reinforcement timing
  - retreat, panic, and surrender traffic
- ensure comms, alerts, track board, and map derive from the same mission truth rather than parallel invented summaries

### Definition of done

- loss of AI availability still leaves a playable deterministic mission
- AI commands cannot bypass deterministic fire-control, sensor, or movement rules
- allied activity feels present and actionable instead of decorative

## Slice 5: De-bloat and Stabilization

Goal: remove misleading code and unreachable UI while preserving useful backend seams for Part 2.

### Target files

- `src/DEADSKY.App/MainWindow.xaml`
- `src/DEADSKY.App/ViewModels/`
- `src/DEADSKY.Core/`
- `tests/DEADSKY.Backend.Tests/`

### Actions

- audit exposed prototype code and dead UI paths first
- remove hidden-but-unmaintained UI branches
- keep backend systems that support Part 2 but are not yet surfaced
- remove only code that is redundant, unreachable, or misleading in the live loop
- add regression coverage for:
  - scenario selection/startup behavior
  - persistence truth
  - fire-control readouts
  - visible command outcomes

### Definition of done

- removed UI paths do not leave broken bindings or dangling commands
- tests cover the core Part 1 truth surfaces
- the live shell reads cleaner because obsolete surfaces are gone, not just hidden

## First Implementation Slice to Start Now

Start with Slice 1 plus the smallest useful part of Slice 2.

That means:

1. tighten `MainWindow.xaml` into a cleaner radar-first shell
2. remove duplicated or low-signal status badges
3. introduce a single operator-facing fire-control summary model
4. make the selected-track area and launch controls the clearest interactive surface in the station

This is the best first move because it improves the live experience immediately and forces a clean truth model that the later scenario and AI work can reuse.

## Guardrails

- do not rewrite the frontend
- do not add live mission resume
- do not let AI directly own movement, pathing, sensor truth, or weapon resolution
- do not keep dead controls visible for future ambitions
- do not remove backend systems merely because their UI is not ready yet

## Test Focus

The minimum acceptance test pass for each slice should cover:

- maximize, restore, and resize behavior
- no blocked clicks or overlay traps
- track selection and radar focus behavior
- no-shot reason transitions
- missile in-flight feedback
- startup scenario selection behavior
- persistence after restart for campaign/profile/logistics/theater state only
- AI available and AI unavailable playability

## Working Rule

If a feature does not improve detection, identification, engagement, comms, logistics, or scenario control in the live station, it is not Part 1 priority.

## Deferred UI Notes

Hold these for the next dedicated comms/UI pass:

- comms tabs and filters should feel connected, with per-channel unread state and clearer read transitions
- tabs should expose quieter status cues instead of feeling disconnected from the message list
- message typography should stay readable and compact, with at most a very small size reduction
- comms rows should use less padding and tighter information density
- preserve visibility while improving compactness; do not shrink the chat into a hard-to-read terminal wall

## Deferred Realism Notes

- current hostile countermeasures are ECM/jamming only; expendable flare/chaff behavior is not implemented yet
