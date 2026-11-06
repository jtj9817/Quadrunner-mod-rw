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

## Open questions

None.
