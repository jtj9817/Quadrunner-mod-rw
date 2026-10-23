# QR-0001: Prioritize surveying by rooms and zones

| Field | Value |
| --- | --- |
| Issue type | Improvement / performance |
| Status | Open — implementation not started |
| Component | Survey routing and local task discovery |
| Ticket key | QR-0001 — local documentation identifier, not an external Jira issue |
| Related design | [Plan 0003: Shared robodog dispatch network](../plans/0003-network-dispatch.md) |
| Authorized change | Research, document and commit this ticket only; do not implement it yet |

## Summary

Replace the utility dog's age-first lattice surveying with area-aware exploration: choose the closest available room, then prioritize storage areas, planting zones, other zones and finally outside the Home area. Use greedy nearest-neighbor selection for successive survey destinations, backed by RimWorld's existing pathfinder rather than a second movement algorithm.

This is both a proposed exploration-policy change and a performance improvement. Changing where dogs look first can improve useful discovery without necessarily reducing CPU cost. **No runtime profiling or speedup measurement was performed for this ticket.** The findings below distinguish existing behavior from recommendations.

## Current behavior and findings

| Finding | Evidence and implication |
| --- | --- |
| Surveying does not understand rooms or zones. | [`Survey.JobFor`](../../Source/Quadrunner/Survey.cs#L19-L60) enumerates a map-wide 20-cell lattice, rejects anchors beyond the dog's 80-cell search radius, then chooses by survey age with squared straight-line distance as the tie-break. The radius limits candidate selection for a leg, not distance from a stable. |
| Some expensive checks precede ranking. | [`GoalNear`](../../Source/Quadrunner/Survey.cs#L65-L71) searches nearby cells and calls `CanReach` before the candidate's age is compared. [`Age`](../../Source/Quadrunner/Survey.cs#L85-L95) traverses all saved survey records for each candidate reaching that stage. Full pathfinding is already skipped for candidates that cannot improve the current age/distance score; it is **not** called for every grid center. |
| Coverage is point-based, not room-based. | [`JobDriver_Survey`](../../Source/Quadrunner/Survey.cs#L113-L135) records the dog's actual position at the end of survey travel. A selected destination does not mean it was visited, and visiting one corner does not cover an entire room. The existing [coverage gotcha](../../TODO.md#testing) explicitly warns against aging neighboring unscanned tiles. |
| Discovery is separate from movement planning. | [`DogWork.ReportLocal`](../../Source/Quadrunner/Work.cs#L63-L127) scans revealed, unforbidden things within 15 cells; cleaning additionally requires Home and 600 ticks of filth age. [`DogNetwork.Tick`](../../Source/Quadrunner/Network.cs#L301-L358) normally scans every 120 ticks, including dogs already travelling or working. This scan has no room-boundary or line-of-sight test. |
| Task dispatch already uses vanilla routes. | [`AssignAll`](../../Source/Quadrunner/Network.cs#L495-L536) normally runs through the 60-tick network refresh and compares returned `PawnPath.TotalCost` for available capable dogs. Discovery is deduplicated by task kind and target ThingID in [`Report`](../../Source/Quadrunner/Network.cs#L250-L262). Survey selection must not become a second task dispatcher. |

**[INFERENCE] Optimization opportunities:** reduce repeated reachability/history work during destination selection and avoid low-value travel before searching colony work areas. Their relative cost and the effect of the proposed category order remain unmeasured; room enumeration and extra route comparisons could themselves become more expensive than the existing grid.

## Algorithm assessment

| Approach | Finding | Recommendation |
| --- | --- | --- |
| Greedy nearest neighbor | A policy for choosing the next destination, not a pathfinder or spatial index. Euclidean-nearest is not necessarily cheapest to walk to. | Within the current priority category and uncovered candidate set, choose the lowest **vanilla returned route cost**, with a deterministic tie-break. Do not claim a globally optimal tour or mathematically shortest room-entry route. |
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
2. Generate usable coverage points from actual area cells or valid adjacent observation positions. A room bounding-box center may be a wall or lie outside a non-convex room; `Zone.Position` is its first cell, not a centroid. One representative point cannot cover a large or irregular area.
3. Define an **available** destination as currently revealed, permitted, reachable through a fog-free route, not conflicting with another survey goal, and still needing coverage. Availability does not mean the room is empty or has a particular room role.
4. Select within the current category by vanilla returned route cost. A geometric shortlist must not silently exclude a better route: either evaluate the remaining eligible candidates or stop only with a validated lower-bound rule. Use deterministic ties and revalidate when the route or area changes.
5. Keep the local scanner active during travel and work. It may report legitimate nearby tasks from another category; the category order governs survey destinations, not a restriction on what a dog can notice.
6. Submit discovered tasks through the existing `DogNetwork.Report` path. Retain task priorities, target-exact job construction, reservations, queue trimming, one assignment per dog and cross-stable assignment. Surveying is idle movement, not a new normal/war task kind.

### Coverage, fairness and state

- Use finite coverage sweeps: prefer the nearest still-uncovered area in the current category, then advance when that category's currently eligible coverage is exhausted. Existing rooms must not cause endless selection of nearby rooms while lower categories never get visited. Revisit completed coverage in later sweeps; a productive assignment can preempt survey travel without falsely completing the interrupted area.
- Derive coverage from positions where a local scan actually occurred. Merely walking past a cell between scan intervals, choosing a goal or entering a room is not full-area observation. Large rooms and zones need multiple coverage points.
- Coordinate survey destinations across dogs without putting exploration reservations into the productive task queues. Preserve destination reservation, interruptibility, war suspension and maintenance/player-order precedence.
- Prefer existing native inventories and simple transient candidate data over a new manager or persistent spatial tree. Avoid rebuilding all room/zone cell sets for every dog on every dispatch tick. Any caching must have explicit invalidation and live validation of selected points.
- Room topology can change after walls/doors are edited. Vanilla exposes `map.events.RegionsRoomsChanged`; roof and fog changes also matter to classification. Zone/Home edits do not expose an equivalent general MapEvents notification: re-read live membership or use bounded refresh rather than assuming the room event covers them. Same-cell-count reshapes must also be detected.
- Keep geometry/index data transient. Do not save `Room`/`Region` references or runtime room IDs. Preserve the coordinator-owned survey/task state, save/load, failover and clean-removal contract. Any new saved coverage/sweep state requires explicit serialization and a safe interpretation of existing saves.

## Policy details to settle before implementation

Record the chosen answers in the new numbered implementation plan required by the repository workflow; do not silently change Plan 0003's completed acceptance results.

1. **Home precedence:** does outside-Home always mean last, even for a room or stockpile there? Recommended interpretation: apply the first four tiers to their Home footprint, include residual Home coverage, then survey all remaining revealed outside-Home cells. This makes the user's final priority explicit rather than accidentally promoting an exterior ruin to the room tier.
2. **Revisit policy:** confirm finite sweeps versus an age-based revisit window. Recommended default is finite category sweeps with shared coverage and later revisits, so an unchanged high-priority area cannot permanently starve exterior discovery.
3. **Search horizon:** confirm how the existing 80-cell candidate horizon applies to area selection. Recommended default is to preserve bounded local travel legs without restoring a stable/Home distance limit; repeated legs must still reach distant eligible areas. A local candidate cutoff must not be presented as a globally nearest-room guarantee.

## Acceptance criteria

All boxes below describe future implementation work and are intentionally unchecked.

- [ ] The chosen Home precedence, revisit/sweep behavior and search-horizon interpretation are recorded, and deterministic fixtures demonstrate the requested category order plus coverage of unzoned Home cells.
- [ ] Within a priority category, wall detours and forbidden doors affect selection correctly: geometric proximity alone cannot win, an unreachable candidate is skipped, and ties are deterministic. Describe selection as vanilla route-cost ranking, not exact global shortest-path optimization.
- [ ] Indoor stockpiles/storage buildings do not trigger redundant visits when room scans already covered them; outdoor stockpiles and shelves remain represented under the chosen Home policy.
- [ ] Large and non-convex rooms/zones, blocked centers, narrow corridors and separated revealed coverage all retain discoverability. A far-corner task is not missed because the area was marked visited at its entrance.
- [ ] Lower-priority areas are eventually surveyed on a finite, static fixture even while higher-priority rooms continue to exist. New work appearing after an earlier sweep is eventually discovered on a revisit.
- [ ] Multiple workers avoid conflicting survey goals and do not report/execute duplicate productive assignments. Interruption, charging, drafting, riding, war and death release or revalidate exploration coverage appropriately.
- [ ] Room split/merge, door access changes, stockpile/growing-zone creation/reshape/deletion, storage-building spawn/despawn, Home edits and fog changes do not leave stale goals or permanently omitted coverage.
- [ ] Normal tasks are not queued from remote area enumeration. Existing 15-cell/120-tick observation, fog/forbidden checks, Home-only mature-filth cleaning, task deduplication and vanilla job eligibility remain intact unless a separately recorded decision changes a tunable.
- [ ] Existing emergency rescue/raid alerts, productive task ordering, route-based worker assignment and productive/player-ordered job precedence remain unchanged. Plant work, DLC support and a replacement movement pathfinder are not introduced.
- [ ] Save/load and coordinator failover preserve productive tasks and safely restore or rebuild exploration state without saved room references; losing the final node and clean mod removal retain their existing behavior.
- [ ] Matched room/zone fixtures demonstrate fewer redundant survey steps or lower task-discovery latency than the current selector, with no loss of discovered eligible targets. Record full-path requests, reachability checks, selection cost/allocations and walked cells so increased CPU cost cannot be hidden behind a shorter route. Do not claim a percentage improvement without measurements.

## Verification plan — future implementation only

- Capture a baseline and the proposed behavior on identical controlled geometry, dog counts, task eligibility and tuning. Separate cold construction/refresh cost from repeated selection cost; include a room-heavy colony, a large zone, a wall-detour case and an open-map fallback.
- Extend behavior scenarios around real observed discovery and physical task completion, not assertions about the implementation's source text or choice of data structure. Include an item beyond the first scan radius in a large room, overlapping storage, unzoned Home work and a lower-priority exterior item.
- Run the affected network scenarios through `tools/autotest.py run Network`, including discovery beyond the old boundary, scan eligibility, queue persistence and nearest-dog assignment. Add focused coverage for the criteria above. Run the shared-code sequence only when the implementation changes shared dispatch/ownership or needs final sequence proof under the repository test rules.
- Exercise actual private-instance movement and queue transitions, then perform save/load and failover checks. If saved state/removal paths change, repeat the independent removal check. Never use the user's running game or profile.
- Record measured results in `docs/test-runs.md` and update the implementation plan/progress only after those runs prove the behavior. None of these game scenarios or benchmarks were run for this documentation-only ticket.

## Implementation touchpoints

| Location | Intended boundary |
| --- | --- |
| [`Source/Quadrunner/Survey.cs`](../../Source/Quadrunner/Survey.cs) | Area/candidate selection, route validation, coverage goals and interruptible survey driver. Reuse `QR_Survey`. |
| [`Source/Quadrunner/Network.cs`](../../Source/Quadrunner/Network.cs) | Existing survey history and coordinator ownership; change only if the chosen coverage/sweep representation requires it. Do not repurpose productive task state or assignment route cost. |
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
