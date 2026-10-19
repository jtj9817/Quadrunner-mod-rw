# Hand-off: T0 harness built and self-tested; first real-game runs pending (user's game must close first)

- **Written:** 2026-10-19 ~21:15 UTC, by the session that set up the repo; it stopped at ~385k tokens of context (the hand-off hook's first real trigger)
- **Repo state:** branch `main`, HEAD `8598bb2` (`Context-limit hand-off hook; identity confirmed`) plus the commit adding this doc; tree clean
- **Test instance:** never launched against the real game. **User's game:** RimWorld was OPEN (pid 2779157, their own colony) at hand-off

## Read first

1. `AGENTS.md`: roles (you drive testing; the user's game is off limits), **The test instance**, the work loop, **Context limit and hand-off**
2. `TODO.md`: current state, and the gotchas, above all *Launching the test instance*
3. `docs/plans/0001-quadrunner.md`: Decisions table and **T0** (3 of 10 boxes ticked)
4. `docs/design.md`: the design (snapshot of the claude.ai doc https://claude.ai/artifact/HaFBXDTNRNVzWa79Hnd9K5, rev 39)

## Where things stand

- Repo created from scratch in `/mnt/0B8533211952FCF2/Quadrunner-mod-rw`; tools and lessons ported from Queen Bee (`/mnt/0B8533211952FCF2/queen-bee-rw-mod`).
- Mod skeleton builds with zero warnings (`tools/build.sh`): `About/About.xml`, `Source/Quadrunner/{QR,AutoTest,Scenario,Scenarios,DebugActions}.cs`. Scenarios: **Smoke**, **Save and reload**.
- Harness `tools/autotest.py` (Python stdlib only) + game side `AutoTest.cs`: file protocol in `.devlog/autotest/` (command file, `events.jsonl`, heartbeat), in-game restart via `GenCommandLine.Restart()`, watchdog, per-launch log archive, baseline diff.
- **Verified only against a fake game:** `tools/selftest.sh` passes all 13 cases. Never run against real RimWorld.
- `.claude/settings.json` hooks + `tools/context-guard.py` + `tools/handoff.sh`: the context-limit rule. Pipe-tested, and it fired live in this session; this hand-off is its first real use.

## In progress when the session stopped

Nothing half-done. The next step (T0 real runs) was waiting for the user to close their game.

## Next steps

1. Do Session start (`tools/status.sh`). Expect one WARN, "no error baseline", until step 3.
2. **Tell the user their RimWorld must be closed** for the first real runs, and wait for their OK (`tools/autotest.py status` shows "user's game"). Don't pass `--beside-user-game` without their explicit agreement.
3. With their game closed, in order, each as a background Bash command:
   1. `tools/autotest.py baseline` (a run without Quadrunner; waits for the log to go quiet, then kills it)
   2. `tools/autotest.py run` (first real launch, both scenarios). Expect surprises: Unity on Xvfb with software GL, first-launch dialogs (AutoTest closes pause-forcing windows), Steam API outside the Steam launcher, the `-logFile` path
   3. A trivial rebuild, then `tools/autotest.py run` again: must say "restarted in game" (proves the `GenCommandLine.Restart()` path)
   4. `tools/autotest.py hang` must report `HANG`, then `tools/autotest.py stop`
4. After each run: add a row to `docs/test-runs.md` (the harness prints a `row:` line), fix what broke, record gotchas in `TODO.md`, correct `AGENTS.md`'s **The test instance** section to match reality, tick T0 boxes only on proof, commit.
5. Check isolation: the user's `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log` and `Config/` must be untouched by the test runs (compare mtimes before and after, without opening their files more than needed).
6. Last T0 box: ask the user whether to try one run beside their open game (`--beside-user-game`), and record the answer in Decisions.
7. Then T1 (dogs and the stable).

## Decisions and answers from the user this session

All recorded in the plan's Decisions table:

- Testing is fully automated by the agent: develop → test → analyze logs and write notes → develop.
- Notify the user and wait before anything that needs their game closed; never touch their game.
- Restart through RimWorld's built-in restart (`GenCommandLine.Restart()`, as used by the Texture Compression option) in the loop.
- Harness code is Python/JS with no packages; it's Python stdlib only.
- Mod identity confirmed: `josh9817.quadrunner`, namespace `Quadrunner`, `QR_` defNames.
- The 250k-context hand-off rule.

## Waiting on the user

- Closing their RimWorld so the first real runs can start (step 2).

## Gotchas learned this session

All are already in `TODO.md`. The important ones:

- `Root.Shutdown()` empties `Application.temporaryCachePath` (shared with the user's game unless `XDG_CACHE_HOME` is overridden; the harness overrides it).
- `-savedatafolder=` values must not contain `=`.
- Workshop Harmony needs Steam running.
- A `GameComponent` would end up in saves, which is why `AutoTest` is a MonoBehaviour.
- The agent's shell runs in tmux under `xvfb-run` (`DISPLAY=:100`); the user's desktop is `:1`. The hand-off opens a tmux window in the current session.
