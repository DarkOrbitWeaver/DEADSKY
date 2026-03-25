# DEADSKY Master Map

This repo should follow the richer `DEADSKY` structure from the revised docs, but our implementation order should follow the audit in Part 3.

## Canonical Structure

```text
DEADSKY.sln
docs/
  MASTER_MAP.md
  plans/
    part1-original-plan.md
    part2-deadsky-addendum.md
    part3-audit-and-completion.md
    revision-notes.md
src/
  DEADSKY.Core/
    Entities/
    Physics/
    Radar/
    Weapons/
    Comms/
    Scenario/
    Simulation/
    Data/
    Economy/
    Personnel/
    Progression/
    Events/
    EnemyAI/
    Campaign/
  DEADSKY.AI/
    Agents/
    Client/
    Tools/
    Context/
    Prompts/
  DEADSKY.Audio/
    Assets/
  DEADSKY.App/
    Themes/
    Views/
    ViewModels/
    Controls/
    Converters/
    Resources/
```

## Execution Order

1. Finish the playable core: physics, radar detection, tracking, engagement, simulation loop.
2. Finish the app shell around that core: main window, radar screen wiring, threat board, comms panel.
3. Add progression systems only after a missile can track, launch, and score outcomes reliably.
4. Layer AI commanders, crew personality, economy, and campaign once the core loop is testable.

## Current Focus

`MainViewModel.cs` depends on many planned systems that do not exist yet. We should treat it as a top-level integration sketch, not the next file to brute-force complete in isolation.
