# Plan 0012 — Tier 3/4 tear gas launchers and the T4 smoke cap

User request (2026-11-13): Tier 3 Combatdogs instead use tear gas modeled on the **tear gas grenades** of
[Vanilla Weapons Expanded — Non-Lethal](https://steamcommunity.com/sharedfiles/filedetails/?id=2454918354)
(`VWE_TearGasGrenade` / `VWE_Gun_TearGasLauncher`, author Oskar Potocki): copy that item into this mod, test with hostile
groups trying to invade while colonists are present, and at Tier 4 equip **both** smoke launchers and tear gas launchers.
Smoke launchers pop "in the radius between them", and when 3 or more Combatdogs are clustered, only 3 smoke launchers pop
per interval and the rest wait.

## Goal

Tier 3 loses its flamethrower and gains a built-in **tear gas launcher** that fires automatically during combat,
interval-based, never interrupting the carrier weapon; Tier 4 gains the same launcher alongside its existing smoke
launcher; smoke shells land in the screen **between the dogs and their target** (no longer on the target); and a cluster
of three or more Tier 4 Combatdogs pops at most **3 smoke launchers per smoke interval**, with the rest waiting.
Colonists caught in the clouds are blinded; the dogs (robots) are immune.

Clarifications recorded from the user (2026-11-13): "Combatrunners" = the Combatdogs; launchers target their current
attack job's target like smoke does; raider testing groups are staged like the existing pack scenarios (not lord-job
raids); the "between them" wording means the cell between the dog and its target; the 15-cell pack coordination radius
defines a cluster; the cap is per smoke interval with the remainder deferred; tear gas is **not** capped; the
colonists-in-gas expectation is "launch anyway — colonists are blinded, dogs are immune".

## Mechanics found (the copied VWE Non-Lethal tear gas)

- **Weapon** `VWE_Gun_TearGasLauncher`: cooldown 4.5 s, warmup 3.5 s, forcedMissRadius 2.9, range 19.9, single shell,
  `canTargetLocations`. Fires `VWENL_Projectile_TearGasGrenade`.
- **Projectile**: speed 10, `explosionRadius` 3.9, `flyOverhead` false, `arcHeightFactor` 2, 100-tick fuse, no damage
  (`harmsHealth=false`, `defaultDamage=0`, `DamageWorker_TearGas` Apply() is inert), spawns one `VWENL_TearGasCloud`
  **in every walkable cell** of the radius (vanilla spawns `postExplosionSpawnThingDef` once per affected cell).
- **Cloud** `VWENL_TearGasCloud` (`Gas_TearGas : Gas`): standalone gas thing, expires 20–30 s, every 120 ticks applies
  or refreshes hediff `VWE_TearGas` on each pawn standing in that cell: `severity += Rand.Range(0.01, 1.0) ×
  (1 − ToxicResistance)`. Mechanoids are skipped; Biotech's gas-mask apparel blocks it.
- **Hediff** `VWE_TearGas`: 0–1 severity, decays −30 per day; stages *wearing off* (sight −0.3, manipulation −0.2,
  moving −0.2; force +1.0 pain) → *teary* at ≥0.6 (sight −0.8 …) → *blinded* at ≥0.8 (sight −1, manipulation −0.6,
  moving −0.6, pain ×2).

## Decisions

| Area | Decision | Why |
| --- | --- | --- |
| Sourcing | Copy the item into Quadrunner; **no dependency** on Vanilla Weapons Expanded — this mod stays Core-only and carries its own art. | Mod ships nothing required; mechanics identical, assets original. |
| Defs layout | `QR_TearGas` (HediffDef), `QR_TearGasDamage` (DamageDef, modeled on vanilla `Smoke`: `harmsHealth=false`, `defaultDamage=0`, no worker), `QR_TearGasCloud` (standalone Gas thing: category `Gas`, `altitudeLayer Gas`, own `Gas` abstract base `QR_BaseGas`), `QR_Projectile_TearGas` (Projectile_Explosive, copied numbers: speed 10, radius 3.9, arc 2, fuse 100, forcedMissRadius 2.9, `canTargetLocations`), `QR_Gun_TearGasLauncher` (the built-in mount). | Exact mechanics, zero DLC references; vanilla `Smoke` damage def proves no worker class is needed for a harmless explosion. |
| Supplemental role | Zero warmup, `nonInterruptingSelfCast`, `equipmentType None`, not a selectable gun, not the Primary verb; fire rate from new XML `tearGasInterval` (default 900, like `smokeInterval`); never replaces the damaging mount, never consumes the primary stance, never fires while seeking charge, only during `AttackStatic` on a living standing hostile target. | Identical to the smoke launcher's contract (C2, 2026-11-03); the item's own warmup/cooldown are dropped because a built-in mount must never interfere with the primary job. |
| Aim | Both launchers shell the **midpoint cell between the dog and its target** (user choice of "between them", 2026-11-13), not the target's cell; cast via `TryStartCastOn(midCell, nonInterruptingSelfCast: true)`. | A gas canister ON the target is outranged by its own 2.9-cell scatter; the screen interpretation puts the cloud on the approach path between dogs and raiders. |
| Cluster cap | Per (map, `ticksGame / tearGasInterval` window, cluster) an in-memory set of firing T4 dogs; cluster = dogs with `tearGasGun` null and `smokeGun` non-null **within 15 cells** of the prospective firer (pack coordination radius); count ≥ 3 → only the first 3 distinct dogs in that window fire; the rest hold (no `nextSmokeAt` stamp; retried next tick, fire next window). **Tear gas is never capped.** | Mostly per-player-cluster; transient (reset on load; worst case one extra volley right after a mid-fight reload — logged, acceptable, never a correctness problem). Only evaluated when smoke is actually due, so no per-tick scans. |
| Fog of war / fog | Unchanged; explosions are not range-checked against fog, same as smoke. | Existing supplemental behavior. |
| Side effects | Colonists in the clouds get the hediff like anyone (user: "launch anyway — colonists are blinded"); non-organic flesh (`fleshType.isOrganic == false`, i.e. every robodog) never gets it; leaf of the cloud drops Biotech's `Apparel_GasMask` if some other mod provides it (null-safe `DefDatabase` lookup). Tear gas can never kill (0 damage, `harmsHealth=false`). | Colonist exposure is inherent to the copied mechanics — the user's chosen expected behavior; dogs can't be blinded by their own mounts. |
| T3 loadout | `slot2` (`QR_Gun_Flamethrower`) **removed**; T3 = slot 1 primary (assault rifle/LMG as before) + built-in tear gas launcher. `QR_Gun_Flamethrower` def + art stay for old saves (minigun precedent); equipped flamethrowers are destroyed on first post-update load (C3-style `MigrateLoadout` in place, no pawn replacement). | "Change the equipment … to instead use the Tear Gas" (user, 2026-11-13); the flamethrower is the only T3 change on the block. |
| T4 loadout | Unchanged paired rifles/shotguns; **tear gas launcher added as a second supplemental mount** next to smoke. Rendering draws both on the dorsal hardpoint. Describe/Overview list them; built-in guns still never drop. | "Now, at Tier 4, the Combatrunners will equip BOTH Smoke Launchers and Tear Gas Launchers" (user, 2026-11-13). |
| Persistence | `nextTearGasAt` saved like `nextSmokeAt` (Scribe default 0: first combat tick may fire after a load; harmless). `Gas_TearGas.destroyTick` saved (copied from VWE). | Every saved object's new fields get Scribe lines; every affected scenario saves/reloads. |
| Radiation of work | No changes to battery, charging, retreat, capture, targeting (AttackStatic job), or dispatch beyond the two new supplemental mounts and the midpoint aim of smoke. | Keeps blast radius to what the user asked. |
| Testing | Staged raiders like current pack scenarios (DefendPoint lord, never fleeing, Raid.Recheck); colonists drafted but present, one stood in the gas path. Assertions: real `QR_Projectile_TearGas` launches, `QR_TearGasCloud` cells spawn, raiders AND the planted colonist refresh `QR_TearGas` (severity grows toward *teary*), dogs never gain it, damaging guns keep firing; a mid-scenario save/load. In the T4 scenario additionally: ≤3 distinct smoke-launching dogs per 900-tick window while tear gas fires from all due dogs; post-cap waiters pop in the next window; cloud centroid lies between dog and target. See G3. | Matches existing combat-scenario proof style and the user's acceptance criteria; persistence of the new saved counters is verified. |

## Targets

### G1 — Copied tear gas stack

- [x] `QR_TearGas` hediff (0–1 severity, wearing off/teary/blinded stages, −30/day decay), `QR_TearGasDamage` (harmsHealth=false), `QR_TearGasCloud` standalone Gas thing (20–30 s; refresh 120 ticks), `QR_Projectile_TearGas` (3.9 radius, 100-tick fuse, arc 2), `QR_Gun_TearGasLauncher` — all load with **0 warnings**, Core-only. (Actual package `3e032f1.20261115T081346Z`: pkg-run BUILD/modRoot validated, affected set **6/6 PASS, 0 new errors**.)
- [x] Gas "ignores" non-organic flesh (robodogs) and Biotech gas masks (null-safe); organic breathing pawns standing in a cloud cell refresh `QR_TearGas`. (Tear gas pack: raiders and the planted colonist blinded; dogs clean; package-verified.)
- [x] Own art (`Art/` SVGs → PNG): tear gas launcher sprite and yellow tear-gas cloud sprite; no VWE assets reused.

### G2 — T3/T4 supplemental mounts and the smoke cap

- [x] T3: flamethrower gone from the slot list and from equipped loadouts on load (migration in place, incl. held and world pawns); tear gas launcher always fitted, automatic, XML interval, never consumes the primary stance, mid-path aim like smoke. (Combat tier legacy migration package-verified.)
- [x] T4: keeps the paired arsenal and smoke launcher; gains tear gas launcher alongside it; both mounts fire automatically on XML intervals; both draw on the dorsal hardpoint; Overview lists them. (Tier 4 pack arsenal package-verified.)
- [x] With ≥3 T4s clustered within 15 cells, at most 3 distinct dogs pop smoke per smoke interval (the rest fire in the next window, longest-wait rotation); tear gas is uncapped; cap is transient and logged. (Cluster scenario: `pops per window: 3/1`, `eligibility == 3`; package-verified.)
- [x] Built-in guns never drop as loot; no saved state corruption on save/load (new `nextTearGasAt` field; cloud things round-trip). (Zero loose guns in arsenal/demos; `Stance_Cooldown` cleanup on built-in gun replacement included.)

### G3 — Test scenarios (committed with code)

- [x] **Tear gas pack**: staged hostile group + colonists present; dogs launch tear gas at them; raiders and the planted colonist gain/refresh `QR_TearGas`, dogs never do; real projectiles and clouds observed; primary guns keep firing; scenario passes with save/load; zero new errors. (Package-verified 08:15.)
- [x] **Tier 4 tear gas & smoke cluster**: ≥3 T4s within 15 cells — smoke pops ≤3 per 900-tick window across the cluster, remaining dogs fire in later windows, smoke lands between dogs and target; tear gas pops from every due dog; existing damaging fire and capture flow unaffected; zero new errors. (Package-verified 08:15, development 1/1 08:13.)
- [x] Existing combat scenarios that sat nearby the copy (Tier 3 pack combat, Tier 4 pack arsenal, Combat tier legacy migration, Tier 2/3/4 weapon demos) pass unchanged (the T3 weapon demo dropped its flamethrower round; Artwork showcase unchanged list; AutoRebuild and PackMigration expect-changed accordingly). (Package-verified 08:15 **6/6** and 08:19 **2/2**, both 0 new errors.)
- [x] Scoped development runs with zero new errors; actual-package smoke of the affected set; package rebuilt after the major feature (AGENTS.md), documented in docs/test-runs.md.

## Open questions

None; all design decisions were recorded 2026-11-13 (user answers): supplemental interval-based mounts for both tiers;
smoke (and tear gas) land **between** dogs and target; 15-cell cluster; 3 pops per interval, the rest wait; tear gas
uncapped; colonists blinded, dogs immune; staged raiders; "Combatrunners" = Combatdogs.
