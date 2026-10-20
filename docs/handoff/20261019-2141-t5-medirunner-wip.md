# Hand-off: T4 done; T5 (Medirunner) written, its two rescue scenarios pass; Assemble Medirunner and the final Run all next

- **Written:** 2026-10-20 01:45 UTC (file name uses the hook's local stamp), by a session that stopped at ~252k tokens of context
- **Repo state:** branch `main`, HEAD is the commit adding this doc; before it `6250438` (`T5 WIP: Medirunner rescues the downed`), `15bd1b5` (T5 decisions), `bb2fe3e` (`T4: Power-down and retrieval`), `590898e` (same-pane hand-off). Tree clean.
- **Test instance:** stopped (`tools/autotest.py stop` at the limit, during a Run all that had done 3 scenarios). **User's game:** open (pid 3358115). Test runs beside it are the default; no need to ask.

## Read first

1. `AGENTS.md`, then `TODO.md` (new gotchas: stale objects after a reload, exact float-menu labels, dogs need a name, in-game restart can segfault, never fire the hand-off hook's Stop from a live session)
2. `docs/plans/0001-quadrunner.md`: Decisions rows **Medirunner idle**, **Medirunner treatment** (user), **Rescue rules** (agent); **T5**'s two boxes (none ticked)
3. The newest rows of `docs/test-runs.md` (01:40, 01:12)

## Where things stand

- **T4 done** and T1's debug-actions box: Run all 23/23 PASS, 0 new errors (run 2026-10-20 01:12), all boxes ticked, commit `bb2fe3e`.
- **T5 code** (commit `6250438`):
  - `Defs`: race `QR_Medirunner` (on `QR_RobodogBase`, `RobodogWork` with `rescue` true, raid factors 1), pawn kind (placeholder husky, whitish tint), recipes `QR_AssembleMedirunner` (80 steel / 50 plasteel / 2 advanced components, 3500 work) and `QR_RebuildMedirunner` (corpse + 40 / 25), both on the stable.
  - `Rescue.cs`: `Rank` (0 colonist, 1 guest, 2 prisoner (checked first), 3 colony animal, −1 robodogs/hostiles/others), `Patients` (downed, `WantsToBeRescued`, not "no medical care"), `JobFor` (rank, then bleed rate, then distance; `RestUtility.FindBedFor` with the patient's guest status; vanilla `Rescue` or `TakeWoundedPrisonerToBed`).
  - `Work.cs`: `RobodogWork.rescue`; `DogWork.FindJob` for a medic returns only `Rescue.JobFor` (ignores raids, no retrieval).
  - `Building_Stable.cs`: `IdleDog(medic)`, `HasIdleDog` counts workers only; `TryDispatch` sends a medic when `Rescue.AnyPatient`, else a worker (never in a raid); `TrySend` helper; backoff only after a dog was tried.
  - `Scenarios.Rescue.cs`: **Mass-casualty raid** (the plan's) and **Rescue order**; helpers `DownBleeding` (cut + anesthetic, removed at the end), `SpawnPawn`, `SpawnMedicalBed`, `Pirates`.
  - `Scenarios.cs`: `AssembleAndDock(name, recipe)` now takes the recipe (materials from its ingredients); new entry **Assemble Medirunner**. Compiled clean (bare `dotnet build`), **never run**.
- **Run 2026-10-20 01:40** (build `15bd1b5-dirty.20261020T013730Z`): Mass-casualty raid PASS, Rescue order PASS, 0 new errors, first try (details in `docs/test-runs.md`).

## In progress when the session stopped

A Run all (25) on build `6250438`-equivalent minus the Assemble refactor; stopped after 3 PASS at the limit. No failure seen.

## Next steps

1. `tools/status.sh`, then `tools/autotest.py run "Assemble"` (background) to check both Assemble scenarios.
2. Then `tools/autotest.py run` (Run all, 26 scenarios, about 13 min). If all pass with 0 new errors: tick T5's two boxes naming that run (box 1 proven by Assemble Medirunner + Mass-casualty raid + Rescue order; box 2 by Mass-casualty raid; note Rescue order and Assemble Medirunner were added), add the `docs/test-runs.md` rows, update `TODO.md` (tick T5, Current state), commit `T5: Medirunner`.
3. Report T4 and T5 to the user, with the agent defaults in the **Rescue rules** row (they may override), then start **T6** (riding). T6 may need design answers; ask with AskUserQuestion and record them in the Decisions table.

## Decisions and answers from the user this session

- **Hand-off in the same pane** (user): at the context limit the new instance replaces the old session in its tmux pane (`tmux respawn-pane -k` run by the hook at the old session's Stop). Done and documented in `AGENTS.md` (commit `590898e`).
- **Medirunner idle: medic only** (user): no hauling, cleaning or construction; recorded (row "Medirunner idle"; carrying powered-down dogs home also excluded, marked as the agent's reading).
- **Medirunner treatment: rescue only** (user): carry to a bed, doctors tend; recorded.

## Waiting on the user

- Whether to wire `tools/statusline.py` into settings (they add it themselves; the classifier refuses settings edits).
- The non-tmux close path of the hand-off hook (SIGTERM to the nearest `claude` ancestor) is untested on purpose: a safe test was refused by the permission classifier. It did work once, by accident (see the gotcha).

## Gotchas learned this session

All are in `TODO.md`. Also: the previous session in this conversation killed itself by running the hook's Stop path from its own shell; this pane was then restarted by the user. A bare `dotnet build` in `Source/Quadrunner` is a safe compile check while a test runs.
