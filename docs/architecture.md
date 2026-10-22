# Architecture

Quadrunner is a RimWorld 1.6 mod (Core + Harmony only, `About/About.xml:7-19`) that adds stable-built robot dogs: **Quadrunners** haul, clean, deliver construction materials and carry riders; **Medirunners** carry the downed to beds; **Combatdogs** fight in four tiers. This document maps the runtime: which object owns which state, how a task or a charge decision flows end to end, and which parts are load-bearing in non-obvious ways. It is not a folder tour; for player-facing behaviour see [README.md](../README.md), for the design rationale [docs/design.md](design.md) and the plans, and for evidence [docs/test-runs.md](test-runs.md).

> **Superseded design:** `docs/design.md` is a snapshot of the living design doc. Its work-dispatch and queue sections are superseded by [plan 0003](plans/0003-network-dispatch.md); the file says so itself at `docs/design.md:6`, and [plan 0001](plans/0001-quadrunner.md) repeats it at line 6. Read those sections as history, not as the current dispatch specification.

## Overview

The shipped assembly is one C# namespace, `Quadrunner`, with one bootstrap: a `[StaticConstructorOnStartup]` class that logs the build id, applies every Harmony patch, and starts the test driver (`Source/Quadrunner/QR.cs:7-21`). Two other `[StaticConstructorOnStartup]` classes exist only to load materials/textures before first draw (`Building_Stable.cs:21-25`, `Robodog.cs:244-253`). There is no map component, game component or world component anywhere in `Source/Quadrunner/`; persistent state lives on ordinary saved objects (the stable building and the dog pawn's comp), and per-map coordination is a plain C# object rebuilt from those objects after loading (`Source/Quadrunner/Network.cs:56-96`).

The moving parts, at runtime:

| Component | Responsibility | Lives as | Saved? | Talks to |
| --- | --- | --- | --- | --- |
| `QR` | Bootstrap: Harmony `PatchAll`, build id, logging | static class, one per process | no | Harmony, `AutoTest` |
| `Building_Stable` | Docking, charging, repair, bills, patrol waypoints, optional network host | `ThingDef.thingClass` (`Defs/ThingDefs_Buildings/QR_Stable.xml:9`) | yes | `CompRobodog`, `DogNetwork`, bills |
| Robodog pawn (`CompRobodog`) | Battery, stable affiliation, gun choices, powered-down state, optional network host | `ThingComp` on every dog race (`Source/Quadrunner/Robodog.cs:30`) | yes | stable, `DogNetwork`, jobs |
| `DogNetwork` | One dispatcher per map: discovery intake, queues, assignment, reconciliation, war mode | static dictionary keyed by `Map`, never saved (`Network.cs:98`) | no (state hosted on a node, see below) | every dog and stable on its map |
| `ChargeSpots` | Cached conduit charge spots, rebuilt from buildings | static dictionary keyed by `Map` (`ChargeSpots.cs:40`) | no | `CompPowerTransmitter` buildings |
| Think-tree job givers | Charge, take-order, dock, wait-in-room | def-driven nodes (`Defs/ThinkTreeDefs/QR_ThinkTrees.xml:6-49`) | no | pawn job system, `DogNetwork` |
| Harmony patches | Six shipped fixes, one test-gated fix | attributes on static classes (`QR.cs:19`) | n/a | vanilla methods |
| `AutoTest` + `Scenario` | Test-only in-game driver and scenario runner | `MonoBehaviour`, never saved (`AutoTest.cs:20-24`) | no | `tools/autotest.py` via files |

## Code map by concern

All paths are under `Source/Quadrunner/`. Files are partials only for the scenario suite (`Scenarios*.cs`); production code is one type per concern.

| Concern | Files | What it owns |
| --- | --- | --- |
| Bootstrap, defs, settings, UI | `QR.cs`, `QR_DefOf.cs`, `Settings.cs`, `DebugActions.cs`, `ITab_StableOverview.cs` | Harmony startup, the `[DefOf]` cache of mod defs that code references directly (`QR_DefOf.cs:6-38`), the one mod setting (turbine distance), dev-mode debug actions, the stable's Overview tab |
| Stable and fabrication | `Building_Stable.cs`, `Assembly.cs`, `Tiers.cs` | Docking container and capacity, charge/repair tick, bills, assemble/rebuild/upgrade recipe workers, per-dog upgrade bills, built-in gun selection |
| Dog pawn | `Robodog.cs`, `Docking.cs` | `CompRobodog` battery/affiliation/guns/siren, powered-down hediff, dock/undock drivers, idle return, wait-in-room fallback |
| Dispatch | `Network.cs`, `Work.cs`, `Survey.cs` | `DogNetwork`, `NetworkState`/`NetworkTask`, task lifecycle, local discovery scanner, coordinator-owned job construction, idle surveying |
| Charging | `Charging.cs`, `ChargeSpots.cs` | Stable-vs-conduit decision, conduit charge driver, spot cache, spot eligibility |
| Rescue, retrieval, field fixes | `Rescue.cs`, `Retrieval.cs`, `FieldFix.cs` | Patient ranking and vanilla rescue jobs; carrying powered-down dogs home; colonist-delivered batteries/repair kits |
| Riding | `Riding.cs` | Ride order and float menu, rider and dog job drivers, ridden-speed stat part, rider draw patch |
| Combat and drafting | `Combat.cs`, `Tiers.cs`, `Drafting.cs` | War-queue job construction, capture/carry-to-room, patrol/guard, tier guns, draft controller patches |
| Rendering | `Rendering.cs` | Per-part gait, whole-body pose animation, mount layer, carried-patient body angle |
| Clean removal | `Removal.cs` | Confirmed export to a save with no Quadrunner content or references |
| Tests | `AutoTest.cs`, `Scenario.cs`, `Scenarios*.cs` | File-protocol driver, scenario DSL, the 55-scenario suite |

## What actually ticks

The dispatcher is driven cooperatively from ticks the game already runs, not from a `MapComponent` or a `GameComponent`:

- `Building_Stable.Tick` calls `DogNetwork.For(Map).Tick()` every tick it ticks (`Building_Stable.cs:226-232`); the building is `tickerType Normal` (`QR_Stable.xml:10`).
- Every spawned dog's `CompRobodog.CompTick` calls `DogNetwork.For(parent.Map).Tick()` as well (`Robodog.cs:158-165`).
- `DogNetwork.Tick` itself rate-limits to `dispatchInterval` (60 ticks) unless called with `force` (`Network.cs:301-308`; tuning at `Defs/ThingDefs_Races/QR_Races.xml:110`). Re-entry is guarded by `ticking` (`Network.cs:303-306, 367`).
- Discovery scans are a slower inner cadence: `scanInterval` 120 ticks (`Network.cs:332-334`; tuning `QR_Races.xml:109`).
- Removing a node (dog despawn/destroy/kill, stable despawn) calls `NodeLeaving`, which re-rosters and re-elects unless a tick is in progress (`Network.cs:242-248`; callers at `Robodog.cs:146-156, 189-192`, `Building_Stable.cs:278`).

Consequences worth knowing: the dispatcher's cadence follows whichever node ticks first, and a map with no stables and no spawned dogs simply has no dispatcher ticking. Docked dogs are despawned (see [Dispatch](#dispatch-the-dognetwork)), so their `CompRobodog.CompTick` returns immediately (`Robodog.cs:158-164`); the stable's tick is what keeps the network alive and re-deploys them.

## Dispatch: the `DogNetwork`

`DogNetwork` is the only thing that matches discovered work to dogs. Dogs never search the map's global work list for themselves; they report local observations and later ask for orders (`Work.cs:50-55`, `Network.cs:538-539`).

### Ownership and election

There is exactly one `DogNetwork` per `Map`, created lazily and pruned when maps are removed (`Network.cs:138-150`). The network object holds only transient caches (roster, assignments, reports, survey dictionary); the persistent `NetworkState` — normal queue, war queue, survey history, `nextId`, `warActive` — is stored on exactly one physical node:

- `NetworkState` is `Scribe_Deep` on the stable (`Building_Stable.cs:293`) and on `CompRobodog` (`Robodog.cs:224`). Only the elected coordinator holds it; election clears it everywhere else and stores it on the new owner (`Network.cs:226-239`).
- Election prefers the spawned player stable with the **lowest `thingIDNumber`**; with no stable, the **newest active** player dog by `BuildOrder` (`Network.cs:207-215`). `BuildOrder` is a saved integer that falls back to the pawn's thing ID when zero (`Robodog.cs:34-35`) and is copied forward on tier upgrades (`Building_Stable.cs:148`), so a dog's seniority survives save/load and rebuilds.
- "Active" means spawned, alive, not downed, player faction, and not powered down (`Network.cs:153-154`).
- First access after a load recovers the state from the elected node if it has one, otherwise scans all stables then all dogs, and adopts the survey history (`Network.cs:216-225`). The `State` getter re-rosters and re-elects when the coordinator is dead or unspawned, which also covers a wiped node leaving a cleared-but-non-null state (`Network.cs:116-129`).
- Losing the last eligible node permanently clears both queues and survey history (`Network.cs:231-237`); a later node never resurrects the old backlog.

The roster is rebuilt from the map: all spawned player stables, and all player robodogs on the map, including ones held in a stable's dock, because vanilla's `MapPawns.AllPawns` includes pawns inside map things (`Network.cs:185-198`; vanilla `MapPawns.AllPawnsUnspawned` walks `ThingOwnerUtility.GetAllThingsRecursively`, `.tools/decomp/Verse/MapPawns.cs:117-146`). Rostered is not the same as active: docked dogs fail `Active` until the network undocks them (`Network.cs:325-331`).

### Two queues and the task lifecycle

`NetworkState` has a **normal** queue (retrieve, frame, blueprint, haul, clean) and a **war** queue (rescue, combat); `Report` chooses by kind (`Network.cs:250-256`). A `NetworkTask` is an intention, not a vanilla job: it stores `kind`, the target's **ThingID** (a cached live `Thing` exists but is not saved), the work giver, the assignee's ThingID, a phase, and progress bookkeeping (`Network.cs:14-53`). The target is re-resolved from its ID on each use, so stale references cannot poison a save (`Network.cs:27-33`).

Phases are `Queued → Assigned → Acknowledged → Working`, with `Reconcile` promoting Assigned to Acknowledged when the dog's current job matches the task's `jobId`, and to Working on subsequent ticks (`Network.cs:438-455`). Job identity is vanilla's `Job.loadID`, which vanilla serializes (`Network.cs:21, 445-446, 528`; `.tools/decomp/Verse.AI/Job.cs:496-506`), so an assignment can be matched across a save/load without saving the job itself.

Deduplication is per queue and by `(kind, targetId)` (`Network.cs:254-256`). Completion is kind-specific and revalidated centrally: haul = target is in valid best storage, frame/blueprint = no material still needed, retrieve = the dog is no longer powered down, rescue = the patient is no longer a patient, combat = target dead/imprisoned/no longer hostile, clean = the filth is gone or its cell left the Home area (`Network.cs:416-436`).

### Discovery, assignment, reconciliation

**Discovery** is local and fog-limited. Every active dog runs `DogWork.ReportLocal` on each scan: it enumerates cells within `scanRadius` (15) and, for each unfogged, non-forbidden thing, asks whether the thing is *known* work — a player frame or blueprint that still needs materials, Home-area filth at least 600 ticks old, or an item on vanilla's "potentially needs hauling" list — and reports it through the giver-derived kind (`Work.cs:63-127`). It also reports other player dogs that are powered down (`Work.cs:80-85`). The haul fallback excludes corpses explicitly (`Work.cs:124-125`). Eligibility for a *particular* dog (forbidden, reservations, reachability) is deliberately deferred to assignment, so one dog's inability never hides work from another (`Work.cs:57-62`). Rescue patients and revealed raid targets are the map-wide exceptions, reported by the network directly (`Network.cs:341-348`). Reports are refused for a target outside this map, destroyed, or on a fogged cell (`Network.cs:252`).

**Assignment** considers each unassigned, retry-ready task and each spawned, available, capable dog without an assignment (`Network.cs:495-507`). `IsAvailable` rejects dead, downed, powered-down, charge-seeking, drafted, mentally broken and player-forced dogs; for a despawed dog it requires the dog to be docked in a spawned stable, not being upgraded, and injury-free; for a spawned dog it also rejects one that should go home for repair (`Network.cs:157-183`). `Capable` maps task kind to the race's `RobodogWork` flags (`Network.cs:481-486`). Route cost is a real pathfind with `PathEndMode` from the task kind, and ties go to the lower thing ID (`Network.cs:509-516`). The selected job is built **for that one target** by `DogWork.JobFor` (or `Rescue`/`Combat`), which asks the task's own vanilla work giver for `JobOnThing`, rejects job defs outside the race's allow-list, disables opportunistic duplicate hauling, and trims vanilla's extra queued targets to network-discovered, unassigned ones (`Work.cs:143-186`).

**Reconciliation** runs every network tick over both queues. An assignment whose dog died, despawned, stopped running the job, or was drafted is released back to `Queued`; an assigned dog that makes no progress for `progressTimeout` (2500 ticks) is stopped safely and given a retry delay (`Network.cs:438-470`; tuning `QR_Races.xml:111`). "Progress" is position, current toil index, or ticks left in the toil changing (`Network.cs:450-455`).

### Flow: one haul end to end

1. An idle Quadrunner is surveying (`Survey.JobFor`, `Survey.cs:19-60`). On the dispatch tick, the network scans it and `DogWork.ReportLocal` sees a steel stack in range, unfogged and unforbidden, on vanilla's haulables list (`Work.cs:71-94`).
2. `DogNetwork.Report` refuses duplicates and appends a `Haul` task with the stack's ThingID to the normal queue (`Network.cs:250-262`).
3. `AssignAll` sorts the war queue (rescue rank, then bleeding rate, then kind, then id; `Network.cs:370-385, 350-358`) and the normal queue by kind and id, finds the nearest available worker by path cost, stores the assignment, and calls `StartJob` with the haul job the vanilla scanner built for that stack (`Network.cs:495-536`).
4. The job runs on vanilla rails: storage filters, reservations, stack counts and hauling toils are all vanilla. Only the target choice was centralized (`Work.cs:137-167`).
5. The next network tick `Reconcile` observes the dog's job/position progress and keeps the task; when the stack is in valid best storage, `Finished` removes the task (`Network.cs:416-436`).
6. The dog's think tree asks `JobGiver_RobodogWork` again (`QR_ThinkTrees.xml:32-33`), which calls `DogNetwork.TakeOrder`; with no assignment it gets a new survey (`Network.cs:539-601`). With no survey reachable it waits; only later think-tree nodes dock it (`QR_ThinkTrees.xml:34-37`, `Docking.cs:14-33`).

### War mode

War is on when vanilla reports any hostile active threat to the player, cached for 60 ticks, or when a queued combat target is a live hostile (`Network.cs:310-315`, `Work.cs:214-232`). On the normal→war edge the network:

- resets every normal task to `Queued` and stops assigned workers whose job matches, then stops any worker running a survey, haul-to-cell/container, clean or carry-home job, dropping carried cargo in place (`Network.cs:393-414`);
- sets `warActive` and keeps the normal queue — it is revalidated when the threat clears (`Network.cs:316-317, 438-470`);
- deploys available Combatdogs, and Medirunners when any patient exists; idle workers get a non-forced `Wait` with a 60-tick expiry instead of wandering (`Network.cs:319-330, 573-583`);
- continues reporting rescue patients map-wide and revealed raid targets, even outside every worker's scan radius (`Network.cs:341-348`).

Normal assignment is skipped while war is on; war tasks still assign (`Network.cs:350-358`). A dog hit by a hostile pings the network directly — report the attacker, drop the raid cache, force a tick — so a stable-less Combatdog still reacts (`Robodog.cs:180-187`, `Network.cs:283-289`).

### A dog's own orders

`JobGiver_RobodogWork` is the bridge from RimWorld's think tree to the network: it returns `DogNetwork.TakeOrder(pawn)` (`Work.cs:205-211`). `TakeOrder` returns the current assigned job while the assignment is valid, rebuilds the next combat job for a combat assignment (adopting a vanilla continuation instead of restarting), refuses to interrupt player-forced or otherwise unavailable jobs, and otherwise returns an idle job — war standby or survey — or null so the later dock/wait nodes run (`Network.cs:539-601`). This is why the think tree never needs a "find work" branch of its own.

## The robodog pawn

### The race

All five dog races (six kinds) derive from an abstract `QR_RobodogBase` (`Defs/ThingDefs_Races/QR_Races.xml:6-92`). The important choices:

- Non-organic `QR_Robotic` flesh type with mechanoid wound visuals and no bleeding, disease or rot (`QR_Races.xml:40`, `Defs/Misc/QR_FleshTypes.xml:7-17`). It is deliberately neither vanilla animal nor mechanoid, so taming, training, hunting, wild spawns, manhunter packs, food and rest are out (`QR_Races.xml:4-5, 38-69`).
- Intelligence `Animal` for utility/rescue dogs; `ToolUser` for the Combatdog base so later tiers can hold built-in guns (`QR_Races.xml:39, 195-198`).
- A custom think tree (`QR_Robodog`) and a custom render tree (`QR_RobodogRenderTree`) (`QR_Races.xml:42-43`).
- `CompRobodog` on every dog race (`QR_Races.xml:76-91`).

`PawnKindDef`s carry graphics, `combatPower`, and the visual scale: Quadrunner 2.0, Medirunner 1.4, Combatdogs 1.5 (`QR_Races.xml:307-427`).

### `CompRobodog`

The comp is the dog's whole persistent identity apart from its pawn record (`Robodog.cs:30-237`):

- Battery as a 0–1 fraction (`battery`, saved at `Robodog.cs:226`), charged by stable or conduit and drained per cell actually moved (`Robodog.cs:158-178`). The drain is `drainPerCell × distance × DrainFactor`, with `DrainFactor` doubling for a working dog during a raid but not while ridden (`Robodog.cs:59-68`; tunings `QR_Races.xml:81, 121`).
- `wantsCharge` latches at `seekChargeAt` (10%) and clears only at 100% (`Robodog.cs:81-106`). At 0% it adds the `QR_PoweredDown` hediff, whose zeroed Moving capacity makes the pawn downed, and tells the network it is leaving; any charge above 0 removes it (`Robodog.cs:107-118`; hediff `Defs/HediffDefs/QR_Hediffs.xml:5-23`). Crossing the threshold mid-fight undrafts a drafted Combatdog or interrupts a combat job so the dog retreats to charge (`Robodog.cs:87-103`).
- `stable` is a saved reference and is the *only* affiliation record: the stable finds its dogs by scanning pawns whose comp points at it, not by keeping a list (`Building_Stable.cs:43-44`, `Robodog.cs:223`).
- Saved gun picks per weapon slot (`gun1`, `gun2`), read through `Tiers.Gun` (`Robodog.cs:229-230`, `Tiers.cs:35-42`).
- `networkState`, `builtOrder`, `waypointsReached` (patrol cursor) (`Robodog.cs:224-228`).
- `carried`/`carriedSince` are deliberately **not** saved: a pawn loaded onto a dog's back is treated as having been there all along, which keeps the scoop animation from replaying after a load (`Robodog.cs:43-46, 127`).

### The think tree: vanilla first, orders second

`QR_Robodog` is a priority list, and the order is the design (`QR_ThinkTrees.xml:6-49`):

1. Despawned / Downed subtrees.
2. `ThinkNode_QueuedJob` — player orders always win.
3. A player-faction drafted branch (`MoveToStandable`, `Orders`) so drafted Combatdogs hold position between orders, as vanilla's mechanoid tree does.
4. `LordDuty` (vanilla lord jobs).
5. `JobGiver_SeekCharge` — charge before any work.
6. `JobGiver_RobodogWork` — the network's order, else survey or war standby.
7. `JobGiver_Dock` — go home and dock; a dog with no stable adopts the nearest reachable one with a free slot (`Docking.cs:14-33`).
8. `JobGiver_WaitInRoom` — no stable at all: walk to and wait in the nearest room that is not a bedroom, barracks or prison (`Docking.cs:56-101`).
9. Wander, then `JobGiver_IdleError`.

### The actual work is vanilla

Quadrunner work is not reimplemented. The race's `RobodogWork.workGivers` names `DeliverResourcesToFrames`, `DeliverResourcesToBlueprints`, `HaulGeneral`, `CleanFilth` (`QR_Races.xml:102-107`); the jobs it may accept are `HaulToCell`, `HaulToContainer`, `Clean` (`QR_Races.xml:115-119`). Discovery uses the same work givers' scanners only to decide *whether* a thing is work (`Work.cs:87-127`), while the job itself is built by the giver's `JobOnThing` (`Work.cs:152-166`). Medirunners and Combatdogs carry a `RobodogWork` with only a flag (`rescue` or `combat`), but they still report utility work they pass, because the scan always uses the Quadrunner race's configuration (`Work.cs:43-47, 63-68`); only workers are `Capable` of taking it (`Network.cs:481-486`).

The mod adds eight job defs with its own drivers: survey, dock, conduit charge, carry-to-stable, field fix, ride-to, being-ridden, carry-to-room (`Defs/JobDefs/QR_Jobs.xml:4-57`). Everything else is `JobDefOf.*`.

### Rescue, riding, combat in one paragraph each

**Rescue.** `Rescue.Patients` is map-wide (no enemy-near check, unlike vanilla) and includes downed colonists, guests, prisoners and colony animals that want rescue and have medical care enabled (`Rescue.cs:12-34`). Rank is colonist → guest → prisoner → colony animal, with enemies, robodogs and wild animals excluded; within a rank the fastest-bleeding wins (`Rescue.cs:19-29`, `Network.cs:370-385`). The job is vanilla `Rescue` or `TakeWoundedPrisonerToBed` to a bed vanilla finds; the patient's reservation keeps it to one Medirunner (`Rescue.cs:47-61`). The red tail siren is drawn while the dog runs either job (`Robodog.cs:205-218`).

**Riding.** The rider's `JobDriver_RideTo` drives the whole ride: walk to the dog, "mount", walk to the destination at `riddenMoveSpeed` 7 (a stat part *replaces* MoveSpeed while mounted, `Riding.cs:229-241`), then dismount (`Riding.cs:122-194`). `Notify_Starting` starts `JobDriver_BeingRidden` on the dog, whose only job is to mirror the rider's cell and rotation each tick until the rider's job ends (`Riding.cs:138-142, 197-226`). The ride ends early on drafting, when the next step enters an indoor room or a door into one, when the dog powers down, or when the rider's job ends for any reason (`Riding.cs:100-115, 146-175`). The rider is drawn on the dog by a `DrawPos` postfix; the dog is drawn under the rider's tween (`Riding.cs:281-300`). The float menu offers "Ride … to…" on a rideable dog or on a stable, which picks its fullest available rideable (`Riding.cs:243-278`, `Building_Stable.cs:51-53`).

**Combat and tiers.** Combatdogs spot standing hostiles within `engageRange` 15 with line of sight and report them to the war queue (`Combat.cs:28-39`; tuning `QR_Races.xml:207`). A standing target gets vanilla `AttackStatic` if the dog can hit from where it stands, else a walk to a cast position vanilla finds, else `AttackMelee` (`Combat.cs:85-110`). A downed humanlike with a guest comp is taken to a prisoner bed with vanilla `Capture`; otherwise it is carried to the nearest qualifying empty room by `QR_CarryToRoom` and left there, not imprisoned (`Combat.cs:112-143, 210-237`). Idle during war: walk the stable's looping patrol waypoints, else guard within 4 cells of the stable (or of the coordinator when stable-less) (`Combat.cs:64-78, 151-195`; waypoints `Building_Stable.cs:160-210`). Upgrades are per-dog bills ordered from the Overview tab, doable only while that dog is docked, and completed by rebuilding the pawn as the next `PawnKindDef` while preserving name, battery, gun picks and `BuildOrder` (`Tiers.cs:101-153`, `Building_Stable.cs:113-158`, `ITab_StableOverview.cs:93-131`). `GunFor` chooses slot 2 when the target is within that gun's range, else slot 1, and `Arm` swaps the actual equipment (built-in guns destroy on drop) (`Tiers.cs:53-74`; tiers `QR_Races.xml:223-305`).

## Charging: stable versus conduit

Charging has its own decision, ahead of all work in the think tree (`QR_ThinkTrees.xml:30-31`). `JobGiver_SeekCharge` compares **time to full**, not distance: straight-line travel plus `missing charge / rate` for the stable and for the best conduit spot, and picks the smaller (`Charging.cs:22-50`). A reachable powered stable is fast (`stableChargePerTick` 0.0012); an unpowered stable is treated as a fallback only used when no spot exists (`Charging.cs:31-33`). A conduit spot is slow (`conduitChargePerTick` 0.0004, so three times slower; `QR_Races.xml:83-84`) but may still win when the stable is far.

A conduit spot is a free standable cell in a roofed, non-doorway, non-map-edge indoor room served by a colony building with `CompPowerTransmitter` (the conduit's own room, or the rooms beside a conduit built into a wall), and not within `turbineDistance` (default 6, a mod setting) of any wind turbine or its clear area (`ChargeSpots.cs:69-110, 121-136`, `Settings.cs:5-16`). The cache is static and never saved; it rebuilds when the colony's building signature changes or after 2500 ticks (`ChargeSpots.cs:27-67`). Volatile rules — prison status, occupied beds, stored energy, allowed area, fire, hostiles, reservations — are rechecked per dog on every use (`ChargeSpots.cs:139-155`). `Best` breadth-first traverses the regions the dog may enter, gathers the first five usable spots, and ranks them by a weighted score — distance, plus 15 per dog already charging in that room, minus stored energy — then picks the lowest (`ChargeSpots.cs:157-195`).

The driver itself lies down and, every tick, takes `conduitChargePerTick` worth of energy from the **power net's battery comps, fullest first**, and stops when full, when the batteries run dry, or when the spot stops being usable (rechecked every 250 ticks) (`Charging.cs:57-130`). This is the main surprise of the charging system: a dog on a conduit is powered by the colony's stored energy, not by live generation, so a net with no batteries cannot charge it even if it is producing power.

Docked dogs do not tick; the stable charges them at `stableChargePerTick` and heals one injury per tick by `repairPerTick` while it has power (`Building_Stable.cs:239-248`, `StableProps` at `Building_Stable.cs:10-15`). At 0% the dog powers down, lies like a downed pawn, and can be rescued by a worker (fire next to it is beaten out first), carried by a colonist from the float menu, or fixed in place by a colonist bringing a battery or repair kit (`Retrieval.cs:15-69, 77-139`; `FieldFix.cs:22-117`). A powered-down dog whose stable cannot reach it raises `Alert_RobodogUnreachable` (`Retrieval.cs:168-189`), and raiders may kidnap it through a patch (see below).

## XML tunables (DefModExtensions)

Gameplay numbers live in XML, attached as `DefModExtension`s. The C# class declares the fields; the def supplies the values.

| Extension | Declared in | Attached at | Controls |
| --- | --- | --- | --- |
| `StableProps` | `Building_Stable.cs:10-15` | `QR_Stable.xml:79-84` | Capacity 15, injury repair per tick |
| `CompProperties_Robodog` | `Robodog.cs:10-23` | `QR_Races.xml:80-91` (base race) | Drain per cell, 10% seek threshold, conduit and stable charge rates, 100 Wd per full charge, field-fix wait and repair threshold |
| `RobodogWork` | `Work.cs:11-48` | `QR_Races.xml:101-122` (Quadrunner), `141-145` (Medirunner), `202-208` (Combatdog base) | Work giver and job allow-lists, raid speed/drain, deployed speed, engage range, scan/dispatch/progress timings, survey spacing/job length/search radius, rescue/combat flags |
| `CombatTier` | `Tiers.cs:12-25` | `QR_Races.xml:223-229, 244-253, 268-281, 295-303` | Tier number, next kind, upgrade recipe, gun choices per slot |
| `Rideable` | `Riding.cs:13-24` | `QR_Races.xml:125-131` | Ridden speed and the rider's per-facing draw offsets |
| `TailSiren` | `Robodog.cs:270-278` | `QR_Races.xml:146-151` | Medirunner tail-siren offset per facing and size |
| `RobodogFixProps` | `FieldFix.cs:9-16` | `Defs/ThingDefs_Items/QR_Items.xml:31-33, 54-56` | Whether a field item charges or repairs |
| `AssembleProps` | `Assembly.cs:9-13` | assemble/rebuild recipes in `Defs/RecipeDefs/QR_Recipes.xml` | Which `PawnKindDef` a recipe produces |
| Gait nodes | `Rendering.cs:67-86` | `QR_RenderTrees.xml:38-121` | Texture part, stride length, cycle phase, swing, lift, hip, stretch, crouch per layer |
| Pose root | `Rendering.cs:147-190` | `QR_RenderTrees.xml:11-31` | Pounce, recoil, brace, scoop, patient offsets |

Two kinds of numbers deliberately are **not** in XML: the mod setting `turbineDistance`, saved in `ModSettings` rather than a save (`Settings.cs:5-16, 42-46`), and a handful of internal cadences and radii kept as C# constants — conduit spot cache age/search radius/candidate count (`ChargeSpots.cs:29-31`), the 250-tick charge recheck (`Charging.cs:59`), the 60-tick combat job expiry and 4-cell guard radius (`Combat.cs:24-26`), and the wait-room limits (`Docking.cs:58-59`). If you are tuning feel rather than balance, check both places.

## Harmony patches and XML stat parts

Harmony is applied once from the static bootstrap (`QR.cs:15-21`). Six patches ship; the design budgeted "about six" (`docs/design.md:218`, list at `216-227`), and each exists because vanilla has no virtual or hook for the job.

| # | Patch | Where | Why it exists |
| --- | --- | --- | --- |
| 1 | `Pawn_DrawTracker.DrawPos` getter, postfix | `Riding.cs:281-300` | Vanilla has no mounted-pawn concept: puts the rider on the dog's back at a fixed altitude and pulls the dog under the rider's tween |
| 2 | `KidnapAIUtility.TryFindGoodKidnapVictim`, postfix | `Retrieval.cs:195-211` | Vanilla kidnappers only look for humanlikes; lets a raider carry off a powered-down robodog, but only after vanilla found no victim |
| 3 | `GenConstruct.CanConstruct`, prefix | `Work.cs:273-284` | Vanilla construction checks touch `pawn.workSettings` (null on a dog) and skills; a robodog only delivers materials, so it runs the check without skills |
| 4 | `PawnComponentsUtility.AddAndRemoveDynamicComponents`, postfix | `Drafting.cs:47-55` | Vanilla creates a `Pawn_DraftController` only for humanlikes, colony mechanoids and mutants; player Combatdogs need one to be drafted |
| 5 | `Pawn.IsColonySubhumanPlayerControlled` getter, postfix | `Drafting.cs:62-70` | Vanilla gates move/attack orders and drafted gizmos on this check; a drafted Combatdog must pass it (only while drafted, so it stays out of player-controlled lists otherwise) |
| 6 | `PawnRenderer.BodyAngle`, postfix | `Rendering.cs:290-298` | A carried pawn's angle is fixed for a humanlike carrier; this lies a patient along the dog's back and flips it during the scoop |

There is a seventh patch class, `Root_Play.SetupForQuickTestPlay` (`AutoTest.cs:213-233`). It is **always applied** by `PatchAll`, but returns immediately unless `AutoTest.Enabled` (i.e. the game was launched with `-quadrunner-autotest`); it chooses the fixture tile and `-quadrunner-mapsize`. It cannot affect a normal game.

Two more hooks are XML `StatPart`s on vanilla `MoveSpeed`, not Harmony: `Patches/QR_Stats.xml:5-18` appends `StatPart_RobodogRaid` (`Work.cs:239-267`) and then `StatPart_Riding` (`Riding.cs:229-241`). Order matters: the raid part multiplies deployed/raid speed factors, and the riding part comes last and *replaces* the value, so a rider's speed is exactly `riddenMoveSpeed` rather than a multiplier on the colonist's.

## Rendering

Each dog is drawn as separate layers: a body and four legs, so the legs can move under a still body. The render tree is Core's animal tree with the carried-thing worker replaced (`Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:3-9`):

- A hidden `Whole` node keeps the full sprite as the pawn's body graphic for icon and card rendering; the visible layers load `<body path>_<part>` (`QR_RenderTrees.xml:32-38`, `Rendering.cs:51-65`). This is why each dog kind has `Body`, `FrontNear`, `FrontFar`, `HindNear`, `HindFar` PNGs per facing.
- The gait worker drives every leg from the distance drawn *along the facing*, not from a timer: `t = ((pos · facing) / stride + phase) · 2π`, so the walk cycle follows speed, stays smooth between ticks and stops when the dog stops (`Rendering.cs:88-145`; stride 1.4 and diagonal phase pairs at `QR_RenderTrees.xml:38-121`). In profile each leg swings about a hip point in sprite pixels; head-on each leg lifts. Hind legs are only visible east/west (`QR_RenderTrees.xml:65-71, 94-100`).
- The root pose worker moves and tilts the whole dog for action: a pounce on each melee strike, a braced crouch plus a recoil kick per shot, and a head dip as it scoops a pawn (`Rendering.cs:147-283`; values `QR_RenderTrees.xml:11-31`). Poses are recomputed from vanilla stance state and the pickup tick, not stored.
- The mount layer extends vanilla's carried-thing worker: a carried pawn is drawn on the back over the dog, otherwise the built-in gun is drawn aimed (Core still supplies the gun's material, facing and recoil), with mount offsets and layer in XML (`Rendering.cs:9-49`, `QR_RenderTrees.xml:122-131`).
- West textures do not exist; `Graphic_Multi` mirrors east (`Textures/Quadrunner/Pawns/`; `.tools/decomp/Verse/Graphic_Multi.cs:128-139`), and the SVG exporter writes `X_<Part>_<facing>.png` with the facing last so those loads work (`tools/export-textures.sh:1-6`).
- The sirens are drawn in `PostDraw` at `MetaOverlays` altitude with a real-time 2 Hz flash, so they animate while paused: yellow for a Combatdog retreating to charge, red on a Medirunner's tail while rescuing (`Robodog.cs:78-79, 205-218, 239-264`).

## Saved state and clean removal

The mod is removable because it saves **no map-scoped state of its own**. Everything persistent hangs off objects that removal deletes or cleans:

| Data | Owner | Serialized at | Removal behaviour |
| --- | --- | --- | --- |
| Docked dogs, stable waypoints | `Building_Stable` | `Building_Stable.cs:289-299` | Stable is destroyed; docked dogs are dropped first (`Building_Stable.cs:263-280`) |
| `NetworkState` (queues, survey history) | elected stable or dog | `Building_Stable.cs:293`, `Robodog.cs:224` | Both node types are removed; nothing remains |
| Battery, affiliation, gun picks, patrol cursor | `CompRobodog` | `Robodog.cs:220-231` | Dog pawns are destroyed |
| Upgrade bill and its dog | stable `BillStack` | `Tiers.cs:128-132` | Bill lives on the removed stable |
| Rider/dog job progress | vanilla jobs | `Riding.cs:188-193`, `Retrieval.cs:134-138`, `Charging.cs:125-129` | Jobs stop; vanilla serializes them |
| Turbine distance | ModSettings | `Settings.cs:11-15` | A setting, not save state |
| Network, charge-spot and raid caches | static dictionaries | none | Rebuilt from the map; `ClearAll` on removal (`Network.cs:150`, `ChargeSpots.cs:42`, `Removal.cs:92-93`) |

`NetworkTask` stores ThingIDs rather than references specifically so a destroyed or missing target does not poison a save; lookups re-resolve and treat a missing target as finished (`Network.cs:27-33, 416-436`). First access after loading adopts whichever node holds the state and rebuilds the transient assignments from the saved queue (`Network.cs:216-225, 472-479`).

Removal is an explicit, destructive cutover, never something that happens on an ordinary save (`Removal.cs:13-39`). `Removal.PrepareSave` pauses, writes `QR_before_removal_<timestamp>` and verifies the file exists, stops every job that touches mod content, strips mod hediffs and nulls mod def references from surviving wounds, releases vanilla cargo and carried pawns (into a caravan when the dog is off-map), destroys every mod pawn/thing (including wrecks, minified stables, blueprints, frames and in-flight mod projectiles), clears the static caches, writes the cleaned save, and then scrubs the XML export: removes mod defNames from `allowedDefs` lists, nulls removed weapon/projectile/cover metadata in battle-log entries while keeping the records, and throws if any `Quadrunner.*` class attribute or leaf value naming a mod def still remains (`Removal.cs:41-201`). The check is deliberately paranoid: on failure it tells the player to keep the mod enabled and reload the backup.

That contract is verified independently: `tools/autotest.py removal-check` loads the prepared save in a fresh process with no Quadrunner assembly, ticks 600, saves/reloads, ticks another 600, and rejects any new error or any loaded `Quadrunner` assembly (`tools/autotest.py:599-663`; `tools/removal-probe/Probe.cs:93-116`). The observer mod carries no Quadrunner reference.

## Test harness

The test suite is an executable specification: every behaviour above has a scenario, and the harness runs scenarios inside a real, isolated game.

### The file protocol

`AutoTest` is a `MonoBehaviour` created at startup by the `QR` static constructor, so it survives save/load and never enters a save (`QR.cs:20`, `AutoTest.cs:20-24, 48-59`). It only speaks the file protocol when the game was launched with `-quadrunner-autotest=<dir>`; without it, the component still exists but only drives `Scenario.Update()`.

- Writes `<dir>/heartbeat` every real-time second and appends JSON lines to `<dir>/events.jsonl` (`AutoTest.cs:61-75, 153-164`). Events include `launched`, `ready` (with build, game version, map size and actual mod root), `scenario`, `result`, `restarting`, `quitting`, `closed_window` and `unknown` (`AutoTest.cs:81-84, 97-141`; verdict emission `Scenario.cs:275`).
- Polls `<dir>/command` twice a second and executes one command at a time: `run [filter]`, `restart`, `quit`, `hang`, `ping` (`AutoTest.cs:104-144`). `restart` calls `GenCommandLine.Restart()` — the same in-game relaunch the Options dialog uses — so a new assembly can be loaded without the harness killing the process (`AutoTest.cs:122-127`).
- Closes any `forcePause` window a test run would otherwise stall on, and drops the frame rate to 5 fps between scenarios to save software-rendering CPU (`AutoTest.cs:66-69, 93-102`).

`tools/autotest.py` is the host side: preflight checks (game version, Steam, Harmony), private profile under `.devlog/rwdata`, private Xvfb display, build via `tools/build.sh`, in-game restart when the loaded build is stale, command dispatch, watchdog on the heartbeat, error diff against a baseline of a run without Quadrunner, and a machine-readable run record (`tools/autotest.py:326-340, 343-356, 521-554, 681-705`). Exit codes: 0 pass, 1 fail, 2 hang/crash, 3 not run (`tools/autotest.py:83`). `pkg-run` cold-launches the actual `dist/` package instead of the dev repo, and `removal-check` is the independent observer path (`tools/autotest.py:561-598, 599-663`). The harness itself is self-tested against a fake game (`tools/selftest.sh`; 19 cases recorded in `TODO.md:41`).

### Scenarios

`Scenario` is a small DSL: `Do`, `Expect`, `Wait`, `SaveAndReload`, with tracked things referenced by ThingID because object references are stale after a load (`Scenario.cs:9-104`). Each scenario disables the storyteller, runs at Ultrafast, refreshes colonists, keeps drafted colonists drafted, and finishes PASS or FAIL with tick/time/fps telemetry, destroying its tracked things and running cleanup (`Scenario.cs:158-281`). `RunAll` queues scenarios and starts each when the previous ends (`Scenario.cs:113-156`); a filter matches case-insensitive comma-separated parts of scenario names (`Scenarios.cs:79-84`).

`Scenarios.All` is the specification list: **55 scenarios**, in Run-all order (`Scenarios.cs:13-72`). A live Run all from the debug menu passes `testSaveLoad: false` and therefore omits the dedicated "Save and reload" scenario, running 54; the harness always passes `true`, so every persistence step runs (`Scenarios.cs:82-83`, `DebugActions.cs:14-15`, `AutoTest.cs:118-120`). The suite is split by area into partials: core/stable (`Scenarios.cs`), dispatch (`Scenarios.Dispatch.cs`), charging (`Scenarios.Charging.cs`), the ten network cases (`Scenarios.Network.cs`), retrieval (`Scenarios.Retrieval.cs`), rescue (`Scenarios.Rescue.cs`), riding (`Scenarios.Riding.cs`), combat (`Scenarios.Combat.cs`), tiers/balance (`Scenarios.Tiers.cs`, `Scenarios.Balance.cs`), and visual capture sets (`Scenarios.Art.cs`, `Scenarios.Gait.cs`, `Scenarios.Animation.cs`, `Scenarios.Demo.cs`, `Scenarios.Removal.cs`). The suite is the place to look for what a behaviour is expected to do; `docs/test-runs.md` is the evidence log (the newest recorded package sequence is 54/55 with the single failing fixture corrected and re-proven individually — `docs/test-runs.md:14-17`).

## Surprises a newcomer will trip on

- **Docked does not mean spawned.** Docking despawns the pawn into the stable's `ThingOwner<Pawn>` (`Building_Stable.cs:56-80`). Docked dogs do not tick and their think tree does not run; the stable still counts them by scanning the map's pawns (`Building_Stable.cs:43-44`), and the network must explicitly undock one before it can work (`Network.cs:325-331`).
- **The dispatcher has no identity in the save.** `DogNetwork` is a static dictionary; only `NetworkState` is saved, and only on the elected node. Deleting the coordinator in dev tools re-elects on the next access; losing every node clears the backlog permanently (`Network.cs:116-129, 207-240`).
- **War mode is destructive to in-flight work by design.** It interrupts assigned normal jobs and drops carried cargo in place, but keeps the queue and revalidates it after the threat (`Network.cs:393-414`). It is triggered by vanilla's active-threat check, so a hostile that is not an "active threat" will not flip it.
- **Conduit charging drains batteries, not generation.** The draw loop takes stored energy from `net.batteryComps`, fullest first (`Charging.cs:110-123`). A producing net with empty batteries is a dead charge spot even though the base is powered.
- **A dog's availability is stricter than "not busy".** Injured docked dogs, dogs that should go home for repair, drafted dogs, dogs with player-forced jobs and dogs that merely *want* a charge are all excluded (`Network.cs:157-183`). This is intentional but easy to mistake for a dispatch bug.
- **`Report` dedupes within a queue only**, by `(kind, targetId)` (`Network.cs:254-256`); the normal and war queues are separate worlds.
- **Rescue and hostile alerts are the only map-wide discoveries.** Everything else must be seen by a dog within 15 cells, in an unfogged cell, and pass vanilla eligibility (`Work.cs:63-127`, `Network.cs:341-348`). Cleaning additionally requires the Home area and 600 ticks of filth age.
- **The think tree is the ordering contract.** Charge before work, work before dock, dock before wait-in-room (`QR_ThinkTrees.xml:30-37`). Inserting a node in the wrong place silently changes priorities for every dog.
- **Built-in guns are real equipment.** They are `ThingWithComps` with `destroyOnDrop`, created and destroyed by `Tiers.Arm`; the saved state is only the picked def per slot, not the gun object (`Tiers.cs:62-74`, `Robodog.cs:229-230`, `QR_Weapons.xml:4-33`).
- **Riding is a rider-driven illusion.** The dog is not a mount in vanilla; the rider's job moves the pair and the dog's job copies the rider's position/rotation each tick (`Riding.cs:197-226`). Anything that ends the rider's job ends the ride.
- **`AutoTest` exists in every launch.** It is a `MonoBehaviour` created unconditionally and idles outside a harness run; only the quicktest patch is gated on the launch argument (`QR.cs:20`, `AutoTest.cs:48-59, 213-233`).
- **Tunables are split.** Most gameplay values are XML `DefModExtension`s; a few pacing constants are C# (see [XML tunables](#xml-tunables-defmodextensions)). The setting `turbineDistance` is the only user-facing mod option.

## Diagrams and related documents

The interactive diagram set generated from the code ([commit `3c4e549`](https://github.com/jtj9817/Quadrunner-mod-rw/commit/3c4e549)) is the visual companion to this document:

- [Overview](diagrams/index.html), [Decomposition](diagrams/decomposition.html), [Traceability](diagrams/traceability.html)
- Subsystems: [Pawn](diagrams/sub--sub-pawn.html), [Network](diagrams/sub--sub-network.html), [Charging](diagrams/sub--sub-charging.html), [Work](diagrams/sub--sub-work.html), [Rescue](diagrams/sub--sub-rescue.html), [Riding](diagrams/sub--sub-riding.html), [Combat](diagrams/sub--sub-combat.html), [Render](diagrams/sub--sub-render.html), [Stable](diagrams/sub--sub-stable.html)
- Flows: [Dispatch a haul](diagrams/flow--flow-dispatch.html), [The charge decision](diagrams/flow--flow-charge.html), [The combat ping chain](diagrams/flow--flow-combat.html)

Related documents, each the canonical home for its subject:

- [README.md](../README.md) — what the mod does and how to install, play, build and remove it.
- [docs/design.md](design.md) — the original design snapshot; **dispatch and queue sections are superseded by plan 0003** (`docs/design.md:6`).
- [docs/plans/0001-quadrunner.md](plans/0001-quadrunner.md) — targets T0–T9 and the decision record, with the dispatch parts explicitly superseded at line 6.
- [docs/plans/0003-network-dispatch.md](plans/0003-network-dispatch.md) — the authoritative dispatch decisions (discovery, assignment, coordination, war, persistence).
- [docs/plans/0002-hearthline-lore.md](plans/0002-hearthline-lore.md) — lore and tooltip scope.
- [docs/test-runs.md](test-runs.md) — the per-run verification log.
- [AGENTS.md](../AGENTS.md) — the development loop, tool reference and test-instance rules.

Paths beginning `.tools/` are generated by `tools/vanilla.sh init` (decompiled game source) and are not part of the repository.
