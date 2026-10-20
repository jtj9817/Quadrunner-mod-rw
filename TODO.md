# TODO

Tracks [Plan 0001](docs/plans/0001-quadrunner.md). Tick a target only once **all** of its acceptance boxes in the plan are ticked. Workflow rules are in [AGENTS.md](AGENTS.md); the iteration log is [docs/test-runs.md](docs/test-runs.md).

## Progress

- [x] **T0**: Scaffold and the automated test loop (tools, mod skeleton, harness, baseline, first real runs, in-game restart, watchdog)
- [x] **T1**: Dogs and the stable (race, stable, assemble bill, docking, Overview tab, debug actions)
- [x] **T2**: Battery and charging (drain, charge-spot search, stable charging, charging edge cases)
- [x] **T3**: Dispatch and work (haul, clean, construction delivery, radii, raids, stable power/destroy/uninstall)
- [x] **T4**: Power-down and retrieval (carry home, field battery and kit, wrecks, raider carry-off)
- [x] **T5**: Medirunner (rescue order, triage)
- [x] **T6**: Riding (stat part, ridden job, dismount rules, draw offset)
- [x] **T7**: Combatdog Tier 1, pings and patrols
- [ ] **T8**: Combatdog tiers 2–4
- [ ] **T9**: Polish and release

## Current state

- Next: **T8**: both T8 scenarios pass (05:45, 0 new errors); the T7 combat scenarios are stable again after scenario fixes (06:04–06:07). Run all to finish T8, then tick boxes 1–3 and start **T9**.
- Balance, worth raising with the user in T9: a Tier 1 Combatdog sent through three fist fights in a row, with no dock between, died in about half the Ping chain runs (thorax). The scenarios now use Melee 0 raiders, so they no longer show it.
- Open flake: **Haul and clean** failed in the 05:26 Run all (and at 03:26, before T7): after the reload both dogs docked unfinished and the stable sent nothing for 29k ticks. It passes alone; the summary now prints the raid state and the stable logs "found no work", so the next Run all should show which branch it is.
- Drafting: `Drafting.cs` (patches 4 and 5, see the plan's Decisions), the comp's draft toggle, a drafted branch in `QR_Robodog`, `Combat.Answers` skips drafted dogs, `SetBattery` undrafts at 10%.
- Hand-off at the context limit now replaces the old session in the same tmux pane (user, 2026-10-20).
- Usage status line: `tools/statusline.py` written; wiring it into settings waits on the user.
- Last run: 2026-10-20 05:28, build `4fe545a-dirty.20261020T052758Z`, Haul and clean 1/1 PASS; before it Run all 05:26, 33/34 PASS, 0 new errors, 336 s, beside the user's open game.
- Baseline: 8 signatures (2026-10-19, Harmony + Core).
- User's saves backed up: `~/RimWorldBackups/20261019-171753.tar.gz` (44 saves).
- `tools/selftest.sh`: all cases ok (2026-10-19 23:28, after removing `--beside-user-game`); stop the real test instance first, its stop/hang cases kill processes.

## Open questions

See the plan's [Open questions](docs/plans/0001-quadrunner.md#open-questions); keep the answered ones there with the answer.

## Agent notes, gotchas and tips

Add an entry here whenever something costs you more than one attempt. Keep each entry to one or two lines. Entries marked *(QB)* were learned on Queen Bee with the same game version and still hold.

### Environment

- **Use the right game copy.** The game is the native Linux build at `~/.steam/debian-installation/steamapps/common/RimWorld` (1.6.4871 rev598). The copy under `/mnt/2E2E2F242E2EE495/SteamLibrary` is an old 1.4 Windows build. Ignore it. *(QB)*
- **The user's profile is not ours.** `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios` (Saves, Config, a ~35-mod list, Player.log) belongs to the user's real game. Tests use `-savedatafolder=` under `.devlog/` instead.
- **The user plays while we work.** RimWorld was running (`RimWorldLinux`, launched by Steam through `start_RimWorld.sh`) when this repo was created. Check `pgrep -x RimWorldLinux` and tell a user-game process from the test instance by its command line (`-savedatafolder=`).
- **Git ownership error.** The repo sits on a root-owned mount, so git fails with "dubious ownership". Use `git -c safe.directory="$PWD" ...` unless the user has added it globally. *(QB)*
- **`sed -i` warning.** On this mount `sed -i` prints "preserving permissions… Operation not permitted" but still writes the file. Prefer the Edit tool. *(QB)*
- **No DLC.** Only Core is installed (`knownExpansions` is empty), so don't reference Biotech/Royalty/Ideology/Anomaly defs or systems (mech recharger, mech work, gestators). *(QB)*
- **The wiki is ahead of our game.** The wiki documents **RimWorld 1.7** (`net10.0`, `Waylith.dll`, `../1.7/Assemblies`). This project targets **1.6**: `net472`, Krafs.Rimworld.Ref 1.6.*, `1.6/Assemblies`. Ignore 1.7-only advice unless `status.sh` reports the game itself updated, and if it did, stop and ask the user. *(QB)*
- **Toolchain.** `dotnet` SDK 10.0.112 builds `net472` through `Microsoft.NETFramework.ReferenceAssemblies`; `inkscape`, `xdotool`, `import` (ImageMagick), `Xvfb`/`xvfb-run` and `python3` are installed. The agent's shell has `DISPLAY=:100`; the user's game ran on `:1` *(QB)*.
- **Reuse Queen Bee's decompiled source.** `/mnt/0B8533211952FCF2/queen-bee-rw-mod/.tools/decomp/` is current for 1.6.4871; copy it into `.tools/decomp/` (with its `.version`) instead of decompiling again.

### Launching the test instance (from the decompiled 1.6.4871 source; the restart, quicktest, savedatafolder and Steam points proven in the 2026-10-19 runs)

- **The restart path.** `Dialog_Options` (Texture Compression) calls `GenCommandLine.Restart()`: `Process.Start(args[0], <the other args, each quoted>)`, then `Root.Shutdown()`. The new process gets the same command line and inherits the environment (Xvfb `DISPLAY`, `XDG_*`), so it comes back into a fresh quicktest map with the autotest argument. Argument values must not contain `"` or spaces.
- **`Root.Shutdown()` empties `Application.temporaryCachePath`**, which on Linux is under `$XDG_CACHE_HOME/unity3d/Ludeon Studios/…`, shared with the user's game unless `XDG_CACHE_HOME` is overridden. The harness overrides it (and `XDG_CONFIG_HOME`, which moves Unity's default `Player.log` and prefs).
- **Our driver is a MonoBehaviour**, not a `GameComponent`: every `GameComponent` subclass is instantiated into every game (`Game.FillComponents`) and saved, which would break "remove the mod cleanly".

- **`-savedatafolder=<path>`** overrides the whole profile (`GenFilePaths.SaveDataFolderPath`). `GenCommandLine.TryGetCommandLineArg` splits each argument on `=` and needs exactly two parts, so the path must not contain `=`.
- **`-quicktest`** makes `QuickStarter` load the Play scene directly, and `Root_Play` generates a quicktest map (`SetupForQuickTestPlay`). No menu clicks needed.
- **Custom launch arguments work.** `GenCommandLine.CommandLineArgPassed("x")` matches `x` or `-x` case-insensitively, so the mod can read its own `-quadrunner-autotest` flag.
- **Quitting from code.** `Root.Shutdown()` shuts Steam down, cleans the temp folder and quits; use it at the end of an automated run.
- **Steam.** `SteamManager.InitIfNeeded` calls `SteamAPI.RestartAppIfNecessary`; `steam_appid.txt` sits in the game folder, so a direct launch should not relaunch through Steam. Workshop mods are listed only when `SteamAPI.Init()` succeeds, i.e. while Steam runs.
- **Harmony comes from the workshop.** Item 2009463077 (`brrainz.harmony`, with a `1.6/` folder) under `steamapps/workshop/content/294100/`. Without Steam running it won't load, and neither will we.
- **Prefs that matter for a headless run** (`PrefsData`): `devMode`, `runInBackground` (keep ticking without focus), `pauseOnError`, `fullscreen`, `screenWidth`/`screenHeight`, `volumeMaster`, `resetModsConfigOnCrash` (default true: a crash could reset the test ModsConfig).

- **Two version strings.** `Version.txt` says `1.6.4871 rev598`, the running game logs `RimWorld 1.6.4871 rev600` (the user's game too). Same build; `status.sh` compares `Version.txt`, so only a change there means an update.
- **The in-game restart can segfault.** Once (2026-10-20 00:47) the relaunched process died in Mono at "Begin MonoManager ReloadAssembly", before mod code; `autotest.py` fell back to a cold start as designed. The log lands in `.devlog/runs/orphan-*`.
- **A kill leaves a crash dump.** Killing an instance (baseline, watchdog) makes Steam's socket thread assert and breakpad writes `/tmp/dumps/assert_*.dmp` (upload fails: no libcurl). Harmless; the in-game quit and restart shut down cleanly with no dump.
- **Other Xvfb displays aren't ours.** `Xvfb :99`–`:110` (started by `xvfb-run`) belong to other shells, `:100` to the agent's own. The harness uses `:90` and up and stops only the pid it recorded, after checking its name is `Xvfb`.
- **Never fire the hand-off hook's Stop from a live session.** At Stop with a launched hand-off, `context-guard.py` SIGTERMs its nearest `claude` ancestor; a manual test run from the agent's shell killed the agent's own session (2026-10-20). Test it only on a separate tmux server (`tmux -L qrtest`) with a fake `claude` first on `PATH`.
- **Permission check.** The auto-mode classifier refuses edits to `.claude/settings.json` and refused `tools/selftest.sh` once and `autotest.py --beside-user-game` (flag since removed); don't work around it, ask the user.
- **Profile snapshot times.** `ls` prints local time (UTC−4), `find -printf %T@` and `autotest.py` UTC; convert before attributing a changed file to a run.

### Game / API

- **A third kind of pawn.** Intelligence `Animal` + a non-organic flesh type makes `RaceProperties.Animal`, `IsMechanoid` and `IsFlesh` all false. Vanilla still gives such a player pawn `playerSettings` and a `training` tracker (`PawnComponentsUtility`), but no needs at all with `foodType None` and `needsRest false`. Disease incidents pick only `IsFlesh` pawns.
- **Pawns inside a building.** A `Building` that implements `IThingHolder` (here `ThingOwner<Pawn>`, deep-saved) keeps pawns out of the world; `MapPawns.AllPawns` still finds them through `GetChildHolders`, but `listerThings` doesn't, so `Scenario.ById` falls back to `AllPawns`.
- **Bills need power.** `Building_WorkTable.CurrentlyUsableForBills` wants `PowerOn`; a scenario powers a stable with a conduit under it plus `DebugSettings.unlimitedPower` (a lone consumer has no PowerNet, so the flag alone does nothing).
- **Prison rooms.** `Room.IsPrisonCell` is only recomputed when the room's shape changes; switching a bed to prisoners only dirties the role, so check `Role == PrisonCell/PrisonBarracks` too. Setting `ForPrisoners = false` logs a vanilla error: set `ForOwnerType` instead.
- **Batteries are transmitters.** A vanilla `Battery` (`transmitsPower`) joins a net only through a conduit it touches; the 6-cell connect radius is for consumers like the stable. A battery two cells from a conduit is its own net.
- **`DebugSettings.unlimitedPower` refills batteries.** It makes every net's gain rate 100000, so any battery on a net fills each tick; scenarios that measure battery energy power the stable from its own battery instead.
- **Batteries self-discharge 5 W** (`CompPowerBattery.CompTick`); negligible in a scenario, but expect it in energy sums.
- **Dogs need Manipulation to haul.** `HaulAIUtility.PawnCanAutomaticallyHaulFast` wants `CapableOf(Manipulation)`, which comes from a `ManipulationLimbCore` part (animals: the jaw); hence the gripper jaw.
- **Construction work givers read `pawn.workSettings`.** `GenConstruct.CanConstruct(…, WorkTypeDef, …)` dereferences it unless forced (and forced also ignores reservations), so robodogs need the Harmony prefix.
- **FilthMaker refuses dirt on natural ground** (`CanMakeFilth`); scenarios spawn `Filth_Dirt` with `GenSpawn` instead. The quicktest map has its own filth (rock rubble) in the home area, so dogs clean that too: assert on your own targets, not on "the dog stayed docked".
- **Rain washes outdoor filth** (`FilthProperties.rainWashes`): a scenario's dirt vanished with no dog involved; `SpawnFilth` roofs the cell.
- **Damage can be deflected** by armor: to injure a dog in a scenario, add the injury hediff (`DamageDefOf.Blunt.hediff`) with a severity instead of `TakeDamage`.
- **Drafting changes the colonist lists.** Iterate `FreeColonistsSpawned.ToList()` when drafting or undrafting.
- **Forcing a bill in a scenario.** `WorkGiverDef.Worker.JobOnThing(colonist, stable, forced: true)` then `jobs.TryTakeOrderedJob`; also finish the recipe's research first.


- **Use the offline wiki.** The mirror at `/mnt/0B8533211952FCF2/rimworld-wiki-local/data/site/pages/` has every Modding Tutorials page as Markdown, so grep it before fetching the web. *(QB)*
- **Look up vanilla, don't guess.** Use `tools/vanilla.sh grep` / `def`. Grepping `strings` on `Assembly-CSharp.dll` gives false negatives because the metadata heap shares suffixes. If ilspycmd complains about the runtime, set `DOTNET_ROLL_FORWARD=Major`. *(QB)*
- **Debug Actions live in `LudeonTK`.** `DebugActionAttribute`, `DebugActionType` and `AllowedGameStates` are in that namespace in 1.6, not `Verse`, so add `using LudeonTK;`. *(QB)*
- **A method named `Log` hides `Verse.Log`.** Inside a class with a `Log` method (our `QR`), write `Verse.Log.Message(...)`. *(QB)*
- **Dev-mode-only logging.** Log through `QR.Log`, which prints only when `Prefs.DevMode` is on and adds the prefix. The `loaded build` line is the one exception: always print it, `autotest.py` compares it with `.devlog/last-build`. *(QB)*
- **Restart to reload.** Assemblies load once at startup. Hot reload Defs is unreliable (it broke Queen Bee's run under the user's mod list); with the test instance's minimal list a fresh launch is cheap, so always relaunch. *(QB)*
- **The game reads only some folders.** `About/`, `Defs/`, `Textures/`, `Patches/`, `Languages/`, `Sounds/` and the version folder `1.6/`; `Source/`, `Art/`, `tools/`, `docs/` are ignored. *(QB)*
- **Case matters on Linux.** Folder names and every `texPath` must match the file case exactly. `About/About.xml` and `LoadFolders.xml` are the only case-insensitive exceptions. *(QB)*
- **Harmony reference.** The Harmony mod ships 0Harmony 2.4.3; reference `Lib.Harmony` 2.4.3 with `ExcludeAssets="runtime"`, and list `brrainz.harmony` in About.xml's `modDependencies` and `loadAfter`. *(QB)*
- **Conduits have CompPower too.** Conduits carry `CompProperties_Power` as transmitters. Find conduits by their transmitter comp and batteries by `CompPowerBattery`; consumers and plants are `CompPowerTrader` (`CompPowerPlant` derives from it). Relevant to the charge-spot rules. *(QB)*
- **Minify vs destroy.** `MakeMinified` despawns with `DestroyMode.Vanish`, the same mode as a plain destroy, so `DeSpawn` can't tell them apart; minify never calls `Destroy`. Relevant to "stable uninstalled" vs "stable destroyed". *(QB)*
- **What gates a bill.** `WorkGiver_DoBill` decides with `bill.ShouldDoNow()`; `RecipeWorker.AvailableOnNow` only affects the UI listing; "Do until you have X" counts through `recipe.WorkerCounter`. `RecipeDef` has no per-bill class, so gating one bill needs a Harmony postfix on `Bill_Production.ShouldDoNow`. *(QB)*
- **No-product recipes are UI-safe** if you override `CanCountProducts`/`CountProducts`/`CanPossiblyStore` (the defaults index `products[0]`). Relevant to "Assemble Quadrunner", whose product is a pawn. *(QB)*
- **Fabrication bench is Smithing.** Vanilla `DoBillsFabricationBench` uses the Smithing work type with the Crafting skill. *(QB)*
- **Attack targets.** `AttackTargetsCache` holds `IAttackTarget`s: pawns and `Building_Turret`s. Hostiles shoot any spawned `IAttackTarget` with a hostile faction and `ThreatDisabled() == false`. *(QB)*
- **Pawn kinds for spawning hostiles.** The vanilla `Pirate` FactionDef has no `basicMemberKind` (null → NRE in `PawnGenerationRequest`). Use `faction.def.basicMemberKind ?? PawnKindDefOf.Pirate`. *(QB)*
- **Explosions land over several ticks**, so a target stays valid for a tick or two after the blast is spawned. Expect the same lag when a Combatdog checks "downed or dead". *(QB)*

- **Built-in guns and stances.** Vanilla's `CancelBusyStanceSoft` ends only a warmup: a gun destroyed or switched mid-cooldown leaves a `Stance_Cooldown` on its verb, which the next save can't resolve ("referenced but not deep-saved"). `CompBuiltInGun.Notify_Unequipped` ends it.

### Log

- **No timestamps.** Player.log has none, so `autotest.py` cuts per launch instead: it moves the live log to `.devlog/runs/<launch>/` before every restart, and reads a run's errors from the byte offset at which it sent `run`. *(QB)*
- **Deduplicated stack traces.** 1.6 prints a repeated stack trace once and later writes `[Ref XXXXXXXX] Duplicate stacktrace, see ref for original`. `autotest.py` resolves these; when reading the raw log, search for the first block with the same `[Ref …]`. *(QB)*
- **Baseline per mod list.** Signatures are the first 60 characters of the error line with digits replaced by `N`. The test instance's list is fixed (Core + Harmony + Quadrunner), so one baseline from a run without Quadrunner lasts until the game updates. *(QB)*
- **Line numbers in traces.** With `DebugType embedded`, Unity's Mono prints `File.cs:line` for our frames, so trust those line numbers. *(QB)*
- **Filtered views can miss things.** When a result surprises you, read the raw `Player.log` of that run. *(QB)*

### Testing

- **Scenario runner design that worked** *(QB)*: static, never-saved scenario state; `Expect(label, Func<bool>)` / `Do` / `Wait(ticks)` steps checked every tick; `SCENARIO <name>: start …` and `PASS/FAIL` lines with tick counts; **Run all** queues every scenario and logs one `n/m PASS` summary. Starting a scenario removes the previous one's things.
- **Static state survives a reload.** Assemblies don't reload when a save loads, so a static scenario runner can save, reload and keep checking its steps. Re-find things by ID after the load; references from before it are stale.
- **The raid check is cached for a second.** A raider spawned this tick isn't seen by `Raid.Active` yet, so the stable may still dispatch; scenarios call `Raid.Recheck(map)` after spawning hostiles.
- **The map center can be rock.** Searching "reachable from the map center" fails everywhere when the center is inside a mountain; `ClearRect` reaches from the nearest standable cell.
- **Colonists are refreshed per scenario.** Scenarios keep colonists drafted, so over a long Run all they never ate or slept (slower walking, empty float menus); `Scenario.Start` refills needs and removes malnutrition, blood loss and fresh injuries.
- **Ordered jobs race the stable.** A colonist's ordered carry and a stable dispatch in the same tick: the dog may reserve the target first. In scenarios, keep the stable's dogs busy (battery 1%: charging dogs aren't sent out).
- **Objects from before a reload are stale.** `Faction`s (like things) captured before `SaveAndReload` belong to the old `Game`; re-find them by `loadID` / ThingID after it.
- **Match float-menu options by exact label.** Drafted colonists also get vanilla's "Carry <pawn>" for any downed pawn, dogs included; a `StartsWith` match picked it over "Carry X to <stable>".
- **Dogs need a name.** `PawnGenerator` gives a `QR_Quadrunner` no name; only `Building_Stable.AddNewDog` names it. Scenarios that read the name start from `AddNewDog`.
- **Implied corpse defs.** XML can name `Corpse_<race defName>` (generated before the second cross-ref pass); `rwdefs.py lint` now knows them.
- **Find clear ground by scanning, not sampling.** `CellFinder.TryFindRandomCellNear` (3000 random tries) missed the few clear 15×15 areas on a dense map; `FindClearArea` now checks every cell in range, nearest first.
- **Quicktest maps differ per launch.** Each launch generates a new map (trees, chunks, crashlanded items near the center); setups must find clear ground with `Walkable` (trees aren't `Standable`) and clear plants and items themselves. Scenarios turn the storyteller off while they run (a random event once left a colonist's relation to an unsaved pawn: 2 red errors on save/load).
- **Fail conditions run before the first toil.** `JobDriver.TryActuallyStartNextToil` checks the job's fail conditions before the toil's pre-init actions; set up what they test (the dog's waiting job) in `Notify_Starting`.
- **Vanilla jobs queue extra targets.** `WorkGiver_CleanFilth.JobOnThing` queues all filth within ~5 cells of the first one, past any filter on the first target; `DogWork.FindJob` now drops queued targets that fail its own radius and nearest-stable checks.
- **A minified building still ticks.** `MinifiedThing` ticks the building inside it, with no map: guard `Tick` with `Spawned`.
- **Screenshots from a scenario.** `ScreenCapture.CaptureScreenshot` works on Xvfb; pause, `CameraDriver.SetRootPosAndSize`, then count frames (the runner checks steps once per frame, and no ticks pass while paused). Files land in `.devlog/screens/`.
- **Test map size.** `SetupForQuickTestPlay` hard-codes 250; `-quadrunner-mapsize` (`QR_MAP_SIZE`, default 200) overrides it, and each scenario's result line ends with its wall time and ticks/s. Changing it needs `autotest.py stop` first.
- **Software rendering is the CPU cost.** The instance renders with llvmpipe; at 1280×720 with a thread per core it used ~1800% CPU. 640×360 with `LP_NUM_THREADS=4` ran as fast at ~400%; fewer threads or a bigger window at 4 threads halved the tick rate. Check `ps -o times,etimes` when changing either.
- **Reloaded games ran at Normal speed.** A loaded game fades in, and `TickManager.CurTimeSpeed`'s setter drops any value while `Game.PlayerHasControl` is false (`ScreenFader.IsFading`), so the reload's "resume" step silently did nothing; `Scenario.Resume` now returns whether it stuck and the step retries (~30 ticks). Every hostile scenario that reloaded had run at ~60 ticks/s.
- **Vanilla defend-point raiders chase.** `LordJob_DefendPoint` defaults to a 28-cell defend radius and a flee toil; scenario raiders that hit a dog chased it home. `SpawnRaider` passes `wanderRadius 2, defendRadius 3, addFleeToil false`.
- **Unarmed pirates die to Combatdog claws** rather than going down, so capture tests down their targets with `DownBleeding` instead of relying on the fight.
- **A docked pawn's job reads "Carried"** (`JobGiver_Carried` from the Despawned think tree): not a bug in a summary.
- **Dead pawns aren't found by ThingID.** `Scenario.ById` misses a killed pawn (its corpse has another ID), so `Fate` prints "gone", and a step that kills a pawn must keep the reference (`p.Kill(null); s.Track(p.Corpse)`).
- **Ping order vs nearest first:** the first ping sends a docked Combatdog at once after that attacker; a nearer one pinged in the same tick is taken at the next re-sort (60-tick expiry with `checkOverrideOnExpire`).
- **Dogs in the field go home.** A Quadrunner placed out with nothing to do walks back to its stable; spawn later hostiles relative to something that stays put, or they appear by the stable and the drafted colonists shoot them.
- **Glow textures have no alpha.** `Things/Mote/LightningGlow` is meant for the additive `MoteGlow` shader; with `Transparent` it draws a dark square, and additive alone hardly shows on bright ground. The siren uses its own lamp texture with alpha.
- **Targeter re-arming.** `Targeter` calls `StopTargeting()` after the click action, so begin the next targeting from `actionWhenFinished` (guarded by a flag set in the action, since right-click also calls it).
- **Battery drains as soon as a dog walks.** "Full and out" never holds past the dispatch tick: check full while docked, then spawned.
- **No digit at the end of a ThingDef name.** ThingIDs append a number to the defName, so `QR_Combatdog2` broke save/load reference resolution ("ends with a numerical digit" config errors, then overflowed IDs); tiers are `QR_CombatdogTwo/Three/Four`.
- **Vanilla undrafts after 10000 idle ticks.** `AutoUndrafter` undrafts a colonist after 10000 ticks of `Wait_Combat` with no hostile around; a long scenario then had colonists hauling and building. `Scenario.Check` resets it each frame (`Notify_Drafted` through `AccessTools.FieldRefAccess`).
- **Drafted colonists shoot ~25 cells out.** Scenarios draft every colonist, so raiders meant for a Combatdog go 30+ cells from the stable.
- **Run fast.** Scenarios ran at Ultrafast with vanilla's forced normal speed off via `DebugViewSettings.neverForceNormalSpeed`; Dev **Tweak values → UltraSpeedBoost** raises it further. *(QB)*
- **One defend point clusters pawns.** A single `LordJob_DefendPoint` for several pawns gathers them at one spot. Give each hostile its own lord. *(QB)*
- **Screenshots: one window only.** Capture by exact window class `RimWorldLinux` on the instance's own `DISPLAY` (read from `/proc/<pid>/environ`); a title match also hits browser tabs about RimWorld. Never `import -window root`. *(QB)*
- **`build.sh` always restamps the DLL**, even for XML-only changes, so every build needs a fresh launch. Fine for the test instance. *(QB)*
- **`[TweakValue]` only works on static fields.** Rider draw offsets live in XML; to tune them live, mirror them into static TweakValue fields in dev mode. *(QB)*

- **"Gone" in a summary can mean dead:** a killed pawn's corpse has another ThingID, so `ById` finds nothing. Robodog deaths are logged (`killed by <instigator> (<damage>, <weapon>, <part>)`).
- **At Ultrafast one frame is ~20 ticks:** a state that lasts a tick or two (docked at 100% before the stable dispatches it, a short walk home from a nearby bed, a retreat that ends docked) can fall between two checks. Latch flags across frames, use thresholds (battery ≥ 98%), or accept the end state.

### Assets

- **SVG is the source of truth.** Edit `Art/**/*.svg`, never the PNGs, then run `tools/export-textures.sh`. The PNG size comes from the SVG's `width`/`height`. *(QB)*
- **Canvas sizes.** At 128 px per tile, a 2×2 building is a 256×256 canvas, and the canvas matches `drawSize`. Sizes must be multiples of 4, ideally powers of two. Pawns need `_north`/`_east`/`_south` (west mirrors east). *(QB)*
- **Compression edges.** RimWorld compresses textures in memory, which can add white fringes around transparent edges. Keep edge colors in the transparent pixels, or add a 1%-opacity dark outline. *(QB)*
- **Preview limits.** `About/Preview.png` must stay under 1 MB (Steam rejects larger) and is 640×360. `About/ModIcon.png` (64×64) is optional. *(QB)*
