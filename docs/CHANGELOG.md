# Changelog

All notable, player-facing changes to Quadrunner. The mod is not versioned by
tags; each shipped package is identified by the build id in
`dist/Quadrunner/BUILD` and by its verification row in
[docs/test-runs.md](test-runs.md). Saved-game identity is stable
(`josh9817.quadrunner`); every save-format change below migrates old saves in
place on load.

## [Unreleased]

### Fixed
- Robodogs no longer walk into known traps (spike traps, IEDs, and Vanilla Furniture
  Expanded - Security's razor wire). In peacetime their paths, work dispatch and survey
  route around traps, and work whose only route crosses one (hauling, cleaning, rescue,
  firefighting) is deferred until a trap-free route exists. In wartime a pursuing
  Combatdog stops one tile short of the first trap on its route and holds there calmly,
  re-checking every second, instead of charging through; patrol waypoints across traps
  are skipped and captures/rescues through them are deferred. Code review tightened two
  edges after the first package: an idle dog whose only wander destinations crossed a
  trap now waits in place instead of logging an internal think-tree error, and a
  firefighter whose approach walk is rebuilt mid-fight respects traps on every rebuild,
  not only when the fire is first claimed. Development 8/8 affected scenarios PASS,
  0 new errors; both fixes ship in package `a43e7f7.20261108T094607Z`.

### Added
- Interactive system diagrams at [docs/diagrams/index.html](diagrams/index.html):
  per-subsystem pages, three behaviour flows (haul, raid, charge decision) and a
  requirement-to-verification traceability view, generated from
  [docs/diagrams/model.json](diagrams/model.json).
- This changelog.

### Changed
- `docs/architecture.md`'s code map now lists `Firefighting.cs` and `Hose.cs`
  (added by plans 0008/0010 after the table was written).

## [2026-11-07] — Medirunner deployment fix

### Fixed
- Colonists no longer rescue downed or powered-down Quadrunners into animal
  beds or sleeping spots, and any animal bed previously claimed by a robodog in
  an existing save is released on load. Non-combat robodogs downed by leg
  injuries (with battery remaining) are now also retrieved to a stable for
  powered repair and offer right-click **Carry to stable** (user-reported;
  package `1bb831f.20261107T082751Z`, 11/11 scenarios, 0 new errors).
- Docked Medirunners no longer undock speculatively every 2–3 seconds when the
  queues hold only work they cannot take (a downed animal with no reachable
  bed, a reserved or unreachable patient or fire, a powered-down Combatdog
  awaiting a colonist battery), and surplus Medirunners no longer deploy for a
  single task. Zero-spawn pre-checks reject impossible work before any dog
  leaves the stable (user-reported; package `62ae7cd.20261107T011508Z`, 12/12
  scenarios, 0 new errors).

## [2026-11-06] — Worker water hose

### Added
- Quadrunners carry an integrated water hose: discovered contiguous Home-area
  patches of four or more blood or firefoam tiles are sprayed in a broad band
  (about 11 cells wide, 7 deep — the foam sprayer's footprint) at 1.5× normal
  cleaning work per affected tile, continuing through the whole patch. Spray is
  blocked by walls; smaller patches and other filth keep using ordinary
  cleaning; removing foam also removes its fire protection.

## [2026-11-05] — Continuous fire response

### Changed
- Medirunners now choose the **nearest reachable unclaimed fire** instead of
  report order, hand off natively from fire to fire without returning home, and
  keep their travel intent across saves. After the last fire they stand by for
  three game seconds and recheck before docking, and during thunderstorms or
  flashstorms deployed responders stay on alert between ignitions (package
  `8fa0518.20261105T081453Z`, 13/13 scenarios, 0 new errors).

## [2026-11-03] — Combat packs and Tier 4 arsenal

### Added
- Nearby Combatdogs of mixed tiers form packs led by Tier 3/4: the pack focuses
  the queued standing hostile nearest to any member, keeps a valid focus, and
  re-targets at native job boundaries so pursuit never gaps into a homeward
  guard (C1–C4; package runs 3/3 then 10/10, 0 new errors).
- Tier 4's fixed arsenal: **two charge rifles** plus **two improved chain
  shotguns** (20.9-cell range) firing two independent shots per burst, and an
  automatic **smoke launcher** that fires independently of the primary stance.

### Changed
- Tier 3/4 chassis speeds raised to 1.5×/1.75× base (the deployed 2× boost is
  unchanged).
- Existing Tier 3/4 dogs are migrated in place on load: identity, battery,
  health, affiliation, cargo and holder state are preserved, and modern
  loadouts are fitted without regenerating pawns.

## [2026-10-29] — Rebuild-bill fixes

### Fixed
- Manual **Rebuild Combatdog** bills can use recovered wrecks — including
  previously forbidden wrecks in old saves — without competing with automatic
  rebuilding or already-paid rebuilds; five colonist bills rebuilt five dogs
  across save/load with exact whole-map material accounting.

## [2026-10-27] — Dispatch allocation fix

### Fixed
- Duplicate work discovery no longer allocates in proportion to the existing
  backlog (24,576 B of task storage with 256 queued reports instead of
  6,291,456 B); the network caches each target's `ThingID` once per scan.

## [2026-10-26] — Medirunner migration and dispatch churn

### Added
- Pre-sprayer Medirunners are migrated in place on load: missing vanilla
  equipment trackers are restored and the foam sprayer fitted, including held
  and off-map (caravan) dogs, without replacing pawns; a second load changes
  nothing.

### Fixed
- Rescuedogs no longer cycle in and out of the stable, and they take queued
  rescues and fires at once instead of being held back up to two seconds or
  indefinitely after a retry.

## [2026-10-25] — Medirunner firefighting

### Added
- Every Medirunner carries a built-in **foam sprayer**. Revealed Home-area fires
  are reported to the war queue and handled **after rescues, during raids too**;
  the nearest available medic sprays them at 2% battery per burst. Fires outside
  the Home area are ignored.

## [2026-10-24] — Manual rescue, wartime recall and Combatdog recovery

### Added
- Select a stable and click **Dispatch Rescuedogs** to refresh the shared
  dispatcher immediately (even while paused) and retry waiting rescues; missing,
  charging or no-patient states disable the action with feedback.
- Workers **return and dock during hostile events** when their home is
  reachable, waiting in place otherwise and resuming revalidated work when the
  threat clears.
- Combatdogs broadcast their downing/death and last-known coordinates: after
  hostilities, healthy workers and medics recover living casualties and wrecks
  ahead of ordinary utility work. Living dogs dock for powered repair and
  charging; wrecks rebuild automatically from **50 steel + 25 plasteel
  physically delivered** into the 3×3 area around the stable's interaction cell
  over 2,000 powered ticks, retaining identity, tier and weapon choices.

## [2026-10-23] — Area-aware surveying and dock indicators

### Added
- Idle surveying became area-aware: Home rooms, stockpiles, growing and other
  zones are swept before the exterior, routes are ranked by real path cost, and
  coverage is credited only by actual revealed scans — finite, scan-certified
  sweeps (plan 0004 / QR-0001, 75/75 scenarios, 0 new errors).
- The stable's three bays show **dog-type icons with live dock occupancy
  lights**: docked types bright with green lights, absent types dim, occupied
  but unpowered bays amber, in all four rotations.

## [2026-10-21] — Shared dispatch network

### Changed
- Work dispatch was centralized (plan 0003): every robodog on a map reports what
  it sees to **one shared dispatcher** with normal and war queues, and jobs go
  to the **nearest available capable dog by reachable travel cost** — the old
  per-stable service radius is gone. Queues survive coordinator loss and
  save/load; a surviving stable coordinates the map, and without one the
  newest-built active dog takes over. Losing every stable and active dog clears
  the queues.

## [2026-10-20] — Initial release

### Added
- The complete mod (plan 0001, targets T0–T9): the **Quadrunner stable**
  (Microelectronics, 200 W, 15 shared slots) with assembly bills and the
  Overview tab; **Quadrunners** that survey, haul, clean and supply
  blueprints/frames; **Medirunners** that rescue the downed (colonists, guests,
  prisoners, colony animals; fastest-bleeding first) with a flashing red tail
  siren; **Combatdogs** in four tiers with attack pings, patrols, capture,
  drafting, low-battery retreat and stable-ordered tier upgrades.
- Energy model: battery drain per cell, seek charge at 10%, power down at 0%,
  stable charging (fast) versus safe conduit spots on **stored battery energy**
  (slow), powered-down retrieval by workers or colonists, field battery and
  repair-kit delivery, raider carry-off of dogs at 0%.
- Riding: right-click **Ride to…**, rider moves at the dog's speed, safe
  dismount on arrival, indoors, drafting or power-down.
- Wartime behavior: workers finish at deployed speed with doubled drain and
  dock; war suspends normal work and revalidates it after the threat.
- Clean removal: **Prepare save for removing Quadrunner** exports a save with
  no mod content or references, verified by an independent observer process
  (load → 600 ticks → save/reload → 600 ticks, zero new errors).
- Hearthline Robotics lore and practical tooltips on all six models, both field
  items, all four built-in weapons and all 11 bills.
- Presentation: per-part walk animation matching stride to speed, camo/black
  liveries, rescue and combat action animations, and Tier 2–4 weapon demo
  scenarios.
- Battery range set to about **3,500 cells per full charge** (≈350 cells left at
  the 10% threshold).

### Changed
- In-game **Scenario: Run all** now executes scenarios sequentially on the
  current map without automatic save/load; the harness path keeps every
  persistence check.
