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
| Persistence | Exploration geometry, room references, scan coverage, sweep and claims are transient. Save/load deliberately rebuilds an unverified sweep while preserving productive tasks and assignments. Remove the obsolete arrival-history writer/reader; old `surveyCells`/`surveyTicks` tags are ignored and cannot certify observations. Coordinator failover within a live map keeps the shared transient sweep; final-node loss clears it. No saved map component or new gameplay Harmony patch. |
| Measurement | Compare identical controlled room-heavy, large-zone, wall-detour and open-fallback workloads. Both physical phases reload the same private fixture save with identical actors, eligibility and tuning. Record actual discovery ticks, native walked cells, selection elapsed cost/GC allocations, full paths/reachability and cold/refresh/invalidation costs, including median and tail. Refresh timing includes the next selection so deferred candidate construction is charged. Whole-survey totals include local scanning, live driver/wait validation, job startup and cold network/survey construction; the historical baseline excludes only the new coverage hook and uses its original arrival writer. Nested geometry/candidate time is not added twice. Stopwatch durations are elapsed-cost proxies, not hardware cycles or separately measured asynchronous pathfinding CPU. The validated Boehm counter is process-total managed allocation, including concurrent engine allocations, not per-thread object sizes. A repeatable planner-cost/allocation regression requires an explicit tradeoff decision, not an unconditional optimization claim. |
| Benchmark instrumentation | Native 1.6 has no pawn-movement map event. Only explicit isolated benchmarks install pre/post hooks on `Pawn_PathFollower.PatherTick` and `TryEnterNextPathCell`, plus request/job-end hooks on `GenerateNewPathRequest` and `Pawn_JobTracker.EndCurrentJob`. Exact native cell transitions count walking; collision recoveries are separate, and jumps/external moves reject the phase once outside Pawn.Tick before metrics can be emitted. Both phases stop leftover followers at job/leg boundaries, avoiding vanilla's stale pending-nextCell artifact under manual job churn. All unannotated hooks are removed on finish; ordinary gameplay and live Run all never install them. Discovery uses the real queue task's first-report tick. |
| CPU scope | In the isolated Linux x64 benchmark, [`clock_gettime(CLOCK_PROCESS_CPUTIME_ID)`](https://man7.org/linux/man-pages/man3/clock_gettime.3.html) measures actual CPU across all private-game threads. Record selection cold/median/p95/tail, refresh and full physical-phase CPU separately from elapsed planner subtotals. Full-phase CPU includes native asynchronous paths, ordinary simulation, rendering and benchmark overhead, excluding save/load/setup; it is not pure planner attribution or an old-binary CPU profile. Both phases retain current test observation bookkeeping; only the scoped elapsed/allocation subtotals subtract that hook from the historical baseline. Proposed cold construction is additionally charged. Retry the existing scenario speed helper after load fade-in instead of silently benchmarking at Normal. |
| Accepted tradeoffs | After reviewing the 07:56 paired measurements, the user explicitly chose **Accept measured tradeoffs** (2026-10-23): retain the requested priorities and scan-certified coverage, accept the measured room-heavy discovery/travel/CPU regression and higher cold allocation/latency, and do not describe the result as an all-workload optimization. This accepts those performance tradeoffs, not weaker observation, coverage or task-eligibility semantics. |

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
- [x] Multiple dogs coordinate pending claims without duplicate productive work; interruptions, expiry, maintenance, draft, riding, war and death do not invent coverage.
- [x] Room/door, zone reshape/create/delete, storage, Home and fog changes invalidate stale goals without permanent omissions.
- [x] Local discovery eligibility, productive/emergency ordering, player precedence and route-based worker assignment remain intact.
- [x] Save/load, legacy arrival history, failover, final-node clearing and independent clean removal pass.

### S3: Measured integration

- [x] Matched workloads record full-path requests, reachability, walked cells, discovery, selection CPU/allocations, cold/refresh/invalidation cost and median/tail selection cost.
- [x] Fewer redundant steps or lower discovery latency is demonstrated without lost eligible targets, and no repeatable total planner CPU/allocation regression is concealed.
- [ ] Affected network cases and the necessary shared-code sequence pass with zero new errors; test evidence and ticket acceptance are updated and committed incrementally.

S1, geometry/fairness and live-edit proof initially passed in build `3954020-dirty.20261023T020344Z` (**18/18 PASS**, zero new errors). Replay-controlled build `fcd9d92-dirty.20261023T053956Z` repeated all fourteen behavior cases and four benches **18/18 PASS**, including a 171-cell initial target separation and 368 cells physically walked. Build `fcd9d92-dirty.20261023T060457Z` then passed the additional distant same-object reshape regression: ordinary dispatch replaced the stale goal and hauled 31 gold without resetting the survey. The recorded failing preflight demonstrates why selector and driver candidate checks must agree ([run log](../test-runs.md)).

The corrected tracing/riding build `ad76f81-dirty.20261023T075350Z` passed all four benchmarks and three riding cases **7/7**, zero new errors. Every measured phase had zero collision recoveries, rejected jumps and external moves. The riding case retained mount/drain/reload/dismount/on-foot arrival and physically retrieved/docked the stopped dog, without a post-stop coverage reset. The measured tradeoffs are accepted; final cross-case sequence proof remains pending.

Independent removal of the current runtime export is **LOADED CLEAN**, zero new errors (2026-10-23 06:54): no Quadrunner assembly, 600 ticks, save/reload, 600 more ticks, expected vanilla contents retained and the private rendered surface inspected. This completes persistence/removal evidence alongside the clean legacy-save and network failover/final-node runs.

## Matched workload evidence

Run: **2026-10-23 07:56**, build `ad76f81-dirty.20261023T075350Z`, launch `20261023-075357-3094063`. Each old/new pair replayed the same private fixture, with identical initial geometry, one physical observer, two eligible targets and default scan/movement tuning. Microbenchmarks used 120 interleaved stationary calls per selector. These are paired samples, not a universal speedup claim. `Old → new` means historical selector → area-aware selector.

### Physical discovery and work

Discovery entries are the two actual first-report ticks relative to phase start. A miss is censored at about 15000 ticks, not proof the old selector could never discover it. Full-path requests include selector and actual movement requests; reachability checks are separate.

| Fixture | Two target report ticks, old → new | Native walked cells, old → new | Full-path requests, old → new | Reach checks, old → new |
| --- | --- | --- | --- | --- |
| Room-heavy | `1 / 601 → 1 / 1201` | 37 → 79 | 22 → 39 | 94 → 0 |
| Large zone | `1 / missed → 1 / 1681` | 881 → 68 | 345 → 42 | 1512 → 0 |
| Wall detour | `121 / missed → 481 / 121` | 886 → 36 | 346 → 23 | 1451 → 0 |
| Open fallback | `1 / missed → 1 / 961` | 885 → 35 | 328 → 33 | 1697 → 0 |

The proposed selector discovered both targets in every fixture. Room-heavy discovery/travel regresses; the first wall-detour target also arrives later while its second target improves. First-radius-entry-to-report delay can exceed 120 ticks if the worker leaves and returns; the scan interval itself remains 120.

### Accounted physical-phase costs

Planner elapsed totals include selection, driver/wait checks, local scans, job startup, historical arrival writing and proposed cold construction, without adding nested geometry/candidate costs twice. Process CPU is the broader all-thread scope specified above, not isolated planner attribution. GC bytes use the validated Boehm process-total counter.

| Fixture | Planner elapsed ms, old → new | Accounted GC bytes, old → new | Whole physical-phase process CPU ms, old → new |
| --- | --- | --- | --- |
| Room-heavy | 5.861 → 41.522 | 56960 → 1618032 | 4806.614 → 7527.521 |
| Large zone | 73.552 → 19.413 | 758848 → 1598112 | 67591.432 → 9431.424 |
| Wall detour | 70.574 → 15.771 | 727056 → 1520784 | 66127.881 → 4333.033 |
| Open fallback | 71.100 → 31.464 | 779664 → 1728976 | 66669.084 → 6402.057 |

### Cold construction and topology invalidation

Each proposed cold network allocated **884128 bytes** on the controlled 200×200 map; its measured construction elapsed/CPU is listed separately below and charged in the totals above. Cold selection includes geometry and needed lazy candidates. Refresh includes a real wall edit, refresh and the next selection, so required deferred work is not hidden.

| Fixture | Cold selection elapsed ms, old → new | Cold selection CPU ms, old → new | Cold selection GC bytes, old → new | New network elapsed / CPU ms | New refresh elapsed / CPU ms; GC bytes |
| --- | --- | --- | --- | --- | --- |
| Room-heavy | 0.903 → 10.375 | 0.919 → 10.375 | 11904 → 593872 | 0.349 / 1.155 | 10.679 / 10.722; 672272 |
| Large zone | 1.776 → 8.230 | 1.768 → 8.322 | 14976 → 596304 | 0.067 / 0.101 | 10.336 / 10.419; 657344 |
| Wall detour | 1.489 → 8.966 | 1.496 → 9.028 | 13776 → 592000 | 2.332 / 5.388 | 10.225 / 10.242; 669696 |
| Open fallback | 1.448 → 24.504 | 1.450 → 24.503 | 13488 → 755088 | 0.074 / 0.101 | 26.059 / 26.331; 658736 |

### Repeated selection distributions

Triples are median / p95 / maximum, in milliseconds. Warm calls retain unobserved candidates without moving; physical calls include cold startup and real progress, so neither distribution substitutes for the other.

| Fixture | Old warm process CPU triple | New warm process CPU triple | Warm GC bytes over 120 calls, old → new |
| --- | --- | --- | --- |
| Room-heavy | 1.129 / 1.710 / 2.021 | 0.035 / 0.109 / 0.452 | 1571984 → 88592 |
| Large zone | 1.285 / 1.731 / 2.060 | 0.036 / 0.082 / 0.401 | 1661760 → 84064 |
| Wall detour | 1.166 / 1.702 / 1.945 | 0.048 / 0.101 / 0.985 | 1677072 → 90304 |
| Open fallback | 1.070 / 1.405 / 1.722 | 0.030 / 0.051 / 0.350 | 1551360 → 81568 |

| Fixture | Old physical-selection process CPU triple | New physical-selection process CPU triple |
| --- | --- | --- |
| Room-heavy | 6.85 / 10.59 / 10.59 | 5.03 / 74.52 / 74.52 |
| Large zone | 5.20 / 10.80 / 11.75 | 0.49 / 46.89 / 46.89 |
| Wall detour | 5.09 / 10.63 / 11.00 | 5.01 / 46.69 / 46.69 |
| Open fallback | 5.22 / 8.93 / 10.63 | 3.82 / 103.09 / 103.09 |

The requested priority/coverage policy therefore has a measured cost: higher cold allocation/latency and worse room-heavy discovery/travel/CPU in this sample, despite faster warm selections and much less work in the other three fixtures. The user explicitly accepted these tradeoffs after reviewing this comparison. Performance acceptance does not mean every fixture improves; no unconditional optimization claim is made.

## Open questions

No unresolved design or performance decisions. The user accepted the disclosed measured tradeoffs without changing policy defaults. Final corrected full-sequence verification remains tracked under S3.
