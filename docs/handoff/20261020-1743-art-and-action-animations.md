# Hand-off: dog repaint done; action animations and Medirunner siren built, scenarios need a verified rerun

- **Written:** 2026-10-20 21:50 UTC, by a session that stopped at ~254k tokens of context
- **Repo state:** branch `main`, HEAD `bdbfe3b` (`WIP: Action animations, Medirunner tail siren flash (unverified)`), tree clean
- **Test instance:** stopped. **User's game:** not running at last `autotest.py status`

## Read first

1. `AGENTS.md`, then `TODO.md` (current state, gotchas)
2. `docs/plans/0001-quadrunner.md`, Decisions table: the new rows **Liveries**, **Action animations**, **Harmony patch 6** (already recorded)
3. The newest two rows of `docs/test-runs.md`
4. `Source/Quadrunner/Rendering.cs` (pose system), `Source/Quadrunner/Scenarios.Animation.cs` (the two new scenarios)

## The user's request (2026-10-20), verbatim

"Change the SVGs/art of the dog types. The combatdog should have green camo pattern paint or just black main color w/ silver accents. The robodog should have red cross colors and have a siren on its tail. The robodog should animate flipping over a colonist/pawn into its back. The combatdog should animate its shooting, melee attacks. commit your changes incrementally."

Interpretation used: "the robodog" with red cross, siren and flipping a colonist onto its back = the **Medirunner** (the rescue dog). Combatdog: both options used, Tiers 1–2 camo, Tiers 3–4 black/silver (tell the user they can pick one scheme for all).

## Where things stand

- **Done and verified** (commit `4941737`): SVG repaint of Medirunner and all four Combatdog tiers, PNGs exported, `About/Preview` updated. Artwork showcase 1/1 PASS, 0 new errors; `.devlog/screens/art-dogs-*.png` inspected (row 21:36 in test-runs).
- **Built, unverified** (commit `bdbfe3b`):
  - `TailSiren` DefModExtension (Robodog.cs) on `QR_Medirunner` (QR_Races.xml, offsets per facing); `CompRobodog.Rescuing`; `CompRobodog.PostDraw` flashes a red lamp via `Siren.Draw`.
  - `CompRobodog` tracks `carried`/`carriedSince` (transient; `PostSpawnSetup` seeds `carried` so no replay after load); `CarriedSince(thing)` returns now when the comp hasn't seen the pickup yet.
  - Rendering.cs: `PawnRenderNodeProperties_Pose` (root props, XML tunables in QR_RenderTrees.xml), `RobodogPose.Of` (pounce on melee cooldown, crouch/brace during ranged warmup/cooldown + recoil kick per shot, head dip after pickup), `PawnRenderNodeWorker_Pose` (root), Gait worker now applies `stretch` (legs) and `crouch` (body; Body node now uses the Gait worker), mount worker draws a carried pawn on the back with `PatientOffset` (arc + 180° flip over `scoopTicks`), Harmony patch 6 `Patch_PawnRenderer_BodyAngle`.
  - Scenarios `Rescue animation` and `Combat animation` registered after `Walk animation` in Scenarios.cs.
- Run 21:42: Walk animation PASS (pose root didn't break gait); Rescue FAIL (pickup race, fixed since); Combat FAIL (Tier 4 had no gun, scenario now calls `Tiers.Arm`). The rerun with both fixes was stopped at the context limit with no result.

## Next steps

1. `tools/autotest.py run "Rescue animation,Combat animation"` (background). Fix until both PASS with 0 new errors.
2. Inspect `.devlog/screens/art-rescue-siren.png`, `art-rescue-flip-{4,16,26,36}.png`, `art-rescue-carry.png`, `art-combat-shot.png`, `art-combat-pounce.png`. Tune in XML if needed: `patientNorth/East/South` (where the patient lies; first guesses), `TailSiren` offsets, pounce/recoil amounts.
3. Then run the scenarios touched by shared rendering/carry code: `Walk animation,Rescue order,Mass-casualty,Carry home,Rider pose,Artwork showcase,Tier 3 and 4` (carried-pawn draw and gun mount changed). Shared code changed, so a Run all before finishing is justified.
4. Add rows to `docs/test-runs.md`, update `TODO.md` (Current state entry for liveries + animations; gotchas below), squash nothing; commit with a non-WIP message (`Animate Medirunner rescues and Combatdog attacks`).
5. Report to the user: what changed, the camo/black split by tier, the Medirunner interpretation, screenshots.

## Decisions and answers from the user this session

None beyond the request above. The agent's choices are recorded in the plan's Decisions table (Liveries, Action animations, Harmony patch 6).

## Waiting on the user

Nothing.

## Gotchas learned this session (add to TODO.md agent notes)

- **Comps tick before jobs.** `Pawn.Tick` runs `base.Tick` (comps) before `TickInterval` (jobs), so a pickup done by a job is visible to rendering/scenarios one tick before a `CompTick` can record it; `CarriedSince` treats an unseen carry as "now".
- **`AddNewDog` doesn't arm a higher tier.** A Combatdog Two–Four added directly holds no gun until `Tiers.Arm(dog, Tiers.Gun(dog, 0))` (the upgrade path does this).
- **`sed -i` on this mount** prints "preserving permissions … Operation not permitted" but still edits; prefer Python or the Edit tool.
- **SVG `<pattern>` fills survive `--export-id-only` part exports** (camo shows on body parts).
