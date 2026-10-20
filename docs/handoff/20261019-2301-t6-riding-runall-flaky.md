# Hand-off: T5 and T6 (riding) built and passing alone; a clean Run all is still needed to tick them; older scenarios flake in long Run alls

- **Written:** 2026-10-20 03:01 UTC (file name uses the hook's local stamp), by a session that stopped at ~256k tokens of context
- **Repo state:** branch `main`, HEAD is the commit adding this doc; before it `3326354` (`T6 WIP: Riding …; fix minified stable tick`), `0daa77d` (previous hand-off). Tree clean.
- **Test instance:** stopped (`tools/autotest.py stop` at the limit, mid Run all). **User's game:** open (pid 3358115). Test runs beside it are the default; no need to ask.

## Read first

1. `AGENTS.md`, then `TODO.md` (three new gotchas: fail conditions run before the first toil, a minified building still ticks, screenshots from a scenario)
2. `docs/plans/0001-quadrunner.md`: Decisions rows **Riding: mounting**, **Riding: trip**, **Ridden speed** (user, this session); **T5** (2 boxes) and **T6** (4 boxes), none ticked
3. The newest rows of `docs/test-runs.md` (02:40 – 02:49)
4. `Source/Quadrunner/Riding.cs` and `Scenarios.Riding.cs` (all of T6)

## Where things stand

- **T5 (Medirunner)**: code from the previous session. Proven alone: Mass-casualty raid + Rescue order (run 01:40), Assemble Medirunner (run 01:44), all 0 new errors. In the Run all of 02:40 (24/26) both rescue scenarios and Assemble passed again; the 1 new error there was a real T5 bug (NRE in `Rescue.Patients` from a minified stable's `Tick`, no map), **fixed** in `3326354` (`Building_Stable.Tick` returns when not spawned). Boxes not ticked yet: waiting for a clean Run all.
- **T6 (riding)**, commit `3326354`:
  - `Rideable` extension on `QR_Quadrunner` (`riddenMoveSpeed` 7, per-facing rider offsets N (0,0,0.42), E (−0.08,0,0.36), S (0,0,0.45), W (0.08,0,0.36)); `StatPart_Riding` patched onto MoveSpeed (`Patches/QR_Stats.xml`).
  - `JobDriver_RideTo` (rider; A = dog, B = cell): walk to dog, mount, ride, dismount on job end (any reason: arrival, drafted, downed, other order), early when the next step enters an indoor room or a door into one (`Riding.EnteringIndoors`), or when the dog powers down/stops; then walks on. The dog is called in `Notify_Starting`.
  - `JobDriver_BeingRidden` (dog; A = rider): waits, then copies the rider's cell and rotation every tick (`handlingFacing`); ends when the rider's ride ends.
  - `FloatMenuOptionProvider_Ride`: "Ride X to…" on a dog that is out, "Ride X from <stable> to…" on the stable (`Building_Stable.IdleRideable`: fullest docked rideable not wanting a charge); both open a cell targeter → `Riding.Ride`.
  - Harmony patch 3: postfix on `Pawn_DrawTracker.DrawPos` getter: rider += offset, drawn at altitude `PawnUnused + AltInc/2`; the ridden dog drawn under the rider's tweened position. TweakValue fields "Quadrunner riding" add to the XML offsets (not used for the tuning this time: tuned in XML from screenshots).
  - No raid drain factor while ridden (`CompRobodog.DrainFactor`). Quadrunner description mentions riding.
  - Scenarios (`Scenarios.Riding.cs`, last in Run all): **Ride until 0%** (plan's), **Ride and dismount**, **Rider pose** (pauses, captures `.devlog/screens/ride-<facing>.png` with `ScreenCapture`). All 3 PASS, 0 new errors (run 02:47), Rider pose again after the offset change (02:49).
- Not yet recorded in the plan: the Harmony patch 3 row (draw offset) and the agent defaults for riding (Quadrunners only via `Rideable`; dismount on any end of the rider's job; indoor rule = next step into a non-outdoor room or a door leading into one, mounting indoors allowed; dog drains per cell as walking, no raid factor while ridden, keeps carrying below 10%; after the ride the dog goes back to work or docks). Add both rows to the Decisions table.

## In progress when the session stopped

Run all (29 scenarios) on build `3326354` (dirty-free, built ~02:50): stopped at the limit after 18 scenarios: 17 PASS, **Two stables FAIL** (not looked at). In the 02:40 Run all, Raid: finish and dock and Field battery and repair kit failed instead; all three pass when run alone (02:43). Hypothesis (unverified): state that builds up over a long Run all (colonist needs, leftover things, game time ~80k+ ticks), not the new code. The Raid and Field battery summaries now print the gold's whereabouts and every colonist's job / drafted / mental state; Two stables has no such detail yet.

## Next steps

1. `tools/status.sh`, then `tools/autotest.py run` (background, ~15 min). Read every FAIL's summary and the raw log (`.devlog/runs/<launch>/` or `.devlog/rwdata/Player.log`) around it.
2. If the flaky scenarios fail again: find the cause from the summaries (e.g. colonists asleep/eating/in a mental state, leftover stockpiles or filth from earlier scenarios), fix the scenario setup (or a real bug), rerun. If the same scenario fails 3 runs in a row without new information, report to the user.
3. On a clean Run all (all PASS, 0 new errors): tick T5's two boxes and T6's four (box 3: "checked by screenshot" by Rider pose; say the tuning was done in XML, TweakValues available), add the Decisions rows above, `docs/test-runs.md` rows, `TODO.md` (tick T5, T6; Current state), commit `T5, T6: Medirunner and riding`.
4. Report T4, T5, T6 to the user (Rescue rules agent defaults and the riding agent defaults; they may override), then start **T7** (Combatdog T1, pings, patrols); ask design questions with AskUserQuestion, record answers in the Decisions table.

## Decisions and answers from the user this session

- **Riding: mounting** = dog or stable (right-click a Quadrunner that is out, or its stable for the fullest docked one). Recorded.
- **Riding: trip** = "Ride to…" a cell; dismount on arrival; before an indoor room's door, the rider gets off and walks. Recorded.
- **Ridden speed** = `riddenMoveSpeed` in XML, default 7 c/s; work speeds unchanged. Recorded.

## Waiting on the user

- Whether to wire `tools/statusline.py` into settings (they add it themselves).
- The non-tmux close path of the hand-off hook is untested on purpose (see TODO.md).

## Gotchas learned this session

All in `TODO.md`: vanilla checks a job's fail conditions before the first toil's pre-init actions (use `Notify_Starting`); a `MinifiedThing` ticks the building inside it with no map; `ScreenCapture.CaptureScreenshot` works on Xvfb, wait by counting frames while paused. Also: run filters match substrings, so "Ride" also matches "Rider pose"; "Raid" also matches "Raider carries off a dog" (use "Raid: finish").
