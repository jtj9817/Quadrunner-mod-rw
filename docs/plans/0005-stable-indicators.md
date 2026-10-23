# Plan 0005: Stable dock indicators

## Goal

Show which dog types are physically inside a stable using icons and lights drawn within the building itself, not a separate UI or floating overlay.

## Decisions

| Topic | Decision |
| --- | --- |
| Scope | User request, 2026-10-23: indicate docked Quadrunners, Combatdogs and Medirunners inside the stable; commit incrementally |
| Layout | Three existing bays become Quadrunner, Combatdog (all tiers), Medirunner indicators, rotating with the stable |
| Occupancy | Only dogs in the stable's actual dock container count; assigned dogs in the field or being carried elsewhere do not |
| Appearance | Bright type-specific dog icon and green light when occupied and powered; dim icon and unlit light when empty; occupied but unpowered uses an amber light |
| Capacity | Presence per type, not a dog count or three separate capacity limits; the existing 15 shared slots are unchanged |
| Implementation | Native building DrawAt hook and cached materials; read docked pawns directly without per-frame list allocation, new saved state or Harmony patches |

## Targets

### S1: In-building dock indicators

- [x] Icons and lights distinguish all three types, including a higher-tier Combatdog.
- [x] Empty, occupied, undocked and unpowered appearances match the actual dock contents.
- [x] Indicators stay inside the building and aligned in all four rotations.
- [x] Actual isolated-game captures are inspected at close and ordinary zoom; save/load preserves the indicated contents with zero new errors.

Verified by **Stable dock indicators**, build `545f4ca-dirty.20261023T162202Z`, 2026-10-23 16:22 UTC: **1/1 PASS, zero new errors**. Actual screenshots inspected in `.devlog/screens/stable-docks-*.png`, including the four powered rotations, reloaded dock, empty dock and ordinary zoom. The unchanged production implementation also passed **Dock and undock** and **Stable power loss** in the 16:19 focused run (**3/3 PASS, zero new errors**); see [test runs](../test-runs.md).

## Open questions

None. These indicators report occupancy, not battery level or individual dog status; the Overview tab retains those details.
