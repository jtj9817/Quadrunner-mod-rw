# Test runs

The iteration log: one row per automated test run (`tools/autotest.sh`), **newest first**. Workflow in [AGENTS.md](../AGENTS.md#work-loop-per-target-develop--test--analyze-and-note--develop).

- **Build:** the `loaded build` id from the run's log (must match `.devlog/last-build`).
- **Result:** `n/m PASS`, `HANG` (watchdog killed it) or `CRASH`, plus the count of new errors against the baseline.
- **Finding:** what the log showed, in a line. Name the failing step and the first new error.
- **Next:** the change this run leads to, or `tick <boxes>` when it proved them.

The run's full log stays in `.devlog/runs/<timestamp>/` (local, git-ignored); copy a short excerpt into a finding only when it matters later.

| Run (UTC) | Target | Build | Scenarios | Result | Finding | Next |
| --- | --- | --- | --- | --- | --- | --- |
