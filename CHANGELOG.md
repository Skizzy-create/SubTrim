# Changelog

All notable changes to SubTrim are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.1] - 2026-11-08

### Fixed

- Memory: the BuoyancyAdjuster driver no longer keeps a static reference to the BuoyancyAdjuster add-on. That add-on is recreated in every flight scene, so the static field kept the previous scene's destroyed instance, and the vessel it tracked, alive. Each driver now holds its own reference.
- Memory: a command part that breaks off or undocks drops the SubTrim vessel module it cached from its old vessel, so the debris no longer keeps that vessel's module, parts and modules alive.

## [1.0.0] - 2026-11-08

First release, for KSP 1.12.x.

### Added

- **Vessel module** that keeps a submarine close to neutral buoyancy and nudges it toward a target depth, about 10 times per second, with no per-tick allocations.
- **Detection**: BDArmory Surface AI in *Submarine* mode (optional `SubTrim.BDArmory.dll` bridge), parts flagged with `ModuleSubTrim isSubmarine = true`, HullBreach 1.8+ submarines, or the player switching the PAW panel to *On*.
- **Depth source**: the BDArmory AI's own depth choice (CombatAltitude, its torpedo/gun/extend logic), a manual depth from the PAW, or hold-current-depth.
- **Ballast drivers**, used in this order:
  1. KerbalPowers Naval `ModuleShipBallast` (target flooding, or KP's own depth hold with `kpNativeDepthHold`).
  2. BuoyancyAdjuster (only when switched on for that vessel).
  3. WildBlueTools `WBIBallastTank` (dive computer depth hold is paused while SubTrim drives).
  4. Ballast-water resources (`BALLAST_RESOURCE`, default `BallastWater`).
  5. Virtual ballast: a rate-limited, EC-costing scale on hull `part.buoyancy` when the boat has no real ballast.
- **Controller**: cascaded depth → vertical speed → net-buoyancy PI loop with anti-windup (integrates only near the target and not while a ballast system is saturated). Trim is limited to ±2 % of vessel weight, so the AI's pitch control keeps authority.
- **Safety**: surfaces for AI deck-gun runs, on *Emergency blow*, when EC runs low, and when HullBreach reports serious flooding. Never holds deeper than 85 % of the shallowest part's crush depth, or closer than 15 m to the sea floor.
- **PAW panel** "SubTrim depth & trim" on command parts: mode (Auto/On/Off), manual depth, status/trim/ballast readouts, emergency blow, and action groups (toggle, emergency blow, 10 m deeper/shallower, follow AI depth).
- `SubTrim.cfg` settings file and ModuleManager patches for MNWS SSBN002, the Iron Seas KANES Balao hull and the Large Boat Parts Pack submarine tower.
- Offline controller simulation (`Source/Tests/ControllerSim`), which also runs in CI.
