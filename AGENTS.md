# AGENTS.md

Instructions for any AI agent working on this repo. Claude Code also reads [CLAUDE.md](CLAUDE.md), which imports this file.

**Primary resource:** [RimWorld Wiki: Modding Tutorials](https://rimworldwiki.com/wiki/Modding_Tutorials). After the wiki, check the decompiled game source and Core Defs (`tools/vanilla.sh`); never rely on memory of the API. The wiki now describes 1.7, and this mod targets 1.6 (see the TODO.md gotchas).

**Offline wiki mirror:** `/mnt/0B8533211952FCF2/rimworld-wiki-local/data/site/pages/` holds the whole wiki as Markdown (modding pages under `Modding Tutorials/`). Grep it instead of fetching the web, e.g. `grep -rl "DebugAction" "/mnt/0B8533211952FCF2/rimworld-wiki-local/data/site/pages/Modding Tutorials"`.

**Prior art:** [Queen Bee](/mnt/0B8533211952FCF2/queen-bee-rw-mod) (`josh9817.queenbee`), the user's previous RimWorld 1.6 mod, built with the same tools. Its `Source/QueenBee/Scenario.cs` and `DebugActions.cs` are the model for this mod's scenario runner. Its lessons are already folded into `TODO.md`.

## What this is

A RimWorld 1.6 mod (Core only, Harmony the only dependency). Robot dogs dispatched from a stable haul, clean, supply construction, carry riders, rescue the downed (Medirunner) and defend the colony (Combatdogs, tiers 1–4). The design is [`docs/design.md`](docs/design.md) (a snapshot of the living doc); the plan with every decision and target is [`docs/plans/0001-quadrunner.md`](docs/plans/0001-quadrunner.md). Don't re-open a decision that's recorded there without asking the user.

## Roles

- **The agent** writes code and assets, builds, **runs the tests itself** in an isolated game instance, reads the log, writes the notes and iterates. No in-game step is handed to the user.
- **The user** settles design questions and plays their own game, often while the agent works. The user's game is off limits:
  - **Never** close, kill, focus, screenshot or send input to the user's game, and never touch their profile (`~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios`: Saves, Config, Player.log).
  - **Test runs go ahead while the user's game is open** (proven on 2026-10-19, run 23:20; the user made it the default). `tools/autotest.py` only notes the open game. `tools/status.sh` reports whether it runs.
  - **Before any other step that needs the user's game closed** (e.g. `tools/backup-saves.sh`), **notify the user and wait** until they say it is closed. Say it in one plain line; when they may be away, also send whatever notification your tool offers.
- **Commands only the user can run** (a global git config change, an interactive login): ask them to run it and show the exact command; never work around it.

## Session start

1. If you were started from a hand-off doc (`docs/handoff/`), read it first.
2. Read [`TODO.md`](TODO.md): progress, current state, open questions, and **agent notes / gotchas**.
3. Read the plan's section for the current target, and the newest entries in [`docs/test-runs.md`](docs/test-runs.md).
4. Run `git -c safe.directory="$PWD" log --oneline -10` to see where the last session stopped.
5. Run `tools/status.sh` and stop to tell the user if the environment check fails, above all if the game version changed.

## Context limit and hand-off (this project only)

At **250k tokens of context** a session stops working and hands off to a fresh one (user, 2026-10-19). It is enforced by the project hooks in `.claude/settings.json`, which run `tools/context-guard.py` after every tool call, at every Stop and on every prompt:

1. Past the limit, the hook tells the session to stop at the next safe point (start nothing new; end a running test with `tools/autotest.py stop` if needed).
2. Commit finished work, then write `docs/handoff/YYYYMMDD-HHMM-<slug>.md` from [`docs/handoff/TEMPLATE.md`](docs/handoff/TEMPLATE.md), complete enough for an instance that remembers nothing, and commit it.
3. Run `tools/handoff.sh --session <session id> docs/handoff/<file>` (the hook's message gives the id). It arranges a **new interactive** Claude Code instance on the doc. In tmux it **replaces the old session in the same pane**: once the old session ends its turn, the hook runs `tmux respawn-pane -k`, which terminates it and starts the new instance there (user, 2026-10-20). Not in tmux: an Alacritty window on the user's display, and the hook closes the old session at its Stop.
4. End the turn with one line saying where the new instance runs. The hook keeps the old session from ending its turn before step 3, tells it to stop once the hand-off is launched, and ends it at that turn's Stop.

A hand-off doc is also written when the user asks for one (see [Work loop](#work-loop-per-target-develop--test--analyze-and-note--develop)); then the session stays open and nothing is launched. A session started from a hand-off doc reads that doc first, then does [Session start](#session-start). Limit and switches: `QR_HANDOFF_TOKENS` (default 250000), `QR_HANDOFF_DISABLE=1` or the file `.devlog/handoff/disabled`.

## How RimWorld loads a mod (why the loop looks like this)

At startup the game loads assemblies, then parses, inherits, patches and resolves Defs, then loads textures, then runs `[StaticConstructorOnStartup]` classes (see the wiki's *Application Startup* page). **Assemblies never reload while the game runs**, so every new build needs a new game process. The test instance stays up between iterations and is **restarted from inside the game** with `GenCommandLine.Restart()`, the same call the Options dialog makes after Texture Compression is toggled: it relaunches the game with the same command line, then shuts the old process down. Restarts are cheap here: the instance loads only Core, Harmony and Quadrunner, and `-quicktest` skips the main menu and generates a small map. They still cost time, so batch every change for a run, and lint and build them all first.

## The test instance

Every automated run uses a **separate game profile**, never the user's:

| Piece | How |
| --- | --- |
| Profile | `-savedatafolder=.devlog/rwdata` with `Config/ModsConfig.xml` (Harmony, Core, Quadrunner) and `Config/Prefs.xml` (Dev mode on, sound off, 640×360 windowed (`QR_SCREEN`), no autosave, no auto-pause, `runInBackground`), both rewritten on every launch; its own saves |
| Isolation | `-logFile .devlog/rwdata/Player.log`, plus `XDG_CONFIG_HOME`/`XDG_CACHE_HOME` under `.devlog/xdg/`, so Unity's prefs, cache and temp folder (which `Root.Shutdown` empties) are never the user's |
| Map | `-quicktest`: fresh Crashlanded map, no menu; 200×200 (`QR_MAP_SIZE`, `-quadrunner-mapsize`). The existing test-only setup patch selects flat, low-swampiness, inland temperate forest without rivers, retaining a random world seed; fixtures build their own obstacles. Normal games are unaffected. A size change needs a cold start (`autotest.py stop`) |
| Display | A private Xvfb display (`:90` and up), so the instance never takes the user's screen or input. It renders in software (llvmpipe), capped at 4 threads (`QR_RENDER_THREADS`, as `LP_NUM_THREADS`): at 1280×720 on every core it took ~1800% CPU beside the user's game for the same speed |
| Control | Launch argument `-quadrunner-autotest=.devlog/autotest`. `AutoTest.cs` (a MonoBehaviour, so never in a save) touches `heartbeat` every second, appends JSON lines to `events.jsonl` (`launched`, `ready`, `scenario`, `result`, `restarting`, `quitting`, `closed_window`), and runs one command at a time from the `command` file: `run [filter]`, `restart`, `quit`, `ping`, `hang`. It also closes any window that would pause the game |
| Restart | `autotest.py` moves the live log to `.devlog/runs/<launch>/Player.log` (the old process keeps writing into the moved file), then sends `restart`; the game calls `GenCommandLine.Restart()` and the new process logs to a fresh file. If no new instance comes up, it kills and cold-starts |
| Watchdog | `HANG` when the watched launch's heartbeat is older than `--hang-after` (120 s), `CRASH` when no instance runs for 30 s, `TIMEOUT` after `--timeout`; the instance is killed and its log archived. `autotest.py hang` proves it |
| Mod install | `Mods/Quadrunner` is a symlink to this repo, created on first launch; the user's own profile doesn't list the mod, so their game never loads it |

Built in T0 and proven against a fake game that speaks the same protocol (`tools/selftest.sh`: baseline, cold start, in-game restart on a new build, filters, failures, baseline-hidden and new errors, the hang watchdog, stop). **Proven against the real game on 2026-10-19** (see `docs/test-runs.md`): the baseline (8 signatures, Harmony + Core), the first launch on Xvfb :90 (`ready` 17 s after launch), both scenarios with a real save and load, the in-game restart onto a new build (21 s, no cold-start fallback) and the hang watchdog. Each launch was made directly, not through Steam, while Steam ran; workshop Harmony loaded and the user's profile stayed untouched (same mtimes before and after). **Beside the user's open game** (run 2026-10-19 23:20): Run all 11/11 PASS, `ready` in 19 s, and nothing in the user's `Config/` or `Saves/` changed during the run; the only writes came from their own game, before the launch.

## Work loop (per target): develop → test → analyze and note → develop

The agent runs this loop on its own, without waiting on the user between iterations:

1. **Develop** the smallest slice that moves an acceptance box. Add or extend the scenario that proves it (`Source/Quadrunner/Scenarios.cs`, in **Run all** order) in the same slice.
2. **Build and test in one command:** `tools/autotest.py run [filter]`. It runs `tools/build.sh` (lint plus build; zero warnings, the csproj treats warnings as errors), restarts the test instance in game when the loaded build is stale (or starts it), sends `run`, waits under the watchdog, and refuses to report results from a build other than `.devlog/last-build`. `filter` is comma-separated parts of scenario names. Never deploy with a bare `dotnet build`, which only writes to `Source/Quadrunner/bin/`.
   - **Run only the scenarios the change can affect** (one target's code → that target's scenarios; a scenario-only change → that scenario). The test instance shares the CPU with the user's game, and the user called rerunning everything after an unrelated change a waste (2026-10-20). **Run all** only when shared code changed (runner, dispatch, the stable tick, combat, races) or when ticking a target's last boxes needs proof that its scenarios pass in sequence; say why in one line. Options go before the subcommand: `tools/autotest.py --timeout 3600 run`.
   - A run takes from seconds to several minutes: start it in the background if your tool allows, and wait for it to exit instead of polling its output.
3. **Analyze:** the output lists each `SCENARIO` verdict, every new error block (red errors diffed against the baseline of a run without Quadrunner; any `[Quadrunner] ERROR` always counts), and a `RESULT:` line. Exit `0` means every scenario passed with **zero new errors**; `1` a failure, `2` hang/crash (instance killed), `3` not run. When a result surprises you, read the raw log (`.devlog/rwdata/Player.log` while the launch lives, `.devlog/runs/<launch>/Player.log` after), not only the filtered view. `tools/screenshot.sh` captures the test instance's window when a visual needs checking (draw offsets, sirens, overlays).
4. **Leave the instance running** between iterations; the next `run` restarts it in game. `tools/autotest.py stop` at the end of a session.
5. **Note:** add one row to [`docs/test-runs.md`](docs/test-runs.md) (newest first): build id, scenarios run, result, what the log showed, what changes next. Anything that took more than one attempt also becomes a one- or two-line gotcha in `TODO.md`.
6. Tick the acceptance boxes the run proved, update `TODO.md` (progress + current state) and **commit** (see [Commits](#commits)). Then back to 1.

Stop the loop and tell the user when:

- every acceptance box of the target is ticked (report, then start the next target unless told otherwise),
- a decision the plan doesn't record is needed (ask; record the answer in the plan's Decisions table in the same commit),
- the same scenario has failed three runs in a row without new information (report what was tried and the log evidence),
- a step needs the user's game closed (see [Roles](#roles)),
- the user asked you to stand by after the current task: finish and verify it, commit, write a hand-off doc from [`docs/handoff/TEMPLATE.md`](docs/handoff/TEMPLATE.md) and commit it, run `tools/autotest.py stop`, then wait for orders. Start nothing else, not even the next target, and don't run `tools/handoff.sh` unless they ask.

## Tracking progress

- **`TODO.md`** holds status: target checkboxes, "Current state" (next step, last run), open questions, and agent notes.
- **The plan** holds the acceptance boxes. Tick them only when an automated run proved them (a `SCENARIO … PASS` line, a clean save/load, zero new errors), and name the run in `docs/test-runs.md`. Never tick based on "should work".
- **`docs/test-runs.md`** is the iteration log: one row per test run, newest first.
- New scope gets a new plan, `docs/plans/NNNN-<slug>.md`, in the same GFM structure: Goal, Decisions, Targets with acceptance boxes, Open questions. No timelines.

## Tools

Every helper script lives in `tools/`. Rules:

- Shell tools: Bash, `#!/usr/bin/env bash` + `set -euo pipefail`, executable. `-h` prints usage; scripts with subcommands also print it when run without one.
- Python: the harness and parsers (XML, binary, logs). Python 3 **standard library only**, run with `python3 -I`; no third-party packages, no pip, no Bun packages: write the code yourself (user, 2026-10-19).
- Source `tools/env.sh` for every shared path and helper (`REPO`, `RW_DIR`, `RW_DATA`, `RW_LOG`, `DEVLOG`, `git_`, `test_pid`, `user_game_pid`, `die`). Never redefine them in a script. `tools/autotest.py` reads the same variables with the same defaults (`QR_DEVLOG`, `RW_DIR`, `RW_DATA`, `HARMONY_DIR`); change both together.
- `RW_DATA` and `RW_LOG` point at the **test instance's** profile (`.devlog/rwdata`), never the user's.
- Prefer plain files over daemons and sockets: the harness and the game share only the files in `.devlog/autotest/`.
- Generated or local state goes in `.devlog/` (runs, baseline, test profile, screenshots, selftest) or `.tools/` (decompiler, decompiled source). Both are git-ignored.
- After changing `tools/autotest.py` or `AutoTest.cs`'s protocol, run `tools/selftest.sh` (and extend `tools/selftest/fake_game.py` to match).
- **Register every tool in the table below in the same commit that adds it.**

| Tool | Purpose | Status |
| --- | --- | --- |
| `tools/env.sh` | Shared paths and helpers, sourced by every shell script | done (T0) |
| `tools/autotest.py` | The harness. `run [filter]` (lint/build/deploy, restart, scenarios, error diff) / `pkg-run [filter]` (cold-launch `QR_PKG`, validate BUILD and ready-event modRoot, restore dev symlink on pass/failure) / `removal-check [--save QR_removal]` (independent observer, no Quadrunner assembly, load/tick/save/reload/tick and new-error check) / `start` / `restart` / `stop` / `status` / `baseline` / `errors [log]` / `follow` / `hang`. Uses only the private profile and Xvfb | T0 proven; T9 package/removal fake-game coverage proven, real release checks in docs/test-runs.md |
| `tools/selftest.sh` + `tools/selftest/` | Runs the harness against `fake_game.py`: baseline, launch/restart, filters, failures, error diff, hang/stop, package source/build and dev-link restoration, removal observer verdict/errors/load failure/cleanup | 19 cases PASS (T9) |
| `Source/Quadrunner/AutoTest.cs` | Game-side MonoBehaviour driver and heartbeat/event/command protocol; ready includes loaded build and actual modRoot; also drives Scenario every frame | T0 and T9 proven in real runs |
| `Source/Quadrunner/Scenario.cs` + `Scenarios*.cs` | `Do` / `Expect` / `Wait` / `SaveAndReload`, tracked ThingIDs, filtered Run all, storyteller disabled during scenarios. T0/T1 in Scenarios.cs; charging, dispatch, retrieval, rescue, riding, combat and tiers in matching partials; T9 controlled endurance, real artwork captures and removal export | proven per-scenario runs in docs/test-runs.md |
| `tools/removal-probe/` | Independent test-only mod, never packaged, with no Quadrunner reference or saved component. Verifies absent assembly, surviving map/caravan contents and battle-history IDs/text, 600 ticks, save/reload and another 600 ticks; writes a verdict and screenshot for removal-check | real load/history/render proof PASS (T9); evidence in docs/test-runs.md |
| `Source/Quadrunner/DebugActions.cs` | **Debug actions → Quadrunner**: Scenario: Run all, Scenario… (pick one), Toggle scenario speed, Spawn Quadrunner docked, Undock all, Set battery %…, Power down, Force dispatch, Highlight charge spots, Dump charge-spot cache | done (T0) |
| `tools/context-guard.py` | The context-limit hook (see [Context limit and hand-off](#context-limit-and-hand-off-this-project-only)): reads the session transcript's latest main-thread usage, blocks with hand-off instructions past `QR_HANDOFF_TOKENS`, ends the old session at its last Stop (the respawn `handoff.sh` recorded, or SIGTERM to the nearest `claude` ancestor); per-session state in `.devlog/handoff/` | done |
| `tools/handoff.sh` | `[--session <id>] [--dry-run] <doc>`: a new interactive Claude Code instance that continues from a committed hand-off doc, in place of the old one: in tmux the same pane (`tmux respawn-pane -k`, run by the hook at the old session's Stop; right away without `--session`), else Alacritty on `$QR_HANDOFF_DISPLAY` (default `:1`) with the old session closed at its Stop; extra `claude` args from `$QR_HANDOFF_CLAUDE_ARGS` | done; same-pane respawn proven on a separate tmux server (2026-10-20) |
| `tools/statusline.py` | Claude Code status line: appends the status-line JSON's `rate_limits` to `.devlog/usage.jsonl` (`--out` / `$QR_USAGE_LOG`) whenever they change, prints the user's usual `user@host:cwd` | done, not wired into settings yet (needs the user's OK) |
| `tools/vanilla.sh` | `init` (decompile Assembly-CSharp once into `.tools/decomp/`) / `grep <regex>` (code + Core Defs) / `def <defName>` (vanilla def plus its ParentName chain) | done (T0, from Queen Bee) |
| `tools/rwdefs.py` | Python behind `vanilla.sh def` and the lint step of `build.sh`: XML parsing, def references (implied `Corpse_<race>` defs included), `Quadrunner/` texture paths (exact case), PNG sizes, preview size | done (T0, from Queen Bee) |
| `tools/build.sh` | Lint, then `dotnet build` stamped with a build id, installed into `1.6/Assemblies` with an atomic rename. The id goes to `.devlog/last-build` | done (T0) |
| `tools/status.sh` | Environment check (game version, toolchain, Steam, Harmony, mod symlink, decomp, baseline), then `autotest.py status` (test instance, its build, the user's game) | done (T0) |
| `tools/screenshot.sh` | Captures only the test instance's exact RimWorldLinux window class on its own Xvfb display into `.devlog/screens/`; never the user's game or desktop | proven and image inspected (T9) |
| `tools/export-textures.sh` | Exports original `Art/**/*.svg` to PNG at mirrored paths with Inkscape; `id="part-<name>"` elements become separate layers | used for all T9 artwork; sources and actual game rendering inspected |
| `tools/backup-saves.sh` | Archives the **user's** profile (Saves, Config; logs excluded) to `~/RimWorldBackups/<stamp>.tar.gz`, read-only, and refuses while their game runs. Run it whenever the user asks, and before anything that could reach their profile | done (ported from Queen Bee; first backup 2026-10-19, 44 saves) |
| `tools/package.sh` | From a clean tree: build, then copy only About/Defs/Textures/1.6 (and Patches/Languages/Sounds) into `dist/Quadrunner` with a `BUILD` stamp, and lint the copy | T9 package built and linted; actual packaged-game results in docs/test-runs.md |

Not ported: `install.sh release|dev`, because tests never touch the user's profile. Bring it back if the user ever enables Quadrunner in a real colony.

## Code conventions

- KISS and DRY. Add no abstraction, manager, interface or settings class the plan doesn't call for. Reuse vanilla systems first.
- Harmony is allowed only when vanilla has no virtual or hook for the job. Note why in the plan when you add it; the design budgets about six patches.
- C# namespace `Quadrunner`; defNames prefixed `QR_`; log via `QR.Log(...)` only (dev mode only, `[Quadrunner]` prefix).
- Tunables go in XML (a `DefModExtension` or Def fields), not C# constants, unless the plan fixes the value.
- Every new field on a saved object gets a `Scribe_*` line in `ExposeData`, and every scenario that changes saved state includes a save/load step.
- Never touch DLC systems (`ModsConfig.BiotechActive` etc.): only Core is installed.
- Textures: edit the SVG in `Art/`, re-export, and commit both.

## Commits

- Commit after every verified iteration or self-contained change. Small commits; never leave work uncommitted at the end of a session.
- Run git as `git -c safe.directory="$PWD" ...` (the mount is root-owned).
- Imperative subject line, prefixed with the target when there is one, e.g. `T2: Conduit charge-spot search`.
- Never commit build output (`1.6/Assemblies/`), `.devlog/` or `.tools/`.
- Don't push unless the user asks.
