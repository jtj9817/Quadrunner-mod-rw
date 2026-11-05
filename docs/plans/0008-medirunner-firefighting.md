# 0008 — Medirunner firefighting

## Goal

Give Rescuedogs (Medirunners) an integrated foam turret, so they respond to any fire in the Home area and extinguish it (user, 2026-10-25).

## Decisions

| Area | Decision |
| --- | --- |
| Priority | Rescues first (user, 2026-10-25). Fire tasks rank after `Rescue` in the war queue; idle medics take fires. Triage is unchanged. |
| Foam supply | Unlimited foam; each spray burst drains battery by `RobodogWork.foamDrainPerBurst` (XML, 2%) (user, 2026-10-25). The charging loop is the only limit; nothing new is saved. |
| Raids | Fires are fought under fire, like rescues: Fire tasks live in the war queue (user, 2026-10-25). |
| Unlock | Built into every Medirunner, existing and new; no research (user, 2026-10-25). |
| Scope | Spawned fires (attached ones included) on revealed Home-area cells, reported map-wide by the coordinator like the downed. Fires outside Home are never queued. |
| Race | The Medirunner's intelligence is `ToolUser`, as for Combatdogs. `Animal` gives no equipment tracker. It keeps the beat-fire verb (`Pawn_NativeVerbs`). |
| Mechanism | `QR_Gun_FoamSprayer`, a built-in gun (Combatdog pattern) with vanilla `Gun_TurretFoam`'s spray and `Proj_Foam`, held via `RobodogWork.builtInGun` and fitted on undock. `Verb_FoamSpray` charges the battery per burst. Vanilla `AttackStatic`/`Goto` jobs, re-issued by the network like combat orders. No Harmony patch. Medirunners have no auto-attack think node, so the sprayer is never turned on enemies. |
| Existing saves | One-time in-place migration (user, 2026-10-26), not replacement pawns. Vanilla's post-load traversal calls each Medirunner's existing comp, including docked, carried and world pawns. Initialize only missing equipment/apparel trackers and fit the sprayer on living medics. The vanilla trackers persist as the completion marker; later loads leave them and existing weapons unchanged. No recurring sweep, new saved component or Harmony patch. |
| Continuous response | On deployment and at native job boundaries, select the nearest reachable, unclaimed revealed Home fire from the shared queue, retaining a valid current focus. No homeward leg between fires (user, 2026-11-05; analogous to combat commit `1ebcbc4`). Rescue priority and single-assignee ownership remain unchanged. |
| Standby and storms | After firefighting, wait in place for three game seconds (180 ticks), check again for fires, then return. Lightning-producing current weather or an active vanilla Flashstorm condition keeps the deployed responder on alert between ignitions; it does not deploy idle docked medics without work. After the storm ends, observe the ordinary three-second quiet period. Charging still takes precedence. Persist the response/quiet deadline across reloads and clear it on docking (user, 2026-11-05). |

## Targets

### F1 — Foam firefighting

- [x] Home fires are extinguished by the Medirunner's sprayer; nothing outside Home is queued.
- [x] Each burst drains battery.
- [x] A rescue outranks a fire.
- [x] Fires are fought during a raid.
- [x] Clean save/load in the middle of the task.
- [x] Existing rescue and removal scenarios still pass, with 0 new errors.
- [x] A screenshot of the sprayer on the dog's back mid-spray, inspected.

Evidence: [test-run log](../test-runs.md), 2026-10-25 20:16–20:39. The **Medirunner firefighting** scenario passed 1/1 standalone and 7/7 in sequence with the rescue, wartime and removal scenarios, 0 new errors (build `3b98c39-dirty.20261025T203749Z`). Run all at 20:36 was 87/88: everything except a firefighting fixture race, fixed by 20:37. Capture `.devlog/screens/medirunner-foam.png` was inspected.

### F2 — Pre-sprayer save migration

- [x] Legacy Medirunners gain both ToolUser trackers and a sprayer on load, including docked, field, carried and caravan instances.
- [x] Preserve ThingID/name, battery, health, stable affiliation, field orders and carried pawns; leave modern medics and utility dogs unchanged.
- [x] A second save/load retains the same sprayer instances with no duplicate equipment or repeated migration.
- [x] A migrated medic physically extinguishes a Home fire and docks again; rescue compatibility and sequential removal preparation pass with zero new errors.

Evidence: [test-run log](../test-runs.md), 2026-10-26 05:09–05:15. The new **Medirunner legacy migration** failed before the fix (0/1, missing trackers), then passed in the scoped batch (7/8; only removal failed on fixture cleanup). After discarding the test's retained world pawns, migration followed by **Prepare removal** passed 2/2 with zero new errors (`b8abf34-dirty.20261026T051424Z`). All six other medic/rescue scenarios passed in the earlier batch. The raw log records four migrations on the first load and none on the second.

Independent removal at 05:16 loaded the cleaned export with no Quadrunner assembly, ticked 600, saved/reloaded and ticked another 600, retaining the expected vanilla contents with zero new errors. Log: `.devlog/runs/removal-20261026-051626/Player.log`.

Packaged proof at 05:18: actual `dist/Quadrunner` build `8771e7f.20261026T051738Z` passed **Manual rescue dispatch**, **Medirunner legacy migration**, **Medirunner firefighting** and **Prepare removal**, 4/4 with zero new errors. The harness checked the loaded BUILD and resolved mod root. Private game/Xvfb stopped and the dev symlink restored.

### F3 — Continuous nearest-fire response and storm alert

- [x] A deployed medic chooses the nearest reachable unclaimed Home fire, not the oldest queued fire, and retains a valid focus.
- [x] Native job transitions immediately reacquire remaining fires without a homeward gap, including after reload and while travelling into spray range.
- [x] After the last fire, hold position for at least 180 ticks, recheck, then dock; a new fire during standby resumes response and resets the quiet period.
- [x] Lightning weather and an independent Flashstorm condition keep responders deployed beyond the quiet period; new fires are handled from the field, including across save/load.
- [x] After the weather/condition ends, the ordinary quiet period and docking resume; low battery overrides storm alert.
- [ ] Existing firefighting, rescue precedence, legacy migration and affected shared-dispatch scenarios pass with zero new errors.
- [ ] The rebuilt actual package passes the affected response scenarios with loaded BUILD and modRoot verified.

Focused evidence: 2026-11-05 06:58, `fb7cd93-dirty.20261105T065800Z`, all four new response cases **4/4 PASS, zero new errors**. Native job observation proves the next fire is acquired without homeward travel; actual foam extinguishes the staged fires. Saves cover active response, the positive quiet deadline and both storm alerts. Shared integration and actual-package criteria remain open.

## Open questions

None.
