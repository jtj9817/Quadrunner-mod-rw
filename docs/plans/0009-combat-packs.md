# 0009 — Combatdog packs and Tier 4 arsenal

## Goal

Give existing and new Tier 3/4 Combatdogs faster chassis and local mixed-tier focus fire, and replace Tier 4's selectable gun with paired charge rifles, paired improved chain shotguns and automatic smoke (user, 2026-11-03).

## Decisions

| Area | Decision |
| --- | --- |
| Movement | T3 is 1.5× and T4 1.75× the base chassis movement speed. Preserve the existing deployed 2× multiplier (user clarified). Lower tiers and armor/health remain unchanged. |
| Pack membership | T3 and T4 lead nearby mixed-tier packs; T1/T2 may join. Distant packs remain independent. Prefer nearby Combatdogs and the closest reachable standing hostile (user clarified). Coordination radius: 15 cells, XML tuning. |
| Orders | Do not override drafted/player-forced orders, charging, incapacitation or capture already in progress. A downed target stops being a focus-fire target and remains eligible for the existing single-carrier capture path. |
| T4 arsenal | All five weapons fitted, no selectable legacy T4 loadout. Both charge rifles fire together at range; both modified chain shotguns fire together close up. Smoke launches automatically in combat (user clarified). |
| Gun tuning | Charge rifles retain Core charge-rifle projectile/burst/range/cycle. Modified chain shotguns retain Core projectile, three-shot burst and cycle, extend range from 12.9 to 20.9 cells and use touch/short/medium/long accuracy 0.80/0.85/0.75/0.60. Each paired shot launches two real projectiles. |
| Smoke | Core smoke-launcher projectile and smoke effects; supplemental to the damaging weapons, with a 900-tick interval controlled in XML. The integrated launcher has zero warmup and a non-interrupting verb, so it never consumes the primary gun's stance. |
| Persistence | Update existing pawns in place, including held/world dogs; retain identity, name, health, battery, affiliation and cargo. Old T4 weapon choices no longer control the loadout. Keep the retired minigun Def/art only for safe deserialization of old equipment and battle-history records, not as an available weapon. New equipment state must survive another reload. |
| Verification | Focused real-game T3/T4 pack fights, actual projectiles/damage/smoke, target transitions, movement, save migration and affected combat lifecycle. No new unit-test suite and no unrelated full-suite run (user). Scoped actual-package smoke before delivery. |
| Delivery | Incremental commits after self-contained verified changes; package from committed source; keep generated build/package output untracked. |
| Engagement continuity | User-reported return trips (2026-11-04): reuse the saved hostile queue, select the next reachable focus at the native job-request boundary, and rank proximity from any eligible pack member rather than just its leader. Preserve valid focus and independent distant groups. No influence-map, spatial-index or planning framework is needed for this handoff fix. |
| Capture priority | While standing hostiles remain, reserve non-leading dogs for captures; T3/T4 leaders keep fighting. After standing threats clear, leaders may capture normally. Never interrupt an already-started capture or retreat. |

## Targets

### C1 — Local focus-fire packs and movement

- [x] T3/T4 move at the requested chassis multipliers, with the existing deployed boost preserved.
- [x] A T3-led mixed pack fights the closest enemy together, then retargets when it falls; a distant pack retains a separate target.
- [x] Drafted orders, low-battery retreat and the single-carrier capture path remain effective.

### C2 — Tier 4 full arsenal

- [x] A T4-led pack fires paired charge-rifle projectiles at range and paired improved shotgun projectiles close up, causing actual enemy damage.
- [x] Automatic launched smoke creates real smoke during combat without replacing sustained damaging fire.
- [x] Actual game rendering shows the paired weapons; no built-in weapons drop as loot.

Evidence for C1/C2: scoped real-game run 2026-11-03 10:16, `f780354-dirty.20261103T101554Z`: both pack fights PASS, zero new errors. The third scenario (migration) failed its dock-holder staging check; no migration pass is claimed from this run. Actual smoke/arsenal rendering `.devlog/screens/combat-t4-pack-smoke.png` inspected. See the [iteration log](../test-runs.md).

### C3 — Existing saves and delivery

- [x] Legacy T3/T4 dogs gain the changes in place, preserving saved identity/state, including docked and off-map examples.
- [x] Migrated dogs fight together after reload, then save/load again without duplicate or stale equipment.
- [x] Scoped development and actual-package scenarios pass with zero new errors; records identify the exercised coverage and package build.

Migration evidence: 2026-11-03 10:19, `f780354-dirty.20261103T101829Z`, **Combat tier legacy migration 1/1 PASS**, zero new errors. Eight legacy dogs retain identity/state across docked, deployed, carried and caravan holders; a second load keeps exact primary/smoke weapon IDs, then migrated T3/T4 dogs fight a three-raider pack together. The harness cold-started after an in-game restart crash before ready.

Final development evidence: 2026-11-03 10:29 **3/3 PASS**, zero new errors, on `c46562c-dirty.20261103T102828Z`; includes distant packs independently choosing a shared nearest enemy, capture completion without idle dispatch churn, and armed T4 combat with simultaneous-shot observations. Final scenario refinement 10:31 on `c46562c-dirty.20261103T103058Z` passed **T4 pack arsenal → Combat tier legacy migration 2/2**, zero new errors, with an additional save/load during the migrated pack's active fight and no stale-verb/discarded-pawn warnings.

Package evidence: 2026-11-03 10:33, actual `dist/Quadrunner` build `4248756.20261103T103230Z`, **Tier 3 pack combat → Tier 4 pack arsenal → Combat tier legacy migration 3/3 PASS**, zero new errors. Loaded build and package mod root verified; packaged armed-combat/smoke render inspected. Private instance/Xvfb stopped and development symlink restored. Only focused combat/migration cases were run; no unrelated full-suite, unit-test suite or independent removal rerun.

### C4 — Continuous engagement and nearest-member targeting

- [x] A target dying immediately after a dispatch refresh never sends the pack toward its stable while other reachable standing enemies are queued; observe native job starts between frames.
- [x] A T2 follower's nearer reachable enemy determines its T4-led pack's new focus; valid focus is retained until invalidated.
- [x] T3/T4 cast-position travel retains combat intent across reload, replaces stale pursuit orders when targets go down, and keeps leaders fighting despite multiple waiting captures.
- [x] Survivors are captured and dogs dock once standing threats and capture work clear; draft/retreat and existing combat scenarios still pass.
- [x] Record development and actual-package evidence with zero new errors, package from committed source and commit the verification records.

Development evidence: unchanged gameplay failed both new regressions at 08:32 (zero new errors): the native trace records a homeward guard job at +4 ticks with two standing enemies already queued, and leader-only proximity ignores a nearer T2 follower. Corrected affected combat sequence at 08:42, `3f4b439-dirty.20261104T083925Z`, passed **10/10**, zero new errors, including movement-intent reload, same-tick pursuit replacement, multiple downed targets, capture completion/docking, draft/retreat, distant packs, weapons and legacy migration. The final nearest-member case at 08:44, `3f4b439-dirty.20261104T084355Z`, passed **1/1**, zero new errors, additionally proving a closer newcomer preserves valid focus and both active firing jobs. See the [iteration log](../test-runs.md).

Package evidence: 2026-11-04 08:51, actual `dist/Quadrunner` build `1ebcbc4.20261104T084614Z`, **10/10 affected combat scenarios PASS**, zero new errors. Loaded BUILD and resolved package modRoot verified; both dogs pursue the next queued enemy at +2 ticks without a homeward order. The final closer-newcomer focus-retention check, mid-pursuit reload/invalidation, capture pressure/completion/docking, draft/retreat, T3/T4 arsenal and legacy migration all pass. Native packaged combat rendering inspected. The old DLL was FUSE-held by the user's running game: its leftover directory was preserved in `.devlog`, then the already-built/linted stage promoted without closing/signalling that game. Private game/Xvfb stopped; original installed-release link restored. No unrelated full-suite or independent removal rerun.

## Open questions

None. The user clarified speed baseline, full simultaneous paired arsenal and mixed-tier pack leadership before implementation.
