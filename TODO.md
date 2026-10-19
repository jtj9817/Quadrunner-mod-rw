# TODO

Tracks [Plan 0001](docs/plans/0001-quadrunner.md). Tick a target only once **all** of its acceptance boxes in the plan are ticked. Workflow rules are in [AGENTS.md](AGENTS.md); the iteration log is [docs/test-runs.md](docs/test-runs.md).

## Progress

- [ ] **T0**: Scaffold and the automated test loop (tools, About.xml, csproj, test profile, baseline, `autotest.sh`, save/load step, watchdog)
- [ ] **T1**: Dogs and the stable (race, stable, assemble bill, docking, Overview tab, debug actions)
- [ ] **T2**: Battery and charging (drain, charge-spot search, stable charging, charging edge cases)
- [ ] **T3**: Dispatch and work (haul, clean, construction delivery, radii, raids, stable power/destroy/uninstall)
- [ ] **T4**: Power-down and retrieval (carry home, field battery and kit, wrecks, raider carry-off)
- [ ] **T5**: Medirunner (rescue order, triage)
- [ ] **T6**: Riding (stat part, ridden job, dismount rules, draw offset)
- [ ] **T7**: Combatdog Tier 1, pings and patrols
- [ ] **T8**: Combatdog tiers 2–4
- [ ] **T9**: Polish and release

## Current state

- Next: **T0**. Docs and repo are in place; no tools, code or test profile yet. Before writing the test harness, tell the user when their game has to be closed (user, 2026-10-19).
- Last run: none.
- Baseline: none yet (capture from a test-instance run without Quadrunner).

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

### Launching the test instance (from the decompiled 1.6.4871 source, unverified in a run)

- **`-savedatafolder=<path>`** overrides the whole profile (`GenFilePaths.SaveDataFolderPath`). `GenCommandLine.TryGetCommandLineArg` splits each argument on `=` and needs exactly two parts, so the path must not contain `=`.
- **`-quicktest`** makes `QuickStarter` load the Play scene directly, and `Root_Play` generates a quicktest map (`SetupForQuickTestPlay`). No menu clicks needed.
- **Custom launch arguments work.** `GenCommandLine.CommandLineArgPassed("x")` matches `x` or `-x` case-insensitively, so the mod can read its own `-quadrunner-autotest` flag.
- **Quitting from code.** `Root.Shutdown()` shuts Steam down, cleans the temp folder and quits; use it at the end of an automated run.
- **Steam.** `SteamManager.InitIfNeeded` calls `SteamAPI.RestartAppIfNecessary`; `steam_appid.txt` sits in the game folder, so a direct launch should not relaunch through Steam. Workshop mods are listed only when `SteamAPI.Init()` succeeds, i.e. while Steam runs.
- **Harmony comes from the workshop.** Item 2009463077 (`brrainz.harmony`, with a `1.6/` folder) under `steamapps/workshop/content/294100/`. Without Steam running it won't load, and neither will we.
- **Prefs that matter for a headless run** (`PrefsData`): `devMode`, `runInBackground` (keep ticking without focus), `pauseOnError`, `fullscreen`, `screenWidth`/`screenHeight`, `volumeMaster`, `resetModsConfigOnCrash` (default true: a crash could reset the test ModsConfig).

### Game / API

- **Use the offline wiki.** The mirror at `/mnt/0B8533211952FCF2/rimworld-wiki-local/data/site/pages/` has every Modding Tutorials page as Markdown, so grep it before fetching the web. *(QB)*
- **Look up vanilla, don't guess.** Use `tools/vanilla.sh grep` / `def`. Grepping `strings` on `Assembly-CSharp.dll` gives false negatives because the metadata heap shares suffixes. If ilspycmd complains about the runtime, set `DOTNET_ROLL_FORWARD=Major`. *(QB)*
- **Debug Actions live in `LudeonTK`.** `DebugActionAttribute`, `DebugActionType` and `AllowedGameStates` are in that namespace in 1.6, not `Verse`, so add `using LudeonTK;`. *(QB)*
- **A method named `Log` hides `Verse.Log`.** Inside a class with a `Log` method (our `QR`), write `Verse.Log.Message(...)`. *(QB)*
- **Dev-mode-only logging.** Log through `QR.Log`, which prints only when `Prefs.DevMode` is on and adds the prefix. The `loaded build` line is the one exception: always print it, `status.sh`/`autotest.sh` compare it with `.devlog/last-build`. *(QB)*
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

- **No timestamps.** Player.log has none, so use `qrlog.sh mark` / `since-mark` (byte offsets) to tell before from after. *(QB)*
- **Deduplicated stack traces.** 1.6 prints a repeated stack trace once and later writes `[Ref XXXXXXXX] Duplicate stacktrace, see ref for original`. `qrlog.sh` resolves these; when reading the raw log, search for the first block with the same `[Ref …]`. *(QB)*
- **Baseline per mod list.** Signatures are the first 60 characters of the error line with digits replaced by `N`. The test instance's list is fixed (Core + Harmony + Quadrunner), so one baseline from a run without Quadrunner lasts until the game updates. *(QB)*
- **Line numbers in traces.** With `DebugType embedded`, Unity's Mono prints `File.cs:line` for our frames, so trust those line numbers. *(QB)*
- **Filtered views can miss things.** When a result surprises you, read the raw `Player.log` of that run. *(QB)*

### Testing

- **Scenario runner design that worked** *(QB)*: static, never-saved scenario state; `Expect(label, Func<bool>)` / `Do` / `Wait(ticks)` steps checked every tick; `SCENARIO <name>: start …` and `PASS/FAIL` lines with tick counts; **Run all** queues every scenario and logs one `n/m PASS` summary. Starting a scenario removes the previous one's things.
- **Static state survives a reload.** Assemblies don't reload when a save loads, so a static scenario runner can save, reload and keep checking its steps. Re-find things by ID after the load; references from before it are stale.
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
