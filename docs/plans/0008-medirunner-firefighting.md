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

## Open questions

None.
