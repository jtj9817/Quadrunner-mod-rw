# Plan 0007: Wartime recall and Combatdog recovery

## Goal

Recall noncombat workers to safety during hostile events, then recover Combatdog casualties before ordinary utility work and restore recovered dogs automatically at a stable.

## Decisions

| Area | Decision |
| --- | --- |
| Wartime standby | Cancel normal work, safely drop cargo, return and dock when reachable; otherwise wait in place and retry. Already-docked workers remain inside. Medical rescue and Combatdog deployment remain active (user, 2026-10-24). |
| Reports | A Combatdog becoming downed or dying reports its identity and last-known coordinates to the elected stable/central node. Reports survive war, save/load and node transfer; recovery tracks real pawns/wrecks, never creates replacements for missing remains. |
| Recovery | After active hostilities clear, available Quadrunners or Medirunners recover Combatdogs ahead of ordinary utility work. Human/animal medical emergencies retain existing medical priority. Reservations/central assignments prevent duplicate carriers. |
| Living casualties | Dock for existing powered charging and injury repair. Other dog types retain their existing locally discovered retrieval rules. |
| Wreck rebuilding | Powered stable rebuilds the same Combatdog with its identity, tier and weapon choices retained; no colonist labor. Keep the existing recipe's 50 steel + 25 plasteel cost (user selected automatic stable rebuild with existing materials, 2026-10-24). |
| Material delivery | Resources must physically reach the 3×3 area around the stable interaction cell (a stockpile there can be supplied through ordinary hauling). The stable marks this area when selected with a recovered wreck. No remote map-wide consumption. Missing ingredients consume nothing and leave the wreck safely waiting. Duration/cost derive from the existing Combatdog rebuild recipe. |
| Lifecycle | Power loss pauses processing. Save/load preserves progress without paying twice. Destroy/uninstall releases recovered wrecks without losing their contents. No new Harmony patches or saved map component. |
| Manual rebuild bills | Stored wrecks can also be ingredients for this stable's existing Combatdog rebuild bill. An active manual bill takes priority over unstarted automatic work when a slot is free; a paid automatic rebuild finishes first, and suspending the bill permits automatic rebuilding. Reservation prevents both paths using one wreck. Newly recovered and previously stored wrecks are un-forbidden for service (user-reported bill fix, 2026-10-29). |

## Targets

### W1: Recall

- [x] Multiple field workers return and dock during a hostile event; normal cargo is safely released.
- [x] No reachable receiving stable means wait in place, not wandering; a restored route resumes docking.
- [x] Docked workers remain inside across reload and resume normal work after hostilities clear.
- [x] An inaccessible or uninstalled home does not prevent wartime docking at another reachable stable; both assignment transfers survive reload.

### W2: Recovery

- [x] Downed and dead Combatdogs beyond worker scan range create centrally retained recovery orders with last-known coordinates.
- [x] Orders survive wartime save/load and coordinator transfer, and do not deploy carriers during hostilities.
- [x] After hostilities, worker and medic carriers physically recover living casualties and wrecks ahead of ordinary work without duplicate assignment; medical emergencies retain priority.
- [x] Missing/blocked receiver or unavailable carrier leaves recovery safely queued; interruption can reassign it.
- [x] A retry-deferred medical emergency keeps its available Medirunner ahead of a fresh casualty order, then recovery proceeds after the patient reaches bed.

### W3: Automatic stable rebuild

- [x] Missing ingredients consume nothing; complete delivered ingredients are consumed exactly once.
- [x] Powered stable progresses without colonist labor; power loss pauses and save/load resumes without duplicate charges.
- [x] Same dog, name, tier and weapon selections return healthy/charged and functional.
- [x] Stable uninstall/destroy safely releases stored wrecks, including a partially funded rebuild.
- [x] A full second stable leaves a rebuilt dog unassigned despite its destroyed former home; after reload it adopts the receiver when a slot opens.
- [x] Five manually queued Combatdog rebuild bills use recovered wrecks across all four tiers, continue through save/load, and consume one set of materials per original dog.
- [x] Suspended bills permit automatic rebuilding; an already-paid automatic rebuild survives save/load and completes before a filtered manual bill uses the next wreck.
- [x] A full stable can still automatically rebuild a wreck even when an active manual bill cannot use its occupied slots.
- [x] A do-until assembly bill counts docked dogs during ingredient search, adds only the missing dog and remains satisfied after reload.
- [x] Affected game scenarios pass with zero new errors; shared-code integration and clean-removal behavior are verified.

The 2026-10-29 bill-fix regressions passed on `0f3fa78-dirty.20261029T061658Z`: the five-wreck case, paid/suspended arbitration, full-capacity arbitration and target-count behavior passed in a six-case focused run with zero new errors. The five-wreck regression and the target-count regression each failed before their respective corrections; see [test runs](../test-runs.md).

The shared integration run on `0f3fa78-dirty.20261029T061906Z` passed 92/93 with zero new errors; its known riding-retrieval failure passed unchanged on a fresh map, together with stronger whole-map bill-cost accounting. No clean 93/93 sequence is claimed. Its independent removal export loaded without Quadrunner, ticked 600, saved/reloaded and ticked 600 with expected vanilla contents and zero new errors.

Evidence: focused runs at 08:33 and 08:37 on 2026-10-24 proved recall, recovery and rebuild behaviors. The 84-case shared sequence (`1645533-dirty.20261024T083733Z`) passed 82 cases with zero new errors; its two fixture-only failures were corrected and passed in the 09:05 affected sequence (`1645533-dirty.20261024T090414Z`, 3/3). No clean 84/84 run is claimed. The independent no-Quadrunner observer then loaded the shared-sequence export, ticked 600, saved/reloaded, ticked another 600 and retained expected vanilla cargo/history with zero new errors. Native rebuild/delivery-area and clean-removal captures were inspected. See [test runs](../test-runs.md) for each build-specific result.

Review regressions (2026-10-24): the unchanged gameplay implementation failed all three new cases at 10:01 (`1425402-dirty.20261024T100055Z`, 0/3, zero new errors). The fixes passed **Wartime alternate stable**, **Combat recovery: deferred rescue priority**, and **Cross-stable rebuild at capacity** at 10:02 (`1425402-dirty.20261024T100210Z`, 3/3, zero new errors), including physical delivery/docking and save/load. Shared integration and packaged evidence are recorded separately in [test runs](../test-runs.md).

The review-fix shared run at 10:18 (`1425402-dirty.20261024T100304Z`) passed **79/87 with zero new errors**, including every new regression. Map-generated centipedes/scythers became active during riding and disrupted eight later riding/combat/setup cases. Those eight passed unchanged on a fresh map at 10:22 (`1425402-dirty.20261024T101940Z`, **8/8, zero new errors**). No clean 87/87 sequence or new independent removal-observer result is claimed.

Actual release package `64f134e.20261024T102338Z` passed **12/12 with zero new errors** at 10:28, verifying loaded BUILD and package root. The three review regressions, original recall/recovery/rebuild cases, peaceful uninstall/reinstall and manual rescue all passed. A first package launch passed its assertions but failed the error check on an unsaved generated pawn's Parent relation; both launch results are retained in the iteration log.

## Open questions

None. Automatic rebuilding with existing material costs was selected explicitly; the other recorded network decisions remain unchanged except where revised above.
