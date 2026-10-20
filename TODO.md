# TODO

Tracks [Plan 0001](docs/plans/0001-quadrunner.md). Tick a target only once **all** of its acceptance boxes in the plan are ticked. Workflow rules are in [AGENTS.md](AGENTS.md); the iteration log is [docs/test-runs.md](docs/test-runs.md).

## Progress

- [x] **T0**: Scaffold and the automated test loop (tools, mod skeleton, harness, baseline, first real runs, in-game restart, watchdog)
- [ ] **T1**: Dogs and the stable (5 of 6 boxes; the debug actions box waits for T3–T4) (race, stable, assemble bill, docking, Overview tab, debug actions)
- [x] **T2**: Battery and charging (drain, charge-spot search, stable charging, charging edge cases)
- [x] **T3**: Dispatch and work (haul, clean, construction delivery, radii, raids, stable power/destroy/uninstall)
- [ ] **T4**: Power-down and retrieval (carry home, field battery and kit, wrecks, raider carry-off)
- [ ] **T5**: Medirunner (rescue order, triage)
- [ ] **T6**: Riding (stat part, ridden job, dismount rules, draw offset)
- [ ] **T7**: Combatdog Tier 1, pings and patrols
- [ ] **T8**: Combatdog tiers 2–4
- [ ] **T9**: Polish and release

## Current state

- Next: **T4** (power-down and retrieval), code written, first run pending. T3 done: all 6 boxes ticked on run 2026-10-20 00:21 (Run all 17/17 PASS, 0 new errors). T1's last box (debug actions: power down, dispatch) comes with T4. T2 done (run 2026-10-19 22:27), T0 done (run 2026-10-19 23:20).
- Usage status line: `tools/statusline.py` written; wiring it into settings waits on the user.
- Last run: 2026-10-20 00:21, build `f4f1996-dirty.20261020T001251Z`, Run all 17/17 PASS, 0 new errors, beside the user's open game.
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
- **A kill leaves a crash dump.** Killing an instance (baseline, watchdog) makes Steam's socket thread assert and breakpad writes `/tmp/dumps/assert_*.dmp` (upload fails: no libcurl). Harmless; the in-game quit and restart shut down cleanly with no dump.
- **Other Xvfb displays aren't ours.** `Xvfb :99`–`:110` (started by `xvfb-run`) belong to other shells, `:100` to the agent's own. The harness uses `:90` and up and stops only the pid it recorded, after checking its name is `Xvfb`.
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

### Log

- **No timestamps.** Player.log has none, so `autotest.py` cuts per launch instead: it moves the live log to `.devlog/runs/<launch>/` before every restart, and reads a run's errors from the byte offset at which it sent `run`. *(QB)*
- **Deduplicated stack traces.** 1.6 prints a repeated stack trace once and later writes `[Ref XXXXXXXX] Duplicate stacktrace, see ref for original`. `autotest.py` resolves these; when reading the raw log, search for the first block with the same `[Ref …]`. *(QB)*
- **Baseline per mod list.** Signatures are the first 60 characters of the error line with digits replaced by `N`. The test instance's list is fixed (Core + Harmony + Quadrunner), so one baseline from a run without Quadrunner lasts until the game updates. *(QB)*
- **Line numbers in traces.** With `DebugType embedded`, Unity's Mono prints `File.cs:line` for our frames, so trust those line numbers. *(QB)*
- **Filtered views can miss things.** When a result surprises you, read the raw `Player.log` of that run. *(QB)*

### Testing

- **Scenario runner design that worked** *(QB)*: static, never-saved scenario state; `Expect(label, Func<bool>)` / `Do` / `Wait(ticks)` steps checked every tick; `SCENARIO <name>: start …` and `PASS/FAIL` lines with tick counts; **Run all** queues every scenario and logs one `n/m PASS` summary. Starting a scenario removes the previous one's things.
- **Static state survives a reload.** Assemblies don't reload when a save loads, so a static scenario runner can save, reload and keep checking its steps. Re-find things by ID after the load; references from before it are stale.
- **Find clear ground by scanning, not sampling.** `CellFinder.TryFindRandomCellNear` (3000 random tries) missed the few clear 15×15 areas on a dense map; `FindClearArea` now checks every cell in range, nearest first.
- **Quicktest maps differ per launch.** Each launch generates a new map (trees, chunks, crashlanded items near the center); setups must find clear ground with `Walkable` (trees aren't `Standable`) and clear plants and items themselves. Scenarios turn the storyteller off while they run (a random event once left a colonist's relation to an unsaved pawn: 2 red errors on save/load).
- **Run fast.** Scenarios ran at Ultrafast with vanilla's forced normal speed off via `DebugViewSettings.neverForceNormalSpeed`; Dev **Tweak values → UltraSpeedBoost** raises it further. *(QB)*
- **One defend point clusters pawns.** A single `LordJob_DefendPoint` for several pawns gathers them at one spot. Give each hostile its own lord. *(QB)*
- **Screenshots: one window only.** Capture by exact window class `RimWorldLinux` on the instance's own `DISPLAY` (read from `/proc/<pid>/environ`); a title match also hits browser tabs about RimWorld. Never `import -window root`. *(QB)*
- **`build.sh` always restamps the DLL**, even for XML-only changes, so every build needs a fresh launch. Fine for the test instance. *(QB)*
- **`[TweakValue]` only works on static fields.** Rider draw offsets live in XML; to tune them live, mirror them into static TweakValue fields in dev mode. *(QB)*

### Assets

- **SVG is the source of truth.** Edit `Art/**/*.svg`, never the PNGs, then run `tools/export-textures.sh`. The PNG size comes from the SVG's `width`/`height`. *(QB)*
- **Canvas sizes.** At 128 px per tile, a 2×2 building is a 256×256 canvas, and the canvas matches `drawSize`. Sizes must be multiples of 4, ideally powers of two. Pawns need `_north`/`_east`/`_south` (west mirrors east). *(QB)*
- **Compression edges.** RimWorld compresses textures in memory, which can add white fringes around transparent edges. Keep edge colors in the transparent pixels, or add a 1%-opacity dark outline. *(QB)*
- **Preview limits.** `About/Preview.png` must stay under 1 MB (Steam rejects larger) and is 640×360. `About/ModIcon.png` (64×64) is optional. *(QB)*
