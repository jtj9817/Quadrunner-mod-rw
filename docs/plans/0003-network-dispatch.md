# 0003 — Shared robodog dispatch network

## Goal

Replace individual work searches and the stable's 40-cell pickup boundary with one coordinated network per map. Dogs discover work locally; a stable keeps normal and war queues and assigns each task to the nearest available capable dog by reachable travel cost. With no stable, the newest-built active dog coordinates the same queues.

## Decisions

| Area | Decision |
| --- | --- |
| Scope | One network per map, shared by all player robodogs and stables. Normal jobs still use vanilla storage, reservations, construction delivery and cleaning eligibility. No discovery through fog; cleaning still requires Home area. Supersedes plan 0001's work dispatch/radius rules (user, 2026-10-20). |
| Exploration | All otherwise-idle Quadrunners survey revealed reachable areas. Queued tasks preempt survey travel; productive jobs are not interrupted for a nearer assignment. Dogs continue local scans while travelling/working (user choices, 2026-10-20). |
| Discovery | AOE scan radius starts at 15 cells, every 120 ticks; dispatch runs every 60 ticks, with 2500 ticks of lost progress before release. Survey waypoints are 20 cells apart, chosen within 80 cells for each leg (not a limit on map coverage or queued work). These tunables live in XML. Normal tasks enter the queue only after local observation. Emergency rescue and hostile alerts remain available map-wide; Medirunners continue emergency rescue during war. |
| Assignment | Nearest available capable dog, using vanilla pathfinder travel cost. Charging, powered-down, downed, drafted, ridden, carried and upgrading dogs are unavailable. Stable affiliation does not restrict assignments (user choices, 2026-10-20). |
| Coordination | Prefer the oldest surviving spawned stable, otherwise the newest-built active spawned player robodog. Transfer queues on election; preserve construction order across save/load and upgrades. If no eligible node remains, clear both queues and survey history. |
| War | Active hostile threats suspend normal work immediately upon detection: release assignments, safely drop cargo and stand by. Normal queue is retained and revalidated after the threat clears. Deploy available Combatdogs, keeping medical rescue active; no Combatdog means standby, not worker combat. A non-hostile loss requeues unfinished work without inventing a war (user choices, 2026-10-20). |
| Protocol | Task lifecycle: discovered/queued → assigned → acknowledged → working → complete, or back to queued on failure, death, manual interruption, lost progress or an invalid route. Track position, assigned job identity, last report/progress and route cost centrally. Use existing comp/building ticks, not a second daemon or permanent saved map component. |
| Persistence | Queue and survey state are saved only on the elected stable/dog, transferred without duplicating jobs, and restored with current jobs after load. Use stable ThingIDs for queued targets/assignees so destroyed references do not poison saves. Preserve the existing clean-removal contract. |

## Targets

### N1: Discovery and normal queue

- [x] Idle workers survey revealed areas, discover nearby tasks and haul items beyond the former 40-cell boundary.
- [x] Undiscovered, fogged, forbidden and out-of-Home cleaning targets are not assigned; duplicate reports do not duplicate a task.
- [x] Frames, blueprints, hauling, cleaning and dog retrieval execute via the shared queue.
- [x] The nearest available capable dog wins, including across stables; barriers affect route ranking and unreachable workers do not win.

### N2: Coordination and progress

- [x] Stable → other stable → newest active dog → next active dog transitions retain queued work and permit completion without a stable.
- [x] Losing the final node clears both queues; a new node never resurrects the discarded backlog.
- [x] Death/interruption releases unfinished work; assignments and progress survive save/load without duplicate execution.
- [x] Queue mode, coordinator and progress can be inspected in game.

### N3: War and integration

- [x] A threat suspends an active normal job and retains its work across save/load; removal of the threat resumes and completes it.
- [x] Combatdogs receive centralized war assignments, including without a stable; without them workers stand by.
- [x] Medirunners keep rescuing during war; charging, riding, drafting, upgrades and capture remain functional.
- [ ] Shared-code Run all has zero new errors; independent removal check loads, ticks and reloads the cleaned save without Quadrunner.

## Open questions

None. The four material behavior choices were answered by the user before implementation.
