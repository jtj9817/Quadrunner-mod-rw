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
| Smoke | Core smoke-launcher projectile and smoke effects; supplemental to the damaging weapons, with a 900-tick interval controlled in XML. |
| Persistence | Update existing pawns in place, including held/world dogs; retain identity, name, health, battery, affiliation and cargo. Old T4 weapon choices no longer control the loadout. Preserve safe loading of old serialized built-in weapons. New equipment state must survive another reload. |
| Verification | Focused real-game T3/T4 pack fights, actual projectiles/damage/smoke, target transitions, movement, save migration and affected combat lifecycle. No new unit-test suite and no unrelated full-suite run (user). Scoped actual-package smoke before delivery. |
| Delivery | Incremental commits after self-contained verified changes; package from committed source; keep generated build/package output untracked. |

## Targets

### C1 — Local focus-fire packs and movement

- [ ] T3/T4 move at the requested chassis multipliers, with the existing deployed boost preserved.
- [ ] A T3-led mixed pack fights the closest enemy together, then retargets when it falls; a distant pack retains a separate target.
- [ ] Drafted orders, low-battery retreat and the single-carrier capture path remain effective.

### C2 — Tier 4 full arsenal

- [ ] A T4-led pack fires paired charge-rifle projectiles at range and paired improved shotgun projectiles close up, causing actual enemy damage.
- [ ] Automatic launched smoke creates real smoke during combat without replacing sustained damaging fire.
- [ ] Actual game rendering shows the paired weapons; no built-in weapons drop as loot.

### C3 — Existing saves and delivery

- [ ] Legacy T3/T4 dogs gain the changes in place, preserving saved identity/state, including docked and off-map examples.
- [ ] Migrated dogs fight together after reload, then save/load again without duplicate or stale equipment.
- [ ] Scoped development and actual-package scenarios pass with zero new errors; records identify the exercised coverage and package build.

## Open questions

None. The user clarified speed baseline, full simultaneous paired arsenal and mixed-tier pack leadership before implementation.
