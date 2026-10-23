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

- [ ] Icons and lights distinguish all three types, including a higher-tier Combatdog.
- [ ] Empty, occupied, undocked and unpowered appearances match the actual dock contents.
- [ ] Indicators stay inside the building and aligned in all four rotations.
- [ ] Actual isolated-game captures are inspected at close and ordinary zoom; save/load preserves the indicated contents with zero new errors.

## Open questions

None. These indicators report occupancy, not battery level or individual dog status; the Overview tab retains those details.
