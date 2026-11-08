# SubTrim

**Automatic neutral trim and depth hold for Kerbal Space Program submarines.**

KSP submarines are hard to keep level. A boat that is a few percent too heavy sinks, and one that is too light pops to the surface. BDArmory's Submarine AI steers by pitching, so it cannot fix that. SubTrim watches every loaded submarine and pumps the boat's own ballast to stay close to neutral, biased toward the depth the AI (or you) wants. The AI keeps flying the boat; SubTrim makes sure its dive planes have something to work with.

<!-- Screenshot placeholder: add docs/paw.png (the "SubTrim depth & trim" PAW group on a submerged boat) -->
> _Screenshot coming soon: the SubTrim panel on a submerged Balao._

## Features

- **Works with BDArmory's Submarine AI out of the box.** SubTrim reads the AI's combat depth and follows the same depth choices the AI makes: periscope depth (-10 m) to launch missiles, and the surface for deck-gun runs.
- **Uses the ballast the boat already has**, from five systems (see below). Boats with no ballast at all get a rate-limited "virtual ballast" that costs EC.
- **Stays near neutral.** Trim is capped at ±2 % of vessel weight while submerged, so the AI's pitch control keeps authority. Far from the target depth, SubTrim only ever pushes toward it and never against the AI.
- **Safety first.** It surfaces on *Emergency blow*, when EC runs low (and dives again once recharged), and when HullBreach reports serious flooding. It never holds deeper than 85 % of the boat's crush depth (the shallowest `part.maxPressure`), or closer than 15 m to the sea floor.
- **Manual mode.** Set a depth on the PAW, step it with action groups, or switch SubTrim on for any vessel.
- **Cheap.** It runs at 10 Hz in FixedUpdate, does no per-tick allocations, and its cost grows linearly with the number of ballast tanks.

## Supported ballast systems

They are used in this order; a boat may combine several.

| # | Mod | How SubTrim drives it |
|---|---|---|
| 1 | **KerbalPowers Naval** (`ModuleShipBallast`) | Sets the ballast's target flooding, so KP's pump rate and EC cost still apply. With `kpNativeDepthHold = true` it only hands KP the target depth. |
| 2 | **BuoyancyAdjuster** | Changes the buoyancy level, but only when BuoyancyAdjuster is switched on for that vessel. It has exclusive control. |
| 3 | **WildBlueTools** (`WBIBallastTank`) | Floods or vents the tank at WBI's own fill and vent rates. The WBI dive computer's depth hold is paused while SubTrim drives and restored afterwards. |
| 4 | **Ballast-water resources** (default `BallastWater`, e.g. MNWS, Large Boat Parts Pack) | Floods for free when submerged and pumps out at 1 % of tank capacity per second, paying EC. Add more resources with `BALLAST_RESOURCE` nodes. |
| 5 | **Virtual ballast** (no ballast mod) | Scales the hull parts' stock buoyancy between 0.2× and 1.5× at 1 %/s, paying EC. |

Optional readers: **BDArmory Plus 1.13+** (Surface AI depth and Submarine mode) and **HullBreach 1.8+** (flooding emergencies and submarine detection).

## Requirements and compatibility

- **KSP 1.12.x** (built and checked against 1.12.5).
- **ModuleManager** (for the PAW panel and the submarine patches).
- Optional: **BDArmory Plus 1.13 or newer**. `SubTrim.BDArmory.dll` is a small bridge that KSP loads only when BDArmory is present; without BDArmory it is skipped and the rest of SubTrim works normally.
- Optional: KerbalPowers Naval, BuoyancyAdjuster, WildBlueTools, HullBreach 1.8+. Each is found at run time; nothing breaks if it is missing.

## Installation

1. Download `SubTrim-<version>.zip` from [Releases](https://github.com/Skizzy-create/SubTrim/releases).
2. Copy `GameData/SubTrim` into your KSP `GameData` folder, so you end up with `Kerbal Space Program/GameData/SubTrim/Plugins/SubTrim.dll`.
3. Install ModuleManager if you do not have it.

Once SubTrim is indexed on CKAN, `SubTrim` can be installed from there too (`SubTrim.netkan` in this repo is the CKAN metadata).

## Usage

### Automatic

Give a submarine a BDArmory **Surface AI** set to **Submarine** and a **CombatAltitude** (for example -50 m), then activate the AI. SubTrim takes over the ballast and holds the AI's depth; no manual trimming is needed. Hulls flagged by the included patches (MNWS SSBN002, Iron Seas KANES Balao, Large Boat Parts Pack submarine tower) and HullBreach submarines are managed even without an AI.

### PAW panel

Every command part gets a collapsed group **"SubTrim depth & trim"**:

| Field | Meaning |
|---|---|
| **SubTrim** | *Auto (submarines)*: manage only boats detected as submarines. *On*: manage this vessel regardless. *Off*: never touch it. |
| **Depth target** | *AI / hold*: follow the BDArmory AI, or hold the depth where SubTrim took over. *Manual (below)*: use **Manual depth**. |
| **Manual depth** | 0–400 m in 5 m steps; 0 means "stay surfaced". |
| **Status / Trim / Ballast** | What SubTrim is doing, the current net buoyancy in % of weight, and the ballast systems in use. |
| **Emergency blow** | Blow all ballast at 5× rate without EC cost, and surface. Press again to cancel. |

Action groups: *SubTrim: toggle on/off*, *emergency blow*, *10 m deeper*, *10 m shallower*, *follow AI depth*.

### Settings

`GameData/SubTrim/SubTrim.cfg` documents every value. KSP reads it once at load. The most useful ones:

| Setting | Default | Meaning |
|---|---|---|
| `maxTrim` | 0.02 | Largest net buoyancy (fraction of weight) allowed while submerged. |
| `maxClimbRate` / `maxDiveRate` | 2 m/s | Vertical speed SubTrim aims for when changing depth. |
| `captureBand` | 15 m | Inside it the speed loop integrates; outside it SubTrim only pushes toward the target. |
| `kp` / `ki` | 0.012 / 0.002 | Speed-loop gains (net buoyancy per m/s). |
| `surfaceReserve` | 0.15 | Reserve buoyancy kept while surfaced. |
| `lowEcFraction` / `resumeEcFraction` | 0.02 / 0.10 | Surface below 2 % EC, dive again above 10 %. |
| `crushSafetyFactor` / `keelClearance` | 0.85 / 15 m | Depth limits. |
| `virtualBallast` | true | Allow virtual ballast on boats without ballast. |
| `kpNativeDepthHold` | false | Let KerbalPowers' own depth logic run. |

### Marking your own submarine

```
@PART[MySubHull]:FOR[SubTrim]
{
	!MODULE[ModuleSubTrim] {}
	MODULE
	{
		name = ModuleSubTrim
		isSubmarine = true
	}
}
```

## How it works

Every 0.1 s SubTrim measures the boat's mass and buoyant capacity, then runs a cascaded controller: depth error → commanded vertical speed (clamped to ±2 m/s) → net buoyancy (PI). The PI part has anti-windup: it integrates only near the target depth, not while a ballast system is saturated, and it is clamped to `maxTrim`. The requested change in lift is handed to the ballast systems in priority order, each limited by its own rates.

## Status

1.0.1 is a bug-fix release (memory leaks; see `CHANGELOG.md`). The controller has been checked in an offline simulation (`Source/Tests/ControllerSim`: 9 scenarios, including AI depth changes, model error, slow and fast pumps, and deep holds). In-game behaviour with each ballast mod still needs wider testing; please report issues with a `KSP.log` (set `debug = true` in `SubTrim.cfg` for more detail).

## Building

Requirements: the .NET SDK (6 or newer) and a KSP 1.12 install.

1. Copy `GameData/BDArmory/Plugins/BDArmory.dll` (BDArmory Plus 1.13.x) to `Source/refs/BDArmory.dll`. It is only used for compiling and must not be committed.
2. Run `package.cmd` (optionally `package.cmd "C:\path\to\KSP"`). It builds both plugins against your KSP install, runs the controller simulation and copies the DLLs into `GameData/SubTrim/Plugins`.

**Releases.** KSP's and Unity's assemblies are proprietary and cannot be uploaded to CI, so the release DLLs are built locally and committed under `GameData/SubTrim/Plugins`. They contain only SubTrim's own code. Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`, which:

1. runs the simulation;
2. checks that the tag, `SubTrim.version`, `AssemblyInfo` and the committed DLLs agree on the version;
3. zips `GameData/SubTrim` as `SubTrim-X.Y.Z.zip` and creates the GitHub release with notes from `CHANGELOG.md`.

To release: bump the version in both `AssemblyInfo.cs` files, `SubTrim.version` and `CHANGELOG.md`; run `package.cmd`; commit; then `git tag vX.Y.Z` and push the tag.

## Credits

- SubTrim by **Skizzy-create**.
- Built to work with the work of others: **BDArmory Plus** (BrettRyland and the BDArmory team), **KerbalPowers Naval**, **WildBlueTools** (Angel-125), **BuoyancyAdjuster** (RedshiftOTF), **HullBreach**, and **ModuleManager** (sarbian). SubTrim contains no code from these mods; it talks to them at run time.

## License

[MIT](LICENSE). `SubTrim.BDArmory.dll` links against BDArmory Plus (GPLv3) at run time. SubTrim's MIT code is GPL-compatible, and no BDArmory code or binary is included here.
