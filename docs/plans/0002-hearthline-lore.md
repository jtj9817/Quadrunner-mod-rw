# 0002 — Hearthline lore and tooltips

## Goal

Introduce the approved manufacturer, **Hearthline Robotics**, in the Quadrunner stable's description first, then give every dog and mod item distinctive flavor text without obscuring what it does. Make the descriptions available through the game's normal information cards and tooltips, including the stable's Overview and weapon-selection menu.

## Decisions

| Decision | Resolution |
| --- | --- |
| Manufacturer | Hearthline Robotics; tagline: “A good dog for a bad world.” Company and dog-lore direction approved by the user, 2026-10-20. |
| Company placement | The stable description leads with Hearthline's remote-settlement origins, repairable common chassis and later “settlement assurance” security range. |
| Writing | Dry corporate frontier science fiction. Distinct lore first, practical instructions in a separate paragraph. Do not imply that Medirunners provide medical treatment or that Tier 4 retains Tier 3's flamethrower. |
| Coverage | Stable, all six dog models, both field consumables, all four built-in weapons, burning fuel and the existing assembly/rebuild/upgrade/item recipes. Keep the powered-down condition's recovery instructions accurate. |
| Native UI | Standard descriptions feed native information cards, inventory tooltips and the build menu. Map hover remains vanilla's brief readout. Overview adds stable lore on the count header, battery/repair-kit information icons, dog name/type descriptions and fitted-weapon/weapon-choice tooltips. No Harmony patch, new saved state or gameplay change. |
| Verification | Extend the existing artwork capture fixture with real information-card captures, inspect real Overview and weapon-menu hovers in the isolated game, and verify the actual rebuilt package. No tests pinned to wording. |

## Targets

### T0 — Company and product descriptions

- [x] The stable introduces Hearthline and retains docking, fabrication and service instructions.
- [x] All six dogs have distinct flavor text and correct role descriptions.
- [x] Both field items and all four weapons have lore and useful operational descriptions; recipes and burning fuel have accompanying descriptions.

### T1 — Usable tooltips

- [x] The native stable/item information cards and tooltips render the new paragraphs legibly in the isolated game.
- [x] Hovering a dog's name or type in Overview shows its model description.
- [x] Fitted-weapon and weapon-choice hovers show the relevant weapon descriptions; upgrade controls retain their cost and action information.

### T2 — Verified local release

- [x] Relevant scenarios pass with zero new errors; actual game screenshots of the changed surfaces are inspected.
- [ ] The rebuilt `dist/Quadrunner` package is exercised, release documentation and the test log are updated, and the verified work is committed.

Development evidence: focused scenarios **5/5 PASS** at 18:25 UTC; gallery-only follow-up **1/1 PASS** at 18:33 UTC on 2026-10-20, both with zero new errors. All 25 native information cards were inspected, and 14 Overview hover targets were reached with observed pointer-coordinate checks on the private 1280x900 display. The resulting tooltips were inspected; see [the run log](../test-runs.md).

## Open questions

None.
