# Quadrunner

A RimWorld 1.6 mod (Core only, requires Harmony): robot dogs dispatched from a stable haul, clean, supply construction, carry riders, rescue the downed (Medirunner) and defend the colony (Combatdogs, tiers 1–4). They charge at conduits or their stable, power down instead of dying when empty, and are carried home.

Core + Harmony release for RimWorld **1.6**. No DLC is required; DLC-enabled configurations have not been verified. Progress and exact verification runs are recorded in [TODO.md](TODO.md) and [docs/test-runs.md](docs/test-runs.md).

The local release is `dist/Quadrunner`, build `4137037.20261020T174523Z`. Its packaged **Run all passed 39/39 with zero new errors** on 2026-10-20 after the 3,500-cell battery-range update. The original T9 release also passed an independent load, 600 ticks, save/reload and another 600 ticks **without the Quadrunner assembly**. See [the run log](docs/test-runs.md) for the separate range-update and removal evidence.

## Hearthline Robotics

*“A good dog for a bad world.”*

Hearthline supplies remote settlements with locally serviceable utility, rescue and security robodogs. Its armed range is marketed as “settlement assurance.”

The **stable's information card** introduces the company. All six dog models, both field consumables and all four built-in weapons have distinct flavor text followed by practical instructions; assembly, rebuild and upgrade bills have accompanying descriptions too.

In **Overview**, hover the robodog-count heading for the stable's lore, a dog's **name or type** for its model description, or the **battery and repair-kit icons** for field-item information. Hover fitted weapons and individual choices in the weapon picker for their descriptions. Upgrade hovers still show the action and material cost.

## Install and play

1. Put the packaged `Quadrunner` folder in RimWorld's `Mods/` directory. Enable **Harmony**, **Core**, then **Quadrunner**, and restart the game.
2. Research **Microelectronics**, then build a **Quadrunner stable** from **Misc** and connect its 200 W power supply. Its 15 slots include dogs currently working away from home.
3. Add **Assemble Quadrunner**, **Assemble Medirunner** or **Assemble Combatdog** bills at the stable. Colonists do the work under Crafting. The **Overview** tab shows each dog's battery, health and current activity.

- **Quadrunners** automatically supply construction map-wide, and haul/clean within 40 cells of their stable. Select an undrafted colonist, right-click a stable or an available Quadrunner, choose **Ride … to…**, and pick a destination. Riders dismount on arrival, drafting or entering a building.
- **Medirunners** rescue reachable downed colonists, guests, prisoners and colony animals to appropriate beds. They do not tend patients or do worker jobs.
- **Combatdogs** answer attack pings and raids. Set a route with the stable's **Add patrol waypoints** / **Clear patrol** buttons, or draft a dog for direct orders. Order paid tier upgrades and choose free built-in gun options in **Overview**. Tier 3 switches between its rifle/LMG and short-range flamethrower; Tier 4 adds the minigun option. Built-in guns are never dropped as loot.
- All robodog models have approximately **3,500 cells** of normal travel per full charge. They seek charging at **10%** (about **350 cells** left) and power down at **0%**; a working Quadrunner's raid drain remains doubled. The powered stable charges and repairs docked dogs. Outdoor dogs can also use safe roofed indoor conduit rooms on a network with stored battery energy; nearby wind turbines invalidate spots (distance configurable in mod settings).
- Other Quadrunners retrieve powered-down dogs. Colonists can **Carry to stable** or automatically deliver field batteries and repair kits made at the stable. Keep a destroyed dog's wreck for its **Rebuild** bill.

## Removing Quadrunner safely

**Do not simply disable the mod on a save that contains its dogs or buildings.**

1. With the colony loaded and Quadrunner still enabled, open **Options → Mod settings → Quadrunner**.
2. Choose **Prepare save for removing Quadrunner...** and read the destructive confirmation.
3. The action first writes `QR_before_removal_<timestamp>`. It releases riders, carried patients and vanilla cargo, removes Quadrunner content and related in-flight shots on all maps and in the world, and creates `QR_removed_<timestamp>`. Mod content is not refunded; surviving injuries are not healed. Surviving battle records keep their IDs, with removed weapon/cover metadata cleared.
4. Note the cleaned save's exact name. **Quit without saving again**, disable Quadrunner, restart and load that cleaned save. The changed-mod-list warning is expected.
5. Keep the backup. To undo removal, re-enable Quadrunner and load the `QR_before_removal_…` backup.

If preparation reports failure, **keep Quadrunner enabled and reload the backup**. No successful clean export is claimed. Bring any off-map robodog cargo without a surviving caravan carrier onto a map before retrying.

## Development

Original SVG sources live in `Art/`; `tools/export-textures.sh` exports the committed PNGs. Preview sources before integration and inspect actual private-game screenshots afterwards.

```sh
tools/autotest.py run "Artwork showcase,Prepare removal"  # private visual captures and removal export
tools/autotest.py stop
tools/autotest.py removal-check          # independent observer, no Quadrunner assembly
tools/package.sh                        # requires a clean committed tree; writes dist/Quadrunner
tools/autotest.py --timeout 3600 pkg-run  # cold-launch the package, run every scenario, restore dev link
tools/autotest.py stop
```

`removal-check` requires an export from the **Prepare removal** scenario (`tools/autotest.py run "Prepare removal"`), not an arbitrary player save. The observer loads the export, ticks 600, saves/reloads and ticks another 600 while checking expected vanilla contents and absence of the Quadrunner assembly. The harness rejects any new errors. Tests use only `.devlog/` data and a private Xvfb display, never the player's profile or window.

Automated quicktest starts use flat, low-swampiness, inland temperate forest without rivers, retaining a random world seed. Scenarios build their own rooms and obstacles; this supplies a consistent building-space prerequisite, not a claim of coverage for every biome. Ordinary games are unaffected.

For native hover checks, cold-start the private display with `QR_SCREEN=1280x900` after `tools/autotest.py stop`. Changing the game window's resolution alone does not enlarge Xvfb's pointer bounds. Artwork captures are evidence only after the actual cards/tooltips have been visually inspected.

- Design snapshot: [docs/design.md](docs/design.md)
- Plan and decisions: [docs/plans/0001-quadrunner.md](docs/plans/0001-quadrunner.md)
- Hearthline lore and tooltip plan: [docs/plans/0002-hearthline-lore.md](docs/plans/0002-hearthline-lore.md)
- Automated work loop and tool reference: [AGENTS.md](AGENTS.md)
- Verification evidence: [docs/test-runs.md](docs/test-runs.md)
