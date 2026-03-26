# DEADSKY Performance and Render Stability Plan

Date: 2026-03-26

## Goals

- Keep gameplay simulation stable regardless of window resize, focus loss, tab switching, or monitor changes.
- Run the UI at a smooth default of 60 FPS.
- Preserve enough GPU and CPU headroom to support an optional 90 FPS render mode on stronger machines.
- Reduce stutter, frozen frames, GC spikes, and layout/render starvation.

## Investigation Summary

### Confirmed problems

- The radar display only repainted when a new simulation snapshot arrived.
- The simulation currently ticks every 100 ms, which means the main scope was effectively capped near 10 FPS.
- UI snapshots were built from live mutable simulation objects, so render code and UI bindings could read objects while the sim thread was still mutating them.
- UI work was marshaled with `DispatcherPriority.Background`, which makes repaint recovery more vulnerable during heavy resize/layout activity.
- Some view-model and drawing paths create avoidable allocations every update.

### Likely player-facing symptoms explained

- Window resize or focus changes can make the picture appear frozen even while sounds continue.
- Rapid UI interactions can starve redraws because the scope had no independent render cadence.
- Render artifacts and inconsistent contact drawing can occur when the UI reads half-updated track or battery state.

## Phase 1: Stability Baseline

Status: in progress

- Keep render invalidation independent from the sim tick.
- Use immutable or cloned snapshots for all UI-facing data.
- Trigger redraw recovery on `Loaded`, `Unloaded`, `IsVisibleChanged`, and resize transitions.
- Favor render-priority UI dispatch for live frame updates.
- Add lightweight diagnostic logging around render starvation, skipped frames, and exception boundaries.

Success criteria:

- No frozen radar image after resize, alt-tab, minimize/restore, or opening/closing subwindows.
- Scope keeps animating smoothly even when simulation cadence is lower than render cadence.
- No cross-thread mutation issues in UI-bound data.

## Phase 2: Frame Pacing and Render Budget

- Introduce a configurable render loop target:
  - `60 FPS` default
  - `90 FPS` optional high-refresh mode
- Track moving averages for:
  - frame time
  - UI update time
  - radar draw time
  - GC pause time
- Add a frame budget overlay or debug telemetry panel.
- Decouple simulation frequency from render frequency explicitly:
  - simulation remains fixed-step
  - rendering interpolates or reuses latest snapshot between sim ticks
- Add automatic FPS fallback if frame time exceeds budget for sustained periods.

Success criteria:

- Stable frame pacing at 16.6 ms on default mode.
- Optional high-refresh mode remains under 11.1 ms on capable hardware.

## Phase 3: UI and Binding Optimization

- Throttle expensive side-panel updates to 4-10 Hz instead of updating every live tick.
- Split critical live bindings from slow informational bindings.
- Cache brushes, pens, geometries, and formatted text where possible.
- Avoid rebuilding threat-board rows when only small fields changed.
- Defer low-priority overlays and narrative panels during resize or heavy action.
- Batch property-change notifications for groups of related values.

Success criteria:

- Resize remains responsive.
- Radar redraws are never blocked by comms/log/layout churn.

## Phase 4: Radar and Map Rendering Optimization

- Pre-render static radar layers into cached surfaces:
  - background
  - range rings
  - bearing markers
  - vignette
  - scanline pattern
- Only rebuild cached layers when size, theme, or radar range changes.
- Reuse Skia resources aggressively:
  - paths
  - shaders
  - paints
  - dash effects
- Limit trail history work by:
  - capping visible history length
  - skipping sub-pixel trail segments
  - culling off-screen tracks earlier
- Add a quality ladder:
  - full effects
  - reduced trail density
  - reduced scanline density
  - minimal effects during stress

Success criteria:

- Static drawing cost becomes near-zero most frames.
- Track-heavy scenes stay smooth without visual collapse.

## Phase 5: Memory and GC Control

- Audit per-frame allocations in:
  - radar drawing
  - tactical map drawing
  - track board updates
  - notifications and comms overlays
- Replace transient objects with reusable pools where practical.
- Keep snapshot cloning efficient and bounded.
- Use immutable lightweight DTO snapshots for render-only data if cloning entities becomes too expensive.
- Add allocation profiling in a representative heavy mission.

Success criteria:

- No recurring GC hitch pattern during live play.
- Frame-time spikes from allocation pressure are rare and small.

## Phase 6: WPF and GPU Pipeline Hardening

- Verify WPF rendering tier and log it at startup.
- Test with:
  - hardware acceleration on
  - software fallback
  - mixed DPI / monitor changes
- Add recovery hooks for graphics/device loss where supported by the stack.
- Ensure canvas visibility toggles and window ownership changes do not leave controls in a stale state.
- Consider moving secondary displays to lower refresh/update rates than the main radar.

Success criteria:

- App recovers cleanly from monitor moves, DPI changes, and minimize/restore cycles.

## Phase 7: Optional Advanced Work

- Raise simulation tick to 20-30 Hz if gameplay fidelity benefits and CPU budget allows.
- Add interpolation between snapshots for contact motion.
- Explore Skia GPU-backed surfaces or alternate control hosting if WPF/`SKElement` remains a bottleneck.
- Add performance presets:
  - cinematic
  - balanced
  - competitive
- Investigate async asset warming and startup cache priming for audio/UI resources.

## Recommended Immediate Next Tasks

1. Add an in-app performance overlay with frame time, draw time, and allocation counters.
2. Cache static radar layers so the main scope spends most frame time only on dynamic contacts and effects.
3. Throttle non-critical panel refresh paths during live simulation.
4. Profile resize behavior and large-contact scenarios with allocation tracing.
5. Add a user setting for `60 FPS` or `90 FPS` render targets.

## Metrics We Should Track

- Average FPS
- 1% low FPS
- 99th percentile frame time
- Radar draw time
- UI thread backlog depth
- GC collections per minute
- Peak track count before missed-frame threshold
- Recovery time after resize/minimize/restore

## Definition of Done

- No render freeze repro during normal desktop interactions.
- Default mode holds visually smooth 60 FPS.
- High-refresh mode is available and stable on capable machines.
- Performance telemetry exists so future regressions are visible quickly.
