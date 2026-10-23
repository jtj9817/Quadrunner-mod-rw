# QR-0001: Prioritize surveying by rooms and zones

| Field | Value |
| --- | --- |
| Issue type | Improvement / performance |
| Status | In Progress — measured tradeoffs accepted; final sequence verification pending |
| Component | Survey routing and local task discovery |
| Ticket key | QR-0001 — local documentation identifier, not an external Jira issue |
| Related design | [Plan 0003: Shared robodog dispatch network](../plans/0003-network-dispatch.md) |
| Authorized change | Implement and verify this ticket with incremental commits (user, 2026-10-22); see [Plan 0004](../plans/0004-area-aware-survey.md) |

## Summary

Replace the utility dog's age-first lattice surveying with area-aware exploration: choose the closest available room, then prioritize storage areas, planting zones, other zones and finally outside the Home area. Use greedy nearest-neighbor selection for successive survey destinations, backed by RimWorld's existing pathfinder rather than a second movement algorithm.

This ticket began as both an exploration-policy proposal and a performance investigation. Changing where dogs look first can improve useful discovery without necessarily reducing CPU cost. Implementation and runtime evidence are now tracked in [Plan 0004](../plans/0004-area-aware-survey.md) and [test-runs.md](../test-runs.md); a speedup claim must account for cold construction, invalidation and total planner costs.

## Pre-implementation behavior and findings

This research snapshot describes local baseline commit `27d7bfd`, before the routing cutover. Its source line anchors refer to that revision, not the current selector.

| Finding | Evidence and implication |
| --- | --- |
| Surveying does not understand rooms or zones. | [`Survey.JobFor`](../../Source/Quadrunner/Survey.cs#L19-L60) enumerates a map-wide 20-cell lattice, rejects anchors beyond the dog's 80-cell search radius, then chooses by survey age with squared straight-line distance as the tie-break. The radius limits candidate selection for a leg, not distance from a stable. |
| Some expensive checks precede ranking. | [`GoalNear`](../../Source/Quadrunner/Survey.cs#L65-L71) searches nearby cells and calls `CanReach` before the candidate's age is compared. [`Age`](../../Source/Quadrunner/Survey.cs#L85-L95) traverses all saved survey records for each candidate reaching that stage. Full pathfinding is already skipped for candidates that cannot improve the current age/distance score; it is **not** called for every grid center. |
| Coverage is point-based, not room-based. | [`JobDriver_Survey`](../../Source/Quadrunner/Survey.cs#L113-L135) records the dog's actual position at the end of survey travel, independently of the [local scan cadence](../../Source/Quadrunner/Network.cs#L332-L340). An arrival record is therefore **not proof that a scan occurred there**. A selected destination does not mean it was visited, and visiting one corner does not cover an entire room. The existing [coverage gotcha](../../TODO.md#testing) explicitly warns against aging neighboring unscanned tiles. |
| Discovery is separate from movement planning. | [`DogWork.ReportLocal`](../../Source/Quadrunner/Work.cs#L63-L127) scans revealed, unforbidden things within 15 cells; cleaning additionally requires Home and 600 ticks of filth age. [`DogNetwork.Tick`](../../Source/Quadrunner/Network.cs#L301-L358) normally scans every 120 ticks, including dogs already travelling or working. This scan has no room-boundary or line-of-sight test. |
| Task dispatch already uses vanilla routes. | [`AssignAll`](../../Source/Quadrunner/Network.cs#L495-L536) normally runs through the 60-tick network refresh and compares returned `PawnPath.TotalCost` for available capable dogs. Discovery is deduplicated by task kind and target ThingID in [`Report`](../../Source/Quadrunner/Network.cs#L250-L262). Survey selection must not become a second task dispatcher. |

**[INFERENCE] Optimization opportunities:** reduce repeated reachability/history work during destination selection and avoid low-value travel before searching colony work areas. Their relative cost and the effect of the proposed category order remain unmeasured; room enumeration and extra route comparisons could themselves become more expensive than the existing grid.

## Algorithm assessment

| Approach | Finding | Recommendation |
| --- | --- | --- |
| Greedy nearest neighbor | A policy for choosing the next area and then coverage points within it, not a pathfinder or spatial index. Euclidean-nearest is not necessarily cheapest to walk to. | Choose an area by its lowest accepted **vanilla returned entry-route cost** among its declared candidates, then choose successive uncovered points inside that area. Do not replace room selection with unrestricted hopping between points in different rooms, or claim a globally optimal tour or mathematically shortest room-entry route. |
| Ball Tree / KD-tree | Spatial indexes accelerate distance queries over points. A tree over centroids does not encode walls, doors, forbidden passages or non-convex room shapes. A fixed geometric top-k shortlist can exclude a route-better destination. Tree construction and maintenance also have a cost. | Do not require a tree for this change. Start with native area inventories and cheap geometric ordering. Add an index only if profiling justifies it; geometry may order candidates or provide a proven lower bound, but cannot certify reachability or final route rank. |
| A* | Vanilla already exposes `map.pathFinder.FindPathNow`. Its grid search uses a weighted heuristic, skips closed nodes and returns the cost of the path it found; it is not a strict optimal-A* guarantee. | Reuse vanilla with consistent pawn traversal parameters, tuning and `PathEndMode.OnCell` for survey goals. Dispose pooled paths. Do not implement a second cell pathfinder. |
| Region traversal | The mod already uses `RegionTraverser.BreadthFirstTraverse` to find a room for idle waiting. Region BFS orders region hops, not terrain/door travel cost. | Reuse topology traversal where useful for candidate enumeration, then validate coverage goals and route costs. Do not reuse the waiting policy's bedroom/prison exclusions as survey eligibility. |

Evidence: [`Docking.cs:56–99`](../../Source/Quadrunner/Docking.cs#L56-L99); local RimWorld 1.6 source `.tools/decomp/Verse/PathFinder.cs:523–591` and `PathFinderJob.cs:90–200,221–237`; [nearest-neighbor algorithm tradeoffs](https://scikit-learn.org/stable/modules/neighbors.html#nearest-neighbor-algorithms). The external reference explains the algorithms; it is **not** a proposed Python or third-party runtime dependency.

## Proposed behavior — not yet implemented

### Area priority and classification

Preserve the requested category order. The following definitions are recommended defaults to settle in the implementation plan, not descriptions of shipped behavior.

| Priority | Area | Proposed interpretation |
| --- | --- | --- |
| 1 | Rooms | Revealed, reachable enclosed rooms with outstanding survey coverage. Exclude doorway rooms and the map-wide outdoor room. Consider `ProperRoom`, `IsDoorway` and an explicit outdoor policy rather than assuming every vanilla `Room` is indoors. |
| 2 | Storage areas | Stockpiles **and player storage buildings**, including shelves; skip coverage already satisfied by room surveying. Storage buildings are not zones. |
| 3 | Planting zones | `Zone_Growing` footprints, surveyed for existing utility tasks. This does not authorize sowing, harvesting or new plant work givers. |
| 4 | Other zones | Other supported Core `Zone` footprints, if any. In the researched Core-only game the usable zone types are stockpile and growing; do not add a DLC dependency to populate this category or treat named allowed Areas as zones. |
| 5 | Outside Home | Remaining revealed, reachable exterior coverage after the earlier categories and the Home-gap fallback described below. This remains a search area for eligible tasks, not permission to clean outside Home. |

**Home-gap fallback:** the requested categories omit unzoned outdoor cells inside Home, such as colony paths and construction sites. Survey these after other zones and before outside-Home coverage; otherwise they could disappear from the search entirely. This is a proposed completeness safeguard, not a new work type.

**Overlap:** a room can contain storage or a growing zone. Satisfy overlapping categories from actual scan coverage instead of automatically scheduling a second visit. Do not mark an area covered merely because it was classified or a waypoint was reserved.

### Destination selection and discovery

1. Enumerate area geometry through native inventories; do not inspect remote item/filth lists to choose a supposedly task-rich area. Planning may use revealed geometry, but normal tasks still enter the queue only through real local observation.
2. Define a bounded, deterministic set of entry/coverage candidates for each area; do not make every room cell a separate full-pathfinding query. Generate usable points from actual area cells or valid adjacent observation positions. A room bounding-box center may be a wall or lie outside a non-convex room; `Zone.Position` is its first cell, not a centroid. Candidate generation must still cover large and irregular areas, including alternative entrances where they affect selection.
3. Define an **available** destination as currently revealed, permitted, still needing coverage, not conflicting with another survey goal, and having an accepted vanilla route whose nodes pass the fog check. Availability does not mean the room is empty or has a particular room role. Rejecting one returned route does not prove that every alternative revealed route is impossible; do not turn such a rejection into permanent completed/unreachable coverage.
4. Select the priority category, then the area by its best accepted entry-candidate route cost, then successive uncovered coverage points within the selected area. Keep the area while its current coverage pass remains useful; productive work and invalidation can interrupt it without discarding real observations. A geometric shortlist must not silently exclude a better declared candidate: either evaluate the remaining eligible candidates or stop only with a validated lower-bound rule. Use deterministic ties. Ranking is over the declared candidates, not every possible cell or a guaranteed optimal path.
5. Keep the local scanner active during travel and work. It may report legitimate nearby tasks from another category; the category order governs survey destinations, not a restriction on what a dog can notice.
6. Submit discovered tasks through the existing `DogNetwork.Report` path. Retain task priorities, target-exact job construction, reservations, queue trimming, one assignment per dog and cross-stable assignment. Surveying is idle movement, not a new normal/war task kind.

### Coverage, fairness and state

- Use a finite cohort of coverage for each sweep: prefer the nearest still-uncovered area in the current category, then advance when that category's coverage has been observed or provisionally skipped under an explicit revalidation rule. Do not reset the sweep merely because a survey expires, productive work interrupts it, or the current local horizon is exhausted. An unchanged nearby room must not repeatedly start a new pass while distant/lower-category coverage remains pending. Revisit completed coverage in later sweeps.
- Derive shared coverage from completed local scans, including scans that found **no tasks**, not from successful `Report` additions or survey arrivals. Record the actual observer position, scan tick and revealed footprint through the existing scan path. If an unobserved coverage goal is reached between scan ticks, wait interruptibly for the next scheduled scan before crediting it; do not add out-of-cycle scans or change the 120-tick interval. Merely walking past a cell, choosing a goal or entering a room is not full-area observation.
- Coordinate survey destinations across dogs without putting exploration reservations into productive task queues. A coverage point claimed by another worker is **pending**, not observed; do not end/reset the shared sweep because all remaining points are temporarily claimed. Release/revalidate claims on interruption and retain only coverage actually observed. Preserve destination reservation, interruptibility, war suspension and maintenance/player-order precedence.
- Prefer existing native inventories and simple transient candidate data over a new manager or persistent spatial tree. Avoid rebuilding all room/zone cell sets for every dog on every dispatch tick. Any caching must have explicit invalidation and live validation of selected points.
- Room topology can change after walls/doors are edited. Vanilla exposes `map.events.RegionsRoomsChanged`; roof and fog changes also matter to classification. Zone/Home edits do not expose an equivalent general MapEvents notification: re-read live membership or use bounded refresh rather than assuming the room event covers them. Same-cell-count reshapes must also be detected.
- Keep geometry/index data transient. Do not save `Room`/`Region` references or runtime room IDs. Preserve coordinator ownership, failover and clean removal. Existing saved `surveyCells`/`surveyTicks` are arrival history, not certified scan observations: an older save must rebuild unverified coverage rather than silently importing it as completed scans, while retaining productive queues/assignments. Any necessary new saved coverage/sweep state needs explicit serialization and a documented load interpretation.

## Policy details to settle before implementation

Record the chosen answers in the new numbered implementation plan required by the repository workflow; do not silently change Plan 0003's completed acceptance results.

1. **Home precedence:** does outside-Home always mean last, even for a room or stockpile there? Recommended interpretation: apply the first four tiers to their Home footprint, include residual Home coverage, then survey all remaining revealed outside-Home cells. This makes the user's final priority explicit rather than accidentally promoting an exterior ruin to the room tier.
2. **Revisit policy:** confirm finite sweeps versus an age-based revisit window. Recommended default is finite category sweeps with shared coverage and later revisits, so an unchanged high-priority area cannot permanently starve exterior discovery.
3. **Search horizon:** confirm how the existing 80-cell candidate horizon applies to area selection. Recommended default is bounded local travel legs with an explicit frontier/progression rule: reach known uncovered areas beyond the current horizon before resetting a completed local pass. Repeated legs alone are not a liveness argument. Prove progress to work beyond two initial leg horizons without fixture teleports; do not restore a stable/Home distance boundary or present local selection as globally nearest-room selection.

## Acceptance criteria

Check boxes only after the corresponding private-game run proves them; measured tradeoffs require the explicit decision described below.

- [x] The chosen Home precedence, revisit/sweep behavior and search-horizon interpretation are recorded, and deterministic fixtures demonstrate the requested category order plus coverage of unzoned Home cells.
- [x] Within a priority category, wall detours and forbidden doors affect selection correctly: geometric proximity alone cannot win, an unreachable candidate is skipped, and ties are deterministic. Describe selection as vanilla route-cost ranking, not exact global shortest-path optimization.
- [x] Area selection and within-area coverage are separate: a fixture with competing rooms demonstrates the declared entry-cost ranking and completion/resumption of the chosen area's coverage, rather than nearest-point hopping between rooms.
- [x] Indoor stockpiles/storage buildings do not trigger redundant visits when room scans already covered them; outdoor stockpiles and shelves remain represented under the chosen Home policy.
- [x] Large and non-convex rooms/zones, blocked centers, alternative entrances, narrow corridors and separated revealed coverage all retain discoverability. A far-corner task is not missed because the area was marked visited at its entrance. Arrival between scan ticks does not credit an unscanned footprint; a genuinely scanned empty area can complete its pass.
- [x] Lower-priority areas are eventually surveyed by idle workers on a finite, static fixture even while higher-priority rooms continue to exist. A separate fixture reaches eligible work beyond two initial 80-cell leg horizons through real travel, without resetting local sweeps indefinitely. New work appearing after an earlier sweep is eventually discovered on a revisit.
- [x] Multiple workers distinguish temporarily claimed coverage from completed scans and avoid conflicting goals. Repeated reports of the same observed target are allowed, but queued tasks and productive assignments remain unique. Interruption, expiry, charging, drafting, riding, war and death release or revalidate claims without inventing coverage or resetting unrelated progress.
- [x] Room split/merge, door access changes, stockpile/growing-zone creation/reshape/deletion, storage-building spawn/despawn, Home edits and fog changes do not leave stale goals or permanently omitted coverage.
- [x] Normal tasks are not queued from remote area enumeration. Existing 15-cell/120-tick observation, fog/forbidden checks, Home-only mature-filth cleaning, task deduplication and vanilla job eligibility remain intact unless a separately recorded decision changes a tunable.
- [x] Existing emergency rescue/raid alerts, productive task ordering, route-based worker assignment and productive/player-ordered job precedence remain unchanged. Plant work, DLC support and a replacement movement pathfinder are not introduced.
- [x] Save/load and coordinator failover preserve productive tasks and safely restore or rebuild exploration state without saved room references. Loading pre-change arrival history cannot suppress an unobserved task; losing the final node and clean mod removal retain their existing behavior.
- [x] Matched room/zone fixtures demonstrate fewer redundant survey steps or lower task-discovery latency than the current selector, with no loss of discovered eligible targets. Record full-path requests, reachability checks, walked cells, selection cost/allocations and cache construction/invalidation cost; include median and tail selection cost, not only a warm-cache best case.
- [x] The measured improvement does not conceal a repeatable regression in total survey CPU cost or allocations under matched workloads. If faster discovery trades against increased planner cost, present the measured tradeoff for an explicit decision rather than accepting it as an unconditional optimization. Do not claim a percentage improvement without measurements.

## Implementation verification plan

- Capture a baseline and the proposed behavior on identical controlled geometry, dog counts, task eligibility and tuning. Separate cold construction/refresh cost from repeated selection cost; include a room-heavy colony, a large zone, a wall-detour case and an open-map fallback.
- Extend behavior scenarios around real observed discovery and physical task completion, not assertions about source text or data structures. Cover room selection versus point selection, scan/arrival misalignment, empty-area completion, a far-corner item, overlapping storage, unzoned Home work, competing worker claims and exterior work beyond two initial search horizons.
- Run the affected network scenarios through `tools/autotest.py run Network`, including discovery beyond the old boundary, scan eligibility, queue persistence and nearest-dog assignment. Add focused coverage for the criteria above. Run the shared-code sequence only when the implementation changes shared dispatch/ownership or needs final sequence proof under the repository test rules.
- Exercise actual private-instance movement and queue transitions, then perform save/load and failover checks, including an older save whose arrival history must not become certified scan coverage. If saved state/removal paths change, repeat the independent removal check. Never use the user's running game or profile.
- Record measured results in `docs/test-runs.md` and update the implementation plan/progress only after those runs prove the behavior. The original documentation-only ticket did not run these scenarios; the authorized implementation now records each exercised result there.

## Implementation progress

- Core cutover: `3954020`; initial behavioral and matched-workload fixtures: `fcd9d92`.
- The initial focused build `3954020-dirty.20261023T020344Z` passed all fourteen Survey behavior cases and four benches, with zero new errors. Network behavior separately passed all ten cases. These runs support the checked policy/coverage/eligibility criteria above.
- Replay-controlled measurements repeated **18/18 PASS** on `fcd9d92-dirty.20261023T053956Z`; all four proposed phases discovered both staged targets, while historical phases missed one within their cap. A separately reproduced distant retained-goal reshape now physically hauls 31 gold on `fcd9d92-dirty.20261023T060457Z` (**1/1 PASS**, zero new errors).
- Independent removal is **LOADED CLEAN**, zero new errors (2026-10-23 06:54), without the Quadrunner assembly through 600 ticks, save/reload and 600 more. Corrected tracing and riding fixtures passed **7/7**, zero new errors, on `ad76f81-dirty.20261023T075350Z`. [Matched measurements](../plans/0004-area-aware-survey.md#matched-workload-evidence) disclose faster warm selection and three improved workloads, but worse room-heavy results and higher cold costs. Final full-sequence proof remains pending; the performance decision is recorded below.
- **Performance decision (2026-10-23):** after reviewing the paired measurements, the user chose **Accept measured tradeoffs**. The room-heavy discovery/travel/CPU regression and higher cold allocation/latency are accepted while keeping the requested priority and scan-certified coverage policy. This is not a claim that every workload improves. Only final corrected shared-sequence proof remains pending.

## Implementation touchpoints

| Location | Intended boundary |
| --- | --- |
| [`Source/Quadrunner/Survey.cs`](../../Source/Quadrunner/Survey.cs) | Area/candidate selection, route validation, coverage goals and interruptible survey driver. Reuse `QR_Survey`. |
| [`Source/Quadrunner/Network.cs`](../../Source/Quadrunner/Network.cs) | Existing scan invocation, survey history and coordinator ownership. Connect completed local observations to shared coverage, including empty scans; distinguish them from arrival history. Keep productive task state/assignment route costs separate and unchanged. |
| [`Source/Quadrunner/Work.cs`](../../Source/Quadrunner/Work.cs#L63-L95) | Local scan completion/footprint integration if needed. Preserve eligibility, scan radius/cadence and task reporting; do not discover tasks remotely or infer coverage only from newly queued work. |
| [`Defs/ThingDefs_Races/QR_Races.xml`](../../Defs/ThingDefs_Races/QR_Races.xml#L101-L119) | Existing XML tuning. Keep new tunables in XML only if the settled design needs them; no new settings framework. |
| [`Source/Quadrunner/Scenarios.Network.cs`](../../Source/Quadrunner/Scenarios.Network.cs), plans and test-run notes | Observable behavior and persistence proof after implementation is authorized. |

## Research references

The mod source links above are tracked evidence. Vanilla references below are from the locally decompiled **RimWorld 1.6.4871**, not the wiki's newer API or new files to package:

- `.tools/decomp/Verse/RegionGrid.cs:28` — `AllRooms`; `Room.cs:189–302,363–407,445–477,536–553` — cell geometry, bounds, outdoor/doorway classification and the one-cell nature of `Room.Fogged`.
- `.tools/decomp/Verse/ZoneManager.cs:13,52–81` and `Zone.cs:58–66,98–108,211–252` — zone inventories, live membership, first-cell position and edits.
- `.tools/decomp/RimWorld/HaulDestinationManager.cs:18–32`, `Zone_Stockpile.cs:7–11`, `Building_Storage.cs:9–17,83–98` — stockpiles and storage buildings share native hauling/slot-group inventories.
- `.tools/decomp/RimWorld/Zone_Growing.cs:10–22`; the researched third vanilla zone, `Zone_Fishing`, is DLC-gated and outside this Core-only ticket. Home is an Area, accessed through `map.areaManager.Home`, not a `Zone`.
- `.tools/decomp/Verse/MapEvents.cs:11–52,167–185` and `RegionAndRoomUpdater.cs:77–111` — topology/roof notifications and their limits for zone/Home invalidation.
- `.tools/decomp/Verse/PathFinder.cs:523–591`, `PathFinderJob.cs:90–200,221–237` and `.tools/decomp/Verse.AI/PawnPath.cs:24–28,75–90` — weighted route search, returned cost and pooled path lifetime.
- [RimWorld modding tutorials](https://rimworldwiki.com/wiki/Modding_Tutorials), consulted through the offline mirror first: `Modding Tutorials/BigAssListOfUsefulClasses.md:55–57` identifies Map as the manager entry point; detailed 1.6 behavior was checked in the game source.
- [Nearest-neighbor algorithms: Ball Tree, KD-tree and construction/query tradeoffs](https://scikit-learn.org/stable/modules/neighbors.html#nearest-neighbor-algorithms).
