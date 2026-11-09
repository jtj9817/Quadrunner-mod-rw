# 0010 — Quadrunner water hose

## Goal

Give worker Quadrunners an integrated water hose to clean clusters of blood and firefoam, following the continuous targeting pattern of Medirunner firefighting (user, 2026-11-05).

## Decisions

| Area | Decision |
| --- | --- |
| Eligible filth | The hose cleans only blood and firefoam, including a mixed patch; it must not remove other filth (user, 2026-11-05). |
| Threshold | Use the hose for a nearby patch of more than three affected tiles, counting distinct tiles rather than filth objects. One- and three-tile patches use normal cleaning. The four-tile boundary is the minimum hose case (user, 2026-11-05; counting the target tile as part of the patch). |
| Continuity | Once a qualifying patch is engaged, continue spraying eligible blood and foam in that area until it is clean, rather than stopping as soon as its remaining count falls to three (user, 2026-11-05). |
| Existing cleaning | Retain the existing Home-area, discovery, reachability and assignment rules and normal cleaning for other filth; the hose is an alternative for qualifying patches, not a replacement for cleaning (existing network contract, plan 0003). |
| Design reference | Reuse the relevant continuous selection and handoff ideas from Medirunner foam response, adapted to cleaning rather than treating filth as fire (user, 2026-11-05). |
| Patch and speed | Eight-neighbor connected, discovered and queueable Home cells, bounded to 12 cells from the first target and 256 cells per job. The hose cleans at 1.5 times normal cleaning work; bounds and rate are XML tunables. |
| Broad application (H2) | Match the Medirunner's `Verb_ArcSpray` band: 5.5-cell half-width, `4 / 2 + 1.5 = 3.5`-cell half-depth, oriented dog-to-aim. Every eligible reserved queue target in the band receives cleaning work per application, with line of sight checked per tile. Per-tile partial layer work is saved; triggered water flecks cover the affected cells. No equipment migration or new Harmony patch. |
| Queue priority (H3) | A blood/firefoam patch that qualifies for the hose ranks in the normal queue directly below Combatdog casualty recovery (plans 0003 and 0007 keep that first) and above every other normal kind, oldest task first within the rank (user, 2026-11-08). Ranking counts discovered, unassigned, mature Home tiles; per-dog reservation and reach stay at assignment. Dirt and patches of one to three tiles keep the ordinary `Clean` rank. Raids still suspend the normal queue, and a running job is not interrupted. The rank is rebuilt each dispatch and never saved. |

## Targets

### H1 — Integrated hose and patch cleaning

- [x] Equip worker Quadrunners with a functioning water hose and visibly spray eligible patches; preserve existing workers and saves.
- [x] One tile of foam and one tile of blood are cleaned normally without hose use.
- [x] A three-tile eligible patch is cleaned normally without hose use.
- [x] A four-tile mixed foam/blood patch starts the hose and is cleaned completely, including the last three tiles; exercise a larger mixed patch as well.
- [x] With no foam or blood, the hose does not start and does not remove unrelated filth.
- [x] Respect Home-area, discovery, reachability, reservations and existing work priorities; leave unrelated filth untouched by the spray.
- [x] Verify the changed path and save/load in the isolated game, run affected shared cleaning/dispatch and firefighting scenarios, and inspect the spray visually. Record results and zero-new-error evidence in `docs/test-runs.md`.
- [x] Build and smoke the actual distributable package with affected scenarios, verify BUILD and modRoot, and record the result and current package in `TODO.md`.

Evidence: affected integration 25/25 PASS with zero new errors (2026-11-06 02:02); dedicated two-worker reservation, fog and Home boundaries 1/1 PASS (02:18). The separate Run all timed out in Artwork showcase with InfoCard errors, so no full-suite pass is claimed. See [test runs](../test-runs.md).

Actual package `b2f6f0e.20261106T021902Z`: affected package smoke 10/10 PASS, zero new errors (2026-11-06 02:20); BUILD and loaded modRoot verified.

Review correction: H1's attempted dirt-first fixture reordered `State.normal`, but dispatch sorted by task ID; the recorded passing runs selected blood first. Those runs prove patch cleanup but not the ordinary-dirt batch fallback. H2 below replaces that ineffective control. Filth also cannot be forbidden with `SetForbidden`; isolate ordinary dirt with a real reservation instead.

### H2 — Review corrections and broad area spray

User request (2026-11-06): fix both scenario defects and enlarge the hose spray AOE to resemble Medirunner firefoam. Run only the affected hose scenarios, including package smoke; no full suite.

- [x] Prove actual dirt-first network assignment through discovery/task order and observe the resulting hose fallback; no list-position trick.
- [x] Replace forbidden-filth no-ops with real dirt isolation; prove the spray leaves dirt alone and ordinary cleaning handles it afterward.
- [x] A hose application physically cleans multiple eligible tiles in a broad band comparable to the Medirunner's native foam spray (half-width 5.5 cells, half-depth 3.5 cells); visual spray covers the cleaning area.
- [x] Preserve one/three/four-plus/no-eligible threshold cases, all layers and last-three continuity, and partial-area progress across save/load.
- [x] Area spray respects queued/discovered ownership, reservations, Home, fog and walls; only blood and foam are affected.
- [x] Affected hose scenarios pass with zero new errors in development and the actual package, with BUILD/modRoot verified and the real spray inspected.

Development evidence: 2026-11-06 06:06, `835eec3-dirty.20261106T060450Z`, eight Hose scenarios only, 8/8 PASS with zero new errors. The scheduler assigns actual older dirt task #11, one application removes 50 cells over a 4×10 coordinate span, and shadow/thickness/band edges and two-worker ownership pass. Native `hose-spray` and `hose-broad-spray` captures inspected.

Package evidence: 2026-11-06 06:09, actual `dist/Quadrunner` build `8f60a1f.20261106T060759Z`, the same eight Hose scenarios 8/8 PASS with zero new errors. BUILD and loaded modRoot verified, actual package spray inspected, private game/Xvfb stopped and release link restored. No other scenarios or full suite run for H2.

### H3 — Patch priority in the normal queue

User request (2026-11-08): put blood and foam patches that are tasks at the top of the queue, with one new scenario and only that scenario run.

- [x] With one worker, the job order is Combatdog recovery, then a newest-discovered qualifying patch, then an older haul, then older dirt and a three-tile blood group, across save/load, zero new errors.
- [x] The same scenario passes from the actual package, with BUILD and modRoot verified.

Development evidence: `Hose: patch priority` failed on the unchanged scheduler with `recover > haul` (2026-11-09 02:37, 0/1) and passed with the fix, `recover > hose > haul > clean` (02:38, 1/1, zero new errors, build `ee8ccbe-dirty.20261109T023751Z`). See [test runs](../test-runs.md).

Known follow-up: `Hose: dirt-first fallback` asserts the old order (older dirt assigned ahead of a newer patch), so it is expected to fail until its fixture is updated. It was neither changed nor run, per the single-scenario request. The batch fallback in `DogWork.JobFor` now fires only when a patch's own tasks are in retry backoff.

## Open questions

- Should `Hose: dirt-first fallback` be rewritten for the new order (for example, by holding the patch's tasks in retry backoff so the dirt still goes first)?
