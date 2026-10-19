# Plan 0001: Quadrunner

Design: [`docs/design.md`](../design.md) (snapshot of the living doc, rev 39). Primary reference: [RimWorld Wiki: Modding Tutorials](https://rimworldwiki.com/wiki/Modding_Tutorials).
Target game: RimWorld **1.6.4871**, Core only (no DLC), Harmony the only dependency.

## Goal

Stable-dispatched robot dogs for vanilla RimWorld 1.6: **Quadrunners** haul, clean, supply construction and carry riders; **Medirunners** rescue the downed; **Combatdogs** (tiers 1–4) answer pings and raids. Dogs charge at conduits or their stable, power down instead of dying when empty, and are carried home. Every behavior is proven by an automated scenario the agent runs itself, with zero new errors and a clean save/load partway through.

## Decisions

The design doc's tables (overview, costs, edge cases, Harmony list) are decisions too; this table repeats the load-bearing ones and records everything decided since.

| Topic | Decision |
| --- | --- |
| Game and toolchain | RimWorld 1.6 only: `net472`, `Krafs.Rimworld.Ref` 1.6.4871 and `Lib.Harmony` 2.4.3, both compile-only (`ExcludeAssets="runtime"`); the Harmony mod (workshop 2009463077) supplies 0Harmony at runtime (design) |
| Dependencies | Harmony only. No DLC, no Giddy-Up, no frameworks; code never touches DLC systems (design) |
| Units | Quadrunner (base dog), Medirunner (medic), Combatdog (tiers 1–4) (design) |
| Research | Microelectronics only, for every dog, every Combatdog tier and the stable (design) |
| Crafting station | The stable itself: dog, battery and repair-kit bills; Combatdog upgrades from its Overview tab. Not the fabrication bench, which would need Fabrication research (design) |
| Costs | As in the design's cost table: Quadrunner 80 steel / 30 plasteel / 1 advanced component, Medirunner 80 / 50 / 2, Combatdog T1 100 / 50 / 2, upgrades T2 100 / 75 / 2, T3 150 / 100 / 3, T4 200 / 150 / 4, battery 20 steel + 1 component, repair kit 30 steel + 10 plasteel, stable 150 steel + 4 components (design, "proposed starting costs") |
| Flesh type | Our own flesh type with the Mechanoid type's properties (no bleeding, no pain, mechanical wounds). Reusing vanilla `Mechanoid` is the fallback, but game code treats it as "hostile mech" (design) |
| Battery | Seek a charger at **10%**, power down at **0%**. Drain per cell tuned so 10% lasts about 150 cells (design) |
| Charging | Lie on a standable cell in a roofed indoor room whose conduit (inside or in a bordering wall) sits on a network with stored battery energy, not within N cells of a wind turbine or its clear area (N a mod setting, default 6). Found by `RegionTraverser`, scored by path distance (highest weight), stored energy, dogs already there, danger; cached per map, rebuilt when conduits, turbines or rooms change (design) |
| Retrieval | Powered down is a condition, not destruction. The stable dispatches a healthy dog to carry it home; colonists get "Carry to stable" and auto-deliver a robodog battery or repair kit to it when one is in stock (design) |
| Riding | "Rider drives, dog carries": a stat part on move speed, a "being ridden" job, a dismount comp, a Harmony draw-offset patch. Riders dismount when drafted. Auto-mount for long trips is phase 2, outside this plan (design) |
| Combatdogs | Deploy on ping, raid or order; 200% move speed while deployed; nearest-first target queue per stable; capture downed targets; patrol the stable's waypoints; retreat at 10% with a yellow siren; draftable. Tiers are separate models; upgrades keep the name and install the tier's gun (design) |
| Harmony budget | About six patches, listed in the design (draw offset, auto-mount, worker-skill fixes, raider carry-off, Tier 3 second mount, drafting). Each new patch is justified here when added (design) |
| Identity | packageId `josh9817.quadrunner`, author Joshtrazyn, C# namespace `Quadrunner`, defNames `QR_`, log prefix `[Quadrunner]` (agent default following Queen Bee, 2026-10-19; confirm or change before T0's About.xml) |
| Testing | **Fully automated, driven by the agent**: develop → test → analyze the log and write notes → develop. The design's manual checklist phase is dropped (user, 2026-10-19) |
| Test instance | A separate game profile (`-savedatafolder` under `.devlog/`, Core + Harmony + Quadrunner only, Dev mode on), started with `-quicktest` on a private Xvfb display, driven by a launch argument that runs the scenarios and quits. Never the user's profile, saves or window (agent, 2026-10-19, from the user's "never touch my game") |
| Test scenarios | Each scenario builds its own setup in code on the quicktest map (like Queen Bee's `Scenario.cs`) instead of loading one saved colony per scenario as the design proposed, because a hand-built colony would need manual play. Pass = every step passes, zero new red errors, and a save/load step partway through comes back clean (agent default, 2026-10-19; design's pass criteria) |
| Harness language | Python 3 standard library only (no packages, no pip, no Bun packages): the harness is our own code (user, 2026-10-19) |
| Restart in the loop | The test instance stays up between iterations and restarts itself in game with `GenCommandLine.Restart()`, the restart the Options dialog does after toggling Texture Compression; a cold start is only the fallback (user, 2026-10-19) |
| User's game | The agent notifies the user and waits before any step that needs the user's game closed, and never acts on that game itself (user, 2026-10-19) |
| DLC check | No DLC is owned; before release one tester who owns the DLCs runs the scenarios (design) |

## Targets

Tick a box only after an automated run proved it; name the run in [`docs/test-runs.md`](../test-runs.md). Scenario names are suggestions until the scenario exists.

### T0: Scaffold and the automated test loop

- [x] Tools ported from Queen Bee and registered in AGENTS.md; decompiled source copied from Queen Bee (current for 1.6.4871)
- [x] `About/About.xml`, `Source/Quadrunner/Quadrunner.csproj`, `QR.cs` (logs `[Quadrunner] loaded build <id>`), `AutoTest.cs`, `Scenario.cs`, `Scenarios.cs`, `DebugActions.cs`; `tools/build.sh` builds with zero warnings
- [x] Harness self-test: `tools/selftest.sh` passes all 13 cases against the fake game (baseline, cold start, in-game restart on a new build, filters, a failing scenario, baseline-hidden and new errors, hang watchdog, stop)
- [ ] Error baseline captured from a test-instance run **without** Quadrunner (`tools/autotest.py baseline`)
- [ ] First real launch: the instance comes up on Xvfb with the test profile, `Mods/Quadrunner` is linked, the quicktest map announces `ready` with the latest build, and the user's profile (`Player.log`, `Config/`, `Saves/`) is untouched
- [ ] **Scenario: Smoke** passes with zero new errors
- [ ] **Scenario: Save and reload** passes: save mid-run, load, the tracked steel is found again, ticks resume
- [ ] In-game restart: after a new build, `autotest.py run` reports "restarted in game" (no cold-start fallback) and the new build is loaded
- [ ] Watchdog: `autotest.py hang` reports `HANG`, kills the instance and archives its log
- [ ] Answered and recorded in Decisions: can the test instance run while the user's game is open?

### T1: Dogs and the stable

- [ ] `QR_Quadrunner` race: own flesh type, no food or rest needs, never rots, immune to disease, psychic drones and animal insanity; can't be tamed, trained, slaughtered or hunted; never spawns wild or in manhunter packs
- [ ] Stable building (Microelectronics, 150 steel + 4 components): 15 shared slots, docks and undocks dogs, saves its docked dogs
- [ ] "Assemble Quadrunner" bill at the stable; the finished dog docks straight into it
- [ ] Overview tab: one row per assigned dog (type, battery, health, current job)
- [ ] Debug actions: spawn a dog docked, set battery %, power down a dog, force a dispatch
- [ ] Scenarios pass, each with a save/load step: **Assemble and dock**, **Stable full (16th dog)**

### T2: Battery and charging

- [ ] Battery drain per cell (about 150 cells for 10%), seek at 10%, power down at 0%
- [ ] Charge-spot search with all five rules and the scoring, cached per map and rebuilt on conduit, turbine or room changes; debug actions highlight valid spots and dump the cache
- [ ] Stable charging much faster than a conduit; a low dog picks whichever fills it soonest, travel included
- [ ] Edge cases: no valid spot (go to the stable), batteries empty mid-charge (search again), room stops qualifying (get up, search again), prisons never, bedrooms only when unused, freezers allowed, allowed areas respected
- [ ] Scenarios pass: **Charging maze** (no spot, spot behind a forbidden door, turbine beside a qualifying wall), **Batteries drain mid-charge**

### T3: Dispatch and work

- [ ] The stable scans its service radius every few seconds and releases one dog at a time; construction delivery is map-wide
- [ ] Quadrunners haul, clean and supply blueprints and frames (Harmony only where vanilla assumes human skills or work priorities)
- [ ] Overlapping radii: the stable with the closest idle dog dispatches; each job is claimed once
- [ ] Raid: dogs finish their task at 3x speed with faster drain, then dock
- [ ] Stable loses power (dispatch continues, repair and charging pause), destroyed (dogs wait in the nearest non-bedroom room), uninstalled (dogs wait, then walk to it once reinstalled)
- [ ] Scenarios pass: **Haul and clean**, **Two stables**, **Stable fire with docked dogs**, **Stable uninstall and reinstall**

### T4: Power-down and retrieval

- [ ] Powered-down dogs lie like downed pawns; the stable spots them and sends a healthy dog to carry each to the nearest stable with a free slot; colonists get "Carry to stable"
- [ ] Robodog battery and repair kit bills; colonists auto-deliver them to powered-down or damaged dogs in the field
- [ ] Carrier hits 10% (finish if the stable is closer than a charge spot, else set down and charge), dog in a fire (put it out, then carry), unreachable (alert with jump-to)
- [ ] Destroyed dog leaves a wreck, rebuilt at the stable for about half the recipe
- [ ] Raiders can carry off a dog at 0% (Harmony)
- [ ] Scenarios pass: **Field battery and repair kit**, **Raider carries off a dog**, **Retrieval under fire**

### T5: Medirunner

- [ ] Medirunner recipe and race; rescues the downed to a bed: colonists, then guests, prisoners, colony animals, never enemies; fastest-bleeding first; one Medirunner per patient; allowed under fire
- [ ] Scenario passes: **Mass-casualty raid** (three downed colonists, one Medirunner)

### T6: Riding

- [ ] Right-click "Ride" on a Quadrunner (`FloatMenuOptionProvider`); the rider moves at the dog's speed (stat part); the dog follows (being-ridden job)
- [ ] Auto-dismount at the destination, indoors, when drafted or downed; dog at 0% while ridden (rider dismounts safely, retrieval starts)
- [ ] Rider drawn on the dog's back, per-direction offsets in XML (Harmony), tuned with TweakValues and checked by screenshot
- [ ] Scenario passes: **Ride until 0%**

### T7: Combatdog Tier 1, pings and patrols

- [ ] Combatdog T1 recipe and race: claws (cut, scratch), 200% move speed while deployed, tool-user intelligence
- [ ] Any dog hit by a hostile pings its stable; the stable sends a docked or returning Combatdog regardless of radius; queue nearest first, retarget each tick, redirect while heading home, not while retreating
- [ ] Capture downed targets to a free prisoner bed, else the nearest empty non-bedroom room; skip the dead
- [ ] Stable patrol waypoints walked in order while deployed
- [ ] Low battery mid-fight: retreat with the yellow siren light, ignore pings until charged
- [ ] Draftable with priority targets (Harmony)
- [ ] Scenarios pass: **Ping chain**, **Combatdog sweep** (captures with and without a free bed, patrol), **Low-battery retreat**, **Drafted priority target**

### T8: Combatdog tiers 2–4

- [ ] Upgrades ordered from the Overview tab rebuild the dog as the next tier, keep its name and install the tier's gun; built-in weapons are never dropped
- [ ] Tier 2 built-in gun, Tier 3 two slots with the new flamethrower (second mount, Harmony if a custom attack can't do it), Tier 4 vanilla minigun with its speed penalty
- [ ] Scenario passes: **Tier upgrade**

### T9: Polish and release

- [ ] Art for every dog, the stable and the UI (SVG in `Art/`, `tools/export-textures.sh`), preview and icon
- [ ] Removing the mod from a save is clean
- [ ] **Run all** passes on the packaged copy; a DLC-owning tester runs the scenarios

## Open questions

- [ ] Can the test instance run while the user's game is open (Steam API, Unity, GPU)? Until T0 answers it, the agent asks the user to close their game before every test run.
- [ ] Does the game render on Xvfb (software GL) fast enough for scenarios? Fallback: a visible window on the user's display, only with the user's OK.
- [ ] Workshop mods (Harmony) load only while Steam runs; is the test instance started outside Steam picked up by it? `status.sh` should check that Steam runs.
- [ ] Identity (packageId, namespace, `QR_` prefix): confirm the agent defaults before T0 writes About.xml.
