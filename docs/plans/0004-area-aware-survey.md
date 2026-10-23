# 0004 — Area-aware survey routing (QR-0001)

## Goal

Implement [QR-0001](../tickets/QR-0001-area-aware-survey-routing.md) without changing productive dispatch, task eligibility or the Core-only dependency contract. Replace arrival-aged lattice selection with shared, scan-certified finite coverage sweeps and vanilla route-cost area selection. Plan 0003's completed results remain historical evidence, not acceptance for this change.

## Decisions

| Area | Decision |
| --- | --- |
| Authorization | The user's 2026-10-22 request authorizes implementation and incremental commits, superseding the ticket's original documentation-only authorization. |
| Home precedence | Classify Home footprints first: enclosed rooms, player stockpiles/storage buildings, growing zones, other supported Core zones, residual Home. All remaining revealed exterior coverage is last, even if it contains a room or storage. Named allowed Areas are not zones. |
| Room policy | Use native rooms that are proper, not doorways, do not touch the map edge and are not outdoors for work. No bedroom/prison exclusions. Points come from actual geometry, not room centers. |
| Observation | Only a completed existing local scan certifies its actual revealed 15-cell footprint, including empty scans. Keep the 120-tick cadence. Survey arrival, selection, claims and task reports do not certify coverage. An arrival waits interruptibly for the next scan. |
| Sweeps | Keep one finite shared cohort. Retain observations through job expiry and interruption. Advance past provisionally rejected routes for the current sweep; retry them in a later sweep or after geometry invalidation. Claimed points remain pending, never completed. Start later sweeps only after the remaining cohort is observed or provisionally skipped, not on local horizon exhaustion. |
| Ranking | Choose category, then area by the lowest accepted vanilla returned entry-candidate route cost, then successive useful points in that area. Evaluate all eligible declared candidates except those excluded by a proven lower bound; deterministic cell-index ties. In 1.6, each path step adds rounded pawn movement ticks and a nonnegative `IndexCost` (maximum with a `ushort` provider cost), so Chebyshev distance times the lesser rounded step cost is a lower bound on every returned route. Prune only when it is strictly greater than the best accepted cost. This is not exact globally shortest-path optimization. Include door-adjacent alternative entries and gap-fill irregular geometry. |
| Travel horizon | The existing 80-cell setting bounds a physical travel leg, not area enumeration or map coverage. Retain the final coverage target across legs and use nodes of its accepted vanilla path for progress. No stable-distance boundary, geometric top-k exclusion, tree or replacement pathfinder. |
| Coordination | Transient per-dog area/goal state, separate from productive queues. Live survey jobs claim destinations. Release/revalidate on interruption, maintenance, player orders, war and death; keep only actual observations. Productive work may preempt surveying. |
| Invalidation | Use native topology/roof/fog/building events plus bounded re-reading of full live Home/zone membership, including same-count reshapes. Revalidate selected points and returned route nodes live. Reuse unchanged geometry/candidate data rather than rebuilding it for every dog. |
| Persistence | Exploration geometry, room references, scan coverage, sweep and claims are transient. Save/load deliberately rebuilds an unverified sweep while preserving productive tasks and assignments. Remove the obsolete arrival-history writer/reader; old `surveyCells`/`surveyTicks` tags are ignored and cannot certify observations. Coordinator failover within a live map keeps the shared transient sweep; final-node loss clears it. No saved map component or new Harmony patch. |
| Measurement | Compare identical controlled room-heavy, large-zone, wall-detour and open-fallback workloads. Both physical phases reload the same private fixture save with identical actors, eligibility and tuning. Record actual discovery ticks, native walked cells, selection elapsed cost/GC allocations, full paths/reachability and cold/refresh/invalidation costs, including median and tail. Refresh timing includes the next selection so deferred candidate construction is charged. Whole-survey totals include local scanning, live driver/wait validation, job startup and cold network/survey construction; the historical baseline excludes only the new coverage hook and uses its original arrival writer. Nested geometry/candidate time is not added twice. Stopwatch durations are elapsed-cost proxies, not hardware cycles or separately measured asynchronous pathfinding CPU. The validated Boehm counter is process-total managed allocation, including concurrent engine allocations, not per-thread object sizes. A repeatable planner-cost/allocation regression requires an explicit tradeoff decision, not an unconditional optimization claim. |
| Benchmark instrumentation | Native 1.6 has no pawn-movement map event. Only explicitly selected isolated benchmarks temporarily patch `Pawn_PathFollower.TryEnterNextPathCell` and `GenerateNewPathRequest` to count actual adjacent moves and movement path requests. These unannotated hooks are installed explicitly and removed on every benchmark finish; ordinary gameplay and live Run all never install them. Discovery uses the real queue task's first-report tick. |

## Targets

### S1: Baseline and scan-certified routing

- [x] Matched current-selector baselines are recorded before making a speedup claim.
- [x] Home-first category order and residual Home discovery are demonstrated.
- [x] Vanilla returned route costs, unreachable/forbidden paths and deterministic ties govern area entry; within-area retention is separate from entry ranking.
- [x] Actual empty/local scans certify coverage; arrivals and old saved history cannot suppress unseen tasks.
- [x] Overlapping indoor storage avoids redundant travel; outdoor stockpiles/shelves remain represented.

### S2: Coverage, fairness and lifecycle

- [x] Large/non-convex footprints, blocked centers, alternative entrances, corridors and separated revealed coverage retain discovery.
- [x] Finite sweeps reach lower categories, revisit new work and travel beyond two initial 80-cell horizons without fixture teleports.
- [ ] Multiple dogs coordinate pending claims without duplicate productive work; interruptions, expiry, maintenance, draft, riding, war and death do not invent coverage.
- [x] Room/door, zone reshape/create/delete, storage, Home and fog changes invalidate stale goals without permanent omissions.
- [ ] Local discovery eligibility, productive/emergency ordering, player precedence and route-based worker assignment remain intact.
- [ ] Save/load, legacy arrival history, failover, final-node clearing and independent clean removal pass.

### S3: Measured integration

- [ ] Matched workloads record full-path requests, reachability, walked cells, discovery, selection CPU/allocations, cold/refresh/invalidation cost and median/tail selection cost.
- [ ] Fewer redundant steps or lower discovery latency is demonstrated without lost eligible targets, and no repeatable total planner CPU/allocation regression is concealed.
- [ ] Affected network cases and the necessary shared-code sequence pass with zero new errors; test evidence and ticket acceptance are updated and committed incrementally.

S1, geometry/fairness and live-edit proof initially passed in build `3954020-dirty.20261023T020344Z` (**18/18 PASS**, zero new errors). Replay-controlled build `fcd9d92-dirty.20261023T053956Z` repeated all fourteen behavior cases and four benches **18/18 PASS**, including a 171-cell initial target separation and 368 cells physically walked. Build `fcd9d92-dirty.20261023T060457Z` then passed the additional distant same-object reshape regression: ordinary dispatch replaced the stale goal and hauled 31 gold without resetting the survey. The recorded failing preflight demonstrates why selector and driver candidate checks must agree ([run log](../test-runs.md)).

Lazy candidate construction, boolean observations and unchanged-input reuse reduced scoped planner elapsed totals in all four matched workloads. Both proposed targets were discovered; each historical phase missed one within its 15000-tick cap. Cold construction/allocation still costs more, so S3 remains open for real CPU scope, final integration and the explicit tradeoff decision.

## Open questions

None before implementation: use the ticket's recommended policy defaults. Any measured speed/discovery-versus-planner-cost tradeoff remains subject to an explicit decision if it occurs.
