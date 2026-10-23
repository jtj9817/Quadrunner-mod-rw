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
| Performance decisions | The user accepted the disclosed 07:56 room-heavy/cold-cost tradeoffs (2026-10-23). Those samples were later found to have a larger revealed world than claimed. Corrected bounded-world measurements at 09:42 improve three workloads but expose a large-zone regression instead; that materially different comparison requires confirmation. Neither decision authorizes weaker coverage or task eligibility, and no all-workload optimization claim is made. |
| Native path-source coherence | RimWorld 1.6's `PathFinderMapData.GatherData` skips a second gather in the same game tick, while `AreaSource.DataForArea` requires the newly created area's native data. Both survey and productive route ranking therefore defer a newly observed nonempty effective allowed area until a later tick. Track only transient unique area IDs/first-use ticks in the existing map network, lazily allocated; prune deleted IDs on new-area use. Deferred candidates keep the whole ranking and lower-priority work pending, not unreachable or completed. No exception suppression, traversal bypass, saved state or Harmony patch. The new regression primes native data before same-tick creation and then proves real discovery/hauling and reload. |

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
- [ ] Fewer redundant steps or lower discovery latency is demonstrated without lost eligible targets, and no repeatable total planner CPU/allocation regression is concealed.
- [x] Affected network cases and the necessary shared-code sequence pass with zero new errors; test evidence and ticket acceptance are updated and committed incrementally.

S1, geometry/fairness and live-edit proof initially passed in build `3954020-dirty.20261023T020344Z` (**18/18 PASS**, zero new errors). Replay-controlled build `fcd9d92-dirty.20261023T053956Z` repeated all fourteen behavior cases and four benches **18/18 PASS**, including a 171-cell initial target separation and 368 cells physically walked. Build `fcd9d92-dirty.20261023T060457Z` then passed the additional distant same-object reshape regression: ordinary dispatch replaced the stale goal and hauled 31 gold without resetting the survey. The recorded failing preflight demonstrates why selector and driver candidate checks must agree ([run log](../test-runs.md)).

The corrected tracing/riding build `ad76f81-dirty.20261023T075350Z` passed all four benchmarks and three riding cases **7/7**, zero new errors. Every measured phase had zero collision recoveries, rejected jumps and external moves. The riding case retained mount/drain/reload/dismount/on-foot arrival and physically retrieved/docked the stopped dog, without a post-stop coverage reset. That run's locally matched benchmark world was subsequently found to leak; its performance samples and their approval are superseded by the bounded-world evidence below.

The 08:16 shared sequence passed **73/74**, zero new errors; all Network, benchmark, riding and removal-export cases passed. Its only failure was a live-edit fixture that sealed its sole room entrance with the preceding wall move and could refog an occupied goal. The corrected fixture preserves an alternate entrance and stages a distant active fog goal while paused. Build `08c4859-dirty.20261023T082729Z` passed that case **1/1**, zero new errors, with real scans of both the revealed replacement and restored remote footprint before the zone/storage checks completed.

Final shared-code build `d30a4c8.20261023T103822Z` passed **75/75**, zero new errors (2026-10-23 10:59, launch `20261023-103839-3940076`). This includes all sixteen Survey behavior cases, four matched benchmarks, ten Network cases, charging, riding, retrieval, rescue, combat, save/load and removal export. The native new-area regression also failed before the correction and then passed alongside charging and both cooldown/raid cases **4/4**: synchronous routing waits for coherent native area inputs, retains full worker ranking, and physically discovers/hauls 38 gold across reload. Functional integration is complete; the performance decision below remains open.

Fresh independent removal of the final `d30a4c8.20261023T103822Z` export is **LOADED CLEAN**, zero new errors (2026-10-23 11:04): no Quadrunner assembly, 600 ticks, save/reload, 600 more ticks, expected vanilla contents retained and the private rendered surface inspected. Proof: `.devlog/runs/removal-20261023-110450/Player.log` and `.devlog/removal-probe/result`. The private observer and Xvfb are stopped. This completes persistence/removal evidence alongside the clean legacy-save and network failover/final-node runs.

## Matched workload evidence

**Scope correction:** these bounded-world measurements supersede the 07:56 samples, which replayed matching local geometry but did not constrain the surrounding revealed map. Setup now refogs the world, reveals the fixture plus only occupied player-pawn cells, and verifies a whole-map fog fingerprint after both replay loads. Native player-pawn respawning must not flood-reveal the outside world. The 09:42 run passed **6/6**, zero new errors; all eight physical phases discovered both targets with zero recoveries, jumps or external moves.

Run: **2026-10-23 09:42**, build `d9b34b3-dirty.20261023T094125Z`, launch `20261023-094132-3663435`. Each old/new pair replayed the same private fixture, with identical initial geometry/fog, one physical observer, two eligible targets and default scan/movement tuning. Revealed cells were 1589 / 2029 / 1354 / 2605 respectively, including only 3 / 4 / 4 / 4 occupied player-pawn cells outside each pad. Microbenchmarks used 120 interleaved stationary calls per selector. These are paired samples, not a universal speedup claim. `Old → new` means historical selector → area-aware selector.

### Physical discovery and work

Discovery entries are the two actual first-report ticks relative to phase start. All staged targets were discovered in both phases. Full-path requests include selector and actual movement requests; reachability checks are separate.

| Fixture | Two target report ticks, old → new | Native walked cells, old → new | Full-path requests, old → new | Reach checks, old → new |
| --- | --- | --- | --- | --- |
| Room-heavy | `1 / 2161 → 1 / 1201` | 127 → 74 | 35 → 57 | 56 → 0 |
| Large zone | `1 / 1081 → 1 / 2521` | 74 → 109 | 34 → 74 | 60 → 0 |
| Wall detour | `121 / 1561 → 481 / 121` | 99 → 30 | 12 → 29 | 21 → 0 |
| Open fallback | `1 / 1801 → 1 / 961` | 122 → 30 | 46 → 25 | 84 → 0 |

The proposed selector loses no staged target in these samples. Large-zone discovery/travel regresses; the first wall-detour target also arrives later while its second target improves. First-radius-entry-to-report delay can exceed 120 ticks if the worker leaves and returns; the scan interval itself remains 120.

### Accounted physical-phase costs

Planner elapsed totals include selection, driver/wait checks, local scans, job startup, historical arrival writing and proposed cold construction, without adding nested geometry/candidate costs twice. Process CPU is the broader all-thread scope specified above, not isolated planner attribution. GC bytes use the validated Boehm process-total counter.

| Fixture | Planner elapsed ms, old → new | Accounted GC bytes, old → new | Whole physical-phase process CPU ms, old → new |
| --- | --- | --- | --- |
| Room-heavy | 27.763 → 28.240 | 122000 → 1158624 | 11030.026 → 7135.744 |
| Large zone | 16.930 → 18.366 | 88976 → 1109136 | 6626.722 → 12694.466 |
| Wall detour | 12.976 → 8.800 | 53680 → 995216 | 8757.358 → 4236.950 |
| Open fallback | 23.954 → 13.096 | 120752 → 1064520 | 9703.614 → 6147.099 |

### Cold construction and topology invalidation

Each proposed cold network allocated **884128 bytes** on the controlled 200×200 map; its measured construction elapsed/CPU is listed separately below and charged in the totals above. Cold selection includes geometry and needed lazy candidates. Refresh includes a real wall edit, refresh and the next selection, so required deferred work is not hidden.

| Fixture | Cold selection elapsed ms, old → new | Cold selection CPU ms, old → new | Cold selection GC bytes, old → new | New network elapsed / CPU ms | New refresh elapsed / CPU ms; GC bytes |
| --- | --- | --- | --- | --- | --- |
| Room-heavy | 2.915 → 3.918 | 2.927 → 3.941 | 6864 → 62144 | 0.243 / 0.268 | 5.230 / 5.575; 129552 |
| Large zone | 2.757 → 3.595 | 2.763 → 3.616 | 8064 → 58816 | 0.069 / 0.094 | 5.658 / 5.673; 139584 |
| Wall detour | 2.830 → 4.021 | 2.839 → 4.034 | 7200 → 56816 | 0.069 / 0.092 | 5.594 / 5.636; 147920 |
| Open fallback | 2.734 → 4.335 | 2.749 → 4.341 | 8592 → 80456 | 0.068 / 0.084 | 6.875 / 7.171; 163104 |

### Repeated selection distributions

Triples are median / p95 / maximum, in milliseconds. Warm calls retain unobserved candidates without moving; physical calls include cold startup and real progress, so neither distribution substitutes for the other.

| Fixture | Old warm process CPU triple | New warm process CPU triple | Warm GC bytes over 120 calls, old → new |
| --- | --- | --- | --- |
| Room-heavy | 1.371 / 1.669 / 2.164 | 0.031 / 0.047 / 0.352 | 831968 → 82352 |
| Large zone | 1.376 / 1.554 / 2.038 | 0.031 / 0.039 / 0.136 | 1008000 → 85312 |
| Wall detour | 1.457 / 1.938 / 2.555 | 0.049 / 0.095 / 0.851 | 871056 → 85936 |
| Open fallback | 1.329 / 1.554 / 6.285 | 0.028 / 0.051 / 0.132 | 984960 → 80944 |

| Fixture | Old physical-selection process CPU triple | New physical-selection process CPU triple |
| --- | --- | --- |
| Room-heavy | 16.31 / 20.09 / 20.09 | 5.50 / 19.76 / 19.76 |
| Large zone | 15.00 / 15.18 / 15.18 | 0.83 / 13.27 / 13.27 |
| Wall detour | 16.17 / 22.39 / 22.39 | 3.11 / 13.18 / 13.18 |
| Open fallback | 14.89 / 19.63 / 19.63 | 1.87 / 12.55 / 12.55 |

The 09:42 comparison has tradeoffs: warm selections improve, but cold allocation/latency remain higher; large-zone discovery/travel and whole-phase CPU regress, and room-heavy accounted planner elapsed is slightly higher despite less travel. Three workloads need less whole-phase CPU in that sample, but this is not isolated planner attribution. The earlier user approval covered the now-superseded leaky-world comparison, not these corrected results.

### Final shared-sequence repetition

The 10:59 full sequence measured another within-run matched sample on the final runtime build `d30a4c8.20261023T103822Z`. All eight physical phases discovered both targets with identical replayed whole-map fog, four occupied player-pawn cells outside each fixture, and zero recoveries, rejected jumps or external moves. Scope and counters are unchanged from above.

| Fixture | Two target report ticks, old → new | Native walked cells, old → new | Full-path requests, old → new | Reach checks, old → new |
| --- | --- | --- | --- | --- |
| Room-heavy | `1 / 2401 → 1 / 1321` | 143 → 77 | 43 → 49 | 70 → 0 |
| Large zone | `1 / 1081 → 1 / 1561` | 70 → 62 | 29 → 35 | 56 → 0 |
| Wall detour | `481 / 961 → 481 / 121` | 59 → 30 | 27 → 14 | 32 → 0 |
| Open fallback | `1 / 1321 → 1 / 1441` | 85 → 53 | 42 → 30 | 83 → 0 |

| Fixture | Planner elapsed ms, old → new | Accounted GC bytes, old → new | Whole physical-phase process CPU ms, old → new |
| --- | --- | --- | --- |
| Room-heavy | 24.649 → 35.306 | 127776 → 1142912 | 12948.213 → 8151.290 |
| Large zone | 12.651 → 33.648 | 77104 → 1064352 | 7126.568 → 9147.342 |
| Wall detour | 12.356 → 7.590 | 73312 → 979856 | 6512.584 → 4447.895 |
| Open fallback | 16.894 → 14.909 | 105280 → 1058856 | 8114.128 → 8745.136 |

Walking improves in all four pairs, but discovery and whole-phase CPU improve only for room-heavy/wall-detour here. Large-zone and open-fallback discovery/whole-phase CPU regress; room-heavy/large-zone accounted planner elapsed and all cold-inclusive allocations are higher. The favorable open-fallback result in the earlier sample is not universal. These disclosed regressions require an actual user decision; a passing benchmark verdict means valid matched discovery/traces, not unconditional performance acceptance.

## Open questions

Confirm whether the disclosed workload-dependent discovery/CPU regressions and higher cold-inclusive costs are accepted while retaining the required routing/coverage policy. Both decision prompts automatically selected the first option after timing out, including the prompt with no recommended default; neither is recorded as explicit user approval. Functional integration is now verified 75/75, but performance acceptance remains open until an actual user response.
