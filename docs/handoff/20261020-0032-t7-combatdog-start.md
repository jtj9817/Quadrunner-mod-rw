# Hand-off: T5 and T6 done, test instance made cheaper; T7 (Combatdog T1) decisions recorded, no T7 code yet

- **Written:** 2026-10-20 04:32 UTC (file name uses the hook's local stamp), by a session that stopped at ~254k tokens of context
- **Repo state:** branch `main`, HEAD is the commit adding this doc; before it `6a00a73` (`T7: Record the Combatdog decisions …`), `46a8bef` (640×360 / 4 render threads), `4f2ebd6` (T5, T6 done). Tree clean.
- **Test instance:** stopped (`tools/autotest.py stop`: while idle it still renders and uses ~370% CPU). **User's game:** closed at hand-off (`autotest.py status`: "user's game not running").

## Read first

1. `AGENTS.md`, then `TODO.md` (new gotchas: test map size, software rendering cost, raid-check cache, map center in rock, colonists refreshed per scenario, vanilla Clean queues extra targets)
2. `docs/plans/0001-quadrunner.md`: **T7** (7 boxes, none ticked) and the four new Decisions rows **Combatdog: raid deploy / engage range / patrol end / idle** (user, this session)
3. `docs/design.md` § Combatdogs (lines ~129–180) and its edge-case table (combat rows)
4. The newest rows of `docs/test-runs.md` (03:26 – 04:24)
5. Memory note: run only the scenarios a change can affect (see below)

## Where things stand

- **T5, T6 done** and ticked (run 2026-10-20 04:07, Run all 25/29 on the 200 map: every T5/T6 scenario PASS, 0 new errors). The 4 FAILs of that run were fixed and passed in a filtered run (04:22, 6/6): room setups (map center in rock → `ClearRect` reaches from the nearest standable cell), Raider carries off a dog (`Raid.Recheck(map)` after spawning the raider), Two stables (now clears the map's own filth first).
- **Real bug fixed:** `DogWork.FindJob` drops queued targets that fail the radius / nearest-stable checks (vanilla `WorkGiver_CleanFilth` queues all filth within ~5 cells).
- **Test instance now:** quicktest map 200×200 (`QR_MAP_SIZE`, `-quadrunner-mapsize`, test-only postfix on `Root_Play.SetupForQuickTestPlay` in `AutoTest.cs`), window 640×360 (`QR_SCREEN`), llvmpipe capped at 4 threads (`QR_RENDER_THREADS` → `LP_NUM_THREADS`). Same speed as before at ~400% CPU instead of ~1800%. Each scenario's result line ends with `N s, M ticks/s`. Size/screen/thread changes need a cold start (`autotest.py stop` first). `tools/selftest.sh` passes.
- **Scenario runner:** `Scenario.Start` refills colonists' needs and removes malnutrition, blood loss and fresh injuries (long Run alls had starved the one quicktest colonist). Summaries use `ColonistStates()` (Scenarios.Dispatch.cs).
- **T7: nothing coded.** I had read `Building_Stable.cs`, `Rescue.cs`, `Robodog.cs`, `Work.cs`, `Docking.cs` (`JobGiver_WaitInRoom.Qualifies` = room that isn't a bedroom/barracks/prison: reuse for capture-to-room), `QR_Races.xml`, `QR_ThinkTrees.xml`, `QR_Jobs.xml`, and vanilla `PawnComponentsUtility` (a ToolUser gets equipment + apparel trackers and a training tracker; no drafter unless humanlike or colony mech → drafting needs the Harmony patch the plan budgets).

## In progress when the session stopped

Planning T7's first slice; no files changed. The sketch (agent's, not yet reviewed by the user, adjust freely):

- `QR_Combatdog` race (parent `QR_RobodogBase`, `intelligence ToolUser`, claw tools cut + scratch, check `QR_Bodies.xml` for a paw parts group), PawnKind with tinted husky placeholder, stable bill at 100 steel / 50 plasteel / 2 components (see Medirunner's recipe in `QR_Recipes.xml` and `Assembly.cs`).
- `RobodogWork.combat` flag (like `rescue`); `deployedSpeedFactor` 2 for the 200% move speed while spawned (extend `StatPart_RobodogRaid` or a sibling).
- Ping: `CompRobodog.PostPostApplyDamage` → if the instigator is a pawn hostile to the player, the dog's stable queues it (saved `List<Pawn>` on `Building_Stable`, `Scribe_Collections` by reference) and dispatches a docked or returning Combatdog (end a `QR_Dock` job to redirect; not when `WantsCharge`).
- Raid: every docked Combatdog with battery deploys (decision). Engage range 15 cells in XML: hostiles in sight join the queue.
- Combat job giver (think tree: after `JobGiver_SeekCharge`, which already gives low-battery retreat and "ignore pings until charged"): nearest queued target not downed/dead → vanilla `AttackMelee` (check how it ends on a downed target); downed → vanilla `Capture` to `RestUtility.FindBedFor(…, GuestStatus.Prisoner)`, else carry to the nearest empty room passing `JobGiver_WaitInRoom.Qualifies` and leave it downed (a small carry-to-cell driver, modelled on `JobDriver_CarryToStable`); dead → skip.
- Patrol (later slice): stable gizmo placing waypoints (saved list), walked in a loop while a raid is on or targets remain, then dock. Open agent default to record: with no waypoints, a deployed Combatdog with nothing in range guards at its stable.
- Yellow siren while retreating (later slice), drafting + priority target via Harmony (later slice).
- Scenarios (plan): **Ping chain**, **Combatdog sweep**, **Low-battery retreat**, **Drafted priority target**, in a new `Scenarios.Combat.cs`, last in Run all.

## Next steps

1. `tools/status.sh`. Do T7 in slices (race + ping + combat loop + capture first, with **Ping chain** and the capture parts of **Combatdog sweep**), each with `tools/autotest.py run "<the new scenarios>"` only.
2. Record agent defaults as Decisions rows as you make them (e.g. no-waypoint guard spot, ping dispatch count = one Combatdog per ping).
3. Run all only at the end of T7 (shared code: think tree, stable dispatch), then tick boxes.
4. Optional, cheap: drop `Application.targetFrameRate` (e.g. to 5) in `AutoTest.Update` while no scenario runs, to cut the idle instance's ~370% CPU. I told the user I'd do it with T7's first run unless they object.

## Decisions and answers from the user this session

All recorded in the plan's Decisions table:
- Combatdog raid deploy: all docked (with battery) deploy on a raid.
- Engage range: 15 cells (XML).
- Patrol end: loop the waypoints while a raid is on or targets remain, then dock.
- Idle: combat only, like the Medirunner.
- Smaller quicktest map (the user's suggestion; 200 chosen by measurement).
- **Standing instruction:** don't Run all to check a change; run only the scenarios a change can affect (saved as a memory note). Run all only when shared code changes or to tick boxes, and say why.

## Waiting on the user

- The riding agent defaults and the Medirunner rescue rules were reported; the user may override them.
- Whether to wire `tools/statusline.py` into settings (they add it themselves).

## Gotchas learned this session

All in `TODO.md`: vanilla Clean queues extra filth past our filters; `Raid.Active` is cached for 60 ticks (`Raid.Recheck`); the map center can be rock; colonists are refreshed per scenario; test map size and software-rendering CPU cost. Also: the camera's minimum zoom is 11 (`CameraMapConfig.sizeRange`); Rider pose lowers it to 3 during capture for the 360 px window.
