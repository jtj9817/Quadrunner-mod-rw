# Hand-off: T2 done (11/11 PASS); the run beside the user's game and the usage-limit status line are next

- **Written:** 2026-10-19 23:10 UTC, by a session that stopped at ~253k tokens of context
- **Repo state:** branch `main`, HEAD is the commit adding this doc; before it `204901c` (`Selftest re-run: all cases ok`) and `d752007` (`T2: Battery and charging`). Tree clean.
- **Test instance:** stopped (`tools/autotest.py stop` before the selftest). **User's game:** OPEN at hand-off (pid 3358115, `./RimWorldLinux -disable-compute-shaders`), and the user wants a test run beside it (see Next steps)

## Read first

1. `AGENTS.md`, then `TODO.md` (current state; new gotchas under *Game / API* and *Testing*)
2. `docs/plans/0001-quadrunner.md`: Decisions (new rows: Battery tunables, Charge-spot search, Agent defaults, Run beside the user's game, Usage limit); T0's last open box; T1's debug-actions box; **T3** next
3. The newest rows of `docs/test-runs.md` (22:27: Run all 11/11 PASS, 0 new errors)

## Where things stand

- **T2 done**, all 5 boxes ticked on run 2026-10-19 22:27 (build `caa6bdc-dirty.20261019T222217Z`). Code: `ChargeSpots.cs` (static per-map cache + per-dog rules + region search), `Charging.cs` (`JobGiver_SeekCharge`, `JobDriver_ChargeAtConduit`), `Building_Stable.Tick` charges docked dogs, `Settings.cs` (turbine distance mod setting), battery column, debug actions. Scenarios in `Scenarios.Charging.cs`; the storyteller is off while any scenario runs (`Scenario.Start`/`Finish`).
- **T1:** 5/6; the debug-actions box waits for T4 (power down) and T3 (dispatch).
- **T0:** 9/10; open box: can the test instance run beside the user's open game.
- `tools/selftest.sh` re-run: all cases ok (stop the real instance first; its stop/hang cases kill processes).

## In progress when the session stopped

1. **Run beside the user's game.** The user said yes ("Let's do #1. I'm running a Rimworld instance"). I snapshotted their profile's Config/Saves file times read-only to the old session's scratchpad (lost; take a new snapshot). The command `tools/autotest.py --beside-user-game run` (the flag goes **before** the subcommand) was **refused by the auto-mode classifier** ("Safety Bypass Flag"). Don't work around it: ask the user to allow it (e.g. a permission rule for `Bash(tools/autotest.py --beside-user-game:*)`) or to run it themselves with `! tools/autotest.py --beside-user-game run`.
2. **Usage-limit status line.** User's choice: Claude Code's built-in auto-continue plus a status line that records usage data. Found (Claude Code 2.1.304, binary `~/.local/share/claude/versions/2.1.304`, unverified in docs): the status-line JSON seems to carry `rate_limits` with `five_hour` / `seven_day` / … entries (nullable; a string says "rate_limits will be null" with a missing profile scope); `resetsAt` and `utilization` appear in the rate-limit schema. The user's global `~/.claude/settings.json` already has a `statusLine` (user@host:cwd via jq). The previous-previous session's attempt to add a project `statusLine` to `.claude/settings.json` was refused by the classifier. Not started.

## Next steps

1. Record nothing new: the user's answers are already in the plan's Decisions table (this commit).
2. Ask the user to allow (or run) `tools/autotest.py --beside-user-game run`; before it, snapshot `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/{Config,Saves}` mtimes read-only (`find … -printf '%T@ %s %p\n'`), compare after (their own game may autosave: judge by which files changed). Then tick T0's last box, answer the open question in the plan, update AGENTS.md (test-instance section) and the user-game rule if beside-runs work.
3. Status line: write `tools/statusline.py` (stdlib only) that reads the status-line JSON from stdin, appends `rate_limits` (with a timestamp and session id) to `.devlog/usage.jsonl` (or a user-chosen path), and prints the same line the user's current command prints. Wiring it into settings needs the user's OK (classifier refuses settings edits): suggest the edit or a `! ` command. The built-in auto-continue is enabled by the user in Claude Code (`/rate-limit-options`, per strings in the binary; verify) — tell them how.
4. Then start **T3** (dispatch and work).

## Decisions and answers from the user this session

All recorded in the plan's Decisions table (this commit):
- Agent defaults (Robodog kind, ownership, full stable/16th dog, stable bills, placeholder art): fine.
- One test run beside their open game: yes.
- Usage limit: built-in auto-continue + a status line recording usage data.
- Agent defaults made this session (overridable): Battery tunables (conduit 0.0004/tick, stable 0.0012/tick, retuned from the WIP so a conduit can ever win), Charge-spot search details.

## Waiting on the user

- Permission (or their own `!` run) for `tools/autotest.py --beside-user-game run`.
- OK to wire the status-line script into settings (global or project).

## Gotchas learned this session

All in `TODO.md`: batteries are transmitters (must touch a conduit); `DebugSettings.unlimitedPower` refills every battery; batteries self-discharge 5 W; `Room.IsPrisonCell` only recomputed on shape change (check the role); `ForPrisoners = false` logs a vanilla error; quicktest maps differ per launch (trees aren't `Standable`); storyteller off during scenarios. Also: `autotest.py`'s `--beside-user-game` is a global option, before the subcommand.
