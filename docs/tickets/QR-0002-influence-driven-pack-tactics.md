# QR-0002: Influence-driven pack tactics and coverage-based combat search

| Field | Value |
| --- | --- |
| Issue type | Improvement / AI architecture / performance investigation |
| Status | Proposed — research and design ticket; gameplay implementation not started |
| Component | Combatdog perception, pack coordination, tactical positioning and combat search |
| Ticket key | QR-0002 — local documentation identifier, not an external Jira issue |
| Requested direction | Multi-layer influence maps + utility AI + a coverage-based search planner, integrated with the existing Think Tree |
| Authorized scope | Research, write and commit this ticket only. No gameplay implementation, tuning changes or package rebuild is authorized by this documentation request. |
| Environment | RimWorld 1.6.4871, Core only; Harmony remains the sole dependency |
| Research baseline | Repository commit `13f7dfc`; gameplay fix `1ebcbc4`, packaged as `1ebcbc4.20261104T084614Z` |
| Related work | [Plan 0009: Combat packs, especially C4](../plans/0009-combat-packs.md#c4--continuous-engagement-and-nearest-member-targeting); [QR-0001: utility surveying](QR-0001-area-aware-survey-routing.md); [Plan 0004: scan-certified utility coverage](../plans/0004-area-aware-survey.md) |

## Summary

Introduce shared spatial information that lets autonomous T3/T4-led mixed-tier packs choose useful firing positions, avoid unnecessary exposure and bunching, and physically investigate lost contacts without repeatedly searching the same space. Keep RimWorld's Think Tree as the authority for behavior priority and vanilla jobs/pathfinding as the execution mechanism.

The requested optimizations are complementary, not interchangeable:

- **Influence layers** represent known threat, support, movement constraints, occupancy and uncertainty in space.
- **Utility scoring** chooses an eligible action and position from those inputs.
- **Shared reservations** coordinate commitments without a global combinatorial planner every tick.
- **Belief tracking and probabilistic movement prediction** prioritize where to look for previously observed contacts.
- **Coverage-based search** ensures physical observation still reaches low-probability space and unexpected contacts.

**This is a proposal, not a description of implemented features.** All implementation acceptance boxes below are deliberately unchecked. Existing C4 results prove the engagement handoff fix, not this architecture or a performance improvement from influence maps.

## Problem and verified findings

The current pack algorithm is an engagement-continuity and nearest-contact heuristic, not a tactical positioning or uncertainty model. The return-to-stable gap has already been fixed; do not reopen or reproduce that bug as if it were still present.

| Finding at the research baseline | Evidence and consequence |
| --- | --- |
| Packs retain a valid focus, then select the standing reachable hostile nearest to any eligible member. | [`PackCombat.TargetFor`](../../Source/Quadrunner/PackCombat.cs#L157-L186). Boolean reachability filters geometric ranking; the selection is not minimum walked route cost, tactical exposure or globally optimal engagement. |
| Contact acquisition already has three paths. | [`Combat.Spot`](../../Source/Quadrunner/Combat.cs#L28-L39) checks local range, target-cell fog and physical LOS; [`DogNetwork.Ping`](../../Source/Quadrunner/Network.cs#L415-L421) reports an attacker; the [wartime alert](../../Source/Quadrunner/Network.cs#L462-L467) reports revealed active threats map-wide without a local LOS requirement. These information sources need explicit provenance. |
| Queue insertion is not a continuing observation stream. | [`Report`](../../Source/Quadrunner/Network.cs#L281-L295) returns false for duplicates and sets `reportedAt` only on insertion. Updating belief only when a new queue row is added would lose later sightings; existing rows must not be mistaken for fresh last-seen evidence. |
| Discovery and strategic selection already have different cadences. | [`DogNetwork.Tick`](../../Source/Quadrunner/Network.cs#L424-L468) normally dispatches every 60 ticks and scans every 120 ticks; `force` bypasses both cadence gates. A forced ping can therefore trigger much more than a small tactical refresh. These are runtime cadences, not development estimates. |
| The native job-request boundary is essential. | [`PackCombat.Target`](../../Source/Quadrunner/PackCombat.cs#L31-L44) can refresh selection without starting/ending jobs; [`TakeOrder`](../../Source/Quadrunner/Network.cs#L775-L825) consumes it before idle behavior. A slow field update must not reintroduce the homeward gap. |
| Current ranged movement uses vanilla cast-position selection, not custom utility scoring. | [`Combat.AttackJob`](../../Source/Quadrunner/Combat.cs#L85-L112) requests `wantCoverFromTarget = false`, creates an attack or movement job, and retains its hostile in movement target B. Native cast-position selection still has its own preferences and validity checks; it is not an empty positioning implementation. |
| Utility coverage cannot certify combat search. | [`Survey.Scanned`](../../Source/Quadrunner/Survey.cs#L97-L122) marks a revealed radial footprint with no LOS restriction. Its [boolean coverage ledger](../../Source/Quadrunner/Survey.cs#L69-L83) lacks per-cell age or contact evidence; [`BeginSweep`](../../Source/Quadrunner/Survey.cs#L300-L307) clears that ledger. Do not interpret a utility-scanned cell as combat-clear. |
| Capture and focus-fire ownership deliberately differ. | [`CaptureKeeper`](../../Source/Quadrunner/PackCombat.cs#L193-L211) keeps non-leading dogs available for captures while leaders fight; [`ReleaseForPack`](../../Source/Quadrunner/Network.cs#L708-L719) frees ordinary combat assignments for shared focus. A single-owner lock on every hostile would break intentional focus fire and independent packs choosing the same nearest target. |
| Hard behavioral precedence already exists. | The [Think Tree](../../Defs/ThinkTreeDefs/QR_ThinkTrees.xml#L8-L37) handles despawn/downing, queued/player orders, Lord duties and charging before network work, and docking afterward. [`CanFight`](../../Source/Quadrunner/PackCombat.cs#L218-L239) also excludes player-forced jobs, carriers, capture jobs and unavailable dogs. Utility must remain subordinate to those rules. |
| Native events are useful but incomplete. | Local 1.6 `MapEvents.cs:16-60` exposes spawn/despawn, topology, door, terrain, fog, roof, glow and reservation notifications, but no general pawn-movement, contact-seen or pawn-downing event. `AttackTargetsCache.cs:110-155` is a hostile-membership cache, not an observed-position or visibility history. Event-only perception cannot be assumed. |

Historical evidence: [C4 development and actual-package results](../plans/0009-combat-packs.md#c4--continuous-engagement-and-nearest-member-targeting), including native job-start observations, saved pursuit intent, capture completion and protected draft/retreat behavior. No influence-map benchmark has been run for this ticket.

## Scope and preserved contracts

### In scope for a future implementation

- Localized, correctly invalidated multi-layer spatial fields shared across packs on a map.
- Separate strategic and tactical resolutions/cadences, with urgent invalidation and perception requests.
- Utility-scored attack continuation, approach, repositioning and investigation within autonomous pack behavior.
- Shared firing-position/search claims and shareable target-engagement commitments.
- Separate LOS-certified, age-aware combat coverage and bounded belief prediction.
- Actual-game behavior, lifecycle and matched cost/quality measurement before acceptance.

### Preserve

- The existing T3/T4 leadership and XML pack radius; T1/T2 may participate without becoming mandatory leaders. Standalone lower-tier behavior must not be changed incidentally.
- Shared hostile task deduplication, active raid alerts, attack pings and reachable target eligibility. Prediction is not permission to remove complete discovery paths.
- C4 native job-boundary retargeting, valid-focus retention, distant-pack independence, saved movement target B, and single-carrier captures. Reservations must not force a distant pack toward a farther target merely because another pack acquired a contact first.
- Drafted/player-forced commands, charging/retreat, incapacity, ongoing captures and existing productive/medical job priority.
- T3/T4 movement, armor, weapons, paired-shot behavior and independent smoke. Evaluate their actual capabilities; do not rebalance them under an AI change.
- Utility surveying and its observation contract. Reuse pure geometry/path/measurement techniques where appropriate, not `Survey`'s coverage, sweep state or Home-first search policy.
- Core-only loading, save compatibility, coordinator failover and clean removal. No new persistent `MapComponent`, external AI package, background service or alternative locomotion pathfinder.

### Not goals

- Mathematically optimal global combat, learned enemy intentions, blind fire at predicted coordinates, arbitrary DLC/mod compatibility, or full GOAP/HTN planning.
- An exclusive target lock for every combatant, mandatory global formation optimization, or a permanent whole-map fine grid per dog.
- Implementing unrelated suspected utility-survey issues discovered while researching reuse boundaries.

## Proposed architecture — not yet implemented

### Ownership and Think Tree integration

Keep one map-shared tactical state owned by the existing `DogNetwork`/`PackCombat` boundary. It contains the observation registry, field revisions, active fine chunks, beliefs, combat-search coverage and leases. Packs retain local objectives and member assignments; sharing fields does not mean sharing one compulsory map-wide target.

Use one decision/execution path:

1. Collect permitted observations and invalidation cues; coalesce them into bounded pending work.
2. Update the affected information layers. Event callbacks invalidate or enqueue work; they do not start pawn jobs.
3. At a scheduler refresh or native job request, validate the dog's hard eligibility and current commitment.
4. Read coherent spatial data, score feasible alternatives, validate native constraints and commit the required claims.
5. Return an intent through `DogNetwork.TakeOrder`/`JobGiver_RobodogWork`; construct the corresponding vanilla attack, movement or capture job through the existing combat path. A dedicated search driver may be necessary to wait for a real combat observation, but not a second dispatcher.
6. Preserve equivalent native jobs rather than repeatedly cancelling warmups, bursts or cooldowns. Commit a replacement only at an allowed decision boundary, unless a hard validity/safety override requires interruption.

Suggested intent data: action, optional **confirmed** target ID, candidate cell, pack/membership revision, observation/geometry revisions and claim generation. This is a proposed contract, not an existing API. Investigation intents reference an uncertain area/post; they must not masquerade as a confirmed enemy attack.

Selection-only requests must remain free of job-start/job-end side effects. If expensive fields are pending, the continuity path still considers already-known feasible combat orders using live validity checks; “not computed” must never become “no enemies” and fall through to the stable. Do not keep invalid actions alive merely to avoid a cache miss.

### Multi-layer information model

Keep independently meaningful raw layers rather than one opaque ally-minus-enemy heatmap.

| Layer | Meaning and permitted source | Resolution/use |
| --- | --- | --- |
| Traversability and geometry | Known walls, doors, terrain, cover, permission constraints and topology revisions. Pawn-specific traversal remains authoritative. | Coarse region/connectivity summaries; exact tile/native checks for committed actions. |
| Confirmed enemy threat | Weapon-dependent threat envelopes from permitted contact snapshots; distinguish ranged lines of fire from melee approach potential. | Coarse conservative summaries and full-resolution active engagement chunks. |
| Friendly support | Actual eligible friendly weapon coverage/readiness, not just headcount. Exclude the requesting dog where self-support would double-count it. | Shared tactical layer; evaluate the member's real weapon/range and action state. |
| Occupancy, hazards and commitments | Separate physical occupancy/fire, intended firing cells, search posts and shareable engagement load. Smoke is not damaging firepower. | Exact cells/local neighborhoods; claims affect contention, not whether space was observed. |
| Contact belief | Conditional location distribution and confidence for a previously observed contact, plus unknown/outside support. | Bounded coarse region/cell distribution; refine only where decisions need detail. |
| Combat observation/search coverage | Observation footprint, timestamp, observer/provenance and applicable geometry revision; stale/unknown coverage remains searchable. | Regional summaries backed by exact LOS-certified tile observations. |

Use fixed, documented normalization scales/curves for utility inputs. Do not renormalize all fields against the current global minimum/maximum after each local edit: that can change scores everywhere and defeat localized invalidation. Preserve raw values separately from clamped/normalized views.

Weapon effectiveness must respect [range-based selection and supplemental smoke](../../Source/Quadrunner/Tiers.cs#L65-L141), and [paired projectile behavior](../../Source/Quadrunner/Tiers.cs#L154-L164). Avoid exact-damage claims from a rough threat model. Cover can contribute to estimated incoming/outgoing effectiveness rather than being counted again as an unrelated benefit for the same effect.

## Optimization findings and recommended design

### 1. Localized updates and removal of obsolete contributions

**Recommendation:** maintain source IDs, observed source signatures, previous affected chunks and topology dependencies. A signature includes the permitted position/state and the weapon/strength/range inputs that determine propagation.

- On a source change, dirty the union of its **old and new** affected chunks. On removal, dirty its old chunks even though no current pawn exists there. Coalesce repeated changes before recomputation.
- Prefer a correctness-first dirty-chunk rebuild: clear the affected raw layer cells and recompute them from every still-relevant source intersecting that chunk. This removes obsolete contributions without storing/copying every source's complete cell stencil.
- Exact subtract-old/add-new stamping is an optional measured optimization for additive raw layers only. It requires the exact old kernel, weights and footprint. Subtracting a capped, normalized or `max`-aggregated value is not generally reversible; overlapping sources must remain correct after one disappears.
- Moving sources, loss of confirmed observation, weapon/range changes, downing, death, despawn, faction changes, charging and pack departure must not leave ghost threat, support or claims. Belief replaces an uncertain contact's confirmed stamp; it is not an additional duplicate confirmed enemy.
- A wall/door edit is not necessarily local in effect. It can open a long firing ray or alter many shortest approaches. Invalidate potentially affected sources by conservative range envelopes/dependency information and revise affected connectivity; include influence that could newly appear beyond the changed blocker, not just cells formerly stamped.
- Topology changes can require a wider regional or bounded cold rebuild. Do not promise constant-size updates for every geometry edit. Until recomputed, dirty data is unknown/stale, not zero danger or newly clear search space.
- Publish a coherent result for affected chunks/revisions, never a partially removed/restamped source visible to a decision. Repeated events and remove/re-add operations must be idempotent.

**Cost hypothesis [INFERENCE]:** this can eliminate repeated whole-map/per-dog field work when few sources change. Dense moving raids, overlap and topology edits may erase that benefit; source-index maintenance and old-footprint bookkeeping must be measured too.

### 2. Staggered strategic updates and urgent reactions

Use distinct responsibilities, not a single slow “AI tick”:

| Work | Scheduling rule |
| --- | --- |
| Damage ping, invalid current target/order, hard retreat/command override | Mark relevant state immediately and service the next eligible native decision; never await the strategic refresh. Preserve native re-entry safeguards. |
| Newly discoverable/changed contact | Request permitted verification from event cues where available; retain a bounded periodic scan for movement/visibility changes without native notifications and for unexpected contacts. |
| Active tactical chunks and claims | Update affected chunks/leases on relevant changes; distribute ordinary work by stable pack/chunk identity while prioritizing dirty data about the next action. |
| Regional summaries, belief diffusion and search ranking | Run less frequently than combat detection, under explicit per-pass work and maximum-age limits. New activation/cold load must not wait for an arbitrary stagger slot. |
| Native movement, stances and weapon execution | Continue on vanilla ticks; a strategic refresh must not restart them. |

Keep the existing 120-tick discovery contract unless a separately approved implementation decision changes it. Event-driven requests supplement it; they are not a claim that the current scanner already observes every movement event. Avoid using `Tick(force: true)` as the only urgent-tactics API: its current semantics also run discovery/utility work. Any extraction of a cheaper urgent path must preserve centralized war-mode transitions, worker recall and existing forced medical dispatch semantics.

**Event-driven feasibility:** RimWorld 1.6 does not expose a native notification for every enemy move or LOS change. Existing own-dog component callbacks/ticks can supply local movement/damage/availability cues without a global patch; a stationary observer still needs bounded verification or a separately approved movement-invalidation hook to detect an enemy walking into sight. If “event-driven perception” means reacting to every such movement event, record that hook requirement explicitly. Do not describe periodic polling as purely event-driven.

Native invalidation limits also need handling:

- `RegionsRoomsChanged` carries no changed-cell payload and is dispatched after the region dirty list is cleared (`RegionAndRoomUpdater.cs:77-110`). Capture useful spatial cues from earlier object/terrain changes; otherwise invalidate the relevant coarse revision conservatively rather than inventing a region diff.
- Faction relationship changes directly refresh the native attack-target cache (`RimWorld/Faction.cs:1066-1069`) but have no corresponding general MapEvents relation-change notification. Revalidate hostile membership before committing an attack; a cached source must not continue projecting hostile influence after relations change.
- General reservation-added/removed events do not cover destination claims, and MapEvents has no gas-diffusion notification. Query live destination ownership at commitment and bound local resampling of dynamic shot-effect inputs. Roof/glow/terrain events affect their own layers; they must not be assumed to be complete physical-LOS invalidation signals.

Bound dirty backlog and data age as well as average work. A flood of events must not starve an older contact or a low-priority search region indefinitely. Validate immediate action legality against current native state even when strategic data remains cached.

### 3. Hierarchical grids and topology-aware propagation

Use regions/connectivity-aware coarse summaries for strategic attention and bounded, reusable one-tile-resolution chunks for firing positions. A coarse chunk that straddles a wall must retain its separate connected components/portals; a favorable average is not proof that every tile is safe or reachable.

- Activate fine chunks around engagements and shortlisted search posts, with a halo sufficient for relevant weapon influence. Sources outside a fine chunk can still threaten it; the chunk boundary is not a weapon-range cutoff.
- A queued target outside the current fine window remains a contact and can become an approach objective. Hierarchy must not create a new stable-centered or pack-window pursuit limit.
- **Direct fire:** use actual weapon geometry and native shot/LOS checks; do not diffuse bullets around corners. Physical LOS, shooting validity, cover and smoke-related shot effects are distinct inputs.
- **Melee/approach influence and travel:** use connectivity and reachable travel distance/cost where relevant. A nearby unit behind a sealed wall is not immediate melee pressure. Region-hop count is a heuristic, not walked distance; `CanReach` is a boolean, not a route cost.
- **Friendly travel:** respect each dog's actual traversal and allowed area. **Enemy prediction:** use an explicitly approximate movement model from observed capabilities; the dog's door permissions cannot be assumed to describe enemy movement.
- Validate/rank a bounded declared candidate set with vanilla returned routes and dispose pooled paths. Where path exposure matters, evaluate the accepted route, not only a safe-looking endpoint. Do not pathfind every cell/source pair or build another locomotion pathfinder.
- Coarse selection may shortlist, but exact fine/native legality is mandatory before commitment. If candidate pruning is heuristic rather than justified by a valid bound, document that it sacrifices optimization quality; do not call it global optimum.
- Preserve [`PathInputsReady`](../../Source/Quadrunner/Network.cs#L142-L162): a newly created allowed area can require a later native tick. Deferred validity/ranking is pending, not unreachable, safe, observed or permission to bypass restrictions.

Reusable radial falloff templates are appropriate for cheap geometric kernels, but obstacle/LOS masks and topology-aware costs still depend on the current map. The external influence-map reference explicitly identifies this limitation; circle stamping alone is not sufficient.

### 4. Shared reservations without exclusive focus locks

Use a small, map-shared commitment ledger, updated for affected actors rather than recomputing a global joint plan every tick.

| Commitment | Ownership/semantics | Required integration |
| --- | --- | --- |
| Standing hostile engagement | Shareable pack/member commitment; optional estimated committed firepower, not a promise of future damage. | Preserve intentional focus fire and independent packs sharing a target. Never use an unconditional single-owner hostile lock. |
| Firing/approach cell | Exclusive autonomous destination claim, with soft spacing/congestion around it where feasible. | Check planner claims **and** native destination/physical occupancy rules before job startup; retain live native checks. |
| Search post/sector | Short-lived claim on planned observation effort, not ownership of every cell in a region. | Avoid redundant observer placement while allowing necessary LOS overlap and urgent rescue of abandoned work. Claimed space is pending, never searched. |
| Downed enemy capture | Existing one-assignee task and vanilla reservation/bed rules. | Do not create a second capturer or let speculative tactical claims block an already-started capture. |

Every lease needs owner identity, pack/membership generation, intent/job identity, relevant cell/target, expiry and progress-based renewal. Validate and commit sequentially on the simulation thread with deterministic actor ordering; revalidate if the world or claim revision changed since scoring. Distinguish an actor's own claim from a conflicting claim.

Release or revalidate on interrupted/failed/completed jobs, expiry, lost progress, death/downing, power-down, charging, draft/player override, capture, map exit, target invalidation, changed geometry and pack merge/split/leader loss. A resumed valid intent must not acquire duplicate claims. Coordinator transfer on a live map must not duplicate or indefinitely orphan leases; final-node loss/load/map removal must safely clear or rebuild them.

**Native limitation:** `PawnDestinationReservationManager.Reserve` records a destination and obsoletes the claimant's older entries; it does not atomically reject a duplicate same-faction destination. Vanilla `JobDriver_Goto.TryMakePreToilReservations` calls it and returns true. Use explicit check/commit coordination; a check-only `CastPositionRequest.validator` is not itself a reservation. Ranged `AttackStatic` does not reserve an exclusive enemy target. See the 1.6 API references below.

An overcommitment penalty is a possible utility input, not authorization to distribute dogs to farther targets contrary to the current focus contract. Any utility-based cross-pack target redistribution needs a recorded policy decision first.

### 5. Utility scoring through the existing Think Tree

Apply hard constraints before soft preferences: authority/role, draft and player orders, battery/health state, active carry/capture, valid confirmed target, allowed movement, native shooting/route validity and required claims. A high score cannot buy permission to violate them.

A proposed fixed consideration set is:

`U(action, cell) = effective fire/progress + usable ally support + expected observation gain - incoming exposure - travel cost - congestion/friendly-fire risk - commitment disruption`

Each term has an explicit bounded scale/response curve and XML weight; this expression is a proposed heuristic, not an existing method or an exact combat outcome predictor. Keep it a small game-specific scorer, not a generic planning framework. Include the current action/current cell among candidates so repositioning must earn its cost.

- Baseline policy keeps C4's nearest-reachable-member target choice and valid shared focus. Utility selects continuation, approach/reposition positions and search actions; utility-driven target switching is a separate approval question below.
- Evaluate cheap predicates and geometric estimates first, then cached spatial inputs, then expensive exact LOS/native routes for plausible candidates. Safe pruning needs valid score bounds; a fixed top-k is an approximation that needs quality measurements.
- Give a valid current commitment a clear switching margin/minimum hold rule. Include interrupted warmup, lost bursts, movement and weapon-switch costs; score jitter must not produce repeated job replacement. Hard invalidity and required retreat/player overrides bypass hysteresis.
- Preserve one coherent pack objective while assigning distinct usable positions within the recorded cohesion/radius policy. Do not accidentally split and re-merge packs every refresh because the position scorer pulls members away from their leader.
- Revalidate before executing: a position score does not prove that a shot remains legal or a route still exists.

**Concrete 1.6 integration:** `CastPositionRequest` supplies a boolean `validator`, `preferredCastPosition` and search bounds, but no arbitrary float utility-scoring callback. Its finder can evaluate/accept a supplied preferred cell before its normal search. Rank declared candidates in mod code and validate them through those native constraints/verb/path APIs; do not pretend that attaching a boolean validator replaces vanilla's preference calculation. Identify and justify any remaining API gap before adding Harmony. Existing hooks are preferable to patching private cast-position scoring.

### 6. Belief maps and probabilistic movement prediction

**Finding:** these can prioritize later observation and uncertainty-aware positioning, but cannot establish where an unseen enemy initially is. With no observation history, prediction cannot replace the first real scan. Existing map-wide revealed raid alerts also reduce the situations in which prediction adds value; model maintenance could cost more than the scan work it saves.

Proposed observation contract:

- Tag direct LOS sightings, authoritative existing raid alerts and damage pings separately, including tick, permitted position precision and last observed capabilities. Preserve each source's current information entitlement; do not silently weaken alerts or grant pings exact hidden locations that the current reporting gate would reject.
- Refresh last-seen snapshots for every qualifying completed sighting/alert, even when `Report` rejects a duplicate task. Separate queue deduplication from observation-sequence deduplication; a later real scan is new evidence, not a redundant queue insertion.
- Use the existing direct-sighting range/fog/physical-LOS predicate as the baseline sensor model. `Combat.Spot` uses raw `GenSight.LineOfSight`, which does not itself perform a gas/smoke visibility test; do not quietly introduce a different visibility rule in the belief module. Native shot validity/effectiveness remains a separate check.
- A global engine event may invalidate a registry/cache entry or request verification, but it is not proof that a dog observed the event's position. Unfogged means revealed, not currently seen.
- When there is no fresh permitted position update, predict from the stored snapshot only. Never read an unobserved enemy's live position, destination, path nodes, job target or hidden current statistics to make the prediction appear accurate.

A compact proposed filtering model separates:

1. **Location conditional on contact existence**, spread over bounded coarse support.
2. **Confidence/existence and unknown/outside mass**, which can decay without artificially concentrating all probability in the remaining cached cells.

The conceptual update is `predicted(y) = sum_x transition(y | x, elapsed, observed motion, known topology) * belief(x)`, followed by `posterior(y) proportional to observationLikelihood(y) * predicted(y)`. This is the prediction/observation separation described in the CS 188 reference, adapted here as a proposed approximate game model.

Use an observed-speed displacement bound and a mixture of observed heading, stopping and alternate reachable turns; do not assume perfect constant velocity through walls or knowledge of the enemy's intended target. Bound active support and update work; keep discarded/pruned support represented as unknown rather than renormalizing it into false certainty. Aggregate per-contact distributions as expected occupancy/threat, not automatically as a calibrated probability that any enemy exists there.

Only a completed observation of an actually detectable LOS footprint can contribute negative evidence. Arrival, a path crossing, a claim or a utility scan cannot. Negative evidence ages and becomes uncertain again; newly spawned enemies and moving contacts can enter a previously clear area. Deduplicate correlated reports within the same observation identity/sequence, without discarding later genuine sightings. If observations eliminate all modeled support, return to an explicitly unknown prior/search policy, never the hidden true position.

Confirmed death/capture/despawn removes obsolete active contributions. A probabilistic search contact alone must not create a fake live `Combat` task, keep wartime deployment running forever, or authorize blind fire.

### 7. Coverage-based combat search and reduced scanning

Search places observers so real perception can acquire/reacquire contacts. It does not identify hostiles by remotely inspecting likely cells or replace native movement.

- Enter investigation/search only when no actionable confirmed engagement, existing assignment, capture or higher-priority order is available. Search is subordinate to engagement continuity, charging and player authority.
- Maintain a finite, shared cohort of candidate observation posts/coverage in permitted revealed search space. Use valid region portals and actual area geometry, not room/coarse-cell centers that may lie in walls.
- Rank useful posts by expected new observation gain, stale/unseen coverage, accepted travel cost, route exposure and existing commitments. Belief focuses effort but must retain a residual revisit/coverage budget for low-probability and newly revealed space.
- Credit the **actual completed combat observation footprint** with its observer position, sequence and geometry revision. A scan earlier in the same tick at a different position is not arrival coverage. Empty observations count; predicted visibility and selecting a post do not.
- Pending claims do not complete a sweep. An inaccessible post does not certify its whole prospective footprint: try other observation positions where useful and retry deferred geometry when it changes. Preserve honest progress through interruptions, and avoid repeatedly resetting a nearby high-probability area while distant pending coverage starves.
- A moving enemy invalidates permanent “area clear” assumptions. Use timestamps, observation validity and revisits, not the utility survey's unaged binary array. Search claims are separate from utility and productive queues.
- Keep existing configured patrol/guard/dock behavior as the closure behavior when no actionable threat/search obligation remains; do not allow speculative confidence to hold packs in perpetual search. Record how configured patrol waypoints seed or constrain searches before implementation.

**Initial-scan optimization recommendation:** compare cheap native hostile-cache enumeration with spatially bounded native cell/chunk candidate queries, then perform exact eligibility/range/fog/LOS filtering only where needed. Any spatial membership cache needs complete movement/lifecycle maintenance. Do not introduce a second index just because prediction is available, and do not skip unseen parts of the sensor footprint merely because their predicted probability is low. A fully observed footprint can certify negative evidence only if its entire declared detection work actually completed.

For overlapping observers, share candidate enumeration and completed observations where the sensor predicates truly match; different positions/LOS still require their own checks. Preserve the independent map-wide wartime alert and a bounded periodic verification path so a never-seen flank/spawn remains detectable. Reduced scan count without measured target recall and detection latency is not an optimization result.

## Implementation touchpoints and API evidence

These are proposed implementation boundaries, not new files created by this ticket.

| Location | Intended change/boundary |
| --- | --- |
| [`PackCombat.cs`](../../Source/Quadrunner/PackCombat.cs) | Keep membership/eligibility/shared focus and the selection-only job-boundary path; consume tactical intents and manage pack revision/commitment continuity. |
| [`Combat.cs`](../../Source/Quadrunner/Combat.cs) | Feed completed combat observations; construct native attack/movement/capture jobs using selected positions and retained combat intent. Keep confirmed attacks separate from uncertain investigation. |
| [`Network.cs`](../../Source/Quadrunner/Network.cs) | Own shared transient tactical state/lifecycle, retain queue authority and war transitions; schedule bounded strategic work and a targeted urgent path without repeatedly rescanning utility work. |
| [`Work.cs`](../../Source/Quadrunner/Work.cs#L224-L251), [Think Tree XML](../../Defs/ThinkTreeDefs/QR_ThinkTrees.xml) | Keep the existing job-giver entry and hard priority order. Combat observation/search must not repurpose `DogWork.ReportLocal` or utility `Survey.Scanned`. |
| [`Robodog.cs`](../../Source/Quadrunner/Robodog.cs#L150-L204), [`Tiers.cs`](../../Source/Quadrunner/Tiers.cs#L65-L141) | Reuse own-dog lifecycle/damage cues and existing real weapon state. Do not add a global per-pawn Harmony movement hook by default. |
| Proposed small combat-specific source files | Observation/field/search/scoring responsibilities may be separated for maintainability, but remain owned by the existing network. No new saved manager/component or general AI framework. |
| [`Defs/ThingDefs_Races/QR_Races.xml`](../../Defs/ThingDefs_Races/QR_Races.xml) | Store combat-only weights, propagation scales, budgets, refresh/age/lease limits and hysteresis through the established DefModExtension pattern. Do not change utility timing or duplicate common tuning on each pawn. |
| [`Scenarios.PackContinuity.cs`](../../Source/Quadrunner/Scenarios.PackContinuity.cs), [`Scenarios.Packs.cs`](../../Source/Quadrunner/Scenarios.Packs.cs), [`Scenarios.SurveyBench.cs`](../../Source/Quadrunner/Scenarios.SurveyBench.cs) | Preserve current regressions; add observable tactical/search and matched measurement cases with isolated instrumentation. Do not turn the utility benchmark into proof of combat visibility. |

Local vanilla evidence is from `.tools/decomp/`, which is ignored development material, not code to ship or an external dependency:

- `Verse/MapEvents.cs:16-60,77-84,157-185`: available change notifications and the absence of a universal movement/LOS/downing event; `ThingFactionChanged` carries factions, not a precise changed-actor observation.
- `Verse.AI/AttackTargetsCache.cs:110-155`: hostile membership and spawn/despawn/hostility updates; not perception memory or a spatial movement index.
- `Verse/RegionAndRoomUpdater.cs:77-110`; `RimWorld/Faction.cs:1066-1069`: global topology notification after dirty-list clearing, and relation changes updating native hostile membership without a general MapEvents relation-change callback.
- `Verse/GenSight.cs:20-67`: physical LOS with an optional cell validator, not a complete weapon visibility/accuracy policy.
- `Verse.AI/CastPositionRequest.cs:5-27`; `CastPositionFinder.cs:141-159,197-290,293-347`: preferred cell/boolean validator, native walkability/allowed area/reachability/hit/reservation/danger checks and its own scoring.
- `Verse/PawnDestinationReservationManager.cs:63-88,165-200`; `Verse.AI/JobDriver_Goto.cs:11-20`: destination registration versus conflict checking, and native Goto startup.
- `Verse.AI/ReservationManager.cs:157-236,300-328`: job/actor-aware reservations and native validation; `JobDriver_AttackStatic.cs:21-24,66-117`: no exclusive enemy reservation and native attack termination.
- Existing [`PathInputsReady`](../../Source/Quadrunner/Network.cs#L142-L162), [`Survey` event lifetime](../../Source/Quadrunner/Survey.Geometry.cs#L27-L59), [map cache cleanup](../../Source/Quadrunner/Network.cs#L164-L183) and [final-node clearing](../../Source/Quadrunner/Network.cs#L261-L279) are verified mod integration patterns. Retain the subscribed event publisher for disposal; do not dereference a cleared `map.events` during map teardown.

## Acceptance criteria

These describe future implementation evidence. A design review or passing historical C4 run must not tick them.

### Behavior and information correctness

- [ ] **AC-01 — Authority and continuity:** real-game drafted/player orders, charging/retreat, carry/capture and incapacity still override utility; the existing C4 target-death/native-job-boundary and saved-pursuit regressions pass with zero homeward detours while actionable queued threats remain.
- [ ] **AC-02 — Tactical value:** matched encounters demonstrate useful individual firing positions/cohesion and less unnecessary bunching/exposure or improved effective fire, without obtaining gains by weakening enemies, changing weapons/armor or exploiting hidden information. Report losses and encounter completion too.
- [ ] **AC-03 — Source replacement:** moving, removing, downing, capturing, despawning, changing faction or changing an observed weapon/support state removes obsolete contributions. On controlled snapshots, incremental field results agree with a full-rebuild reference within a documented numeric tolerance, including overlapping sources, repeated events and map edges.
- [ ] **AC-04 — Topology and hierarchy:** wall/door opening/closing/destruction, cover/terrain edits and coarse cells straddling disconnected space do not permit fire/propagation/reachable-position claims through inappropriate barriers. Test newly exposed cells beyond an old blocker and relevant sources outside a fine chunk. Region heuristics never substitute for final pawn-specific legality.
- [ ] **AC-05 — Urgent events versus staggering:** target invalidation, damage pings, commanded retreat and changed immediate safety do not wait for a strategic field refresh. Dense dirty backlogs respect age/work limits without missing a new contact or restoring C4's idle gap; stale/half-published data is never treated as valid clear space.
- [ ] **AC-06 — Stable decisions:** near-equal scores, moving targets, weapon-range boundaries and a closer newcomer do not repeatedly restart native firing jobs, destroy sustained fire or oscillate pack membership. Required invalidation/retreat still interrupts promptly.
- [ ] **AC-07 — Reservation lifecycle:** two same-tick autonomous choices do not commit the same exclusive firing/search post. Focus remains shareable, claims do not block legitimate captures, and interruption/failure/no progress/expiry/leader loss/merge/split release or revalidate ownership without duplicates or deadlock.
- [ ] **AC-08 — Honest belief:** observations from different permitted sources are distinguishable. Later sightings of an already-queued contact refresh its last-seen snapshot independently of queue insertion. Hidden movement/routes cannot update that snapshot; topology-aware movement prediction represents uncertainty and unknown/outside support. Tests include stopping, reversal, a blocked corridor and an unexpected alternate approach; predictions never become attackable phantom pawns.
- [ ] **AC-09 — Negative evidence:** utility scans, unfogging, arrival, claims and same-tick scans at old positions cannot certify combat-clear space. Actual empty LOS observations count, stale negative evidence expires, and overlapping observers do not manufacture confidence through duplicate evidence.
- [ ] **AC-10 — Coverage and discovery:** cold start, no-history contacts, low-probability flanks, newly revealed cells, a contact spawning after a clear scan, irregular rooms and unreachable first-choice posts remain discoverable through real perception/search. On finite static eligible fixtures, pending coverage cannot starve indefinitely; claimed/unreachable coverage is not silently marked observed.
- [ ] **AC-11 — Existing systems:** independent/distant mixed packs, shared-nearest focus, configured patrol closure, single-carrier capture, T3 weapons, paired T4 fire/smoke, utility surveying and medical/worker war behavior remain correct. Any approved target-priority or patrol-policy change is explicit rather than an accidental score effect.

### Lifecycle and engineering

- [ ] **AC-12 — Load/failover/removal:** save/load during attack, tactical travel and search rebuilds transient fields, beliefs and leases without inventing past observations or losing valid productive/capture orders. Repeated live coordinator failover, final-node loss and map teardown retain/dispose the correct state. No saved `Room`/`Region` pointers or new persistent map component; required independent no-mod removal proof is clean.
- [ ] **AC-13 — Native contracts:** selected positions and routes pass live verb/allowed-area/native reservation checks; new allowed-area timing is deferred correctly. Runtime callbacks/Think Tree evaluation do not recursively start/end jobs, and event handlers are unsubscribed from the original publisher. Tunables are in combat XML, and gameplay remains Core + Harmony only.
- [ ] **AC-14 — Bounded implementation:** shared/chunk storage and reusable buffers avoid full fine grids per pawn, repeated whole-map clearing/copies and avoidable per-tick allocations. Bounded work has a progress/data-age guarantee, not an indefinite pending state; all existing callers use the new completed path with no obsolete parallel planner.

### Evidence and delivery

- [ ] **AC-15 — Matched measurement:** record the current C4 baseline and proposed planner against identical controlled geometry, fog, forces, weapons, initial snapshots and contact schedules. Measure cold construction, normal selection, prediction, dirty/invalidation work and dense moving worst cases; report median/p95/tail and full-encounter CPU/allocations/memory, not only warm field lookup or reduced scan count.
- [ ] **AC-16 — Discovery quality and combat outcomes:** record actual first-observation and first-queue-report ticks, missed contacts, probability-prior errors, physical walked cells, route/LOS requests, job replacements/warmup interruptions, casualty/damage results and completion/timeout outcomes. Continuously observable unexpected contacts must not gain a larger detection bound merely to improve compute figures. Faster search cannot conceal lost recall, permanent false-clear space or combat-quality regression.
- [ ] **AC-17 — Honest performance conclusion:** demonstrate a repeatable benefit under declared workloads and retain correctness. If better tactics or lower travel trades against higher CPU, memory or detection latency, report that tradeoff for explicit acceptance; this ticket does not authorize treating fewer computations in one subsystem as lower total cost. No unmeasured percentage or optimality claim.
- [ ] **AC-18 — Actual execution and package:** scoped isolated-game scenarios exercise native movement, firing, observation, claims and job boundaries, including normal and accelerated tick speeds; tests alone do not substitute for that runtime proof. Follow repository shared-code test scope, record zero new errors, commit verified implementation, build from committed source and run affected scenarios from the actual package with loaded BUILD/modRoot checked. Update plan/progress/evidence only after the corresponding run; stop the private instance before delivery.

## Verification design and measurement cautions

Use controlled native encounters, not tests that only assert a field exists or a mocked score was returned. Include open terrain, a two-entrance/wall-detour fight, a doorway chokepoint, separated packs, a dense moving raid, a lost-contact search, a wrong-prediction flank and a no-contact/expired-belief control. Reload the same private pre-encounter snapshot for matched phases and pin/stage conditions that materially affect comparison. Report repeated outcomes rather than implying one stochastic firefight proves a universal win rate.

[`Scenarios.PackContinuity.cs`](../../Source/Quadrunner/Scenarios.PackContinuity.cs#L13-L29) already observes native job starts between rendered frames. Reuse that measurement principle for homeward gaps, claim transitions and warmup churn. Instrumentation remains scenario-only; disable/unsubscribe temporary runtime probes when each run finishes.

[`Scenarios.SurveyBench.cs`](../../Source/Quadrunner/Scenarios.SurveyBench.cs#L17-L43) provides useful matched-world/cost patterns, but is not a combat benchmark:

- Its target polling currently inspects normal work; combat contacts are in the war queue. Record real sensor/report events, not merely entering a distance radius or reaching a goal.
- Full-phase process CPU includes all private-game threads, native paths/rendering and instrumentation; distinguish it from scoped planner elapsed time and avoid double-counting nested subtotals.
- Validate allocation counters with a real allocation probe. This Unity/Boehm runtime can return zero from the managed current-thread API; process-total fallback is a different scope, not exact planner attribution.
- Distinguish a refresh call/check, actual geometry/chunk rebuild, LOS test and route request. Instrument the actual call sites; carrying an unincremented legacy counter into a new report proves nothing.
- Charge source tracking, dirty-queue/index maintenance, negative-evidence processing, prediction, claim validation, per-tick driver checks and cache teardown/cold load. Do not move expensive work outside the timed selection and call it eliminated.
- Keep the entire observed/revealed benchmark world matched, not just a local pad; existing utility work documented native fog/reload and pending-path pitfalls in [Plan 0004](../plans/0004-area-aware-survey.md#decisions).

No new game run, performance benchmark or package build was performed for this documentation-only ticket. The existing package's C4 **10/10 PASS, zero new errors** is baseline evidence only; none of AC-01 through AC-18 is claimed satisfied here.

## Policy decisions to record before implementation

1. **Target utility:** retain C4's nearest reachable shared focus and use utility for actions/positions/search (recommended conservative interpretation), or explicitly authorize utility-based target switching/overcommit redistribution. The latter changes recorded nearest-target behavior and needs agreed switching rules and fixtures.
2. **Search bounds and patrol intent:** define the active combat-search domain, how existing player-configured patrol waypoints constrain/seed it, and when uncertainty is exhausted so patrol/guard/docking resumes. Predictions alone must not keep a colony in war mode.
3. **Information entitlement:** preserve current revealed raid alerts, local physical-LOS sightings and ping gates (recommended). Any stricter LOS-only intelligence or smoke-aware detection is a separate gameplay decision, not a performance optimization disguised as a cache change.
4. **Utility tradeoffs:** agree the priority among effective fire, incoming risk, cohesion, travel and search gain, and the permitted switching threshold. Weights, chunk size, update/age limits, lease durations and support caps require measured tuning; this ticket does not invent a percentage speedup or assert a universally best setting.
5. **Runtime hooks:** prefer native event cues plus bounded verification. If measurements require a gameplay movement/visibility hook, identify the missing 1.6 API, demonstrate why existing hooks are insufficient and record the narrow Harmony addition in the implementation plan before installing it.

## Research references

### Project and native API sources

Tracked source links above describe the `13f7dfc` research baseline. The listed `.tools/decomp/` ranges were checked against local RimWorld **1.6.4871**, not assumed from the wiki's newer API. The [offline-mirror-first modding guidance](https://rimworldwiki.com/wiki/Modding_Tutorials) and local `Modding Tutorials/Code MendingJob.md` explain job/driver/toil execution and reservation preflight; the decompiled 1.6 methods establish the concrete limitations recorded here.

Related implementation evidence: [test-runs](../test-runs.md), [current combat plan](../plans/0009-combat-packs.md), [existing search policy/performance tradeoffs](../plans/0004-area-aware-survey.md). Their completed boxes do not authorize marking this ticket implemented.

### Method references

- Dave Mark, [Modular Tactical Influence Maps, Game AI Pro 2, chapter 30](https://www.gameaipro.com/GameAIPro2/GameAIPro2_Chapter30_Modular_Tactical_Influence_Maps.pdf), sections 30.2–30.6: shared information rather than behavior instructions, separate proximity/threat layers, LOS/path-distance-aware propagation, reusable templates and bounded working maps. Its sample full-map refresh policy is **not** adopted as a localized-update implementation or as measured RimWorld tuning.
- Mike Lewis, [Choosing Effective Utility-Based Considerations, Game AI Pro 3, chapter 13](https://www.gameaipro.com/GameAIPro3/GameAIPro3_Chapter13_Choosing_Effective_Utility-Based_Considerations.pdf), sections 13.2, 13.3 and 13.5: normalized considerations, cheap early-outs, spatial influence inputs and the risks of oscillating decisions. Its game-specific scoring examples are guidance, not evidence of RimWorld behavior or performance.
- UC Berkeley CS 188, [Hidden Markov Models: the Forward Algorithm](https://inst.eecs.berkeley.edu/~cs188/textbook/hmms/hmm.html): separate prediction and observation updates with normalization. This supports the proposed belief-update structure, not a claim that game movement is known, independent, calibrated or Bayes-optimal. No external inference library is proposed.
