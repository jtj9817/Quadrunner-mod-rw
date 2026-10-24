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

## Targets

### W1: Recall

- [x] Multiple field workers return and dock during a hostile event; normal cargo is safely released.
- [x] No reachable receiving stable means wait in place, not wandering; a restored route resumes docking.
- [x] Docked workers remain inside across reload and resume normal work after hostilities clear.

### W2: Recovery

- [x] Downed and dead Combatdogs beyond worker scan range create centrally retained recovery orders with last-known coordinates.
- [x] Orders survive wartime save/load and coordinator transfer, and do not deploy carriers during hostilities.
- [x] After hostilities, worker and medic carriers physically recover living casualties and wrecks ahead of ordinary work without duplicate assignment; medical emergencies retain priority.
- [x] Missing/blocked receiver or unavailable carrier leaves recovery safely queued; interruption can reassign it.

### W3: Automatic stable rebuild

- [x] Missing ingredients consume nothing; complete delivered ingredients are consumed exactly once.
- [x] Powered stable progresses without colonist labor; power loss pauses and save/load resumes without duplicate charges.
- [x] Same dog, name, tier and weapon selections return healthy/charged and functional.
- [x] Stable uninstall/destroy safely releases stored wrecks, including a partially funded rebuild.
- [x] Affected game scenarios pass with zero new errors; shared-code integration and clean-removal behavior are verified.

Evidence: focused runs at 08:33 and 08:37 on 2026-10-24 proved recall, recovery and rebuild behaviors. The 84-case shared sequence (`1645533-dirty.20261024T083733Z`) passed 82 cases with zero new errors; its two fixture-only failures were corrected and passed in the 09:05 affected sequence (`1645533-dirty.20261024T090414Z`, 3/3). No clean 84/84 run is claimed. The independent no-Quadrunner observer then loaded the shared-sequence export, ticked 600, saved/reloaded, ticked another 600 and retained expected vanilla cargo/history with zero new errors. Native rebuild/delivery-area and clean-removal captures were inspected. See [test runs](../test-runs.md) for each build-specific result.

## Open questions

None. Automatic rebuilding with existing material costs was selected explicitly; the other recorded network decisions remain unchanged except where revised above.
