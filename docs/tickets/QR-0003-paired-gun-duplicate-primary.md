# QR-0003: Tier 4 paired guns occasionally duplicate the primary weapon instance

| Field | Value |
| --- | --- |
| Issue type | Bug |
| Severity / priority | Low |
| Status | Open |
| Component | Tier 4 paired arsenal, `Tiers.Arm`/`GunFor` |
| Ticket key | QR-0003 — local documentation identifier, not an external Jira issue |
| Related design | [Plan 0009: Combat packs](../plans/0009-combat-packs.md), [Plan 0012: Tear gas](../plans/0012-tear-gas.md) |
| Found | Scrutiny of the 2026-11-15 test runs (scripts in `.devlog/runs/…/Player.log`); the defect dates at least to the C2 delivery on 2026-11-03 |

## Summary

At the Tier 4 weapon-demo round transitions, vanilla occasionally logs
`Pawn <dog> got primaryInt equipment QR_Gun_Dual*… while already having primaryInt equipment …`:
a built-in paired gun that is flagged as the pawn's primary is added while another flagged instance is still
present, although `Tiers.Arm` destroys the held instance before adding a new one. It is benign today (scenario
ends destroy the dogs; the summary reports zero loose guns) but unexplained — worth an investigation ticket,
not a silent suppress-or-guess fix.

## Evidence

- `.devlog/runs/20261109-005353-1667961/Player.log:8476,8491,8492` — three occurrences in the Tier 4 weapon
  demo, each immediately after a `network #N Combat: complete or no longer applicable` line (a round of
  raiders down), never anywhere else in the fight.
- `.devlog/runs/20261115-073447-1169201/Player.log` (run 2026-11-15 Tier 3/4 weapon demo) — one occurrence,
  same round-transition marker, T4 demo only.
- `.devlog/runs/20261103-101601-2225150/Player.log` and `…-102204-2248366/Player.log` — one occurrence each,
  same pattern (present since the paired-gun delivery, C2).
- Never observed in the actual combat scenarios (Tier 3 pack combat, Tier 4 pack arsenal, Combat tier legacy
  migration) in any archive surveyed. The weapon demos end with `loose guns 0` in every run.

## Why this is filed as a bug for investigation, not fixed

- The demo is the only place this signature fires; combat scenarios never produce it. The round-transition flow
  (`Scenarios.Demo.cs` → `Tiers.Arm`/`GunFor` after teleport + `AddNewDog`… upgrade chain) is the only
  code differing from the fight code.
- The duplicate is likely the *held* instance swapped between `Arm` calls sharing a burst boundary — the exact
  gap inside `AddEquipment` (explicit primary-already-null error), not the paired verb.
- Blindly reordering the destruction and the add risks breaking the paired-volley simultaneity (or, worse,
  letting a real held gun drop).

## Acceptance criteria

- [ ] Root cause identified in code: which pair of calls adds the second primary-flagging gun during the demo —
      `Arm`'s destroy→add boundary, the picked-gun evaluation before `Arm`, or something in the demo/fixture.
- [ ] A verified regression assertion or fix lands in the demo scenario (or in `Arm`) so
      `Pawn.EquipmentTracker.Primary` is never duplicated on a loadout swap.
- [ ] Paired-gun combat, demos and the affected combat scenarios pass with zero new errors after the fix.

## Constraints

Built-in guns remain `destroyOnDrop`, never player-lootable, and paired shots must remain two independent
rolls in one burst. No change to the paired role in gameplay code.
