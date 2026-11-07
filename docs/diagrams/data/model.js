window.SYSTEM_MODEL = {
 "meta": {
  "name": "Quadrunner",
  "mode": "brownfield",
  "version": "1.0.0",
  "description": "A RimWorld 1.6 mod: stable-built robot dogs that haul, clean, build, ride, rescue and fight, coordinated map-wide by one dispatcher per map. Requirements are taken from the plan acceptance boxes; every verification names the scenario or tool that proved it.",
  "source_of_truth": "docs/diagrams/model.json",
  "generated": "2026-11-07"
 },
 "nodes": [
  {
   "id": "sys.quadrunner",
   "name": "Quadrunner mod",
   "level": 0,
   "kind": "system",
   "parent": null,
   "owner": "jtj9817",
   "description": "One RimWorld mod (Core + Harmony only): an assembly in namespace Quadrunner plus XML defs, textures and stat patches, shipped as dist/Quadrunner. Saved-game identity josh9817.quadrunner."
  },
  {
   "id": "sub.foundation",
   "name": "Foundation & UI",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Process bootstrap, the XML definition set, mod settings and the player-facing stable UI.",
   "evidence": [
    "Source/Quadrunner/QR.cs"
   ]
  },
  {
   "id": "sub.stable",
   "name": "Stable & fabrication",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "The Quadrunner stable building and every way a dog is created, rebuilt or upgraded: docking bays, recipes, tiers, arsenals.",
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs"
   ]
  },
  {
   "id": "sub.pawn",
   "name": "Robodog pawn platform",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "What a robodog is: six races, the CompRobodog identity comp, docking behavior, the priority-ordered think tree.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs",
    "Defs/ThingDefs_Races/QR_Races.xml"
   ]
  },
  {
   "id": "sub.power",
   "name": "Energy & charging",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Battery drain, the stable-versus-conduit charge decision, and the cached conduit charge-spot search.",
   "evidence": [
    "Source/Quadrunner/Charging.cs",
    "Source/Quadrunner/ChargeSpots.cs"
   ]
  },
  {
   "id": "sub.dispatch",
   "name": "Shared dispatch network",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "One DogNetwork per map: discovery intake, normal and war queues, assignment, reconciliation, coordinator election, idle surveying. The only thing that matches work to dogs.",
   "evidence": [
    "Source/Quadrunner/Network.cs"
   ]
  },
  {
   "id": "sub.rescue",
   "name": "Rescue, firefighting & hose",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Medirunner behaviors: patient triage and rescue jobs, the integrated foam sprayer, and the worker's water hose for blood/firefoam patches.",
   "evidence": [
    "Source/Quadrunner/Rescue.cs",
    "Source/Quadrunner/Firefighting.cs",
    "Source/Quadrunner/Hose.cs"
   ]
  },
  {
   "id": "sub.recovery",
   "name": "Retrieval & field maintenance",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Getting powered-down, wrecked or casualty dogs home: worker carry jobs, colonist carry menu, field battery/repair-kit delivery, the unreachable alert.",
   "evidence": [
    "Source/Quadrunner/Retrieval.cs",
    "Source/Quadrunner/FieldFix.cs"
   ]
  },
  {
   "id": "sub.combat",
   "name": "Combatdogs & drafting",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "War-queue combat jobs, local mixed-tier focus-fire packs, patrol/guard idle, capture, and the draft controller patches.",
   "evidence": [
    "Source/Quadrunner/Combat.cs",
    "Source/Quadrunner/PackCombat.cs",
    "Source/Quadrunner/Drafting.cs"
   ]
  },
  {
   "id": "sub.riding",
   "name": "Riding",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Colonists ride Quadrunners: float menu, the rider-driven job pair, the ridden speed stat part, rider drawing.",
   "evidence": [
    "Source/Quadrunner/Riding.cs"
   ]
  },
  {
   "id": "sub.render",
   "name": "Rendering & animation",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Per-part walk gait, action poses, weapon mounts and rider/patient layering, plus the original SVG artwork and its export pipeline.",
   "evidence": [
    "Source/Quadrunner/Rendering.cs",
    "Art/"
   ]
  },
  {
   "id": "sub.removal",
   "name": "Clean removal",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "The explicit, destructive cutover that exports a save with no Quadrunner content or references.",
   "evidence": [
    "Source/Quadrunner/Removal.cs"
   ]
  },
  {
   "id": "sub.verification",
   "name": "Verification harness",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Test-only: the in-game driver and scenario suite, the host-side harness, its self-test against a fake game, and the independent removal observer. Ships inside the assembly but is gated by the -quadrunner-autotest launch flag and dev mode; never enters saves.",
   "evidence": [
    "Source/Quadrunner/AutoTest.cs",
    "tools/autotest.py"
   ]
  },
  {
   "id": "comp.bootstrap",
   "name": "Bootstrap",
   "level": 2,
   "kind": "service",
   "parent": "sub.foundation",
   "tech": [
    "C# StaticConstructorOnStartup"
   ],
   "description": "Logs the build id, applies every Harmony patch, starts the test driver. The one entry point the game calls at startup.",
   "responsibilities": [
    "Build-id log line the harness compares",
    "Harmony PatchAll",
    "Create AutoTest MonoBehaviour"
   ],
   "evidence": [
    "Source/Quadrunner/QR.cs:8"
   ]
  },
  {
   "id": "comp.defs",
   "name": "XML definition set",
   "level": 2,
   "kind": "library",
   "parent": "sub.foundation",
   "tech": [
    "RimWorld XML defs"
   ],
   "description": "Races, stable, items, recipes, jobs, think/render trees, Hediffs, plus the two MoveSpeed StatParts patched onto vanilla via Patches/QR_Stats.xml.",
   "responsibilities": [
    "All tunables (DefModExtensions)",
    "StatPart_RobodogRaid and StatPart_Riding on vanilla MoveSpeed"
   ],
   "evidence": [
    "Defs/",
    "Patches/QR_Stats.xml",
    "Source/Quadrunner/QR_DefOf.cs"
   ]
  },
  {
   "id": "comp.settings",
   "name": "Mod settings",
   "level": 2,
   "kind": "ui",
   "parent": "sub.foundation",
   "description": "The one setting (turbine distance, default 6) and the entry point for Prepare-save-for-removal.",
   "evidence": [
    "Source/Quadrunner/Settings.cs:6",
    "Source/Quadrunner/Settings.cs:18"
   ]
  },
  {
   "id": "comp.stable-ui",
   "name": "Overview tab & debug actions",
   "level": 2,
   "kind": "ui",
   "parent": "sub.foundation",
   "description": "Per-dog roster (type, battery, health, activity) with Hearthline lore hovers and upgrade orders; dev-mode scenario/spawn/battery/debug actions.",
   "evidence": [
    "Source/Quadrunner/ITab_StableOverview.cs:14",
    "Source/Quadrunner/DebugActions.cs"
   ]
  },
  {
   "id": "comp.stable-building",
   "name": "Building_Stable",
   "level": 2,
   "kind": "service",
   "parent": "sub.stable",
   "tech": [
    "thingClass of QR_Stable"
   ],
   "description": "The 200 W work-table building: 15 shared dock slots (a typed despawned pawn container), charge/repair tick, bills host, wreck delivery bay with dock occupancy lights, patrol waypoints, optional network host.",
   "responsibilities": [
    "Dock/undock container",
    "Charge + repair while powered",
    "Wreck bay and powered rebuild",
    "Ticks the map's DogNetwork"
   ],
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs:22",
    "Defs/ThingDefs_Buildings/QR_Stable.xml"
   ]
  },
  {
   "id": "comp.fabrication",
   "name": "Assembly & recipes",
   "level": 2,
   "kind": "service",
   "parent": "sub.stable",
   "description": "Recipe workers for assemble/rebuild/upgrade: product is a pawn that docks straight into the stable; the do-bills work giver exposes only this stable's unprocessed wrecks.",
   "evidence": [
    "Source/Quadrunner/Assembly.cs:16",
    "Source/Quadrunner/Assembly.cs:33",
    "Source/Quadrunner/Assembly.cs:77",
    "Source/Quadrunner/Tiers.cs:182"
   ]
  },
  {
   "id": "comp.tiers",
   "name": "Tier arsenal & migration",
   "level": 2,
   "kind": "service",
   "parent": "sub.stable",
   "description": "Per-tier gun choices and fixed paired mounts, the paired-shot verb, built-in gun comp, and the one-time in-place migration that fits modern loadouts on legacy T3/T4 dogs.",
   "evidence": [
    "Source/Quadrunner/Tiers.cs:37",
    "Source/Quadrunner/Tiers.cs:155",
    "Source/Quadrunner/Tiers.cs:171"
   ]
  },
  {
   "id": "comp.comprobodog",
   "name": "CompRobodog",
   "level": 2,
   "kind": "service",
   "parent": "sub.pawn",
   "description": "The dog's persistent identity: battery fraction, stable affiliation, gun picks, powered-down hediff, patrol cursor, next smoke launch tick; draws the tail siren; reports casualties and attack pings.",
   "responsibilities": [
    "Battery drain per cell moved",
    "Seek-charge latch at 10%",
    "The only affiliation record",
    "Casualty/ping reporting"
   ],
   "evidence": [
    "Source/Quadrunner/Robodog.cs:30",
    "Source/Quadrunner/Robodog.cs:274"
   ]
  },
  {
   "id": "comp.docking",
   "name": "Docking behavior",
   "level": 2,
   "kind": "service",
   "parent": "sub.pawn",
   "description": "Dock home (adopting the nearest reachable stable with a free slot when homeless), idle return, and the wait-in-a-non-bedroom-room fallback when no stable exists.",
   "evidence": [
    "Source/Quadrunner/Docking.cs:14",
    "Source/Quadrunner/Docking.cs:48",
    "Source/Quadrunner/Docking.cs:69"
   ]
  },
  {
   "id": "comp.races",
   "name": "Races & think tree",
   "level": 2,
   "kind": "library",
   "parent": "sub.pawn",
   "description": "Six races on abstract QR_RobodogBase: non-organic QR_Robotic flesh, Animal or ToolUser intelligence, custom think tree whose priority order is the behavior contract (queued > drafted > charge > network order > dock > wait > wander).",
   "evidence": [
    "Defs/ThingDefs_Races/QR_Races.xml",
    "Defs/ThinkTreeDefs/QR_ThinkTrees.xml"
   ]
  },
  {
   "id": "comp.charging",
   "name": "Charge seeking",
   "level": 2,
   "kind": "service",
   "parent": "sub.power",
   "description": "JobGiver_SeekCharge compares time-to-full (travel + missing/rate) between stable and best conduit spot; the conduit driver draws stored energy from the net's battery comps, fullest first, rechecking the spot every 250 ticks.",
   "evidence": [
    "Source/Quadrunner/Charging.cs:14",
    "Source/Quadrunner/Charging.cs:57"
   ]
  },
  {
   "id": "comp.chargespots",
   "name": "Charge spot cache",
   "level": 2,
   "kind": "service",
   "parent": "sub.power",
   "description": "Static per-map cache of valid conduit charge spots: free standable roofed indoor non-doorway cells near a power transmitter, outside every wind turbine's clear radius; rebuilt on building-signature change or 2500 ticks; volatile rules rechecked per dog per use.",
   "evidence": [
    "Source/Quadrunner/ChargeSpots.cs:27"
   ]
  },
  {
   "id": "comp.network",
   "name": "DogNetwork",
   "level": 2,
   "kind": "service",
   "parent": "sub.dispatch",
   "description": "One dispatcher per map, a static dictionary never saved. Owns discovery intake, the normal and war queues (NetworkState saved on exactly one elected node), assignment by real path cost, reconciliation with progress timeout, war-mode transitions and coordinator election.",
   "responsibilities": [
    "Queued \u2192 Assigned \u2192 Acknowledged \u2192 Working",
    "Dedupe by (kind, targetId) per queue",
    "Coordinator election and failover",
    "War-mode suspension and revalidation"
   ],
   "evidence": [
    "Source/Quadrunner/Network.cs:98",
    "Source/Quadrunner/Network.cs:14",
    "Source/Quadrunner/Network.cs:65",
    "Source/Quadrunner/Network.cs:165",
    "Source/Quadrunner/Network.cs:425",
    "Source/Quadrunner/Network.cs:1023"
   ]
  },
  {
   "id": "comp.work",
   "name": "DogWork & job giver",
   "level": 2,
   "kind": "service",
   "parent": "sub.dispatch",
   "description": "Local fog-limited discovery via the Quadrunner race's work-giver scanners; job construction for one assigned target; JobGiver_RobodogWork as the think-tree bridge to DogNetwork.TakeOrder; the raid speed stat part and the construction-eligibility Harmony patch.",
   "evidence": [
    "Source/Quadrunner/Work.cs:78",
    "Source/Quadrunner/Work.cs:261",
    "Source/Quadrunner/Work.cs:293",
    "Source/Quadrunner/Work.cs:329"
   ]
  },
  {
   "id": "comp.survey",
   "name": "Survey",
   "level": 2,
   "kind": "service",
   "parent": "sub.dispatch",
   "description": "Idle exploration policy: classifies Home footprints (enclosed rooms, stockpiles, growing zones, other zones, unzoned Home, then the exterior), picks route-ranked entry candidates by real path cost, and credits coverage only from actual revealed 15-cell scans.",
   "evidence": [
    "Source/Quadrunner/Survey.cs:14",
    "Source/Quadrunner/Survey.Geometry.cs",
    "Source/Quadrunner/Survey.cs:370"
   ]
  },
  {
   "id": "comp.rescue",
   "name": "Rescue",
   "level": 2,
   "kind": "service",
   "parent": "sub.rescue",
   "description": "Map-wide patient ranking (colonist \u2192 guest \u2192 prisoner \u2192 colony animal, fastest-bleeding within rank, enemies and robodogs excluded) and vanilla rescue/capture job construction; CanPotentiallyRescue pre-check rejects unassignable patients without spawning a medic.",
   "evidence": [
    "Source/Quadrunner/Rescue.cs:16",
    "Source/Quadrunner/Rescue.cs:46"
   ]
  },
  {
   "id": "comp.firefighting",
   "name": "Foam sprayer",
   "level": 2,
   "kind": "service",
   "parent": "sub.rescue",
   "description": "Every Medirunner carries a built-in foam sprayer (vanilla foam-turret arc verb). Revealed Home-area fires become war-queue tasks handled after rescues; nearest reachable unclaimed fire per responder; 2% battery per burst; 180-tick quiet standby; storm field alert.",
   "evidence": [
    "Source/Quadrunner/Firefighting.cs:14",
    "Source/Quadrunner/Firefighting.cs:100"
   ]
  },
  {
   "id": "comp.hose",
   "name": "Water hose",
   "level": 2,
   "kind": "service",
   "parent": "sub.rescue",
   "description": "The worker's integrated hose: discovered contiguous Home-area blood/firefoam patches of \u22654 tiles are sprayed in an 11\u00d77 band (1.5\u00d7 clean work per tile), continuing through the whole patch; walls block; smaller patches fall back to ordinary cleaning.",
   "evidence": [
    "Source/Quadrunner/Hose.cs:17",
    "Source/Quadrunner/Hose.cs:173"
   ]
  },
  {
   "id": "comp.retrieval",
   "name": "Retrieval",
   "level": 2,
   "kind": "service",
   "parent": "sub.recovery",
   "description": "Carries powered-down dogs (and Combatdog wrecks) to the nearest stable with a free slot; colonist Carry-to-stable float menu; raider kidnap postfix; the unreachable-dog alert. CanPotentiallyRecover pre-check prevents speculative undocks.",
   "evidence": [
    "Source/Quadrunner/Retrieval.cs:15",
    "Source/Quadrunner/Retrieval.cs:61",
    "Source/Quadrunner/Retrieval.cs:183",
    "Source/Quadrunner/Retrieval.cs:305",
    "Source/Quadrunner/Retrieval.cs:332"
   ]
  },
  {
   "id": "comp.fieldfix",
   "name": "Field fixes",
   "level": 2,
   "kind": "service",
   "parent": "sub.recovery",
   "description": "Robodog battery and repair-kit items, their bills, and the work giver that has colonists auto-deliver them to powered-down or damaged dogs in the field within a 2500-tick window.",
   "evidence": [
    "Source/Quadrunner/FieldFix.cs:22",
    "Source/Quadrunner/FieldFix.cs:51",
    "Source/Quadrunner/FieldFix.cs:83"
   ]
  },
  {
   "id": "comp.combat",
   "name": "Combat jobs",
   "level": 2,
   "kind": "service",
   "parent": "sub.combat",
   "description": "Combatdogs spot standing hostiles within 15 cells with LOS and report them; AttackStatic from position, else walk to a cast position, else melee; downed humans captured to a bed or carried to the nearest empty room; patrol waypoints then guard when idle.",
   "evidence": [
    "Source/Quadrunner/Combat.cs:22",
    "Source/Quadrunner/Combat.cs:213"
   ]
  },
  {
   "id": "comp.packcombat",
   "name": "Pack tactics",
   "level": 2,
   "kind": "service",
   "parent": "sub.combat",
   "description": "Available T3/T4 leaders recruit nearby Combatdogs of any tier within 15 cells; the pack focuses the queued reachable standing hostile nearest to any member, retains a valid focus, and re-selects at native job boundaries so engagement is continuous.",
   "evidence": [
    "Source/Quadrunner/PackCombat.cs:14"
   ]
  },
  {
   "id": "comp.drafting",
   "name": "Drafting patches",
   "level": 2,
   "kind": "service",
   "parent": "sub.combat",
   "description": "Two Harmony patches give player Combatdogs a vanilla draft controller and pass the colony-subhuman control gate only while drafted; the network excludes drafted dogs and SetBattery undrafts at 10%.",
   "evidence": [
    "Source/Quadrunner/Drafting.cs"
   ]
  },
  {
   "id": "comp.riding",
   "name": "Riding",
   "level": 2,
   "kind": "service",
   "parent": "sub.riding",
   "description": "Float-menu Ride to\u2026; the rider's JobDriver_RideTo drives the ride while the dog's JobDriver_BeingRidden mirrors it; a MoveSpeed stat part replaces speed with riddenMoveSpeed 7; auto-dismount on arrival, indoors, drafting or power-down; DrawPos postfix draws the rider.",
   "evidence": [
    "Source/Quadrunner/Riding.cs:32",
    "Source/Quadrunner/Riding.cs:122",
    "Source/Quadrunner/Riding.cs:197",
    "Source/Quadrunner/Riding.cs:229",
    "Source/Quadrunner/Riding.cs:282"
   ]
  },
  {
   "id": "comp.render-nodes",
   "name": "Render nodes",
   "level": 2,
   "kind": "service",
   "parent": "sub.render",
   "description": "Gait worker swings four legs from distance walked along the facing (stops with the dog); pose worker adds pounce/recoil/brace/scoop; mount layer draws carried pawns, paired T4 guns and the smoke launcher; BodyAngle postfix lays a carried patient along the back.",
   "evidence": [
    "Source/Quadrunner/Rendering.cs:116",
    "Source/Quadrunner/Rendering.cs:294",
    "Source/Quadrunner/Rendering.cs:314"
   ]
  },
  {
   "id": "comp.art",
   "name": "Artwork & export",
   "level": 2,
   "kind": "library",
   "parent": "sub.render",
   "description": "Original SVG sources per dog/stable/item with id=\"part-\u2026\" layers; exported to per-facing PNGs by Inkscape so Graphic_Multi mirrors west from east.",
   "evidence": [
    "Art/",
    "Textures/",
    "tools/export-textures.sh"
   ]
  },
  {
   "id": "comp.removal",
   "name": "Removal export",
   "level": 2,
   "kind": "service",
   "parent": "sub.removal",
   "description": "Prepare-save cutover: writes QR_before_removal backup, stops mod jobs, releases riders/patients/cargo (to a caravan when off-map), destroys all mod things including in-flight shots, clears caches, then scrubs def references from the exported XML while keeping battle-history IDs.",
   "evidence": [
    "Source/Quadrunner/Removal.cs:14"
   ]
  },
  {
   "id": "comp.autotest",
   "name": "AutoTest driver",
   "level": 2,
   "kind": "service",
   "parent": "sub.verification",
   "description": "MonoBehaviour (never saved, never a GameComponent): heartbeat + JSONL events, one-file-at-a-time command polling, in-game restart via GenCommandLine.Restart(), closes forcePause windows; only speaks the protocol when launched with -quadrunner-autotest.",
   "evidence": [
    "Source/Quadrunner/AutoTest.cs:24",
    "Source/Quadrunner/AutoTest.cs:214"
   ]
  },
  {
   "id": "comp.scenarios",
   "name": "Scenario suite",
   "level": 2,
   "kind": "service",
   "parent": "sub.verification",
   "description": "The executable specification: Do/Expect/Wait/SaveAndReload DSL, tracked ThingIDs, storyteller disabled, filtered Run-all order; partials per feature area. Fixtures build their own obstacles on a private quicktest map.",
   "evidence": [
    "Source/Quadrunner/Scenario.cs",
    "Source/Quadrunner/Scenarios.cs"
   ]
  },
  {
   "id": "comp.host-harness",
   "name": "Host harness",
   "level": 2,
   "kind": "job",
   "parent": "sub.verification",
   "tech": [
    "python3 -I, stdlib only"
   ],
   "description": "tools/autotest.py and friends: lint/build/deploy, private profile and Xvfb launch, stale-build restart, command dispatch, hang/crash watchdog, baseline error diff, packaged pkg-run with BUILD/modRoot validation; plus build.sh/package.sh.",
   "evidence": [
    "tools/autotest.py",
    "tools/build.sh",
    "tools/package.sh",
    "tools/env.sh"
   ]
  },
  {
   "id": "comp.selftest",
   "name": "Harness self-test",
   "level": 2,
   "kind": "job",
   "parent": "sub.verification",
   "description": "tools/selftest.sh drives the harness against a fake game speaking the same protocol: baseline, cold start, in-game restart, filters, failures, error diff, hang watchdog, stop, package source and dev-link restoration. 19 cases.",
   "evidence": [
    "tools/selftest.sh",
    "tools/selftest/fake_game.py"
   ]
  },
  {
   "id": "comp.removal-probe",
   "name": "Removal observer",
   "level": 2,
   "kind": "job",
   "parent": "sub.verification",
   "description": "An independent test-only mod with no Quadrunner reference: loads the cleaned save in a fresh process, verifies absent assembly and surviving contents, ticks 600, saves/reloads, ticks 600, rejects any new error.",
   "evidence": [
    "tools/removal-probe/",
    "tools/removal-probe/Probe.cs"
   ]
  },
  {
   "id": "actor.player",
   "name": "Player",
   "kind": "person",
   "level": 0,
   "description": "Builds stables, orders assembly/upgrade bills, sets patrol waypoints, rides Quadrunners, drafts Combatdogs, dispatches Rescuedogs, prepares saves for removal."
  },
  {
   "id": "actor.developer",
   "name": "Developer / agent",
   "kind": "person",
   "level": 0,
   "description": "Runs the isolated test harness, reads run records, rebuilds the distributable package. The in-game work loop (AGENTS.md) is executed by this actor, never handed to the player."
  },
  {
   "id": "ext.rimworld",
   "name": "RimWorld 1.6 (Core)",
   "kind": "external",
   "level": 0,
   "description": "The vanilla game: think trees and jobs, work givers, pathfinder, power nets, rooms/zones, raids and threats, render tree, save system.",
   "tech": [
    "1.6.4871",
    "net472"
   ]
  },
  {
   "id": "ext.harmony",
   "name": "Harmony",
   "kind": "external",
   "level": 0,
   "description": "Runtime patch library; the mod's only dependency. Six gameplay patches ship, each where vanilla has no virtual or hook.",
   "tech": [
    "0Harmony 2.4.3"
   ]
  }
 ],
 "edges": [
  {
   "id": "e.player.stable",
   "from": "actor.player",
   "to": "comp.stable-building",
   "label": "bills, waypoints, gizmos",
   "kind": "sync",
   "mechanism": "Vanilla bill UI on the work table plus gizmo buttons: Add patrol waypoints / Clear patrol / Dispatch Rescuedogs."
  },
  {
   "id": "e.player.ride",
   "from": "actor.player",
   "to": "comp.riding",
   "label": "right-click: Ride to\u2026",
   "kind": "sync",
   "mechanism": "FloatMenuOptionProvider_Ride appears on an undrafted colonist's right-click on a stable or an available Quadrunner; destination picked from the map."
  },
  {
   "id": "e.player.draft",
   "from": "actor.player",
   "to": "comp.drafting",
   "label": "draft move/attack orders",
   "kind": "sync",
   "mechanism": "Vanilla draft gizmos once the patches give the Combatdog a draft controller; drafted dogs leave centralized assignment."
  },
  {
   "id": "e.player.settings",
   "from": "actor.player",
   "to": "comp.settings",
   "label": "mod settings, Prepare save",
   "kind": "sync",
   "mechanism": "Options \u2192 Mod settings \u2192 Quadrunner; Prepare-save shows a destructive confirmation before running."
  },
  {
   "id": "e.dev.harness",
   "from": "actor.developer",
   "to": "comp.host-harness",
   "label": "run / pkg-run / removal-check",
   "kind": "sync",
   "mechanism": "CLI with a private profile (-savedatafolder under .devlog), private Xvfb display and heartbeat watchdog; never the player's profile or window; runs beside the player's open game."
  },
  {
   "id": "e.bootstrap.harmony",
   "from": "comp.bootstrap",
   "to": "ext.harmony",
   "label": "PatchAll at startup",
   "kind": "control",
   "mechanism": "Six gameplay patches (riding draw, kidnap victim, construction check, draft controller, control gate, carried-pawn angle) plus a seventh quicktest patch that returns immediately unless launched with -quadrunner-autotest."
  },
  {
   "id": "e.bootstrap.autotest",
   "from": "comp.bootstrap",
   "to": "comp.autotest",
   "label": "creates the driver",
   "kind": "control",
   "mechanism": "MonoBehaviour created in the static constructor, so it survives save/load and never enters a save (a GameComponent would be instantiated into every game and break clean removal)."
  },
  {
   "id": "e.game.stable",
   "from": "ext.rimworld",
   "to": "comp.stable-building",
   "label": "Normal tick, power, bills",
   "kind": "sync",
   "mechanism": "tickerType Normal; 200 W CompPowerTrader \u2014 power loss pauses charging/repair/bill use but not dispatch; CurrentlyUsableForBills requires PowerOn."
  },
  {
   "id": "e.game.dog",
   "from": "ext.rimworld",
   "to": "comp.comprobodog",
   "label": "Pawn.Tick \u2192 CompTick",
   "kind": "sync",
   "mechanism": "Comps tick before jobs. Docking despawns the pawn into the stable's container, so a docked dog does not tick \u2014 the stable's tick keeps the network alive."
  },
  {
   "id": "e.stable.network",
   "from": "comp.stable-building",
   "to": "comp.network",
   "label": "network Tick() every tick",
   "kind": "control",
   "mechanism": "Every spawned dog's CompTick calls DogNetwork.For(Map).Tick() too; the network rate-limits itself to dispatchInterval 60 ticks with a re-entrancy guard, so whichever node ticks first drives it."
  },
  {
   "id": "e.dog.network",
   "from": "comp.comprobodog",
   "to": "comp.network",
   "label": "tick, pings, casualty reports",
   "kind": "event",
   "mechanism": "A dog hit by a hostile reports the attacker, drops the 60-tick raid cache and forces a tick, so a stable-less Combatdog still reacts; ReportCasualty/ReportDeath dedupe by casualty identity."
  },
  {
   "id": "e.network.work",
   "from": "comp.network",
   "to": "comp.work",
   "label": "JobFor(assigned target)",
   "kind": "sync",
   "mechanism": "Jobs are built for exactly one assignee and target; a Clean task tries the hose job for a qualifying patch before ordinary cleaning; ThingID is cached per scan to keep duplicate discovery allocation-free."
  },
  {
   "id": "e.work.game",
   "from": "comp.work",
   "to": "ext.rimworld",
   "label": "work-giver scanners, JobOnThing",
   "kind": "sync",
   "mechanism": "Discovery uses the Quadrunner race's scanners only to test eligibility (15 cells, unfogged, unforbidden, Home + 600-tick age for filth); the job itself is the giver's vanilla JobOnThing, so reservations, storage filters and stack counts stay vanilla. A CanConstruct prefix runs the construction check without skills or workSettings."
  },
  {
   "id": "e.network.survey",
   "from": "comp.network",
   "to": "comp.survey",
   "label": "idle job / sweep credit",
   "kind": "sync",
   "mechanism": "TakeOrder returns a survey job when idle; only actual revealed 15-cell scans credit coverage; war mode stops and invalidates survey legs."
  },
  {
   "id": "e.network.rescue",
   "from": "comp.network",
   "to": "comp.rescue",
   "label": "war-queue rescue tasks",
   "kind": "sync",
   "mechanism": "Patients are map-wide alerts independent of worker scans; assignment sorts war queue by rescue rank then bleeding rate; docked Medirunners undock only when CanPotentiallyRescue passes for the winning assignee (2026-11-07 fix removed speculative undocking)."
  },
  {
   "id": "e.network.fire",
   "from": "comp.network",
   "to": "comp.firefighting",
   "label": "fire tasks after rescues",
   "kind": "sync",
   "mechanism": "Revealed Home fires join the war queue after rescues; each available medic gets the nearest reachable unclaimed fire (squared distance, ThingID tie); travel intent survives reloads via the job's targetB."
  },
  {
   "id": "e.network.combat",
   "from": "comp.network",
   "to": "comp.combat",
   "label": "war-queue combat jobs",
   "kind": "sync",
   "mechanism": "60-tick combat job expiry; pings deploy docked or returning Combatdogs regardless of radius; capture/carry-to-room jobs for downed humans."
  },
  {
   "id": "e.network.pack",
   "from": "comp.network",
   "to": "comp.packcombat",
   "label": "pack refresh",
   "kind": "control",
   "mechanism": "Refreshed after discovery and undocking, before one-assignee work; TakeOrder also requests a selection-only refresh at native job boundaries, never starting a job inside think-tree evaluation."
  },
  {
   "id": "e.network.retrieval",
   "from": "comp.network",
   "to": "comp.retrieval",
   "label": "recovery tasks, sorted first",
   "kind": "sync",
   "mechanism": "Casualty recovery is normal-queue work ranked ahead of utility work and available to healthy Quadrunners or Medirunners after threats clear; war-queue medical emergencies still take priority."
  },
  {
   "id": "e.thinktree.work",
   "from": "comp.races",
   "to": "comp.work",
   "label": "think-tree node \u2192 TakeOrder",
   "kind": "control",
   "mechanism": "QR_Robodog priority order is the contract: player-queued jobs, drafted stance, lord duties, seek-charge, network order (JobGiver_RobodogWork), dock, wait-in-room, wander."
  },
  {
   "id": "e.thinktree.charge",
   "from": "comp.races",
   "to": "comp.charging",
   "label": "node 5: charge first",
   "kind": "control",
   "mechanism": "Charge seeking sits above all work: a dog that wants a charge never takes orders."
  },
  {
   "id": "e.thinktree.dock",
   "from": "comp.races",
   "to": "comp.docking",
   "label": "nodes 7\u20138: dock / wait",
   "kind": "control",
   "mechanism": "Only reached when the network returns no order; peaceful docking preserves an uninstalled home's affiliation, wartime fallback re-homes to any reachable stable with a free slot."
  },
  {
   "id": "e.charging.spots",
   "from": "comp.charging",
   "to": "comp.chargespots",
   "label": "Best(dog) spot query",
   "kind": "sync",
   "mechanism": "Breadth-first over regions the dog may enter, first found candidates by path cost; volatile rules (prison, occupied bed, stored energy, allowed area, fire, hostiles, reservations) rechecked per dog on every use."
  },
  {
   "id": "e.charging.game",
   "from": "comp.charging",
   "to": "ext.rimworld",
   "label": "draw batteryComps, fullest first",
   "kind": "data",
   "mechanism": "conduitChargePerTick 0.0004 per tick taken from the net's stored energy only \u2014 a producing net with empty batteries is a dead charge spot; stops when full, dry, or the spot stops qualifying (rechecked every 250 ticks)."
  },
  {
   "id": "e.chargespots.game",
   "from": "comp.chargespots",
   "to": "ext.rimworld",
   "label": "transmitters, rooms, turbines",
   "kind": "data",
   "mechanism": "Static cache keyed by Map, rebuilt when the colony's building signature changes or after 2500 ticks; turbine exclusion radius is the one mod setting."
  },
  {
   "id": "e.stable.dog",
   "from": "comp.stable-building",
   "to": "comp.comprobodog",
   "label": "charge/repair docked; find by comp",
   "kind": "data",
   "mechanism": "Docked dogs charge at stableChargePerTick 0.0012 (3\u00d7 conduit) and heal one injury per tick while powered; the stable finds its dogs by scanning pawns whose comp points at it \u2014 affiliation is a single saved reference, not a list."
  },
  {
   "id": "e.docking.stable",
   "from": "comp.docking",
   "to": "comp.stable-building",
   "label": "dock into free slot",
   "kind": "sync",
   "mechanism": "Docking despawns the pawn into a typed ThingOwner<Pawn> deep-saved on the building; 15 slots shared with field dogs; a rebuilt dog's old affiliation is cleared before TakeNewDog so a full receiver leaves it unassigned, not misassigned."
  },
  {
   "id": "e.fieldfix.game",
   "from": "comp.fieldfix",
   "to": "ext.rimworld",
   "label": "scanner + delivery jobs",
   "kind": "sync",
   "mechanism": "WorkGiver_FieldFixRobodog finds powered-down/damaged dogs in the field; colonists deliver a battery (charges to 30%) or repair kit; the 2500-tick AwaitsFieldFix window expires safely."
  },
  {
   "id": "e.retrieval.stable",
   "from": "comp.retrieval",
   "to": "comp.stable-building",
   "label": "AcceptWreck / dock carry",
   "kind": "sync",
   "mechanism": "Living dogs enter the dock; Combatdog corpses transfer into a separately held ThingOwner<Corpse> behind a child IThingHolder, preserving inner pawn and cargo for rebuild."
  },
  {
   "id": "e.fabrication.stable",
   "from": "comp.fabrication",
   "to": "comp.stable-building",
   "label": "bills on stable BillStack",
   "kind": "control",
   "mechanism": "Assemble/rebuild recipes produce a pawn that docks straight in; upgrade is a per-dog Bill_UpgradeDog that rebuilds the dog as the next tier keeping name and BuildOrder seniority."
  },
  {
   "id": "e.tiers.fabrication",
   "from": "comp.tiers",
   "to": "comp.fabrication",
   "label": "arsenals + loadout migration",
   "kind": "data",
   "mechanism": "Tier XML names the guns; T4's fixed slots are paired charge rifles and 20.9-cell chain shotguns plus an independent smoke launcher; MigrateLoadout fits missing modern equipment on legacy dogs in place through each pawn's normal post-load traversal."
  },
  {
   "id": "e.drafting.game",
   "from": "comp.drafting",
   "to": "ext.rimworld",
   "label": "2 patches: draft controller, control gate",
   "kind": "control",
   "mechanism": "PawnComponentsUtility postfix adds a Pawn_DraftController to player Combatdogs; IsColonySubhumanPlayerControlled postfix returns true only while drafted, so dogs stay out of player-controlled lists otherwise."
  },
  {
   "id": "e.riding.game",
   "from": "comp.riding",
   "to": "ext.rimworld",
   "label": "DrawPos postfix, MoveSpeed stat part",
   "kind": "data",
   "mechanism": "StatPart_Riding is appended last on vanilla MoveSpeed and replaces the value with riddenMoveSpeed 7, so rider speed is exactly the dog's; the DrawPos postfix pins the rider to the dog at fixed altitude and pulls the dog under the rider's tween."
  },
  {
   "id": "e.render.game",
   "from": "comp.render-nodes",
   "to": "ext.rimworld",
   "label": "render-tree workers, BodyAngle patch",
   "kind": "data",
   "mechanism": "Core's Misc render tree with custom workers: gait recomputes leg transforms from distance walked along the facing every frame; poses recompute from vanilla stance state and the pickup tick, never stored; a hidden Whole node keeps the full sprite for icons."
  },
  {
   "id": "e.removal.game",
   "from": "comp.removal",
   "to": "ext.rimworld",
   "label": "destroy mod things, scrub export",
   "kind": "control",
   "mechanism": "Removes mod defNames from allowedDefs, nulls removed weapon/projectile/cover metadata in battle-log entries while keeping the records, and throws on any leftover mod reference rather than writing a broken save."
  },
  {
   "id": "e.settings.removal",
   "from": "comp.settings",
   "to": "comp.removal",
   "label": "Prepare save action",
   "kind": "control",
   "mechanism": "Pauses, writes and verifies the QR_before_removal backup before touching anything; on failure the player keeps Quadrunner enabled and reloads the backup."
  },
  {
   "id": "e.ui.stable",
   "from": "comp.stable-ui",
   "to": "comp.stable-building",
   "label": "roster read, upgrade orders",
   "kind": "data",
   "mechanism": "Overview scans the stable's roster including docked dogs (the scoped holder view hides them from MapPawns); dock indicators read actual dock contents, not assignments."
  },
  {
   "id": "e.harness.autotest",
   "from": "comp.host-harness",
   "to": "comp.autotest",
   "label": "file protocol: commands \u2194 events",
   "kind": "sync",
   "mechanism": ".devlog/autotest/: heartbeat every real second, JSONL events (ready carries build id and resolved modRoot), one command at a time (run [filter] / restart / quit / ping / hang); restart uses the game's own GenCommandLine.Restart so a new assembly loads without killing the process; watchdog declares HANG after 120 s."
  },
  {
   "id": "e.autotest.scenarios",
   "from": "comp.autotest",
   "to": "comp.scenarios",
   "label": "drives Scenario.Update",
   "kind": "control",
   "mechanism": "One scenario at a time in Run-all order with a case-insensitive comma filter; 5 fps between scenarios to spare software rendering; every scenario PASS/FAIL emits a result event with tick/time/fps telemetry."
  },
  {
   "id": "e.selftest.harness",
   "from": "comp.selftest",
   "to": "comp.host-harness",
   "label": "fake-game protocol test",
   "kind": "sync",
   "mechanism": "19 cases: baseline, launch/restart, filters, failures, error diff, hang/stop, package source validation and dev-link restoration; must be rerun whenever the protocol changes."
  },
  {
   "id": "e.probe.game",
   "from": "comp.removal-probe",
   "to": "ext.rimworld",
   "label": "loads cleaned save, no assembly",
   "kind": "sync",
   "mechanism": "Fresh process, Quadrunner absent from the mod list; verifies surviving map/caravan contents and battle-history text rendering; any new error or any loaded Quadrunner assembly fails the check."
  }
 ],
 "interfaces": [
  {
   "id": "iface.autotest",
   "name": "Autotest file protocol",
   "provider": "comp.autotest",
   "consumers": [
    "comp.host-harness",
    "comp.selftest"
   ],
   "contract": ".devlog/autotest/{heartbeat, events.jsonl, command}",
   "description": "Plain files, no sockets: the harness and the game share only this directory. Events: launched, ready (build, game version, map size, actual mod root), scenario, result, restarting, quitting, closed_window."
  },
  {
   "id": "iface.networkstate",
   "name": "NetworkState hosting",
   "provider": "comp.network",
   "consumers": [
    "comp.stable-building",
    "comp.comprobodog"
   ],
   "contract": "Scribe_Deep NetworkState on exactly one elected node",
   "description": "The dispatcher has no identity in the save: normal/war queues, nextId and warActive are deep-saved on the elected node (lowest-ID spawned player stable, else newest active dog); election clears it everywhere else; losing every eligible node clears the backlog permanently."
  },
  {
   "id": "iface.robodogwork",
   "name": "RobodogWork extension",
   "provider": "comp.work",
   "consumers": [
    "comp.races",
    "comp.network"
   ],
   "contract": "XML DefModExtension on QR_Races.xml (workGivers, jobs, flags, timings)",
   "description": "The tunable contract between defs and the dispatcher: which work givers/jobs a race may take, rescue/combat flags, raid speed/drain, engage range, scan/dispatch/progress cadences, survey spacing."
  },
  {
   "id": "iface.removal-export",
   "name": "Cleaned save export",
   "provider": "comp.removal",
   "consumers": [
    "actor.player",
    "comp.removal-probe"
   ],
   "contract": "QR_removed_<timestamp> save file",
   "description": "The player loads it after disabling the mod; the independent observer loads it with no Quadrunner assembly and rejects any new error across load \u2192 600 ticks \u2192 save/reload \u2192 600 ticks."
  }
 ],
 "requirements": [
  {
   "id": "REQ-SYS-001",
   "level": 0,
   "text": "Stable-built Quadrunners perform the colony's utility work \u2014 hauling, cleaning, construction-material delivery \u2014 using vanilla job mechanics, without a stable-centered pickup boundary.",
   "allocated_to": [
    "sub.dispatch",
    "sub.stable",
    "sub.pawn"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "README.md"
   ],
   "rationale": "Plan 0001 Goal: dogs that work so colonists don't have to."
  },
  {
   "id": "REQ-SYS-002",
   "level": 0,
   "text": "Combatdogs defend the colony across four upgrade tiers during raids, including attack pings outside any radius, capture, patrols and automated casualty recovery, without player micromanagement.",
   "allocated_to": [
    "sub.combat",
    "sub.dispatch",
    "sub.stable"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "docs/plans/0007-wartime-recovery.md",
    "docs/plans/0009-combat-packs.md"
   ]
  },
  {
   "id": "REQ-SYS-003",
   "level": 0,
   "text": "Medirunners rescue reachable downed colonists, guests, prisoners and colony animals to appropriate beds with triage, under fire, and fight revealed Home-area fires after rescues.",
   "allocated_to": [
    "sub.rescue",
    "sub.dispatch"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "docs/plans/0008-medirunner-firefighting.md"
   ]
  },
  {
   "id": "REQ-SYS-004",
   "level": 0,
   "text": "A player can ride an available Quadrunner to a chosen destination at the dog's speed, with safe dismount on arrival, indoors, drafting or power-down.",
   "allocated_to": [
    "sub.riding"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-SYS-005",
   "level": 0,
   "text": "Every robodog manages its own energy: about 3,500 cells per full charge, seeks charging at 10%, powers down at 0%, and charges at a powered stable or a safe conduit spot on stored battery energy.",
   "allocated_to": [
    "sub.power",
    "sub.pawn",
    "sub.recovery"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "README.md"
   ]
  },
  {
   "id": "REQ-SYS-006",
   "level": 0,
   "text": "The mod can be removed from a save cleanly: an explicit export leaves no Quadrunner content or references, keeps surviving battle history, and loads without the assembly with zero new errors.",
   "allocated_to": [
    "sub.removal",
    "sub.verification"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-SYS-007",
   "level": 0,
   "text": "Every gameplay behavior is proven by automated scenarios in an isolated real game instance before delivery, and the distributable package is re-verified by cold-launching it.",
   "allocated_to": [
    "sub.verification"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "AGENTS.md"
   ],
   "rationale": "The core T0 decision: the agent runs the game tests itself; no in-game step is handed to the user."
  },
  {
   "id": "REQ-SYS-008",
   "level": 0,
   "text": "All work on a map is coordinated by one shared dispatcher whose queues survive coordinator loss and save/load; dogs never search for work themselves.",
   "allocated_to": [
    "sub.dispatch"
   ],
   "evidence": [
    "docs/plans/0003-network-dispatch.md"
   ]
  },
  {
   "id": "REQ-SYS-009",
   "level": 0,
   "text": "The dogs read as living animals and a coherent product line: per-part walk animation, combat/rescue action poses, liveries, sirens, and Hearthline lore on every model, bill and item.",
   "allocated_to": [
    "sub.render",
    "sub.foundation",
    "comp.art"
   ],
   "evidence": [
    "docs/plans/0002-hearthline-lore.md",
    "TODO.md"
   ],
   "rationale": "User-requested after T9: animation, liveries, weapon demos and lore (2026-10-20)."
  },
  {
   "id": "REQ-FND-001",
   "level": 1,
   "parent": "REQ-SYS-007",
   "text": "The mod boots on RimWorld 1.6 with Core + Harmony only: one static bootstrap applies every patch at startup and logs the loaded build id that the harness compares against the build it made.",
   "allocated_to": [
    "comp.bootstrap"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "Source/Quadrunner/QR.cs"
   ],
   "rationale": "T0 acceptance: the loaded-build log line is the harness's stale-build check."
  },
  {
   "id": "REQ-FND-002",
   "level": 1,
   "parent": "REQ-SYS-009",
   "text": "The stable's Overview tab shows one row per dog (type, battery, health, current activity) with Hearthline lore on hover for the company, every model, field item and weapon choice, without losing upgrade actions or costs.",
   "allocated_to": [
    "comp.stable-ui"
   ],
   "evidence": [
    "docs/plans/0002-hearthline-lore.md"
   ]
  },
  {
   "id": "REQ-DIS-001",
   "level": 1,
   "parent": "REQ-SYS-008",
   "text": "Utility discovery is local and fog-limited: a dog reports work within 15 cells in unfogged cells passing vanilla eligibility (filth additionally needs Home and 600 ticks of age); rescue patients and revealed fires and raid targets are map-wide alerts.",
   "allocated_to": [
    "comp.work",
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0003-network-dispatch.md",
    "Source/Quadrunner/Work.cs"
   ]
  },
  {
   "id": "REQ-DIS-002",
   "level": 1,
   "parent": "REQ-SYS-008",
   "text": "Each task is assigned once, to the nearest available capable dog by real pathfind cost (ties to the lower thing ID); availability excludes dead, downed, powered-down, charge-seeking, drafted, mentally broken and player-forced dogs.",
   "allocated_to": [
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0003-network-dispatch.md",
    "Source/Quadrunner/Network.cs"
   ]
  },
  {
   "id": "REQ-DIS-003",
   "level": 1,
   "parent": "REQ-SYS-008",
   "text": "Assignments are reconciled every network tick: lost dogs or jobs release the task to Queued, and an assignee making no progress for 2500 ticks is stopped safely and retried with a backoff delay.",
   "allocated_to": [
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0003-network-dispatch.md"
   ]
  },
  {
   "id": "REQ-DIS-004",
   "level": 1,
   "parent": "REQ-SYS-008",
   "text": "On a hostile active threat the network suspends normal work (stopping jobs and dropping cargo in place), deploys Combatdogs and rescue-capable Medirunners, and revalidates the kept normal queue when the threat clears.",
   "allocated_to": [
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "docs/plans/0003-network-dispatch.md"
   ]
  },
  {
   "id": "REQ-DIS-005",
   "level": 1,
   "parent": "REQ-SYS-008",
   "text": "Coordinator failover is automatic: queues transfer to a surviving node on loss, and losing every eligible node permanently clears both queues rather than resurrecting a stale backlog.",
   "allocated_to": [
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0003-network-dispatch.md"
   ]
  },
  {
   "id": "REQ-DIS-006",
   "level": 1,
   "parent": "REQ-SYS-001",
   "text": "Idle surveying is area-aware and scan-certified: Home rooms, stockpiles, growing and other zones before the exterior, routes ranked by real path cost, coverage credited only by actual revealed 15-cell scans, sweeps finite and revalidated after load.",
   "allocated_to": [
    "comp.survey"
   ],
   "evidence": [
    "docs/plans/0004-area-aware-survey.md"
   ]
  },
  {
   "id": "REQ-WRK-001",
   "level": 1,
   "parent": "REQ-SYS-001",
   "text": "Hauling, cleaning and blueprint/frame delivery run on vanilla work givers and jobs (storage filters, reservations, forbidding respected), with Harmony only where vanilla assumes human skills or work priorities.",
   "allocated_to": [
    "comp.work",
    "comp.defs"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-STB-001",
   "level": 1,
   "parent": "REQ-SYS-001",
   "text": "The stable docks up to 15 dogs in shared slots (including dogs working away), saves them, charges and repairs them while powered; power loss pauses charging/repair and bill use but not dispatch; destruction drops dogs safely.",
   "allocated_to": [
    "comp.stable-building",
    "comp.docking"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-STB-002",
   "level": 1,
   "parent": "REQ-SYS-001",
   "text": "Assembly bills at the stable produce each dog model under Crafting, and the finished dog docks straight into the stable; no-product recipe UI and do-until counting are safe because the product is a pawn.",
   "allocated_to": [
    "comp.fabrication",
    "comp.stable-building"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-PWN-001",
   "level": 1,
   "parent": "REQ-SYS-001",
   "text": "Robodog races are neither animal nor mechanoid: no food or rest needs, no disease, rot, taming, training, hunting or wild spawns; think-tree priority keeps player orders above charge seeking above network orders above docking.",
   "allocated_to": [
    "comp.races",
    "comp.defs"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-CHG-001",
   "level": 1,
   "parent": "REQ-SYS-005",
   "text": "A dog wanting a charge picks stable or conduit by smallest time-to-full (travel + missing/rate); a conduit spot is a free standable roofed indoor non-doorway cell near a colony power transmitter, outside the (configurable) turbine radius, and charging draws stored battery energy fullest-first.",
   "allocated_to": [
    "comp.charging",
    "comp.chargespots",
    "comp.settings"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-CHG-002",
   "level": 1,
   "parent": "REQ-SYS-005",
   "text": "Battery drains per cell actually moved (about 3,500 cells per charge; doubled while working during a raid, not while ridden); at 0% the dog gains a powered-down hediff and lies like a downed pawn; crossing 10% mid-fight undrafts or interrupts so the dog retreats to charge.",
   "allocated_to": [
    "comp.comprobodog"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-RSC-001",
   "level": 1,
   "parent": "REQ-SYS-003",
   "text": "Rescue triage ranks colonist \u2192 guest \u2192 prisoner \u2192 colony animal, fastest-bleeding first within a rank, one Medirunner per patient by reservation, allowed under fire; enemies, robodogs and wild animals are never rescued.",
   "allocated_to": [
    "comp.rescue"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-RSC-002",
   "level": 1,
   "parent": "REQ-SYS-003",
   "text": "A stable's Dispatch Rescuedogs action refreshes the shared dispatcher immediately (even paused), clearing retry backoff only for unassigned rescue tasks, with disabled-state feedback and same-tick distinct assignments.",
   "allocated_to": [
    "comp.rescue",
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0006-manual-rescue-dispatch.md"
   ]
  },
  {
   "id": "REQ-RSC-003",
   "level": 1,
   "parent": "REQ-SYS-003",
   "text": "Revealed Home-area fires are war-queue tasks handled after rescues even during raids: nearest reachable unclaimed fire per responder, 2% battery per foam burst, fire-to-fire continuation, 180-tick quiet standby with recheck, and a field alert during thunder/flashstorms; docked Medirunners deploy only for assignable work (no speculative undocking).",
   "allocated_to": [
    "comp.firefighting",
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0008-medirunner-firefighting.md",
    "TODO.md"
   ]
  },
  {
   "id": "REQ-HOS-001",
   "level": 1,
   "parent": "REQ-SYS-001",
   "text": "The worker hose cleans discovered contiguous Home-area patches of at least four blood/firefoam tiles in a broad 11\u00d77 band at 1.5\u00d7 cleaning work per affected tile, continuing through the whole patch; spray is blocked by walls; smaller patches and other filth use ordinary cleaning; foam removal also removes fire protection.",
   "allocated_to": [
    "comp.hose"
   ],
   "evidence": [
    "docs/plans/0010-water-hose.md"
   ]
  },
  {
   "id": "REQ-REC-001",
   "level": 1,
   "parent": "REQ-SYS-005",
   "text": "Powered-down dogs are retrieved: the network sends a healthy dog to carry each to the nearest stable with a free slot (fire beside it beaten out first); colonists get a Carry-to-stable float menu; raiders can kidnap a dog at 0%; an unreachable dog raises a jump-to alert.",
   "allocated_to": [
    "comp.retrieval"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-REC-002",
   "level": 1,
   "parent": "REQ-SYS-005",
   "text": "Robodog battery and repair-kit items are built by bill and colonists auto-deliver them to powered-down or damaged dogs in the field within the 2500-tick window.",
   "allocated_to": [
    "comp.fieldfix"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-REC-003",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "Combatdogs broadcast their downing/death with last-known coordinates; after threats clear, healthy Quadrunners or Medirunners recover living casualties and wrecks ahead of ordinary utility work (deferred medical emergencies still first); living dogs dock for powered repair, and wrecks rebuild automatically from 50 steel + 25 plasteel physically delivered into the 3\u00d73 bay over 2,000 powered ticks, retaining identity, tier and guns.",
   "allocated_to": [
    "comp.network",
    "comp.retrieval",
    "comp.stable-building"
   ],
   "evidence": [
    "docs/plans/0007-wartime-recovery.md"
   ]
  },
  {
   "id": "REQ-CBT-001",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "Combatdogs engage standing hostiles within 15 cells with line of sight: AttackStatic when hittable from position, else walk to a vanilla cast position, else melee; downed humans are captured to a free prisoner bed or carried to the nearest qualifying empty room, never left imprisoned in the open.",
   "allocated_to": [
    "comp.combat"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-CBT-002",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "Any dog hit by a hostile pings the network, which dispatches a docked or returning Combatdog regardless of radius, retargeting each tick; a Combatdog crossing 10% battery mid-fight retreats with a yellow siren and ignores pings until charged.",
   "allocated_to": [
    "comp.combat",
    "comp.network"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-CBT-003",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "While deployed and idle, Combatdogs walk the stable's patrol waypoints in order (persisting across save/load) or guard within 4 cells of the stable, or of the coordinator when stable-less.",
   "allocated_to": [
    "comp.combat",
    "comp.stable-building"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-CBT-004",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "Tiers 1\u20134 are ordered from the Overview tab and rebuild the dog keeping name, seniority and identity; built-in weapons are never dropped; Tier 4 carries paired charge rifles plus paired 20.9-cell chain shotguns firing two independent shots per burst, with an automatically launching smoke launcher; legacy T3/T4 dogs are migrated in place on load.",
   "allocated_to": [
    "comp.fabrication",
    "comp.tiers"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "docs/plans/0009-combat-packs.md"
   ]
  },
  {
   "id": "REQ-CBT-005",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "Nearby Combatdogs of mixed tiers form packs led by T3/T4 within 15 cells: the pack focuses the queued reachable standing hostile nearest to any member, retains a valid focus to avoid thrashing, and re-selects at native job boundaries so engagement is continuous; distant groups keep independent targets; draft, capture and retreat are never overridden.",
   "allocated_to": [
    "comp.packcombat"
   ],
   "evidence": [
    "docs/plans/0009-combat-packs.md"
   ]
  },
  {
   "id": "REQ-CBT-006",
   "level": 1,
   "parent": "REQ-SYS-002",
   "text": "Player Combatdogs are draftable for direct orders with priority targets; drafted dogs are excluded from centralized assignment and undraft automatically at 10% battery.",
   "allocated_to": [
    "comp.drafting"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-RID-001",
   "level": 1,
   "parent": "REQ-SYS-004",
   "text": "Riding moves at exactly the dog's riddenMoveSpeed (a stat part replaces MoveSpeed), works from dismount rules (arrival, indoor step or door, drafting, power-down, job end for any reason), draws the rider on the back with per-facing offsets, and leaves no saved carried-pawn state (a loaded rider is treated as always mounted).",
   "allocated_to": [
    "comp.riding"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-RND-001",
   "level": 1,
   "parent": "REQ-SYS-009",
   "text": "Walk animation is driven by distance walked along the facing (diagonal leg pairs, matching stride to speed, stopping with the dog, including while ridden); action poses (pounce, brace/recoil, scoop) recompute from vanilla stance state; sirens flash in real time even while paused.",
   "allocated_to": [
    "comp.render-nodes"
   ],
   "evidence": [
    "TODO.md",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "REQ-REM-001",
   "level": 1,
   "parent": "REQ-SYS-006",
   "text": "The Prepare-save export backs up first (QR_before_removal), releases riders/patients/vanilla cargo safely (failing rather than discarding unplaceable cargo), destroys every mod thing including in-flight projectiles, clears static caches, scrubs def references from the XML while keeping battle-history IDs, and throws rather than write a broken save.",
   "allocated_to": [
    "comp.removal",
    "comp.settings"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-TST-001",
   "level": 1,
   "parent": "REQ-SYS-007",
   "text": "The harness self-verifies against a fake game (19 cases), keeps a per-mod-list error baseline from a run without Quadrunner, restarts the game in-process when the loaded build is stale, and watchdogs hangs (120 s) and crashes.",
   "allocated_to": [
    "comp.host-harness",
    "comp.selftest"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  },
  {
   "id": "REQ-TST-002",
   "level": 1,
   "parent": "REQ-SYS-007",
   "text": "Scenarios run inside a real isolated game on a private profile and Xvfb display, never the player's, and can run beside the player's open game; each scenario disables the storyteller, cleans up tracked things, and reports PASS/FAIL with telemetry.",
   "allocated_to": [
    "comp.scenarios",
    "comp.autotest",
    "comp.host-harness"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md",
    "AGENTS.md"
   ]
  },
  {
   "id": "REQ-TST-003",
   "level": 1,
   "parent": "REQ-SYS-007",
   "text": "The distributable package is rebuilt from a clean committed tree after every major feature or fix and verified by cold-launching the actual package, requiring the loaded build and resolved mod root to match it.",
   "allocated_to": [
    "comp.host-harness"
   ],
   "evidence": [
    "AGENTS.md",
    "tools/package.sh"
   ]
  },
  {
   "id": "REQ-TST-004",
   "level": 1,
   "parent": "REQ-SYS-006",
   "text": "Removal is verified independently: an observer mod with no Quadrunner reference loads the cleaned save, ticks 600, saves/reloads and ticks 600, checking surviving contents and rejecting any new error or loaded Quadrunner assembly.",
   "allocated_to": [
    "comp.removal-probe"
   ],
   "evidence": [
    "docs/plans/0001-quadrunner.md"
   ]
  }
 ],
 "verifications": [
  {
   "id": "VER-SYS-001",
   "level": 0,
   "method": "test",
   "name": "Utility work scenarios",
   "verifies": [
    "REQ-SYS-001"
   ],
   "status": "passing",
   "procedure": "Haul/clean/construction-delivery scenarios with fixtures on a fresh map; latest affected integration 25/25 PASS, 0 new errors (2026-11-06 02:02).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-002",
   "level": 0,
   "method": "test",
   "name": "Combat & recovery scenarios",
   "verifies": [
    "REQ-SYS-002"
   ],
   "status": "passing",
   "procedure": "Combat/pack/recovery scenarios; actual package 10/10 PASS, 0 new errors (2026-11-04 08:51, build 1ebcbc4).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs",
    "Source/Quadrunner/Scenarios.Packs.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-003",
   "level": 0,
   "method": "test",
   "name": "Rescue & firefighting scenarios",
   "verifies": [
    "REQ-SYS-003"
   ],
   "status": "passing",
   "procedure": "Rescue/triage/manual-dispatch/fire/storm scenarios; actual release package 12/12 PASS, 0 new errors (2026-11-07 01:17, build 62ae7cd).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Rescue.cs",
    "Source/Quadrunner/Scenarios.Firefighting.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-004",
   "level": 0,
   "method": "test",
   "name": "Riding scenarios",
   "verifies": [
    "REQ-SYS-004"
   ],
   "status": "passing",
   "procedure": "Ride and dismount / Ride until 0% / Rider pose; part of the recorded 39/39 packaged Run all (2026-10-20).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Riding.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-005",
   "level": 0,
   "method": "test",
   "name": "Battery & charging scenarios",
   "verifies": [
    "REQ-SYS-005"
   ],
   "status": "passing",
   "procedure": "Battery drain/power-down, Charging maze, Stable or conduit, room rules; packaged Run all 39/39 PASS after the 3,500-cell range increase (2026-10-20 17:51).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-006",
   "level": 0,
   "method": "test",
   "name": "Independent removal check",
   "verifies": [
    "REQ-SYS-006"
   ],
   "status": "passing",
   "procedure": "tools/autotest.py removal-check: fresh process, no Quadrunner assembly, load \u2192 600 ticks \u2192 save/reload \u2192 600 ticks, 0 new errors; latest clean run recorded with the F2 migration (2026-10-26).",
   "evidence": [
    "tools/autotest.py",
    "tools/removal-probe/Probe.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-007",
   "level": 0,
   "method": "test",
   "name": "Packaged Run all",
   "verifies": [
    "REQ-SYS-007"
   ],
   "status": "passing",
   "procedure": "Full scenario sequence against the actual dist/Quadrunner package: 39/39 PASS, 0 new errors, BUILD and resolved modRoot verified (2026-10-20 09:04). Later releases verify scoped affected scenarios per package (see annotation on this model's verification subsystem).",
   "evidence": [
    "docs/test-runs.md",
    "tools/package.sh"
   ]
  },
  {
   "id": "VER-SYS-008",
   "level": 0,
   "method": "test",
   "name": "Network dispatch scenarios",
   "verifies": [
    "REQ-SYS-008"
   ],
   "status": "passing",
   "procedure": "Ten network cases from the actual package: discovery, blocked-route ranking, reload, final-node clearing, repeated failover, war transitions (2026-10-21).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-SYS-009",
   "level": 0,
   "method": "inspection",
   "name": "Artwork & animation captures",
   "verifies": [
    "REQ-SYS-009",
    "REQ-FND-002"
   ],
   "status": "passing",
   "procedure": "Actual private-game screenshots inspected for gait, poses, liveries, sirens, Overview tooltips and weapon demos; Walk animation / Rescue animation scenarios PASS (2026-10-20).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Gait.cs",
    "Source/Quadrunner/Scenarios.Animation.cs",
    "Source/Quadrunner/Scenarios.Art.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-DIS-001",
   "level": 1,
   "method": "test",
   "name": "Discovery cases",
   "verifies": [
    "REQ-DIS-001"
   ],
   "status": "passing",
   "procedure": "Network scenario set proves local fog-limited reporting, dedupe and map-wide alerts; allocation regression proved single-report storage at 256 reports (2026-10-27).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-DIS-002",
   "level": 1,
   "method": "test",
   "name": "Assignment cases",
   "verifies": [
    "REQ-DIS-002"
   ],
   "status": "passing",
   "procedure": "Two-stables and nearest-dog scenarios: the closest idle dog dispatches, each job claimed once; excluded-availability states checked in dispatch scenarios.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs"
   ]
  },
  {
   "id": "VER-DIS-003",
   "level": 1,
   "method": "test",
   "name": "Reconciliation & retry cases",
   "verifies": [
    "REQ-DIS-003"
   ],
   "status": "passing",
   "procedure": "Manual-rescue retry scenarios prove automatic refresh waits while a forced dispatch clears backoff only for unassigned rescues; progress-timeout release covered by network cases.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Rescue.cs",
    "docs/plans/0006-manual-rescue-dispatch.md"
   ]
  },
  {
   "id": "VER-DIS-004",
   "level": 1,
   "method": "test",
   "name": "War-mode transitions",
   "verifies": [
    "REQ-DIS-004"
   ],
   "status": "passing",
   "procedure": "Raid scenarios: workers finish/dock with raid speed and drain, cargo dropped in place, queue revalidated after the threat; wartime medic and recovery sequence 19/20\u2192fixed (2026-11-07).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs",
    "Source/Quadrunner/Scenarios.CombatRecovery.cs"
   ]
  },
  {
   "id": "VER-DIS-005",
   "level": 1,
   "method": "test",
   "name": "Coordinator failover",
   "verifies": [
    "REQ-DIS-005"
   ],
   "status": "passing",
   "procedure": "Repeated failover, active-assignment reload and final-node clearing pass from the actual package (plan 0003 complete, 2026-10-21).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs"
   ]
  },
  {
   "id": "VER-DIS-006",
   "level": 1,
   "method": "test",
   "name": "Survey behavior & benchmarks",
   "verifies": [
    "REQ-DIS-006"
   ],
   "status": "passing",
   "procedure": "Sixteen survey behavior cases plus matched historical/current benchmarks and a legacy-save case: 75/75 PASS, 0 new errors (2026-10-23 10:59).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Survey.cs",
    "Source/Quadrunner/Scenarios.SurveyBench.cs"
   ]
  },
  {
   "id": "VER-WRK-001",
   "level": 1,
   "method": "test",
   "name": "Vanilla-rail work cases",
   "verifies": [
    "REQ-WRK-001"
   ],
   "status": "passing",
   "procedure": "Haul and clean / far blueprint and frame delivery / storage-filter and forbidden checks across the dispatch scenario set.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs"
   ]
  },
  {
   "id": "VER-STB-001",
   "level": 1,
   "method": "test",
   "name": "Stable lifecycle cases",
   "verifies": [
    "REQ-STB-001"
   ],
   "status": "passing",
   "procedure": "Assemble and dock / Stable full (16th dog) / Dock and undock / Stable power loss / Stable fire with docked dogs / Stable uninstall and reinstall, each with save/load.",
   "evidence": [
    "Source/Quadrunner/Scenarios.cs",
    "Source/Quadrunner/Scenarios.Dispatch.cs"
   ]
  },
  {
   "id": "VER-STB-002",
   "level": 1,
   "method": "test",
   "name": "Assembly bill cases",
   "verifies": [
    "REQ-STB-002"
   ],
   "status": "passing",
   "procedure": "Assemble Quadrunner/Medirunner/Combatdog scenarios; product docks straight in; do-until bill counting with docked dogs included explicitly.",
   "evidence": [
    "Source/Quadrunner/Scenarios.cs"
   ]
  },
  {
   "id": "VER-PWN-001",
   "level": 1,
   "method": "inspection",
   "name": "Race def review",
   "verifies": [
    "REQ-PWN-001"
   ],
   "status": "passing",
   "procedure": "QR_Races.xml defines QR_Robotic flesh, no needs/taming/wild spawns; think-tree order exercised by every dispatch/charging scenario (charge before work, orders before dock).",
   "evidence": [
    "Defs/ThingDefs_Races/QR_Races.xml",
    "Defs/ThinkTreeDefs/QR_ThinkTrees.xml"
   ]
  },
  {
   "id": "VER-CHG-001",
   "level": 1,
   "method": "test",
   "name": "Charging decision cases",
   "verifies": [
    "REQ-CHG-001"
   ],
   "status": "passing",
   "procedure": "Stable or conduit (3\u00d7 rate difference), Charging maze (turbine, forbidden door), Batteries drain mid-charge, Charging room rules (prisons never, bedrooms unused only, freezers allowed, allowed areas).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs"
   ]
  },
  {
   "id": "VER-CHG-002",
   "level": 1,
   "method": "test",
   "name": "Battery drain & power-down",
   "verifies": [
    "REQ-CHG-002"
   ],
   "status": "passing",
   "procedure": "Battery drain and power-down / Ride until 0% / mounted power-down and Low-battery retreat (undraft/retreat at the threshold).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs",
    "Source/Quadrunner/Scenarios.Riding.cs",
    "Source/Quadrunner/Scenarios.Combat.cs"
   ]
  },
  {
   "id": "VER-RSC-001",
   "level": 1,
   "method": "test",
   "name": "Triage & mass casualty",
   "verifies": [
    "REQ-RSC-001"
   ],
   "status": "passing",
   "procedure": "Mass-casualty raid (three downed colonists, one Medirunner), Rescue order, wartime medic cases.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Rescue.cs"
   ]
  },
  {
   "id": "VER-RSC-002",
   "level": 1,
   "method": "test",
   "name": "Manual rescue dispatch",
   "verifies": [
    "REQ-RSC-002"
   ],
   "status": "passing",
   "procedure": "Plan 0006 sequence: disabled states, same-tick distinct assignments, repeat-click safety, save/load, bed delivery, unpowered return (3/3 PASS 2026-10-24).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Rescue.cs",
    "docs/plans/0006-manual-rescue-dispatch.md"
   ]
  },
  {
   "id": "VER-RSC-003",
   "level": 1,
   "method": "test",
   "name": "Firefighting & deployment guard",
   "verifies": [
    "REQ-RSC-003"
   ],
   "status": "passing",
   "procedure": "Fire continuity, fires after foam, storm standby and the idle deployment guard (zero spawns across 690+ ticks with unassignable queue entries); package 12/12 PASS (2026-11-07).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Firefighting.cs",
    "Source/Quadrunner/Scenarios.FireContinuity.cs",
    "Source/Quadrunner/Scenarios.Standby.cs"
   ]
  },
  {
   "id": "VER-HOS-001",
   "level": 1,
   "method": "test",
   "name": "Hose scenarios",
   "verifies": [
    "REQ-HOS-001"
   ],
   "status": "passing",
   "procedure": "Eight Hose scenarios: dirt-first dispatch, thresholds, patch completion across reload, two-worker boundaries, broad 50-cell band application, shadow/layer/edge respect; actual package 8/8 PASS (2026-11-06).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Hose.cs"
   ]
  },
  {
   "id": "VER-REC-001",
   "level": 1,
   "method": "test",
   "name": "Retrieval cases",
   "verifies": [
    "REQ-REC-001"
   ],
   "status": "passing",
   "procedure": "Carry home / Carrier runs low / Retrieval under fire / Raider carries off a dog / unreachable alert.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs"
   ]
  },
  {
   "id": "VER-REC-002",
   "level": 1,
   "method": "test",
   "name": "Field fix cases",
   "verifies": [
    "REQ-REC-002"
   ],
   "status": "passing",
   "procedure": "Field battery and repair kit: bills make the items, colonists auto-deliver to field dogs.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs"
   ]
  },
  {
   "id": "VER-REC-003",
   "level": 1,
   "method": "test",
   "name": "Casualty recovery & auto-rebuild",
   "verifies": [
    "REQ-REC-003"
   ],
   "status": "passing",
   "procedure": "Combat recovery cases (living and wreck, identity/tier/guns retained), auto-rebuild with delivered 50 steel + 25 plasteel across save/load, and manual rebuild bills using recovered wrecks (2026-10-24/29).",
   "evidence": [
    "Source/Quadrunner/Scenarios.CombatRecovery.cs",
    "Source/Quadrunner/Scenarios.AutoRebuild.cs",
    "Source/Quadrunner/Scenarios.RebuildBills.cs"
   ]
  },
  {
   "id": "VER-CBT-001",
   "level": 1,
   "method": "test",
   "name": "Engagement & capture cases",
   "verifies": [
    "REQ-CBT-001"
   ],
   "status": "passing",
   "procedure": "Combatdog sweep: capture with and without a free bed, patrol order across reload, engagement ranges.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs"
   ]
  },
  {
   "id": "VER-CBT-002",
   "level": 1,
   "method": "test",
   "name": "Ping chain & retreat",
   "verifies": [
    "REQ-CBT-002"
   ],
   "status": "passing",
   "procedure": "Ping chain retargeting and Low-battery retreat with the yellow siren (screenshot inspected).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs"
   ]
  },
  {
   "id": "VER-CBT-003",
   "level": 1,
   "method": "test",
   "name": "Patrol persistence",
   "verifies": [
    "REQ-CBT-003"
   ],
   "status": "passing",
   "procedure": "Waypoints 1\u21922\u21923\u21921 walked in order across a reload (2026-10-20 05:09).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs"
   ]
  },
  {
   "id": "VER-CBT-004",
   "level": 1,
   "method": "test",
   "name": "Tiers, arsenal & migration",
   "verifies": [
    "REQ-CBT-004"
   ],
   "status": "passing",
   "procedure": "Tier upgrade / Tier 3 and 4 weapons / Tier 2/3/4 weapon demos / pack-migration fixtures across holders, repeat and mid-fight loads (2026-11-03).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Tiers.cs",
    "Source/Quadrunner/Scenarios.Demo.cs",
    "Source/Quadrunner/Scenarios.PackMigration.cs"
   ]
  },
  {
   "id": "VER-CBT-005",
   "level": 1,
   "method": "test",
   "name": "Pack continuity",
   "verifies": [
    "REQ-CBT-005"
   ],
   "status": "passing",
   "procedure": "Pack scenarios: nearest-member focus, focus retention, closer-newcomer joins, engagement across native job boundaries; actual package 10/10 PASS (2026-11-04).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Packs.cs",
    "Source/Quadrunner/Scenarios.PackContinuity.cs"
   ]
  },
  {
   "id": "VER-CBT-006",
   "level": 1,
   "method": "test",
   "name": "Drafting cases",
   "verifies": [
    "REQ-CBT-006"
   ],
   "status": "passing",
   "procedure": "Drafted priority target; excluded-from-assignment and undraft-at-10% covered in dispatch/charging scenarios.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs"
   ]
  },
  {
   "id": "VER-RID-001",
   "level": 1,
   "method": "test",
   "name": "Riding cases",
   "verifies": [
    "REQ-RID-001"
   ],
   "status": "passing",
   "procedure": "Ride and dismount / Ride until 0% (7.0 c/s) / Rider pose captures of all four facings.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Riding.cs"
   ]
  },
  {
   "id": "VER-RND-001",
   "level": 1,
   "method": "inspection",
   "name": "Animation captures",
   "verifies": [
    "REQ-RND-001"
   ],
   "status": "passing",
   "procedure": "Walk animation and Rescue animation scenarios PASS; gait/pose/siren screenshots inspected from the actual game (2026-10-20).",
   "evidence": [
    "Source/Quadrunner/Scenarios.Gait.cs",
    "Source/Quadrunner/Scenarios.Animation.cs"
   ]
  },
  {
   "id": "VER-REM-001",
   "level": 1,
   "method": "test",
   "name": "Prepare removal scenario",
   "verifies": [
    "REQ-REM-001"
   ],
   "status": "passing",
   "procedure": "Prepare-removal fixture: backup verified, riders/patients/cargo retained in the backup, wrecks/packed stables/in-flight shots removed from the export, unrelated vanilla shots kept, battle metadata cleared with IDs kept.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Removal.cs"
   ]
  },
  {
   "id": "VER-TST-001",
   "level": 1,
   "method": "test",
   "name": "Harness self-test",
   "verifies": [
    "REQ-TST-001"
   ],
   "status": "passing",
   "procedure": "tools/selftest.sh: 19 cases OK against the fake game (2026-10-20 08:52).",
   "evidence": [
    "tools/selftest.sh",
    "tools/selftest/fake_game.py"
   ]
  },
  {
   "id": "VER-TST-002",
   "level": 1,
   "method": "test",
   "name": "Scenario suite runs",
   "verifies": [
    "REQ-TST-002",
    "REQ-FND-001"
   ],
   "status": "passing",
   "procedure": "Every row of docs/test-runs.md is produced this way; proven beside the user's open game (2026-10-19 23:20, 11/11 PASS, user profile untouched).",
   "evidence": [
    "docs/test-runs.md",
    "Source/Quadrunner/Scenario.cs"
   ]
  },
  {
   "id": "VER-TST-003",
   "level": 1,
   "method": "test",
   "name": "Package verification",
   "verifies": [
    "REQ-TST-003"
   ],
   "status": "passing",
   "procedure": "pkg-run cold-launches dist/Quadrunner, requires loaded BUILD and resolved modRoot to match, runs affected scenarios, restores the dev link; latest package 62ae7cd.20261107T011508Z 12/12 PASS (2026-11-07).",
   "evidence": [
    "tools/autotest.py",
    "docs/test-runs.md"
   ]
  },
  {
   "id": "VER-TST-004",
   "level": 1,
   "method": "test",
   "name": "Independent removal observer",
   "verifies": [
    "REQ-TST-004"
   ],
   "status": "passing",
   "procedure": "removal-check reports LOADED CLEAN with 0 new errors; observer carries no Quadrunner reference or saved component.",
   "evidence": [
    "tools/removal-probe/Probe.cs",
    "docs/test-runs.md"
   ]
  }
 ],
 "flows": [
  {
   "id": "flow.haul",
   "name": "One haul, end to end",
   "trigger": "An idle Quadrunner finishes its current survey leg on a peaceful map",
   "description": "The system's core loop: local discovery, central assignment, vanilla execution, central completion. This is the path that pays for the mod.",
   "steps": [
    {
     "n": 1,
     "from": "ext.rimworld",
     "to": "comp.comprobodog",
     "action": "Pawn ticks; think tree evaluated",
     "mechanism": "Priority order means a dog with no player order, not drafted and not charge-seeking reaches JobGiver_RobodogWork (node 6)."
    },
    {
     "n": 2,
     "from": "comp.work",
     "to": "comp.network",
     "action": "ReportLocal: steel stack seen",
     "condition": "dog inside dispatch tick (network rate-limits to 60 ticks)",
     "mechanism": "The Quadrunner race's work-giver scanners test eligibility \u2014 within 15 cells, unfogged, unforbidden, on the haulables list; the dog's cached ThingID is reused so duplicate discovery allocates nothing."
    },
    {
     "n": 3,
     "from": "comp.network",
     "to": "comp.network",
     "action": "Queue Haul task, deduped",
     "mechanism": "Refuses a second task with the same (kind, targetId) in the normal queue; tasks store ThingIDs, never references, so a destroyed target cannot poison a save."
    },
    {
     "n": 4,
     "from": "comp.network",
     "to": "comp.work",
     "action": "AssignAll \u2192 JobFor(dog, stack)",
     "mechanism": "Sorts war queue (rescue rank, bleeding, kind, id) then normal queue (kind, id); nearest available capable dog by real pathfind with the task's PathEndMode; ties to lower thing ID; the job is built for exactly this one target."
    },
    {
     "n": 5,
     "from": "comp.work",
     "to": "ext.rimworld",
     "action": "StartJob on vanilla rails",
     "mechanism": "Reservations, storage filters and stack counts are all vanilla \u2014 only the target choice was centralized."
    },
    {
     "n": 6,
     "from": "comp.network",
     "to": "comp.network",
     "action": "Reconcile each tick",
     "mechanism": "Assigned \u2192 Acknowledged when the dog's running job matches the task's saved jobId (vanilla Job.loadID, so it survives save/load); \u2192 Working on progress; no progress for 2500 ticks stops the dog safely and retries with backoff."
    },
    {
     "n": 7,
     "from": "comp.network",
     "to": "comp.comprobodog",
     "action": "Finished \u2192 next order",
     "condition": "stack is in valid best storage",
     "mechanism": "Completion is revalidated centrally; the dog's next think-tree pass asks TakeOrder again, gets a new survey or docks when nothing is reachable."
    }
   ],
   "failure_modes": [
    {
     "when": "Target destroyed or despawns mid-task",
     "then": "Re-resolution treats the missing ThingID as finished; no error, no poisoned save.",
     "detected_by": "VER-DIS-001"
    },
    {
     "when": "Assignee dies, is drafted or loses its job",
     "then": "Reconciliation releases the task to Queued for the next dispatch tick.",
     "detected_by": "VER-DIS-003"
    },
    {
     "when": "War breaks out mid-haul",
     "then": "The normal\u2192war edge resets the task to Queued, stops the job and drops carried cargo in place; the kept queue is revalidated when the threat clears.",
     "detected_by": "VER-DIS-004"
    }
   ]
  },
  {
   "id": "flow.war",
   "name": "Raid: war mode to recovery",
   "trigger": "Vanilla reports any hostile active threat to the player (cached 60 ticks), or a dog is hit by a hostile",
   "description": "The path with the most state transitions: suspension, deployment, casualty tracking and the return to work. Deliberately destructive to in-flight work.",
   "steps": [
    {
     "n": 1,
     "from": "ext.rimworld",
     "to": "comp.network",
     "action": "War edge",
     "mechanism": "Sets warActive, resets every normal task to Queued, stops matching jobs and any survey/haul/clean/carry-home job, dropping cargo in place; the normal queue itself is kept."
    },
    {
     "n": 2,
     "from": "comp.comprobodog",
     "to": "comp.network",
     "action": "Attack ping (bypasses cadence)",
     "mechanism": "A dog hit by a hostile reports the attacker, drops the raid cache and forces an immediate tick \u2014 a stable-less Combatdog still reacts."
    },
    {
     "n": 3,
     "from": "comp.network",
     "to": "comp.combat",
     "action": "Deploy Combatdogs",
     "mechanism": "Available Combatdogs deploy; idle workers return to their home stable when reachable, else any reachable stable with a free slot (wartime-only re-homing), else wait and retry; Medirunners deploy when patients exist."
    },
    {
     "n": 4,
     "from": "comp.network",
     "to": "comp.packcombat",
     "action": "Pack focus",
     "mechanism": "T3/T4 leaders recruit within 15 cells; focus = queued reachable standing hostile nearest to any member; retained while valid; re-selected at native job boundaries so pursuit never hands off to a homeward guard gap."
    },
    {
     "n": 5,
     "from": "comp.comprobodog",
     "to": "comp.network",
     "action": "Casualty broadcast",
     "condition": "Combatdog downed or destroyed",
     "mechanism": "ReportCasualty/ReportDeath dedupe by casualty identity and save pawn ID, last-known cell and living/wreck state; a destroyed wreck never produces a replacement task."
    },
    {
     "n": 6,
     "from": "comp.network",
     "to": "comp.retrieval",
     "action": "Recovery after threats",
     "condition": "warActive clears",
     "mechanism": "Normal queue revalidated; recovery tasks sort ahead of utility work; healthy Quadrunners or Medirunners carry living dogs to a dock and wrecks to the stable's holder \u2014 deferred medical emergencies still outrank recovery."
    },
    {
     "n": 7,
     "from": "comp.stable-building",
     "to": "comp.fabrication",
     "action": "Powered wreck rebuild",
     "condition": "50 steel + 25 plasteel delivered into the 3\u00d73 interaction bay",
     "mechanism": "Consumes ingredients only when all present, one recipe-work unit per tick for 2,000 powered ticks; load never spends twice; resurrection restores the same pawn, tier and guns."
    }
   ],
   "failure_modes": [
    {
     "when": "Coordinator stable is destroyed mid-war",
     "then": "Election moves NetworkState to a surviving node (next stable by thingID, else newest active dog) without losing the queues.",
     "detected_by": "VER-DIS-005"
    },
    {
     "when": "Every stable and active dog is lost",
     "then": "Both queues and transient exploration are permanently cleared; a later node never resurrects the old backlog.",
     "detected_by": "VER-DIS-005"
    },
    {
     "when": "Home stable unreachable for a returning worker",
     "then": "Wartime-only fallback re-homes to another reachable stable with a free slot; with none, the dog waits in place and retries on later refreshes.",
     "detected_by": "VER-DIS-004"
    },
    {
     "when": "Rebuild interrupted by power loss",
     "then": "Processing pauses and resumes; ingredient accounting survives save/load with no duplicate payment.",
     "detected_by": "VER-REC-003"
    }
   ]
  },
  {
   "id": "flow.charge",
   "name": "Charge decision: stable versus conduit",
   "trigger": "Battery crosses 10% (latch wantsCharge; clears only at 100%)",
   "description": "The decision that surprises newcomers: a dog on a conduit is powered by the colony's stored energy, not by live generation.",
   "steps": [
    {
     "n": 1,
     "from": "comp.comprobodog",
     "to": "comp.charging",
     "action": "Think-tree node 5 fires",
     "mechanism": "Charge seeking sits above all work; a dog that wants a charge never takes network orders. Crossing 10% mid-fight undrafts or interrupts a combat job so the dog retreats."
    },
    {
     "n": 2,
     "from": "comp.charging",
     "to": "comp.chargespots",
     "action": "Compare time-to-full",
     "mechanism": "Straight-line travel + missing-charge/rate for the powered stable (fast: 0.0012/tick) versus the best conduit spot (slow: 0.0004/tick, 3\u00d7 slower but may win when the stable is far); an unpowered stable is a fallback only when no spot exists."
    },
    {
     "n": 3,
     "from": "comp.chargespots",
     "to": "ext.rimworld",
     "action": "Spot eligibility",
     "mechanism": "Free standable roofed indoor non-doorway cell served by a colony power transmitter, outside the turbine radius (mod setting, default 6); static cache rebuilt on building-signature change or 2500 ticks."
    },
    {
     "n": 4,
     "from": "comp.charging",
     "to": "ext.rimworld",
     "action": "Draw from batteries",
     "condition": "conduit spot chosen",
     "mechanism": "Lies down and takes 0.0004/tick from the net's batteryComps, fullest first; stops when full, when batteries run dry, or when the spot stops qualifying (rechecked every 250 ticks)."
    },
    {
     "n": 5,
     "from": "comp.charging",
     "to": "comp.comprobodog",
     "action": "Charged \u2192 back to orders",
     "condition": "battery reaches 100%",
     "mechanism": "wantsCharge clears only at full; the dog's next think-tree pass takes network orders or surveys again."
    }
   ],
   "failure_modes": [
    {
     "when": "No valid conduit spot exists",
     "then": "The dog charges at the stable \u2014 an unpowered stable is accepted as the fallback.",
     "detected_by": "VER-CHG-001"
    },
    {
     "when": "Batteries run dry mid-charge",
     "then": "The driver gets up and searches again; a producing net with empty batteries is a dead spot.",
     "detected_by": "VER-CHG-001"
    },
    {
     "when": "Battery hits 0% before any charger",
     "then": "QR_PoweredDown hediff zeroes Moving (downed); the network queues retrieval, a colonist can carry the dog, or a field battery is delivered; raiders may kidnap it.",
     "detected_by": "VER-REC-001"
    }
   ]
  }
 ],
 "annotations": [
  {
   "id": "ann.dispatchersavenotidentity",
   "target": "comp.network",
   "kind": "decision",
   "text": "The dispatcher deliberately has no identity in the save. DogNetwork is a static per-map dictionary; only NetworkState is saved, on exactly one elected node. Deleting the coordinator in dev tools re-elects on next access; losing every node clears the backlog permanently.",
   "author": "plan 0003 decision, restated in docs/architecture.md"
  },
  {
   "id": "ann.conduitbatteries",
   "target": "comp.charging",
   "kind": "risk",
   "text": "Conduit charging draws stored battery energy, fullest first. A power net that is producing but has no stored energy cannot charge a dog even though the base is powered \u2014 the single most surprising behavior in the mod.",
   "author": "docs/architecture.md"
  },
  {
   "id": "ann.dockednotspawned",
   "target": "comp.stable-building",
   "kind": "note",
   "text": "Docked does not mean spawned: docking despawns the pawn into the stable's container, so docked dogs do not tick and their think tree does not run. The stable's tick keeps the network alive and must explicitly undock a dog before it can work.",
   "author": "docs/architecture.md"
  },
  {
   "id": "ann.scopedverification",
   "target": "sub.verification",
   "kind": "risk",
   "text": "Recent practice is scoped runs (only scenarios the change can affect), not full Run all: the shared instance shares the CPU with the player's game and some fixtures are sequence-sensitive on a reused map. The last clean full packaged Run all is 39/39 (2026-10-20); later releases record scoped package verifications in docs/test-runs.md. No recent full-suite pass is claimed.",
   "author": "TODO.md current state, 2026-11-07"
  },
  {
   "id": "ann.besideusergame",
   "target": "comp.host-harness",
   "kind": "decision",
   "text": "The harness runs beside the player's open game (proven 2026-10-19, made the default): private profile, private Xvfb display, private log. It never touches the player's profile, window or input; tools/status.sh reports both games.",
   "author": "AGENTS.md"
  },
  {
   "id": "ann.harmonybudget",
   "target": "comp.bootstrap",
   "kind": "decision",
   "text": "Harmony budget of about six patches, each where vanilla has no virtual or hook: rider draw, kidnap victims, construction eligibility, draft controller, colony-subhuman gate, carried-pawn angle. The seventh (quicktest setup) is test-gated and inert in a normal game. XML StatParts were preferred where a hook existed.",
   "author": "plan 0001 Decisions; docs/design.md"
  },
  {
   "id": "ann.newestsubsystem",
   "target": "comp.hose",
   "kind": "note",
   "text": "Newest gameplay subsystem (plan 0010, delivered 2026-11-06). Hose jobs are chosen inside Work's clean-task construction: a qualifying \u22654-tile patch takes the hose job before ordinary cleaning.",
   "author": "docs/plans/0010-water-hose.md"
  },
  {
   "id": "ann.modelmaintenance",
   "target": "sys.quadrunner",
   "kind": "note",
   "text": "This model is generated documentation: to change any diagram, edit docs/diagrams/model.json and re-run the renderer (see docs/diagrams/README.md). Line-number evidence was captured at generation time (2026-11-07) and will drift as source moves.",
   "author": "docs/diagrams README"
  }
 ],
 "index": {
  "reqs_for_node": {
   "sub.dispatch": [
    "REQ-SYS-001",
    "REQ-SYS-002",
    "REQ-SYS-003",
    "REQ-SYS-008"
   ],
   "sub.stable": [
    "REQ-SYS-001",
    "REQ-SYS-002"
   ],
   "sub.pawn": [
    "REQ-SYS-001",
    "REQ-SYS-005"
   ],
   "sub.combat": [
    "REQ-SYS-002"
   ],
   "sub.rescue": [
    "REQ-SYS-003"
   ],
   "sub.riding": [
    "REQ-SYS-004"
   ],
   "sub.power": [
    "REQ-SYS-005"
   ],
   "sub.recovery": [
    "REQ-SYS-005"
   ],
   "sub.removal": [
    "REQ-SYS-006"
   ],
   "sub.verification": [
    "REQ-SYS-006",
    "REQ-SYS-007"
   ],
   "sub.render": [
    "REQ-SYS-009"
   ],
   "sub.foundation": [
    "REQ-SYS-009"
   ],
   "comp.art": [
    "REQ-SYS-009"
   ],
   "comp.bootstrap": [
    "REQ-FND-001"
   ],
   "comp.stable-ui": [
    "REQ-FND-002"
   ],
   "comp.work": [
    "REQ-DIS-001",
    "REQ-WRK-001"
   ],
   "comp.network": [
    "REQ-DIS-001",
    "REQ-DIS-002",
    "REQ-DIS-003",
    "REQ-DIS-004",
    "REQ-DIS-005",
    "REQ-RSC-002",
    "REQ-RSC-003",
    "REQ-REC-003",
    "REQ-CBT-002"
   ],
   "comp.survey": [
    "REQ-DIS-006"
   ],
   "comp.defs": [
    "REQ-WRK-001",
    "REQ-PWN-001"
   ],
   "comp.stable-building": [
    "REQ-STB-001",
    "REQ-STB-002",
    "REQ-REC-003",
    "REQ-CBT-003"
   ],
   "comp.docking": [
    "REQ-STB-001"
   ],
   "comp.fabrication": [
    "REQ-STB-002",
    "REQ-CBT-004"
   ],
   "comp.races": [
    "REQ-PWN-001"
   ],
   "comp.charging": [
    "REQ-CHG-001"
   ],
   "comp.chargespots": [
    "REQ-CHG-001"
   ],
   "comp.settings": [
    "REQ-CHG-001",
    "REQ-REM-001"
   ],
   "comp.comprobodog": [
    "REQ-CHG-002"
   ],
   "comp.rescue": [
    "REQ-RSC-001",
    "REQ-RSC-002"
   ],
   "comp.firefighting": [
    "REQ-RSC-003"
   ],
   "comp.hose": [
    "REQ-HOS-001"
   ],
   "comp.retrieval": [
    "REQ-REC-001",
    "REQ-REC-003"
   ],
   "comp.fieldfix": [
    "REQ-REC-002"
   ],
   "comp.combat": [
    "REQ-CBT-001",
    "REQ-CBT-002",
    "REQ-CBT-003"
   ],
   "comp.tiers": [
    "REQ-CBT-004"
   ],
   "comp.packcombat": [
    "REQ-CBT-005"
   ],
   "comp.drafting": [
    "REQ-CBT-006"
   ],
   "comp.riding": [
    "REQ-RID-001"
   ],
   "comp.render-nodes": [
    "REQ-RND-001"
   ],
   "comp.removal": [
    "REQ-REM-001"
   ],
   "comp.host-harness": [
    "REQ-TST-001",
    "REQ-TST-002",
    "REQ-TST-003"
   ],
   "comp.selftest": [
    "REQ-TST-001"
   ],
   "comp.scenarios": [
    "REQ-TST-002"
   ],
   "comp.autotest": [
    "REQ-TST-002"
   ],
   "comp.removal-probe": [
    "REQ-TST-004"
   ]
  },
  "vers_for_req": {
   "REQ-SYS-001": [
    "VER-SYS-001"
   ],
   "REQ-SYS-002": [
    "VER-SYS-002"
   ],
   "REQ-SYS-003": [
    "VER-SYS-003"
   ],
   "REQ-SYS-004": [
    "VER-SYS-004"
   ],
   "REQ-SYS-005": [
    "VER-SYS-005"
   ],
   "REQ-SYS-006": [
    "VER-SYS-006"
   ],
   "REQ-SYS-007": [
    "VER-SYS-007"
   ],
   "REQ-SYS-008": [
    "VER-SYS-008"
   ],
   "REQ-SYS-009": [
    "VER-SYS-009"
   ],
   "REQ-FND-002": [
    "VER-SYS-009"
   ],
   "REQ-DIS-001": [
    "VER-DIS-001"
   ],
   "REQ-DIS-002": [
    "VER-DIS-002"
   ],
   "REQ-DIS-003": [
    "VER-DIS-003"
   ],
   "REQ-DIS-004": [
    "VER-DIS-004"
   ],
   "REQ-DIS-005": [
    "VER-DIS-005"
   ],
   "REQ-DIS-006": [
    "VER-DIS-006"
   ],
   "REQ-WRK-001": [
    "VER-WRK-001"
   ],
   "REQ-STB-001": [
    "VER-STB-001"
   ],
   "REQ-STB-002": [
    "VER-STB-002"
   ],
   "REQ-PWN-001": [
    "VER-PWN-001"
   ],
   "REQ-CHG-001": [
    "VER-CHG-001"
   ],
   "REQ-CHG-002": [
    "VER-CHG-002"
   ],
   "REQ-RSC-001": [
    "VER-RSC-001"
   ],
   "REQ-RSC-002": [
    "VER-RSC-002"
   ],
   "REQ-RSC-003": [
    "VER-RSC-003"
   ],
   "REQ-HOS-001": [
    "VER-HOS-001"
   ],
   "REQ-REC-001": [
    "VER-REC-001"
   ],
   "REQ-REC-002": [
    "VER-REC-002"
   ],
   "REQ-REC-003": [
    "VER-REC-003"
   ],
   "REQ-CBT-001": [
    "VER-CBT-001"
   ],
   "REQ-CBT-002": [
    "VER-CBT-002"
   ],
   "REQ-CBT-003": [
    "VER-CBT-003"
   ],
   "REQ-CBT-004": [
    "VER-CBT-004"
   ],
   "REQ-CBT-005": [
    "VER-CBT-005"
   ],
   "REQ-CBT-006": [
    "VER-CBT-006"
   ],
   "REQ-RID-001": [
    "VER-RID-001"
   ],
   "REQ-RND-001": [
    "VER-RND-001"
   ],
   "REQ-REM-001": [
    "VER-REM-001"
   ],
   "REQ-TST-001": [
    "VER-TST-001"
   ],
   "REQ-TST-002": [
    "VER-TST-002"
   ],
   "REQ-FND-001": [
    "VER-TST-002"
   ],
   "REQ-TST-003": [
    "VER-TST-003"
   ],
   "REQ-TST-004": [
    "VER-TST-004"
   ]
  },
  "flows_for_node": {
   "ext.rimworld": [
    "flow.haul",
    "flow.war",
    "flow.charge"
   ],
   "comp.comprobodog": [
    "flow.haul",
    "flow.war",
    "flow.charge"
   ],
   "comp.work": [
    "flow.haul"
   ],
   "comp.network": [
    "flow.haul",
    "flow.war"
   ],
   "comp.combat": [
    "flow.war"
   ],
   "comp.packcombat": [
    "flow.war"
   ],
   "comp.retrieval": [
    "flow.war"
   ],
   "comp.stable-building": [
    "flow.war"
   ],
   "comp.fabrication": [
    "flow.war"
   ],
   "comp.charging": [
    "flow.charge"
   ],
   "comp.chargespots": [
    "flow.charge"
   ]
  },
  "children": {
   "sys.quadrunner": [
    "sub.foundation",
    "sub.stable",
    "sub.pawn",
    "sub.power",
    "sub.dispatch",
    "sub.rescue",
    "sub.recovery",
    "sub.combat",
    "sub.riding",
    "sub.render",
    "sub.removal",
    "sub.verification"
   ],
   "sub.foundation": [
    "comp.bootstrap",
    "comp.defs",
    "comp.settings",
    "comp.stable-ui"
   ],
   "sub.stable": [
    "comp.stable-building",
    "comp.fabrication",
    "comp.tiers"
   ],
   "sub.pawn": [
    "comp.comprobodog",
    "comp.docking",
    "comp.races"
   ],
   "sub.power": [
    "comp.charging",
    "comp.chargespots"
   ],
   "sub.dispatch": [
    "comp.network",
    "comp.work",
    "comp.survey"
   ],
   "sub.rescue": [
    "comp.rescue",
    "comp.firefighting",
    "comp.hose"
   ],
   "sub.recovery": [
    "comp.retrieval",
    "comp.fieldfix"
   ],
   "sub.combat": [
    "comp.combat",
    "comp.packcombat",
    "comp.drafting"
   ],
   "sub.riding": [
    "comp.riding"
   ],
   "sub.render": [
    "comp.render-nodes",
    "comp.art"
   ],
   "sub.removal": [
    "comp.removal"
   ],
   "sub.verification": [
    "comp.autotest",
    "comp.scenarios",
    "comp.host-harness",
    "comp.selftest",
    "comp.removal-probe"
   ]
  },
  "page_for": {
   "sys.quadrunner": "index.html",
   "sub.foundation": "sub--sub-foundation.html",
   "sub.stable": "sub--sub-stable.html",
   "sub.pawn": "sub--sub-pawn.html",
   "sub.power": "sub--sub-power.html",
   "sub.dispatch": "sub--sub-dispatch.html",
   "sub.rescue": "sub--sub-rescue.html",
   "sub.recovery": "sub--sub-recovery.html",
   "sub.combat": "sub--sub-combat.html",
   "sub.riding": "sub--sub-riding.html",
   "sub.render": "sub--sub-render.html",
   "sub.removal": "sub--sub-removal.html",
   "sub.verification": "sub--sub-verification.html",
   "comp.bootstrap": "sub--sub-foundation.html",
   "comp.defs": "sub--sub-foundation.html",
   "comp.settings": "sub--sub-foundation.html",
   "comp.stable-ui": "sub--sub-foundation.html",
   "comp.stable-building": "sub--sub-stable.html",
   "comp.fabrication": "sub--sub-stable.html",
   "comp.tiers": "sub--sub-stable.html",
   "comp.comprobodog": "sub--sub-pawn.html",
   "comp.docking": "sub--sub-pawn.html",
   "comp.races": "sub--sub-pawn.html",
   "comp.charging": "sub--sub-power.html",
   "comp.chargespots": "sub--sub-power.html",
   "comp.network": "sub--sub-dispatch.html",
   "comp.work": "sub--sub-dispatch.html",
   "comp.survey": "sub--sub-dispatch.html",
   "comp.rescue": "sub--sub-rescue.html",
   "comp.firefighting": "sub--sub-rescue.html",
   "comp.hose": "sub--sub-rescue.html",
   "comp.retrieval": "sub--sub-recovery.html",
   "comp.fieldfix": "sub--sub-recovery.html",
   "comp.combat": "sub--sub-combat.html",
   "comp.packcombat": "sub--sub-combat.html",
   "comp.drafting": "sub--sub-combat.html",
   "comp.riding": "sub--sub-riding.html",
   "comp.render-nodes": "sub--sub-render.html",
   "comp.art": "sub--sub-render.html",
   "comp.removal": "sub--sub-removal.html",
   "comp.autotest": "sub--sub-verification.html",
   "comp.scenarios": "sub--sub-verification.html",
   "comp.host-harness": "sub--sub-verification.html",
   "comp.selftest": "sub--sub-verification.html",
   "comp.removal-probe": "sub--sub-verification.html",
   "actor.player": "index.html",
   "actor.developer": "index.html",
   "ext.rimworld": "index.html",
   "ext.harmony": "index.html",
   "REQ-SYS-001": "traceability.html",
   "REQ-SYS-002": "traceability.html",
   "REQ-SYS-003": "traceability.html",
   "REQ-SYS-004": "traceability.html",
   "REQ-SYS-005": "traceability.html",
   "REQ-SYS-006": "traceability.html",
   "REQ-SYS-007": "traceability.html",
   "REQ-SYS-008": "traceability.html",
   "REQ-SYS-009": "traceability.html",
   "REQ-FND-001": "traceability.html",
   "REQ-FND-002": "traceability.html",
   "REQ-DIS-001": "traceability.html",
   "REQ-DIS-002": "traceability.html",
   "REQ-DIS-003": "traceability.html",
   "REQ-DIS-004": "traceability.html",
   "REQ-DIS-005": "traceability.html",
   "REQ-DIS-006": "traceability.html",
   "REQ-WRK-001": "traceability.html",
   "REQ-STB-001": "traceability.html",
   "REQ-STB-002": "traceability.html",
   "REQ-PWN-001": "traceability.html",
   "REQ-CHG-001": "traceability.html",
   "REQ-CHG-002": "traceability.html",
   "REQ-RSC-001": "traceability.html",
   "REQ-RSC-002": "traceability.html",
   "REQ-RSC-003": "traceability.html",
   "REQ-HOS-001": "traceability.html",
   "REQ-REC-001": "traceability.html",
   "REQ-REC-002": "traceability.html",
   "REQ-REC-003": "traceability.html",
   "REQ-CBT-001": "traceability.html",
   "REQ-CBT-002": "traceability.html",
   "REQ-CBT-003": "traceability.html",
   "REQ-CBT-004": "traceability.html",
   "REQ-CBT-005": "traceability.html",
   "REQ-CBT-006": "traceability.html",
   "REQ-RID-001": "traceability.html",
   "REQ-RND-001": "traceability.html",
   "REQ-REM-001": "traceability.html",
   "REQ-TST-001": "traceability.html",
   "REQ-TST-002": "traceability.html",
   "REQ-TST-003": "traceability.html",
   "REQ-TST-004": "traceability.html",
   "VER-SYS-001": "traceability.html",
   "VER-SYS-002": "traceability.html",
   "VER-SYS-003": "traceability.html",
   "VER-SYS-004": "traceability.html",
   "VER-SYS-005": "traceability.html",
   "VER-SYS-006": "traceability.html",
   "VER-SYS-007": "traceability.html",
   "VER-SYS-008": "traceability.html",
   "VER-SYS-009": "traceability.html",
   "VER-DIS-001": "traceability.html",
   "VER-DIS-002": "traceability.html",
   "VER-DIS-003": "traceability.html",
   "VER-DIS-004": "traceability.html",
   "VER-DIS-005": "traceability.html",
   "VER-DIS-006": "traceability.html",
   "VER-WRK-001": "traceability.html",
   "VER-STB-001": "traceability.html",
   "VER-STB-002": "traceability.html",
   "VER-PWN-001": "traceability.html",
   "VER-CHG-001": "traceability.html",
   "VER-CHG-002": "traceability.html",
   "VER-RSC-001": "traceability.html",
   "VER-RSC-002": "traceability.html",
   "VER-RSC-003": "traceability.html",
   "VER-HOS-001": "traceability.html",
   "VER-REC-001": "traceability.html",
   "VER-REC-002": "traceability.html",
   "VER-REC-003": "traceability.html",
   "VER-CBT-001": "traceability.html",
   "VER-CBT-002": "traceability.html",
   "VER-CBT-003": "traceability.html",
   "VER-CBT-004": "traceability.html",
   "VER-CBT-005": "traceability.html",
   "VER-CBT-006": "traceability.html",
   "VER-RID-001": "traceability.html",
   "VER-RND-001": "traceability.html",
   "VER-REM-001": "traceability.html",
   "VER-TST-001": "traceability.html",
   "VER-TST-002": "traceability.html",
   "VER-TST-003": "traceability.html",
   "VER-TST-004": "traceability.html",
   "flow.haul": "flow--flow-haul.html",
   "flow.war": "flow--flow-war.html",
   "flow.charge": "flow--flow-charge.html"
  }
 }
};
