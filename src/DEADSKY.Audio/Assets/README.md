# DEADSKY Audio Assets

This folder is the stable home for sampled audio used by the game. The current runtime still uses a lightweight placeholder engine, but the asset layout below is now the canonical structure for future integration.

## Goals

- Keep the audio stack light enough for modest PCs.
- Separate alert, UI, ambience, radio, and weapons audio so later mixing stays manageable.
- Support two future voice paths:
  - pre-generated voice packs for deterministic timing and low CPU cost
  - optional local TTS for adaptive radio lines

## Folder Layout

- `alerts/`
- `ambience/`
- `impacts/`
- `radio/common/`
- `radio/crew/`
- `radio/hq/`
- `radio/intel/`
- `ui/`
- `voice_input/`
- `weapons/`

## Required Placeholder Names

See [asset_manifest.txt](./asset_manifest.txt). You can drop real files in with those names later and we can wire playback against the same identifiers.

## Immediate Priority Hunt

If you only want to search for a few high-value assets right now, focus on these first:

- `radio/common/radio_squelch_open.wav`
- `radio/common/radio_squelch_close.wav`
- `ui/radar_contact_ping_01.wav`
- `alerts/lock_warning_pulse.wav`
- `alerts/alert_red_klaxon.wav`
- `weapons/missile_launch_01.wav`
- `impacts/battery_hit_near.wav`
- `impacts/missile_splash_far.wav`

Recommended variant counts:

- radar/contact pings: 3 variants
- lock/warning pulses: 2-3 variants
- radio squelch open/close: 3 variants each
- missile launch: 2 variants
- impacts/explosions: 4+ variants total

The repo now also contains generated functional placeholders for these non-voice cues so the app can sound alive while the real library grows.

## Recommended Formats

- `wav` for short UI, alert, and weapon cues
- `ogg` for longer ambience loops
- `wav` or `ogg` for radio voices after filtering

## Planned Audio Buses

- `UI`
- `ALERTS`
- `RADIO`
- `WEAPONS`
- `AMBIENCE`
- `VOICE_INPUT_MONITOR`

## Voice Plan

Short term:

- Use sampled placeholders and pre-generated crew/HQ/intel lines.

Medium term:

- Add radio filtering, layered squelch, and per-channel loudness balancing.
- Allow AI-generated lines to be converted to speech only when timing and hardware budget permit.

Long term:

- Support local STT for player radio commands.
- Route interpreted commands into the same command/crew/intel tool chain the keyboard UI already uses.
