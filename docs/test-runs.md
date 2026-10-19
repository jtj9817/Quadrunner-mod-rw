# Test runs

The iteration log: one row per automated test run (`tools/autotest.py run`, which prints a ready-made `row:` line), **newest first**. Workflow in [AGENTS.md](../AGENTS.md#work-loop-per-target-develop--test--analyze-and-note--develop).

- **Build:** the `loaded build` id from the run's log (must match `.devlog/last-build`).
- **Result:** `n/m PASS`, `HANG` (watchdog killed it) or `CRASH`, plus the count of new errors against the baseline.
- **Finding:** what the log showed, in a line. Name the failing step and the first new error.
- **Next:** the change this run leads to, or `tick <boxes>` when it proved them.

The run's full log stays in `.devlog/runs/<timestamp>/` (local, git-ignored); copy a short excerpt into a finding only when it matters later.

| Run (UTC) | Target | Build | Scenarios | Result | Finding | Next |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-19 21:49 | T1 | 0ab90b9-dirty.20261019T214906Z | Stable full (16th dog) | 1/1 PASS, 0 new errors | Overview tab drawn (frame counter went up) with no GUI errors; restarted in game in 22 s | — |
| 2026-10-19 21:48 | T1 | 0ab90b9-dirty.20261019T214611Z | Dock and undock, Assemble and dock, Stable full (16th dog) | 3/3 PASS, 0 new errors | Dog moved 20 cells off walks home and docks across a reload (346 ticks); psychic sensitivity 0, corpse without rot comp, no needs at all, named; Overview rows checked | tick race, stable, assemble, T1 scenarios |
| 2026-10-19 21:45 | T1 | 0ab90b9-dirty.20261019T214326Z | Dock and undock, Assemble and dock, Stable full (16th dog) | 3/3 PASS, 0 new errors | The walk-home step works; needs list empty | add immunity asserts, draw the tab |
| 2026-10-19 21:42 | T1 | 0ab90b9-dirty.20261019T214046Z | Dock and undock, Assemble and dock, Stable full (16th dog) | 3/3 PASS, 0 new errors | First T1 slice (race, stable, dock job, assemble bill) passes first try; a colonist assembled a dog in 5789 ticks. Dock and undock passed in 25 ticks: the dog was dropped on the interaction cell, so docking needed no walk | make the dog walk home; check the Overview rows |
| 2026-10-19 21:27 | T0 | e5bcc76-dirty.20261019T212653Z | Smoke, Save and reload | 2/2 PASS, 0 new errors | Second run on a fresh build: "restarted in game in 21 s" via `GenCommandLine.Restart()`, no cold-start fallback; the old process logged a clean Unity shutdown into its moved log | tick in-game restart |
| 2026-10-19 21:26 | T0 | e5bcc76-dirty.20261019T212551Z | Smoke, Save and reload | 2/2 PASS, 0 new errors | First real launch: Xvfb :90, `-quicktest` map, `ready` in 17 s; save `QR_autotest.rws` written to the test profile, reloaded, steel re-found by ID. User profile mtimes unchanged | tick first launch, Smoke, Save and reload |
| 2026-10-19 21:24 | T0 | (no Quadrunner) | baseline | 8 signatures | Harmony + Core only, killed after 121 s: FMOD (no audio device), breakpad/libcurl minidump upload, Unity allocator stats; the kill leaves a Steam-sockets assert dump in `/tmp/dumps` | tick baseline |
