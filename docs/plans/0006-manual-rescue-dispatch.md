# 0006 — Manual rescue dispatch

## Goal

Add an action on the stable itself to manually dispatch Rescuedogs (Medirunners), as requested by the user.

## Decisions

| Area | Decision |
| --- | --- |
| Surface | A **Dispatch Rescuedogs** gizmo on a selected, spawned player stable, using the existing Medirunner dock icon. |
| Behavior | Immediately refresh the existing shared map dispatcher, including emergency rescue discovery and assignments. Do not add a second rescue scheduler, change automatic rescue, or restrict assignment by stable affiliation. |
| Eligibility | Keep existing triage, revealed-patient discovery, reservations, reachable beds, battery and availability rules. Disable the action with an explanation when no Medirunner is available or no revealed patient needs rescue. Stable power is not required for dispatch. |
| Manual retries | Explicit forced stable dispatch clears retry backoff only for unassigned rescue tasks before the shared refresh. Automatic refreshes, active rescues and combat/normal-work backoff remain unchanged. |
| Persistence | No new saved fields, definitions or Harmony patches. Existing queued jobs retain their persistence behavior. |

## Targets

### R1 — Stable action

- [x] The selected stable visibly offers the action, including explanatory disabled states (06:49 behavioral run; 06:51 full-resolution enabled/busy captures inspected).
- [x] Invoking the action while paused immediately sends two available Medirunners to distinct patients; repeated dispatch does not interrupt active rescues (06:49 and 06:51).
- [x] Previously deferred patients are retried at the same paused tick after medics become available; automatic refresh still honors backoff and repeated manual input preserves active job IDs (2026-10-24 07:11; failing-before 07:10).
- [x] Both patients reach beds across save/load and the dogs return to an unpowered stable, with zero new errors (06:49 and 06:51).
- [x] Existing triage and wartime rescue scenarios remain passing (06:49, 3/3 PASS).

Initial action evidence: [test-run log](../test-runs.md), 2026-10-24 06:49 and 06:51, build
`d6ad429-dirty.20261024T065003Z`; private captures `.devlog/screens/manual-rescue-ready.png`
and `manual-rescue-dispatched.png` show the native 1280×720 stable action enabled and then greyed while both medics work.

Queued-rescue regression: 2026-10-24 07:10 failed before the correction; 07:11 passed with existing triage
and wartime rescue, **3/3 PASS, 0 new errors**, build `36a1abc-dirty.20261024T071107Z`. Both patients were
queued while medics charged; after recharging, automatic refresh still deferred them, but the native manual
action assigned distinct patients without advancing simulation. Repeat input, save/load, bed delivery and
return to the unpowered stable also passed.


## Open questions

None.
