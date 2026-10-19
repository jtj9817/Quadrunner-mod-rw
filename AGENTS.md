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
  - **Before any step that needs the user's game closed, notify the user and wait** until they say it is closed. Until T0 proves that a test instance can run beside the user's game, that means before every test run. `tools/status.sh` reports whether the user's game is running.

## Session start

1. Read [`TODO.md`](TODO.md): progress, current state, open questions, and **agent notes / gotchas**.
2. Read the plan's section for the current target, and the newest entries in [`docs/test-runs.md`](docs/test-runs.md).
3. Run `git -c safe.directory="$PWD" log --oneline -10` to see where the last session stopped.
4. Run `tools/status.sh` and stop to tell the user if the environment check fails, above all if the game version changed.

## How RimWorld loads a mod (why the loop looks like this)

At startup the game loads assemblies, then parses, inherits, patches and resolves Defs, then loads textures, then runs `[StaticConstructorOnStartup]` classes (see the wiki's *Application Startup* page). **Assemblies never reload while the game runs**, so every test run is a fresh launch of the test instance. Launches are cheap here: the test instance loads only Core, Harmony and Quadrunner, and `-quicktest` skips the main menu and generates a small map. They still cost a minute or more each, so batch every change for a run, and lint and build them all first.

## The test instance

Every automated run uses a **separate game profile**, never the user's:

| Piece | How |
| --- | --- |
| Profile | `-savedatafolder=<repo>/.devlog/rwdata` (git-ignored): its own `Config/ModsConfig.xml` (Core, Harmony, Quadrunner only), `Config/Prefs.xml` (Dev mode on, sound off, windowed), saves and `Player.log` |
| Map | `-quicktest`: straight into a freshly generated small map, no menu |
| Display | A private Xvfb display by default, so the run never takes the user's screen or input |
| Driver | A custom launch argument (`-quadrunner-autotest`, optionally `=<scenario filter>`) makes the mod's scenario runner start every scenario once the map is ready, log one `SCENARIO <name>: PASS/FAIL` line each and a summary, then quit the game |
| Watchdog | `tools/autotest.sh` kills the test instance after a wall-clock timeout and records the run as a hang |
| Mod install | `Mods/Quadrunner` is a symlink to this repo (dev mode); the user's own profile doesn't list the mod, so their game never loads it |

The whole harness is target T0 in the plan and is **not built yet**. Its flags and file names above are the plan, not facts; correct this section as T0 verifies each piece.

## Work loop (per target): develop → test → analyze and note → develop

The agent runs this loop on its own, without waiting on the user between iterations:

1. **Develop** the smallest slice that moves an acceptance box. Add or extend the scenario that proves it (in `Scenario` runs, inside **Run all**) in the same slice.
2. **Build:** `tools/build.sh` (lint plus build; zero warnings, the csproj treats warnings as errors; zero lint failures). Never deploy with a bare `dotnet build`, which only writes to `Source/Quadrunner/bin/`.
3. **Test:** `tools/autotest.sh [filter]` launches the test instance, waits for the summary line, quits it, and keeps the run's log in `.devlog/runs/<timestamp>/`. Check that its `loaded build` id matches `.devlog/last-build` before trusting any result.
4. **Analyze:** read the run's `SCENARIO` lines and `tools/qrlog.sh errors` (red errors diffed against the baseline of a vanilla run without Quadrunner). Every scenario must pass with **zero new errors**, and every save/load step must come back clean. When a result surprises you, read the raw `Player.log` of that run, not only the filtered view. `tools/screenshot.sh` captures the test instance's window when a visual needs checking (draw offsets, sirens, overlays).
5. **Note:** add one row to [`docs/test-runs.md`](docs/test-runs.md) (newest first): build id, scenarios run, result, what the log showed, what changes next. Anything that took more than one attempt also becomes a one- or two-line gotcha in `TODO.md`.
6. Tick the acceptance boxes the run proved, update `TODO.md` (progress + current state) and **commit** (see [Commits](#commits)). Then back to 1.

Stop the loop and tell the user when:

- every acceptance box of the target is ticked (report, then start the next target unless told otherwise),
- a decision the plan doesn't record is needed (ask; record the answer in the plan's Decisions table in the same commit),
- the same scenario has failed three runs in a row without new information (report what was tried and the log evidence),
- a test run needs the user's game closed (see [Roles](#roles)).

## Tracking progress

- **`TODO.md`** holds status: target checkboxes, "Current state" (next step, last run), open questions, and agent notes.
- **The plan** holds the acceptance boxes. Tick them only when an automated run proved them (a `SCENARIO … PASS` line, a clean save/load, zero new errors), and name the run in `docs/test-runs.md`. Never tick based on "should work".
- **`docs/test-runs.md`** is the iteration log: one row per test run, newest first.
- New scope gets a new plan, `docs/plans/NNNN-<slug>.md`, in the same GFM structure: Goal, Decisions, Targets with acceptance boxes, Open questions. No timelines.

## Tools

Every helper script lives in `tools/`. Rules:

- Bash, `#!/usr/bin/env bash` + `set -euo pipefail`, executable. `-h` prints usage; scripts with subcommands also print it when run without one. Use Python 3 (stdlib only, run with `python3 -I`) only for parsing XML or binary files.
- Source `tools/env.sh` for every shared path and helper (`REPO`, `RW_DIR`, `RW_DATA`, `RW_LOG`, `DEVLOG`, `git_`, `die`). Never redefine them in a script.
- `RW_DATA` and `RW_LOG` point at the **test instance's** profile (`.devlog/rwdata`), never the user's.
- Prefer plain pipes and files over daemons. A marker file is the only shared state between log commands.
- Generated or local state goes in `.devlog/` (runs, marks, baseline, test profile, screenshots) or `.tools/` (decompiler, decompiled source). Both are git-ignored.
- **Register every tool in the table below in the same commit that adds it.**

Ported from Queen Bee (`tools/qblog.*` becomes `qrlog.*`) and adapted to the test instance in T0; nothing is in `tools/` yet:

| Tool | Purpose | Status |
| --- | --- | --- |
| `tools/env.sh` | Shared paths and helpers, sourced by every script | planned (T0) |
| `tools/qrlog.sh` + `tools/qrlog.awk` | `follow` / `mark <label>` / `since-mark` / `baseline` / `errors` on the test instance's Player.log, with whole stack traces and `[Ref …]` duplicates resolved. `QRLOG_ALL=1` shows baseline noise too | planned (T0) |
| `tools/vanilla.sh` | `init` (decompile Assembly-CSharp once into `.tools/decomp/`) / `grep <regex>` (code + Core Defs) / `def <defName>` (vanilla def plus its ParentName chain) | planned (T0) |
| `tools/rwdefs.py` | Python behind `vanilla.sh def` and the lint step of `build.sh`: XML parsing, def references, `Quadrunner/` texture paths (exact case), PNG sizes, preview size | planned (T0) |
| `tools/build.sh` | Lint, then `dotnet build` stamped with a build id, installed into `1.6/Assemblies` with an atomic rename. The id goes to `.devlog/last-build` | planned (T0) |
| `tools/status.sh` | Environment check (game version, toolchain, Steam, Harmony, mod symlink, decomp, baseline) plus live state (user's game running?, test instance running?, last build, last run) | planned (T0) |
| `tools/autotest.sh` | New. Prepares the test profile, launches the test instance on Xvfb with `-quicktest` and the autotest argument, waits for the summary or the timeout, quits or kills it, saves the run to `.devlog/runs/<timestamp>/`, prints the `SCENARIO` lines and new errors. Exit `0` all pass, `1` a failure, `2` hang or crash | planned (T0) |
| `tools/screenshot.sh` | Captures only the test instance's window to `.devlog/screens/`; never the user's game or desktop | planned (T0) |
| `tools/export-textures.sh` | Exports `Art/**/*.svg` to PNG at the mirrored path via Inkscape; `id="part-<name>"` elements become separate layers | planned (when art starts) |
| `tools/package.sh` | From a clean tree: build, then copy only About/Defs/Textures/1.6 (and Patches/Languages/Sounds) into `dist/Quadrunner` with a `BUILD` stamp, and lint the copy | planned (release) |

Not ported: `backup-saves.sh` and `install.sh release|dev`, because tests never touch the user's profile. Bring them back if the user ever enables Quadrunner in a real colony.

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
