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
| Identity | packageId `josh9817.quadrunner`, author Joshtrazyn, C# namespace `Quadrunner`, defNames `QR_`, log prefix `[Quadrunner]` (user confirmed, 2026-10-19) |
| Context hand-off | At 250k tokens of context a session stops, writes a hand-off doc in `docs/handoff/` and launches a new interactive Claude Code instance on it; enforced by project-only hooks (`tools/context-guard.py`, `tools/handoff.sh`) (user, 2026-10-19) |
| Testing | **Fully automated, driven by the agent**: develop → test → analyze the log and write notes → develop. The design's manual checklist phase is dropped (user, 2026-10-19) |
| Test instance | A separate game profile (`-savedatafolder` under `.devlog/`, Core + Harmony + Quadrunner only, Dev mode on), started with `-quicktest` on a private Xvfb display, driven by a launch argument that runs the scenarios and quits. Never the user's profile, saves or window (agent, 2026-10-19, from the user's "never touch my game") |
| Test scenarios | Each scenario builds its own setup in code on the quicktest map (like Queen Bee's `Scenario.cs`) instead of loading one saved colony per scenario as the design proposed, because a hand-built colony would need manual play. Pass = every step passes, zero new red errors, and a save/load step partway through comes back clean (agent default, 2026-10-19; design's pass criteria) |
| Harness language | Python 3 standard library only (no packages, no pip, no Bun packages): the harness is our own code (user, 2026-10-19) |
| Restart in the loop | The test instance stays up between iterations and restarts itself in game with `GenCommandLine.Restart()`, the restart the Options dialog does after toggling Texture Compression; a cold start is only the fallback (user, 2026-10-19) |
| User's game | The agent notifies the user and waits before any step that needs the user's game closed, and never acts on that game itself (user, 2026-10-19) |
| Processes and anomalies | The agent signals no process except the test RimWorld (and the Xvfb display the harness started for it, checked by name before it is stopped). Anything out of the ordinary: ping the user and stop development (user, 2026-10-19) |
| Save backups | Back up the user's profile with `tools/backup-saves.sh` (to `~/RimWorldBackups/`) when the user asks and before anything that could reach it; first backup 2026-10-19 (user, 2026-10-19) |
| Robodog kind | Intelligence `Animal` on the non-organic `QR_Robotic` flesh type: vanilla sees neither an animal nor a mechanoid (`RaceProperties.Animal`, `IsMechanoid`, `IsFlesh` all false), which gives the T1 immunities for free; body `QR_Robodog` from vanilla mechanoid parts. Proven by **Dock and undock** (agent, 2026-10-19) |
| Dog ownership | The dog's `CompRobodog.stable` is the only record; a stable counts its dogs from its map's pawns (docked, out, carried), so out-of-stable dogs keep their slot (agent, 2026-10-19) |
| Full stable | A dog bill doesn't start while all 15 slots are taken (docked or out); a dog finished anyway (slot taken meanwhile) waits beside the stable, unassigned (agent default, 2026-10-19; not in the design) |
| Stable bills | Colonists do them under **Crafting** (Crafting skill, General labor speed), as Queen Bee's bills; the stable needs power for bills like any vanilla workbench (200 W) (agent default, 2026-10-19) |
| Placeholder art | Until T9: the dog draws as the vanilla husky tinted steel blue, the stable as the vanilla comms console (3×2) (agent, 2026-10-19) |
| Battery tunables | `CompProperties_Robodog` on the race: drain 0.000667 per cell walked (10% ≈ 150 cells), seek at ≤ 10% and keep wanting a charge until 100%, power down at 0% (hediff `QR_PoweredDown`: Moving 0, so downed). Conduit 0.0004/tick (empty to full in 2500 ticks), stable 0.0012/tick (3×), so from 10% a conduit wins only when the stable is about 120 cells further; a full conduit charge draws 100 Wd from the net's batteries, fullest first. A jump of more than one cell (teleport) drains nothing (agent default, 2026-10-19) |
| Charge-spot search | Static per-map cache, never saved, rebuilt when the colony's buildings change (position/def/rotation signature) or after 2500 ticks. Each transmitter (conduit) gives at most one spot per qualifying room (its own, or the four sides of the wall it's under): the nearest standable cell without an edifice within 10 cells, dropped if that cell is within N of a turbine or its wind cells. Checked per dog at query time: room still qualifies, no prison cell, bedroom/barracks only with no occupied bed, stored energy > 0, allowed area, no fire or hostile in the room, cell not reserved. `RegionTraverser` from the dog with its `TraverseParms` (forbidden doors block), first 5 spots, score = straight-line distance + 15 × dogs charging in that room − 10 × min(stored Wd / 1000, 1). Choice vs the stable: straight-line distance × `TicksPerMoveCardinal` + missing / rate; an unpowered stable only when no spot is usable. Turbine distance N is a mod setting, 0–15, default 6 (agent default, 2026-10-19) |
| Agent defaults | The user accepted the agent defaults recorded above (Robodog kind, Dog ownership, Full stable / 16th dog, Stable bills, Placeholder art) (user, 2026-10-19) |
| Run beside the user's game | Test runs go ahead while the user's game is open; `autotest.py` no longer refuses and the `--beside-user-game` flag is gone (user, 2026-10-19, after the proving run 23:20: 11/11 PASS, the user's profile untouched by the test instance) |
| Usage limit | When the 5-hour usage limit is hit: Claude Code's built-in auto-continue, plus a status line that records the usage (rate-limit) data (user, 2026-10-19). Not set up yet |
| Dispatch | `StableProps` on the stable: service radius 40 cells (from the stable's position), a check every 250 ticks that sends out at most one idle docked dog (a worker not waiting for a charge, fullest first). The work search needs a spawned pawn, so the dog steps out and docks again in the same tick when it finds nothing; the stable then waits 1000 ticks. A dog that is out keeps taking work in its radius until none is left, then docks (agent default, 2026-10-19) |
| Robodog work | `RobodogWork` extension on the Quadrunner: vanilla work givers in priority order `DeliverResourcesToFrames`, `DeliverResourcesToBlueprints` (both map-wide), `HaulGeneral`, `CleanFilth`; only `HaulToCell`, `HaulToContainer` and `Clean` jobs are taken (a blocked blueprint would ask for plant cutting). A target closer (straight line) to another stable that covers it and has an idle dog is left to that stable; reservations make each job claimed once. The body gets a gripper jaw (`ManipulationLimbCore`): vanilla hauling needs Manipulation (agent default, 2026-10-19) |
| Raid | `GenHostility.AnyHostileActiveThreatToPlayer`, cached a second per map. Dogs with a stable take no new work and are dispatched by none; move speed x3 (`StatPart_RobodogRaid` on MoveSpeed, patched in) and drain per cell x2 (`raidDrainFactor`), then they dock (agent default, 2026-10-19) |
| Stable repair | Docked dogs' injuries heal 0.02 HP per tick (first injury first) while the stable has power, like charging (agent default, 2026-10-19) |
| Stable destroyed or uninstalled | Docked dogs are dropped at the interaction cell. Destroyed (told apart from minify by a `Destroy` override): every dog of the stable is unassigned; a dog without a stable adopts the nearest reachable stable with a free slot (not in the design) or waits. Uninstalled: dogs keep it and walk to it once it's installed again. Waiting = in the nearest indoor room (region search) that isn't a bedroom, barracks or prison, `Wait` 300 ticks at a time (agent default, 2026-10-19) |
| Harmony patch 1 | Prefix on `GenConstruct.CanConstruct(Thing, Pawn, WorkTypeDef, bool, JobDef)`: for a robodog, skip the `pawn.workSettings` check (null on a dog: NRE) and skills; the design's "worker-skill fixes" (agent, 2026-10-19) |
| DLC check | No DLC is owned; before release one tester who owns the DLCs runs the scenarios (design) |

## Targets

Tick a box only after an automated run proved it; name the run in [`docs/test-runs.md`](../test-runs.md). Scenario names are suggestions until the scenario exists.

### T0: Scaffold and the automated test loop

- [x] Tools ported from Queen Bee and registered in AGENTS.md; decompiled source copied from Queen Bee (current for 1.6.4871)
- [x] `About/About.xml`, `Source/Quadrunner/Quadrunner.csproj`, `QR.cs` (logs `[Quadrunner] loaded build <id>`), `AutoTest.cs`, `Scenario.cs`, `Scenarios.cs`, `DebugActions.cs`; `tools/build.sh` builds with zero warnings
- [x] Harness self-test: `tools/selftest.sh` passes all 13 cases against the fake game (baseline, cold start, in-game restart on a new build, filters, a failing scenario, baseline-hidden and new errors, hang watchdog, stop)
- [x] Error baseline captured from a test-instance run **without** Quadrunner (`tools/autotest.py baseline`)
- [x] First real launch: the instance comes up on Xvfb with the test profile, `Mods/Quadrunner` is linked, the quicktest map announces `ready` with the latest build, and the user's profile (`Player.log`, `Config/`, `Saves/`) is untouched
- [x] **Scenario: Smoke** passes with zero new errors
- [x] **Scenario: Save and reload** passes: save mid-run, load, the tracked steel is found again, ticks resume
- [x] In-game restart: after a new build, `autotest.py run` reports "restarted in game" (no cold-start fallback) and the new build is loaded
- [x] Watchdog: `autotest.py hang` reports `HANG`, kills the instance and archives its log
- [x] Answered and recorded in Decisions: can the test instance run while the user's game is open? Yes (run 2026-10-19 23:20)

### T1: Dogs and the stable

- [x] `QR_Quadrunner` race: own flesh type, no food or rest needs, never rots, immune to disease, psychic drones and animal insanity; can't be tamed, trained, slaughtered or hunted; never spawns wild or in manhunter packs
- [x] Stable building (Microelectronics, 150 steel + 4 components): 15 shared slots, docks and undocks dogs, saves its docked dogs
- [x] "Assemble Quadrunner" bill at the stable; the finished dog docks straight into it
- [x] Overview tab: one row per assigned dog (type, battery, health, current job); battery column checked in **Stable full (16th dog)** (run 2026-10-19 22:27)
- [ ] Debug actions: spawn a dog docked, set battery %, power down a dog, force a dispatch. Done: spawn docked, undock all, set battery % (T2; 0% powers a dog down); a dedicated power-down action and dispatch come with T4 and T3
- [x] Scenarios pass, each with a save/load step: **Assemble and dock**, **Stable full (16th dog)** (also **Dock and undock**; run 2026-10-19 21:48)

### T2: Battery and charging

- [x] Battery drain per cell (about 150 cells for 10%), seek at 10%, power down at 0% (**Battery drain and power-down**, run 2026-10-19 22:27)
- [x] Charge-spot search with all five rules and the scoring, cached per map and rebuilt on conduit, turbine or room changes; debug actions highlight valid spots and dump the cache (**Conduit charging**, **Charging maze**; room changes without a building change are checked per query, see Decisions)
- [x] Stable charging much faster than a conduit; a low dog picks whichever fills it soonest, travel included (**Stable or conduit**: 3×, stable at 10%, conduit at 97%)
- [x] Edge cases: no valid spot (go to the stable), batteries empty mid-charge (search again), room stops qualifying (get up, search again), prisons never, bedrooms only when unused, freezers allowed, allowed areas respected (**Charging maze**, **Batteries drain mid-charge**, **Charging room rules**)
- [x] Scenarios pass: **Charging maze** (no spot, spot behind a forbidden door, turbine beside a qualifying wall), **Batteries drain mid-charge** (run 2026-10-19 22:27, each with a save/load step)

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

- [x] Can the test instance run while the user's game is open (Steam API, Unity, GPU)? Yes: launched directly beside it (both on Steam, test instance on Xvfb :90), ready in 19 s, Run all 11/11 PASS, 0 new errors; the user's `Config/` and `Saves/` unchanged during the run (run 2026-10-19 23:20).
- [x] Does the game render on Xvfb (software GL) fast enough for scenarios? Yes: ready 17 s after launch, both T0 scenarios in 9 s at Ultrafast (run 2026-10-19 21:26).
- [x] Workshop mods (Harmony) load only while Steam runs; is the test instance started outside Steam picked up by it? Yes: launched directly (not through Steam) while Steam ran, `SteamAPI_Init` succeeds and `brrainz.harmony` loads (run 2026-10-19 21:24). `status.sh` checks that Steam runs.
- [x] Identity (packageId, namespace, `QR_` prefix): confirmed by the user as written (2026-10-19).
