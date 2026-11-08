# Plan 0011 — Trap avoidance (peacetime dispatch, wartime standoff)

User-reported (2026-11-07): every robodog type walks into the "Steel Razor Wire"
(`VFES_BarbedWire`, a `Building_TrapDamager`) added by Vanilla Furniture Expanded -
Security, and through other traps, instead of avoiding them. The root cause is recorded
in the session analysis: traps are a soft 800 path cost that the robodogs' inflated
weighted-A* heuristic and autonomous loops (survey, work dispatch, rescue, combat)
routingly override, and no robodog movement or dispatch decision ever consults
`PawnUtility.KnownDangerAt`.

## Goal

Robodogs treat known traps (`Building_Trap` edifices the player faction is not hostile
to — spike traps, IEDs, VFES razor wire) as ground to avoid:

- **Peacetime work dispatch**: paths route around traps, targets are not taken when the
  only route crosses a trap, and survey never goals or crosses one.
- **Wartime**: pursuit never wades in — the dog stops **one tile before** the first trap
  on its route and holds there calmly (re-checking every `Combat.RecheckTicks`), fighting
  once the target can be reached without crossing; trap-locked rescue/fire/recovery work
  is deferred rather than taken through the wire.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | Peacetime avoidance is a **+2000 per-trap-cell path cost** attached to every robodog `PathFinder.CreateRequest` (Harmony patch 7, postfix on the 8-arg overload used by `Pawn_PathFollower`), not a blocked (10000) grid. | A blocked grid desyncs region `CanReach` (which ignores customizers) from `PathFinderJob` and strands dogs; a finite cost keeps liveness (crossing when no alternative exists, e.g. reaching its own charger) while preferring detours up to ~150 cells. Vanilla has no traverse flag or virtual for trap avoidance, so a patch is the only hook that reaches every path a dog takes. |
| 2 | The customizer applies only when the pawn is an undrafted, `ByPawn` robodog on a map whose `DogNetwork` is **not at war**, and only when no other mod already set a customizer. | Drafted dogs are player-controlled; war behavior is the standoff (below), and wartime recall/docking may cross traps to get home. Respecting existing customizers (e.g. VEF phasing) avoids stacking grids. |
| 3 | Wartime standoff is implemented at the **job level** in `Combat` (`AttackJob`, `GuardJob`, `PatrolJob`, `CaptureJob`): if the route to the target enters a known trap, the dog walks to the last safe path node **one tile before** the first trap and waits there with `expiryInterval = RecheckTicks`. | The pathfinder cannot express "stop before"; the job level already owns re-check cadence, pack handoff and continuation semantics. Armed Tier 2+ dogs still `AttackStatic` from where they stand (shooting over the wire) — only the walk legs are intercepted. |
| 4 | Patrol waypoints whose route enters a trap count as **passed** (like unreachable ones); guard posts and capture targets across traps hold/defer respectively. | Keeps the patrol flowing instead of stalling every expiry at the same waypoint; captures through wire are deferred (the downed enemy is not ours to reach through a trap). |
| 5 | Trap-crossing **rescue, fire and recovery tasks are deferred** (`retryAt` backoff), never parked at the wire. | A medic standing at a wire line in the open during a raid is worse than deferring; the task retries and proceeds as soon as the patient/fire/casualty is reachable without crossing, or colonists handle it. |
| 6 | Survey excludes trap cells as goals (`Survey.Usable`) and rejects routes whose nodes enter a trap (`Survey.Accepted`). | Spike traps are `Standable`, so they can otherwise be picked as `PathEndMode.OnCell` survey goals. |
| 7 | Trap cost is the C# constant `TrapAvoidance.PathCost = 2000`; not an XML tunable. | Fixed by this plan (on top of vanilla's 800 `KnowsOfTrap` provider cost; at the dogs' 12-tick step ≈ 150-cell detour horizon). One shared per-map grid, no per-race differences. |
| 8 | No saved state changes. The one Def change is the think tree's idle fallback: `JobGiver_WanderAnywhere` becomes `Quadrunner.JobGiver_WanderAvoidTraps`. | The grid is transient per map (rebuilt on trap spawn/despawn, released with the network's map pruning); the wander swap closes the last peacetime movement (idle wandering) that could force a crossing into a sealed area. |

## Targets

### T1 / P1 — Peacetime path & target avoidance

- [x] Every undrafted robodog's peacetime path request carries the trap-cost grid
      (patch applied, grid rebuilt on trap changes, released on map removal).
- [x] A haul/clean task whose only route crosses a trap is deferred, not assigned; work
      with a trap-free detour is taken via the detour.
- [x] Survey never selects a trap cell as a goal and never routes across one.
- [x] **Scenario `Trap avoidance: peacetime work`**: a granite box whose only door is a
      spike trap, gold inside, dirt outside — the dirt is cleaned, no dog ever stands on
      the trap, and the gold stays inside (task deferred). Fails on unchanged gameplay.

### T2 / P2 — Wartime standoff

- [x] `Combat.AttackJob` melee and cast-position walks, and `Combat.GuardJob` posts,
      hold one tile before the first trap on the route, calmly re-checking.
- [x] Patrol waypoints across traps are skipped; captures across traps are deferred.
- [x] Rescue/fire/recovery routes crossing traps defer the task.
- [x] **Scenario `Trap standoff: wartime pursuit`**: a raider inside the trapped box —
      the Combatdog deploys, stops beside the trap, never touches it, holds for 600+
      ticks, and leaves once the raider dies. Fails on unchanged gameplay.

### T3 — Verification & release

- [x] Both new scenarios PASS with 0 new errors; reproduction run on unchanged gameplay
      failed first.
- [ ] Affected integration (dispatch, survey, combat, rescue, firefighting) passes with
      0 new errors (shared dispatch/combat code changed; full Run all is not claimed —
      its Artwork-showcase stall is a known open issue).
- [ ] Package rebuilt, `pkg-run` trap scenarios PASS from `dist/Quadrunner`, records
      committed, pushed to origin.

## Open questions

None.
