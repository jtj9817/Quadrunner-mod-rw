window.SYSTEM_MODEL = {
 "meta": {
  "name": "Quadrunner (RimWorld 1.6 mod)",
  "mode": "brownfield",
  "version": "1.0.0",
  "description": "How the Quadrunner mod works: one stable builds robot dogs, a per-map dispatch network assigns vanilla jobs to the nearest dog, and battery, recovery, rescue, riding, combat and rendering each follow explicit rules. Requirements are reconstructed from plans 0001/0003 and the code.",
  "source_of_truth": "docs/diagrams/model.json",
  "generated": "2026-10-21"
 },
 "nodes": [
  {
   "id": "sys.quadrunner",
   "name": "Quadrunner",
   "level": 0,
   "kind": "system",
   "parent": null,
   "description": "A RimWorld 1.6 mod (package josh9817.quadrunner): a stable builds robot dogs, a per-map network dispatches them to vanilla work, and each dog's battery, recovery, rescue, riding, combat and rendering follow explicit rules.",
   "tech": [
    "C# net472",
    "RimWorld 1.6"
   ],
   "responsibilities": [
    "Ship one DLL plus Defs/Textures",
    "Stay inside Core + Harmony"
   ],
   "evidence": [
    "About/About.xml:1-29",
    "Source/Quadrunner/QR.cs:7-21"
   ]
  },
  {
   "id": "sub.stable",
   "name": "Stable & Fabrication",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Houses, builds and services dogs: docking and charging, bills, the Overview tab, recovery and field fixes.",
   "owner": "stable",
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs",
    "Source/Quadrunner/Assembly.cs",
    "Source/Quadrunner/ITab_StableOverview.cs",
    "Source/Quadrunner/Retrieval.cs",
    "Source/Quadrunner/FieldFix.cs",
    "Source/Quadrunner/Removal.cs"
   ]
  },
  {
   "id": "sub.pawn",
   "name": "Robodog Pawn",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "The robot race, its battery comp and the think tree that orders charge, network work, dock and standby.",
   "owner": "pawn",
   "evidence": [
    "Defs/ThingDefs_Races/QR_Races.xml",
    "Source/Quadrunner/Robodog.cs",
    "Defs/ThinkTreeDefs/QR_ThinkTrees.xml"
   ]
  },
  {
   "id": "sub.network",
   "name": "Dispatch Network",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "One dispatcher per map: local discovery, survey exploration, and nearest-capable assignment by path cost.",
   "owner": "network",
   "evidence": [
    "Source/Quadrunner/Network.cs",
    "Source/Quadrunner/Work.cs",
    "Source/Quadrunner/Survey.cs"
   ]
  },
  {
   "id": "sub.charging",
   "name": "Charging",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "How a low dog picks stable or conduit, what it draws from the grid, and how it is recovered at 0%.",
   "owner": "charging",
   "evidence": [
    "Source/Quadrunner/Charging.cs",
    "Source/Quadrunner/ChargeSpots.cs",
    "Source/Quadrunner/Settings.cs"
   ]
  },
  {
   "id": "sub.work",
   "name": "Work Execution",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Turns one queued target into one vanilla job, plus the raid speed/drain stat part and construction check patch.",
   "owner": "work",
   "evidence": [
    "Source/Quadrunner/Work.cs",
    "Patches/QR_Stats.xml",
    "Defs/WorkGiverDefs/QR_WorkGivers.xml"
   ]
  },
  {
   "id": "sub.rescue",
   "name": "Rescue",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Medirunner triage and the vanilla rescue jobs: ranked patients, one medic each, map-wide and under fire.",
   "owner": "rescue",
   "evidence": [
    "Source/Quadrunner/Rescue.cs",
    "Source/Quadrunner/Network.cs",
    "Defs/ThingDefs_Races/QR_Races.xml"
   ]
  },
  {
   "id": "sub.riding",
   "name": "Riding",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "The ride order, the rider/dog job pair with its dismount rules, ridden speed and rider draw offset.",
   "owner": "riding",
   "evidence": [
    "Source/Quadrunner/Riding.cs",
    "Patches/QR_Stats.xml"
   ]
  },
  {
   "id": "sub.combat",
   "name": "Combat & Tiers",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "Pings, war-queue engagement, capture, patrol and guard, drafting, and four tiers with built-in guns.",
   "owner": "combat",
   "evidence": [
    "Source/Quadrunner/Combat.cs",
    "Source/Quadrunner/Tiers.cs",
    "Source/Quadrunner/Drafting.cs",
    "Defs/ThingDefs_Misc/QR_Weapons.xml"
   ]
  },
  {
   "id": "sub.render",
   "name": "Rendering",
   "level": 1,
   "kind": "subsystem",
   "parent": "sys.quadrunner",
   "description": "The render tree, split-leg gait, action poses, back guns and carried pawns, and the two sirens.",
   "owner": "render",
   "evidence": [
    "Source/Quadrunner/Rendering.cs",
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml",
    "Source/Quadrunner/Robodog.cs"
   ]
  },
  {
   "id": "comp.stable",
   "name": "Stable building",
   "level": 2,
   "kind": "service",
   "parent": "sub.stable",
   "description": "Docks up to 15 dogs by despawning them into a ThingOwner, charges and repairs them while powered, stores patrol waypoints and owns the network state when elected.",
   "tech": [
    "Building_WorkTable, IThingHolder"
   ],
   "responsibilities": [
    "Dock and undock dogs",
    "Charge at 0.0012/tick and heal 0.02 HP/tick while powered",
    "Place and clear patrol waypoints",
    "Host the map NetworkState when elected"
   ],
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs:10-15",
    "Source/Quadrunner/Building_Stable.cs:22-88",
    "Source/Quadrunner/Building_Stable.cs:137-221",
    "Source/Quadrunner/Building_Stable.cs:226-299",
    "Defs/ThingDefs_Buildings/QR_Stable.xml:73-82"
   ]
  },
  {
   "id": "comp.bills",
   "name": "Dog bills",
   "level": 2,
   "kind": "job",
   "parent": "sub.stable",
   "description": "The stable is the crafting station: assemble and rebuild bills produce or resurrect dogs; a dog bill refuses to start when all 15 slots are taken.",
   "tech": [
    "Source/Quadrunner/Assembly.cs, Bill_Production"
   ],
   "responsibilities": [
    "Generate a dog per finished bill",
    "Rebuild a wreck as the same dog",
    "Count dogs for 'do until X' bills",
    "Block dog bills on a full stable"
   ],
   "evidence": [
    "Source/Quadrunner/Assembly.cs:10-90",
    "Defs/RecipeDefs/QR_Recipes.xml:18-236"
   ]
  },
  {
   "id": "comp.overview",
   "name": "Overview tab",
   "level": 2,
   "kind": "ui",
   "parent": "sub.stable",
   "description": "One row per assigned dog (docked first) with type, battery, health and current job; Combatdogs also get tier, per-slot gun pickers and one upgrade button.",
   "tech": [
    "ITab_StableOverview"
   ],
   "responsibilities": [
    "Render the stable's dog rows",
    "Pick a built-in gun per slot",
    "Order or cancel one upgrade bill per dog"
   ],
   "evidence": [
    "Source/Quadrunner/ITab_StableOverview.cs:11-49",
    "Source/Quadrunner/ITab_StableOverview.cs:92-131",
    "Languages/English/Keyed/QR_Keyed.xml:3"
   ]
  },
  {
   "id": "comp.retrieval",
   "name": "Dog recovery",
   "level": 2,
   "kind": "job",
   "parent": "sub.stable",
   "description": "Powered-down dogs lie like downed pawns: a network carrier or a colonist carries one to the nearest stable with a free slot; a fire beside it is beaten out first; raiders can take it.",
   "tech": [
    "Source/Quadrunner/Retrieval.cs, Source/Quadrunner/Docking.cs"
   ],
   "responsibilities": [
    "Build the CarryToStable job",
    "Beat out fire within 1.5 cells first",
    "Alert on a dog no stable can reach",
    "Let hostile kidnappers take a powered-down dog"
   ],
   "evidence": [
    "Source/Quadrunner/Retrieval.cs:15-69",
    "Source/Quadrunner/Retrieval.cs:77-211",
    "Source/Quadrunner/Docking.cs:14-100"
   ]
  },
  {
   "id": "comp.fieldfix",
   "name": "Field fixes",
   "level": 2,
   "kind": "job",
   "parent": "sub.stable",
   "description": "Colonists bring a repair kit to a dog below 70% health, then a battery to a powered-down one; both items are made at the stable under Hauling priority 110.",
   "tech": [
    "Source/Quadrunner/FieldFix.cs, WorkGiverDef"
   ],
   "responsibilities": [
    "Choose the needed item (kit first)",
    "Deliver and apply it in the field"
   ],
   "evidence": [
    "Source/Quadrunner/FieldFix.cs:10-118",
    "Defs/ThingDefs_Items/QR_Items.xml:16-55",
    "Defs/WorkGiverDefs/QR_WorkGivers.xml:24-35"
   ]
  },
  {
   "id": "comp.mod-core",
   "name": "Mod core & removal",
   "level": 2,
   "kind": "service",
   "parent": "sub.stable",
   "description": "Startup and logging, PatchAll, the turbine-distance setting, the dev-only debug actions, and the confirmed clean-removal cutover that writes a backup and a cleaned export.",
   "tech": [
    "Source/Quadrunner/QR.cs, Source/Quadrunner/Settings.cs, Source/Quadrunner/Removal.cs"
   ],
   "responsibilities": [
    "Log the build id and install the six Harmony patches",
    "Store mod settings",
    "Prepare and export a save without mod content"
   ],
   "evidence": [
    "Source/Quadrunner/QR.cs:7-33",
    "Source/Quadrunner/Settings.cs:6-45",
    "Source/Quadrunner/Removal.cs:14-203",
    "Source/Quadrunner/DebugActions.cs:10-131"
   ]
  },
  {
   "id": "comp.race",
   "name": "Dog races",
   "level": 2,
   "kind": "service",
   "parent": "sub.pawn",
   "description": "One abstract robot race on a custom non-organic flesh type, Animal intelligence, no food/rest, untrainable; six concrete models tune work, rescue, combat, riding and tiers.",
   "tech": [
    "QR_RobodogBase + 6 ThingDefs"
   ],
   "responsibilities": [
    "Define body, flesh, needs and immunity",
    "Set per-model work/speed/riding tunables",
    "Name each Combatdog tier and its upgrade"
   ],
   "evidence": [
    "Defs/ThingDefs_Races/QR_Races.xml:6-92",
    "Defs/ThingDefs_Races/QR_Races.xml:94-153",
    "Defs/ThingDefs_Races/QR_Races.xml:157-305",
    "Defs/Bodies/QR_Bodies.xml",
    "Defs/Misc/QR_FleshTypes.xml"
   ]
  },
  {
   "id": "comp.battery",
   "name": "Battery comp",
   "level": 2,
   "kind": "service",
   "parent": "sub.pawn",
   "description": "Per-dog battery: drains per cell walked, latches 'wants a charge' at 10%, powers down at 0% with QR_PoweredDown, and carries the saved stable/network/gun state.",
   "tech": [
    "CompRobodog"
   ],
   "responsibilities": [
    "Drain on cell changes under 1.5 cells",
    "Latch/clear want-charge and retreat",
    "Add/remove QR_PoweredDown",
    "Save battery, stable, guns and waypoint count"
   ],
   "evidence": [
    "Source/Quadrunner/Robodog.cs:10-23",
    "Source/Quadrunner/Robodog.cs:30-138",
    "Source/Quadrunner/Robodog.cs:158-187",
    "Source/Quadrunner/Robodog.cs:220-231",
    "Defs/HediffDefs/QR_Hediffs.xml:6-22"
   ]
  },
  {
   "id": "comp.think-tree",
   "name": "Think tree",
   "level": 2,
   "kind": "service",
   "parent": "sub.pawn",
   "description": "The main think tree: despawned/downed sub-trees, drafted orders, seek-charge, network order, dock, wait-in-room, wander. A dog never searches the map for work on its own.",
   "tech": [
    "QR_Robodog ThinkTreeDef"
   ],
   "responsibilities": [
    "Order the job givers",
    "Run drafted orders as vanilla",
    "Fall back to dock then standby"
   ],
   "evidence": [
    "Defs/ThinkTreeDefs/QR_ThinkTrees.xml:4-51",
    "Source/Quadrunner/Work.cs:207-211",
    "Source/Quadrunner/Docking.cs:14-33"
   ]
  },
  {
   "id": "comp.net-node",
   "name": "Network node",
   "level": 2,
   "kind": "service",
   "parent": "sub.network",
   "description": "One dispatcher per map driven by existing ticks: it elects a node, owns the normal and war queues, reconciles progress, and assigns tasks to the nearest capable dog.",
   "tech": [
    "DogNetwork, NetworkState"
   ],
   "responsibilities": [
    "Elect and transfer the saved NetworkState",
    "Queue, dedupe and reconcile tasks",
    "Assign by path cost and suspend work in war",
    "Report queues/coordinator for inspection"
   ],
   "evidence": [
    "Source/Quadrunner/Network.cs:13-81",
    "Source/Quadrunner/Network.cs:96-130",
    "Source/Quadrunner/Network.cs:200-248",
    "Source/Quadrunner/Network.cs:301-368",
    "Source/Quadrunner/Network.cs:387-536",
    "Source/Quadrunner/Network.cs:618-628"
   ]
  },
  {
   "id": "comp.discovery",
   "name": "Local discovery",
   "level": 2,
   "kind": "job",
   "parent": "sub.network",
   "description": "A dog's radial observation: 15 cells every 120 ticks, revealed only, eligibility per target kind, deduplicated by the network. This is the only way ordinary work enters the queue.",
   "tech": [
    "DogWork.ReportLocal"
   ],
   "responsibilities": [
    "Scan around each active dog",
    "Map a work giver to a task kind",
    "Filter fog, forbidden, Home and freshness"
   ],
   "evidence": [
    "Source/Quadrunner/Work.cs:55-135",
    "Source/Quadrunner/Network.cs:332-349"
   ]
  },
  {
   "id": "comp.survey",
   "name": "Survey",
   "level": 2,
   "kind": "job",
   "parent": "sub.network",
   "description": "Idle exploration: Home-first rooms, storage and zones, then residual Home and exterior; shared scan-certified finite sweeps and vanilla route-cost area selection with retained 80-cell travel legs.",
   "tech": [
    "Survey, JobDriver_Survey"
   ],
   "responsibilities": [
    "Choose a survey goal",
    "Keep fog-free paths",
    "Credit actual local scan footprints in transient shared sweeps"
   ],
   "evidence": [
    "Source/Quadrunner/Survey.cs",
    "Source/Quadrunner/Survey.Geometry.cs",
    "Source/Quadrunner/Network.cs"
   ]
  },
  {
   "id": "comp.charge-decision",
   "name": "Charge decision",
   "level": 2,
   "kind": "service",
   "parent": "sub.charging",
   "description": "Chooses between the dog's stable and the best conduit spot by which fills the battery soonest, travel included; with no usable spot it still goes to the stable.",
   "tech": [
    "JobGiver_SeekCharge"
   ],
   "responsibilities": [
    "Compute ticks-to-full for both",
    "Choose the sooner (stable wins ties)",
    "Fall back to an unpowered stable"
   ],
   "evidence": [
    "Source/Quadrunner/Charging.cs:14-50",
    "Defs/ThingDefs_Races/QR_Races.xml:80-90"
   ]
  },
  {
   "id": "comp.charge-spots",
   "name": "Charge spots",
   "level": 2,
   "kind": "service",
   "parent": "sub.charging",
   "description": "A per-map cache of standable room cells by conduits that pass room and turbine rules, rebuilt on building changes, re-checked per dog for the volatile rules, then ranked.",
   "tech": [
    "ChargeSpots"
   ],
   "responsibilities": [
    "Build and cache spots",
    "Re-check volatile rules per dog",
    "Rank the first five by distance/occupancy/energy"
   ],
   "evidence": [
    "Source/Quadrunner/ChargeSpots.cs:27-58",
    "Source/Quadrunner/ChargeSpots.cs:61-136",
    "Source/Quadrunner/ChargeSpots.cs:139-195",
    "Source/Quadrunner/Settings.cs:6-15"
   ]
  },
  {
   "id": "comp.conduit-charge",
   "name": "Conduit charge",
   "level": 2,
   "kind": "job",
   "parent": "sub.charging",
   "description": "Lies on the spot and draws the conduit net's stored energy into the dog: up to 100 Wd per full charge, fullest battery first, re-checking the spot every 250 ticks.",
   "tech": [
    "JobDriver_ChargeAtConduit"
   ],
   "responsibilities": [
    "Draw per-tick charge from the net",
    "Pick the fullest batteries first",
    "End on full, dry net or unusable spot"
   ],
   "evidence": [
    "Source/Quadrunner/Charging.cs:57-130",
    "Defs/JobDefs/QR_Jobs.xml:19-24"
   ]
  },
  {
   "id": "comp.work-job",
   "name": "Job builder",
   "level": 2,
   "kind": "job",
   "parent": "sub.work",
   "description": "For one queued target, asks the task's vanilla scanner for exactly that job, refuses anything outside HaulToCell/HaulToContainer/Clean, and trims vanilla's extra targets.",
   "tech": [
    "DogWork.JobFor"
   ],
   "responsibilities": [
    "Build one scanner job per target",
    "Enforce the allowed job defs",
    "Disable opportunistic hauling and trim queues"
   ],
   "evidence": [
    "Source/Quadrunner/Work.cs:143-202",
    "Defs/ThingDefs_Races/QR_Races.xml:100-121"
   ]
  },
  {
   "id": "comp.construct-patch",
   "name": "Construction patch",
   "level": 2,
   "kind": "service",
   "parent": "sub.work",
   "description": "Vanilla's construction check asks for work settings (null on a dog) and skills; for a robodog the prefix reruns it with checkSkills off so material delivery works.",
   "tech": [
    "Harmony Patch_GenConstruct_CanConstruct"
   ],
   "responsibilities": [
    "Skip workSettings/skills for robodogs only",
    "Leave every other pawn untouched"
   ],
   "evidence": [
    "Source/Quadrunner/Work.cs:269-284"
   ]
  },
  {
   "id": "comp.raid-part",
   "name": "Raid speed part",
   "level": 2,
   "kind": "service",
   "parent": "sub.work",
   "description": "MoveSpeed multiplier for a dog that is out (Combatdogs \u00d72 deployed) and \u00d73 while a raid is active; the raid answer is cached for one second.",
   "tech": [
    "StatPart_RobodogRaid, Raid"
   ],
   "responsibilities": [
    "Multiply MoveSpeed while out/deployed",
    "Apply the raid speed factor",
    "Cache AnyHostileActiveThreatToPlayer"
   ],
   "evidence": [
    "Source/Quadrunner/Work.cs:214-266",
    "Patches/QR_Stats.xml:4-10"
   ]
  },
  {
   "id": "comp.triage",
   "name": "Patient triage",
   "level": 2,
   "kind": "service",
   "parent": "sub.rescue",
   "description": "Who is worth rescuing and in what order: colonist, guest, prisoner, colony animal; fastest-bleeding first within a rank; enemies, robodogs, NoCare skipped.",
   "tech": [
    "Rescue.Rank, Rescue.Patients"
   ],
   "responsibilities": [
    "Rank a downed pawn",
    "Enumerate patients",
    "Skip no-medical-care patients"
   ],
   "evidence": [
    "Source/Quadrunner/Rescue.cs:19-36",
    "Source/Quadrunner/Network.cs:370-385"
   ]
  },
  {
   "id": "comp.rescue-job",
   "name": "Rescue jobs",
   "level": 2,
   "kind": "job",
   "parent": "sub.rescue",
   "description": "Builds vanilla Rescue or TakeWoundedPrisonerToBed to a vanilla-found bed, with count 1 so the patient's reservation keeps one medic on it; no bed means no job.",
   "tech": [
    "Rescue.JobFor"
   ],
   "responsibilities": [
    "Find a bed",
    "Build the rescue job",
    "Decline when forbidden, reserved or unreachable"
   ],
   "evidence": [
    "Source/Quadrunner/Rescue.cs:38-61",
    "Defs/ThingDefs_Races/QR_Races.xml:135-153"
   ]
  },
  {
   "id": "comp.ride-order",
   "name": "Ride order",
   "level": 2,
   "kind": "job",
   "parent": "sub.riding",
   "description": "The right-click offer on an out Quadrunner or its stable (fullest idle rideable), the destination targeter, undocking and the ordered QR_RideTo job.",
   "tech": [
    "FloatMenuOptionProvider_Ride, Riding.Ride"
   ],
   "responsibilities": [
    "Offer a ride on dog or stable",
    "Pick the destination",
    "Undock and order the ride"
   ],
   "evidence": [
    "Source/Quadrunner/Riding.cs:13-24",
    "Source/Quadrunner/Riding.cs:68-99",
    "Source/Quadrunner/Riding.cs:244-278",
    "Source/Quadrunner/Building_Stable.cs:52-53"
   ]
  },
  {
   "id": "comp.ride-driver",
   "name": "Ride drivers",
   "level": 2,
   "kind": "job",
   "parent": "sub.riding",
   "description": "The rider walks to the dog, mounts, walks to the destination while the dog mirrors the rider, and dismounts at arrival, before a door indoors, when drafted/downed, or when the dog powers down.",
   "tech": [
    "JobDriver_RideTo, JobDriver_BeingRidden"
   ],
   "responsibilities": [
    "Mount and mirror the dog",
    "Apply the dismount rules",
    "Free the dog when the ride ends"
   ],
   "evidence": [
    "Source/Quadrunner/Riding.cs:118-226",
    "Defs/JobDefs/QR_Jobs.xml:39-51"
   ]
  },
  {
   "id": "comp.ride-stats",
   "name": "Ride stat & draw",
   "level": 2,
   "kind": "service",
   "parent": "sub.riding",
   "description": "Mounted riders move at the XML riddenMoveSpeed (7 cells/s) and are drawn at the dog's tweened position plus per-facing offsets, half an altitude step up.",
   "tech": [
    "StatPart_Riding, DrawPos patch"
   ],
   "responsibilities": [
    "Set the rider's MoveSpeed",
    "Offset the rider and dog drawing"
   ],
   "evidence": [
    "Source/Quadrunner/Riding.cs:229-241",
    "Source/Quadrunner/Riding.cs:281-299",
    "Patches/QR_Stats.xml:12-18"
   ]
  },
  {
   "id": "comp.ping",
   "name": "Ping",
   "level": 2,
   "kind": "service",
   "parent": "sub.combat",
   "description": "A hostile hitting any player dog reports that attacker into the war queue and forces an immediate dispatch, with or without a stable.",
   "tech": [
    "CompRobodog.PostPostApplyDamage, DogNetwork.Ping"
   ],
   "responsibilities": [
    "Detect hostile damage",
    "Queue the attacker and force a tick"
   ],
   "evidence": [
    "Source/Quadrunner/Robodog.cs:180-187",
    "Source/Quadrunner/Network.cs:283-289"
   ]
  },
  {
   "id": "comp.engage",
   "name": "Engage & capture",
   "level": 2,
   "kind": "job",
   "parent": "sub.combat",
   "description": "Fights the one war target the network assigned: gun from where it stands, else a cast position, else claws; captures a downed humanlike to a bed or carries any downed hostile to an empty room.",
   "tech": [
    "Combat.Spot, Combat.JobFor"
   ],
   "responsibilities": [
    "Spot hostiles in sight",
    "Attack the assigned target",
    "Capture or carry a downed one"
   ],
   "evidence": [
    "Source/Quadrunner/Combat.cs:22-56",
    "Source/Quadrunner/Combat.cs:85-139",
    "Source/Quadrunner/Combat.cs:210-237"
   ]
  },
  {
   "id": "comp.patrol",
   "name": "Patrol & guard",
   "level": 2,
   "kind": "job",
   "parent": "sub.combat",
   "description": "With nothing to fight during a raid: walk the stable's waypoints in a loop, else guard within 4 cells of the stable (or coordinator); jobs expire every 60 ticks.",
   "tech": [
    "Combat.IdleJob"
   ],
   "responsibilities": [
    "Walk the patrol loop",
    "Guard the post",
    "Dock when the map is clear"
   ],
   "evidence": [
    "Source/Quadrunner/Combat.cs:64-78",
    "Source/Quadrunner/Combat.cs:151-195",
    "Source/Quadrunner/Building_Stable.cs:161-221"
   ]
  },
  {
   "id": "comp.drafting",
   "name": "Drafting",
   "level": 2,
   "kind": "service",
   "parent": "sub.combat",
   "description": "Player Combatdogs get a drafter and pass vanilla's controllable-subhuman check only while drafted, so vanilla orders, gizmos and melee attack orders work.",
   "tech": [
    "Drafting, 2 Harmony patches"
   ],
   "responsibilities": [
    "Add a drafter to Combatdogs",
    "Pass control checks only while drafted",
    "Offer the draft toggle while undrafted"
   ],
   "evidence": [
    "Source/Quadrunner/Drafting.cs:15-70"
   ]
  },
  {
   "id": "comp.tiers",
   "name": "Tiers & guns",
   "level": 2,
   "kind": "service",
   "parent": "sub.combat",
   "description": "The tier chain QR_Combatdog to Tier 4, the upgrade bill done only while docked, rebuilding the pawn with its name/battery/gun picks, and one held built-in gun chosen by range.",
   "tech": [
    "CombatTier, Tiers, Bill_UpgradeDog"
   ],
   "responsibilities": [
    "Define tier, next kind and slots",
    "Order and finish upgrade bills",
    "Pick and arm the gun for the target"
   ],
   "evidence": [
    "Source/Quadrunner/Tiers.cs:12-153",
    "Defs/ThingDefs_Races/QR_Races.xml:213-302",
    "Defs/ThingDefs_Misc/QR_Weapons.xml:36-194"
   ]
  },
  {
   "id": "comp.render-tree",
   "name": "Render tree",
   "level": 2,
   "kind": "service",
   "parent": "sub.render",
   "description": "Core's Misc tree with our mount worker: whole sprite for icons, body plus five moving parts, carried pawns and the back-mounted gun drawn by offset.",
   "tech": [
    "QR_RobodogRenderTree"
   ],
   "responsibilities": [
    "Replace the carried-thing worker",
    "Select per-part textures",
    "Draw guns and carried pawns"
   ],
   "evidence": [
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:9-134",
    "Source/Quadrunner/Rendering.cs:9-65"
   ]
  },
  {
   "id": "comp.pose",
   "name": "Action poses",
   "level": 2,
   "kind": "service",
   "parent": "sub.render",
   "description": "The root node moves the whole dog per frame: pounce on a melee strike, braced crouch and recoil per shot, head dip and flip when scooping a pawn; the carried pawn's angle is patched.",
   "tech": [
    "RobodogPose, BodyAngle patch"
   ],
   "responsibilities": [
    "Animate pounce, recoil and scoop",
    "Turn a carried pawn onto the back"
   ],
   "evidence": [
    "Source/Quadrunner/Rendering.cs:148-298"
   ]
  },
  {
   "id": "comp.gait",
   "name": "Gait",
   "level": 2,
   "kind": "service",
   "parent": "sub.render",
   "description": "Legs swing and lift from the pather state; hind legs draw only in profile; the parts use _body/_leg texture layers exported from the SVG.",
   "tech": [
    "PawnRenderNodeWorker_Gait"
   ],
   "responsibilities": [
    "Animate legs while walking",
    "Hide hind legs in profile"
   ],
   "evidence": [
    "Source/Quadrunner/Rendering.cs:67-145",
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:35-121"
   ]
  },
  {
   "id": "comp.siren",
   "name": "Siren",
   "level": 2,
   "kind": "service",
   "parent": "sub.render",
   "description": "Two lamp draws above everything: a yellow retreat siren on a Combatdog charging home and a red tail dome on a rescuing Medirunner, flashing at 2 Hz real time.",
   "tech": [
    "Siren, TailSiren"
   ],
   "responsibilities": [
    "Draw the retreat and rescue lamps",
    "Place the tail dome per facing"
   ],
   "evidence": [
    "Source/Quadrunner/Robodog.cs:74-79",
    "Source/Quadrunner/Robodog.cs:205-218",
    "Source/Quadrunner/Robodog.cs:244-278"
   ]
  },
  {
   "id": "actor.player",
   "name": "Player",
   "kind": "person",
   "level": 0,
   "description": "The colony player: builds the stable, works its bills, reads the Overview tab, rides dogs, drafts Combatdogs and can invoke the removal cutover."
  },
  {
   "id": "ext.vanilla",
   "name": "Vanilla RimWorld 1.6",
   "kind": "external",
   "level": 0,
   "description": "Core's runtime: think trees and job drivers, work givers/scanners, reservations and the pathfinder, the power grid, raid/kidnap AI, rendering, bills and save/load. The only other game assembly installed.",
   "tech": [
    "Assembly-CSharp 1.6.4871",
    "Core only"
   ]
  },
  {
   "id": "ext.harmony",
   "name": "Harmony",
   "kind": "external",
   "level": 0,
   "description": "brrainz.harmony supplies 0Harmony at runtime; the mod installs six gameplay patches plus a test-only one through PatchAll.",
   "tech": [
    "Lib.Harmony 2.4.3",
    "workshop 2009463077"
   ]
  },
  {
   "id": "ext.harness",
   "name": "Automated test harness",
   "kind": "external",
   "level": 0,
   "description": "Development-time system outside the shipped package: tools/autotest.py launches a private game instance and drives it through files. Its in-game adapter (Source/Quadrunner/AutoTest.cs) and the 55-scenario suite are compiled into the DLL but dormant unless launched with -quadrunner-autotest=.",
   "tech": [
    "Python 3 stdlib",
    "Source/Quadrunner/AutoTest.cs file protocol"
   ],
   "evidence": [
    "tools/autotest.py:556-700",
    "Source/Quadrunner/AutoTest.cs:24-164",
    "Source/Quadrunner/Scenarios.cs:13-72"
   ]
  }
 ],
 "edges": [
  {
   "id": "e.player.stable",
   "from": "actor.player",
   "to": "comp.stable",
   "label": "build, bills, dock, patrol waypoints",
   "kind": "control",
   "protocol": "RimWorld UI",
   "mechanism": "Microelectronics research and 150 steel + 4 components; 200 W; bills start only with a free slot; waypoints placed one click at a time (the targeter reopens until right-click). A 16th dog made while full waits beside the stable unassigned.",
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs:190-221",
    "Source/Quadrunner/Building_Stable.cs:226-249",
    "Source/Quadrunner/Assembly.cs:77-90",
    "Defs/ThingDefs_Buildings/QR_Stable.xml:52-82"
   ]
  },
  {
   "id": "e.player.overview",
   "from": "actor.player",
   "to": "comp.overview",
   "label": "read rows, pick gun, order upgrade",
   "kind": "control",
   "protocol": "inspect tab",
   "mechanism": "Rows rebuild from stable.Dogs() every frame, docked first; the upgrade button adds one Bill_UpgradeDog and refuses a duplicate; a gun button calls Tiers.PickGun and is limited to that tier's list.",
   "evidence": [
    "Source/Quadrunner/ITab_StableOverview.cs:35-49",
    "Source/Quadrunner/ITab_StableOverview.cs:92-131"
   ]
  },
  {
   "id": "e.player.ride",
   "from": "actor.player",
   "to": "comp.ride-order",
   "label": "right-click Ride \u2026 to\u2026",
   "kind": "control",
   "protocol": "float menu + targeter",
   "mechanism": "Offered on a rideable dog that is out, or on its stable (which picks the fullest idle rideable and undocks it); the destination targeter then orders QR_RideTo.",
   "evidence": [
    "Source/Quadrunner/Riding.cs:76-99",
    "Source/Quadrunner/Riding.cs:244-278"
   ]
  },
  {
   "id": "e.player.draft",
   "from": "actor.player",
   "to": "comp.drafting",
   "label": "draft toggle, move, melee order",
   "kind": "control",
   "protocol": "vanilla gizmos/orders",
   "mechanism": "The toggle comes from the comp while undrafted and from vanilla once drafted; only Combatdogs; restricted to spawned player pawns.",
   "evidence": [
    "Source/Quadrunner/Drafting.cs:24-43",
    "Source/Quadrunner/Drafting.cs:47-70"
   ]
  },
  {
   "id": "e.player.removal",
   "from": "actor.player",
   "to": "comp.mod-core",
   "label": "Prepare save for removing Quadrunner",
   "kind": "control",
   "protocol": "mod settings",
   "mechanism": "A confirmation dialog; then an untouched QR_before_removal_* save, content removal on every map and the world, and a separate QR_removed_* export. Never automatic.",
   "evidence": [
    "Source/Quadrunner/Settings.cs:26-40",
    "Source/Quadrunner/Removal.cs:19-45"
   ]
  },
  {
   "id": "e.vanilla.stable",
   "from": "ext.vanilla",
   "to": "comp.stable",
   "label": "Building.Tick, power, bills, save",
   "kind": "control",
   "protocol": "Core tick",
   "mechanism": "Core ticks the building and its CompPowerTrader; without power docked dogs neither charge nor heal, but the network still dispatches and the bills still work.",
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs:226-249",
    "Defs/ThingDefs_Buildings/QR_Stable.xml:73-75"
   ]
  },
  {
   "id": "e.vanilla.think",
   "from": "ext.vanilla",
   "to": "comp.think-tree",
   "label": "think tree + job driver per tick",
   "kind": "control",
   "protocol": "Core AI",
   "mechanism": "Core's JobGiver pipeline evaluates QR_Robodog; QR job drivers run their toils; the drafted branch uses vanilla JobGiver_Orders/JobGiver_MoveToStandable.",
   "evidence": [
    "Defs/ThinkTreeDefs/QR_ThinkTrees.xml:4-51",
    "Defs/JobDefs/QR_Jobs.xml:5-54"
   ]
  },
  {
   "id": "e.vanilla.battery",
   "from": "ext.vanilla",
   "to": "comp.battery",
   "label": "CompTick, damage, draw",
   "kind": "control",
   "protocol": "ThingComp",
   "mechanism": "CompTick drains only for single-cell moves (a >1.5-cell teleport costs nothing) and runs before the job that picks a thing up; PostPostApplyDamage reports hostile hits; PostDraw draws the sirens.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:158-187",
    "Source/Quadrunner/Robodog.cs:209-218"
   ]
  },
  {
   "id": "e.vanilla.work",
   "from": "ext.vanilla",
   "to": "comp.work-job",
   "label": "WorkGiver_Scanner checks",
   "kind": "sync",
   "protocol": "scanner API",
   "interface": "iface.vanilla-scanner",
   "mechanism": "ShouldSkip, HasJobOnThing and JobOnThing decide and build the job; vanilla reservations and pathfinder apply; our code rejects any job def outside HaulToCell/HaulToContainer/Clean.",
   "evidence": [
    "Source/Quadrunner/Work.cs:143-167",
    "Defs/ThingDefs_Races/QR_Races.xml:102-121"
   ]
  },
  {
   "id": "e.vanilla.construct",
   "from": "ext.vanilla",
   "to": "comp.construct-patch",
   "label": "CanConstruct(Thing, Pawn, \u2026)",
   "kind": "sync",
   "protocol": "Harmony prefix",
   "mechanism": "For a robodog only, the prefix reruns CanConstruct with checkSkills:false so the null workSettings and skills are skipped; every other pawn goes through vanilla.",
   "evidence": [
    "Source/Quadrunner/Work.cs:269-284"
   ]
  },
  {
   "id": "e.vanilla.stat",
   "from": "ext.vanilla",
   "to": "comp.raid-part",
   "label": "MoveSpeed stat evaluation",
   "kind": "sync",
   "protocol": "StatPart",
   "mechanism": "StatPart_RobodogRaid multiplies a spawned dog's MoveSpeed by deployedSpeedFactor (2 for Combatdogs) and, during a raid, by 3; otherwise 1. The raid answer is cached 60 ticks.",
   "evidence": [
    "Source/Quadrunner/Work.cs:239-266",
    "Patches/QR_Stats.xml:4-10"
   ]
  },
  {
   "id": "e.vanilla.spots",
   "from": "ext.vanilla",
   "to": "comp.charge-spots",
   "label": "rooms, conduits, turbines, power nets",
   "kind": "sync",
   "protocol": "Core map queries",
   "mechanism": "Rooms from cells, conduits by CompPowerTransmitter, stored energy by PowerNet, wind cells by WindTurbineUtility; a cache signature hashes every colony building.",
   "evidence": [
    "Source/Quadrunner/ChargeSpots.cs:61-96",
    "Source/Quadrunner/ChargeSpots.cs:121-136"
   ]
  },
  {
   "id": "e.vanilla.rendertree",
   "from": "ext.vanilla",
   "to": "comp.render-tree",
   "label": "PawnRenderer draws the tree",
   "kind": "sync",
   "protocol": "render tree",
   "mechanism": "Core parses PawnRenderTreeDef and calls our node/worker classes each frame; the never-drawn Whole node supplies the icon graphic.",
   "evidence": [
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:9-134",
    "Source/Quadrunner/Rendering.cs:55-65"
   ]
  },
  {
   "id": "e.vanilla.bodyangle",
   "from": "ext.vanilla",
   "to": "comp.pose",
   "label": "PawnRenderer.BodyAngle",
   "kind": "sync",
   "protocol": "Harmony postfix",
   "mechanism": "For a pawn carried by a robodog the postfix lies it along the back and turns it over during the scoop; vanilla's humanlike rule is bypassed only for our carrier.",
   "evidence": [
    "Source/Quadrunner/Rendering.cs:290-298",
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:122-134"
   ]
  },
  {
   "id": "e.vanilla.drawpos",
   "from": "ext.vanilla",
   "to": "comp.ride-stats",
   "label": "DrawPos getter, MoveSpeed",
   "kind": "sync",
   "protocol": "Harmony postfix + StatPart",
   "mechanism": "A mounted rider is drawn at the dog's tweened position plus the XML per-facing offset, half an altitude step above pawns; StatPart_Riding sets the rider's MoveSpeed to riddenMoveSpeed.",
   "evidence": [
    "Source/Quadrunner/Riding.cs:229-241",
    "Source/Quadrunner/Riding.cs:281-299",
    "Patches/QR_Stats.xml:12-18"
   ]
  },
  {
   "id": "e.vanilla.components",
   "from": "ext.vanilla",
   "to": "comp.drafting",
   "label": "dynamic components, control checks",
   "kind": "sync",
   "protocol": "Harmony postfixes",
   "mechanism": "AddAndRemoveDynamicComponents gives a spawned player Combatdog a Pawn_DraftController; IsColonySubhumanPlayerControlled returns true only while it is drafted, so vanilla orders and gizmos accept it.",
   "evidence": [
    "Source/Quadrunner/Drafting.cs:47-70"
   ]
  },
  {
   "id": "e.vanilla.kidnap",
   "from": "ext.vanilla",
   "to": "comp.retrieval",
   "label": "TryFindGoodKidnapVictim",
   "kind": "sync",
   "protocol": "Harmony postfix",
   "mechanism": "Runs only when vanilla found no victim: the nearest reachable powered-down player robodog in range becomes the victim; vanilla JobDriver_Kidnap does the carrying, so colonists still come first.",
   "evidence": [
    "Source/Quadrunner/Retrieval.cs:195-211"
   ]
  },
  {
   "id": "e.vanilla.rescue",
   "from": "ext.vanilla",
   "to": "comp.rescue-job",
   "label": "Rescue jobs, FindBedFor",
   "kind": "sync",
   "protocol": "vanilla jobs",
   "mechanism": "Vanilla carries the patient and picks the bed; our code supplies patient, bed and count=1, and declines when no bed or reach is available.",
   "evidence": [
    "Source/Quadrunner/Rescue.cs:38-61"
   ]
  },
  {
   "id": "e.vanilla.combat",
   "from": "ext.vanilla",
   "to": "comp.engage",
   "label": "AttackStatic/Melee, Capture, cast search",
   "kind": "sync",
   "protocol": "vanilla jobs",
   "mechanism": "Our job chooses the verb/gun and hands vanilla the job; a 60-tick expiry with checkOverrideOnExpire re-asks the network, so a new target is picked up about once a second.",
   "evidence": [
    "Source/Quadrunner/Combat.cs:85-139",
    "Source/Quadrunner/Combat.cs:210-237"
   ]
  },
  {
   "id": "e.vanilla.tiers",
   "from": "ext.vanilla",
   "to": "comp.tiers",
   "label": "equipment verbs, gun stats",
   "kind": "sync",
   "protocol": "ThingDef/equipment",
   "mechanism": "Built-in guns are destroyOnDrop mech-style weapons; Arm replaces the held one and CompBuiltInGun cancels a busy stance so a cooldown never points at a removed verb.",
   "evidence": [
    "Source/Quadrunner/Tiers.cs:63-98",
    "Defs/ThingDefs_Misc/QR_Weapons.xml:36-194"
   ]
  },
  {
   "id": "e.vanilla.siren",
   "from": "ext.vanilla",
   "to": "comp.siren",
   "label": "PostDraw, DrawMesh",
   "kind": "sync",
   "protocol": "Unity drawing",
   "mechanism": "The lamp is drawn in MetaOverlays above everything at a 2 Hz real-time phase, with an additive lightning-glow mesh 2.4 times the lamp width.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:244-263"
   ]
  },
  {
   "id": "e.harmony.core",
   "from": "ext.harmony",
   "to": "comp.mod-core",
   "label": "0Harmony runtime + PatchAll",
   "kind": "sync",
   "protocol": "HarmonyLib",
   "mechanism": "QR's static constructor installs one Harmony instance with PatchAll, which registers the six gameplay patches (plus a test-only one) and the XML StatParts are loaded separately; no other reflection.",
   "evidence": [
    "Source/Quadrunner/QR.cs:15-21",
    "About/About.xml:10-20",
    "Patches/QR_Stats.xml:1-18"
   ]
  },
  {
   "id": "e.harness.stable",
   "from": "ext.harness",
   "to": "comp.stable",
   "label": "run scenarios, stage fixtures",
   "kind": "control",
   "protocol": "file protocol",
   "interface": "iface.autotest-files",
   "mechanism": "One command at a time from the command file, JSON lines to events.jsonl, heartbeat each second; fixtures call AddNewDog/TryUndock/OrderUpgrade and set battery through the comp; the watchdog kills the private instance on a stale heartbeat.",
   "evidence": [
    "Source/Quadrunner/AutoTest.cs:24-164",
    "Source/Quadrunner/Scenarios.cs:13-72",
    "tools/autotest.py:556"
   ]
  },
  {
   "id": "e.harness.net",
   "from": "ext.harness",
   "to": "comp.net-node",
   "label": "force dispatch, inspect queues",
   "kind": "control",
   "protocol": "in-process calls",
   "mechanism": "Scenarios call Tick(force:true) and Inspect(); a build change restarts the instance in game with GenCommandLine.Restart(); the result event carries pass count and verdicts.",
   "evidence": [
    "Source/Quadrunner/Network.cs:283-289",
    "Source/Quadrunner/Network.cs:618-628",
    "Source/Quadrunner/AutoTest.cs:118-127"
   ]
  },
  {
   "id": "e.harness.removal",
   "from": "ext.harness",
   "to": "comp.mod-core",
   "label": "PrepareSave + independent no-mod load",
   "kind": "control",
   "protocol": "scenario + probe",
   "mechanism": "The Prepare removal scenario calls Removal.PrepareSave; removal-check launches a second game process without Quadrunner, loads the export, ticks 600, saves/reloads and ticks 600, then asserts the keeper list.",
   "evidence": [
    "Source/Quadrunner/Scenarios.Removal.cs:24-216",
    "tools/removal-probe/Probe.cs:1-40",
    "tools/autotest.py:599"
   ]
  },
  {
   "id": "e.think.net",
   "from": "comp.think-tree",
   "to": "comp.net-node",
   "label": "TakeOrder(dog)",
   "kind": "control",
   "protocol": "think node",
   "mechanism": "JobGiver_RobodogWork asks the network; null falls through to dock, wait-in-room, wander. A dog never searches the map for work itself.",
   "evidence": [
    "Source/Quadrunner/Work.cs:207-211",
    "Source/Quadrunner/Network.cs:539-571"
   ]
  },
  {
   "id": "e.think.charge",
   "from": "comp.think-tree",
   "to": "comp.charge-decision",
   "label": "seek-charge branch",
   "kind": "control",
   "protocol": "think node",
   "mechanism": "JobGiver_SeekCharge sits before JobGiver_RobodogWork and returns a charge job whenever the comp wants a charge.",
   "evidence": [
    "Defs/ThinkTreeDefs/QR_ThinkTrees.xml:30-33",
    "Source/Quadrunner/Charging.cs:14-20"
   ]
  },
  {
   "id": "e.battery.think",
   "from": "comp.battery",
   "to": "comp.think-tree",
   "label": "WantsCharge latch",
   "kind": "data",
   "protocol": "comp state",
   "mechanism": "SetBattery latches at or below seekChargeAt and clears only at full; at the latch a drafted Combatdog is undrafted and its attack/wait/goto job interrupted.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:81-106"
   ]
  },
  {
   "id": "e.battery.net",
   "from": "comp.battery",
   "to": "comp.net-node",
   "label": "power down: NodeLeaving",
   "kind": "event",
   "protocol": "in-process",
   "mechanism": "At 0% QR_PoweredDown is added, the dog leaves the roster and is never assigned; above 0% it powers up and re-enters on the next roster refresh.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:107-118",
    "Source/Quadrunner/Network.cs:153-167"
   ]
  },
  {
   "id": "e.race.rendertree",
   "from": "comp.race",
   "to": "comp.render-tree",
   "label": "body + render tree defs",
   "kind": "data",
   "protocol": "ThingDef",
   "mechanism": "The race names body QR_Robodog and renderTree QR_RobodogRenderTree; Core builds the tree once per pawn from those defs.",
   "evidence": [
    "Defs/ThingDefs_Races/QR_Races.xml:39-43",
    "Defs/Bodies/QR_Bodies.xml"
   ]
  },
  {
   "id": "e.race.tiers",
   "from": "comp.race",
   "to": "comp.tiers",
   "label": "tier, next kind, slot lists",
   "kind": "data",
   "protocol": "DefModExtension",
   "mechanism": "CombatTier on each Combatdog race names the tier, the next PawnKindDef, the upgrade recipe and the allowed guns per slot; slot 1 is the default.",
   "evidence": [
    "Defs/ThingDefs_Races/QR_Races.xml:212-305",
    "Source/Quadrunner/Tiers.cs:12-24"
   ]
  },
  {
   "id": "e.bills.stable",
   "from": "comp.bills",
   "to": "comp.stable",
   "label": "AddNewDog / TakeNewDog",
   "kind": "data",
   "protocol": "in-process",
   "mechanism": "A finished bill generates a pawn, assigns its stable and docks it; if no slot is free it is spawned beside the stable with a caution message; a rebuild resurrects the wreck's pawn.",
   "evidence": [
    "Source/Quadrunner/Assembly.cs:16-58",
    "Source/Quadrunner/Building_Stable.cs:91-111"
   ]
  },
  {
   "id": "e.overview.bills",
   "from": "comp.overview",
   "to": "comp.bills",
   "label": "OrderUpgrade \u2192 one bill",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "One Bill_UpgradeDog per dog (a duplicate is refused); it is doable only while the dog is docked and still that tier, and removed when done or invalid.",
   "evidence": [
    "Source/Quadrunner/ITab_StableOverview.cs:117-131",
    "Source/Quadrunner/Tiers.cs:101-133"
   ]
  },
  {
   "id": "e.overview.tiers",
   "from": "comp.overview",
   "to": "comp.tiers",
   "label": "PickGun(slot, gun)",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "The pick is saved on the comp (gun1/gun2), limited to the tier's list, and re-arms slot 1 at once.",
   "evidence": [
    "Source/Quadrunner/ITab_StableOverview.cs:92-110",
    "Source/Quadrunner/Tiers.cs:44-50"
   ]
  },
  {
   "id": "e.stable.battery",
   "from": "comp.stable",
   "to": "comp.battery",
   "label": "dock charge + repair",
   "kind": "data",
   "protocol": "Building.Tick",
   "mechanism": "While powered: +0.0012/tick (3x the conduit rate) until full and the first injury heals 0.02 HP/tick; switched off, neither happens though dispatch continues.",
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs:239-248"
   ]
  },
  {
   "id": "e.stable.net",
   "from": "comp.stable",
   "to": "comp.net-node",
   "label": "tick the network, own its state",
   "kind": "control",
   "protocol": "Building.Tick",
   "mechanism": "Building_Stable.Tick calls DogNetwork.For(map).Tick(); the NetworkState is scribed on exactly one elected node, and NodeLeaving runs on despawn/destroy.",
   "evidence": [
    "Source/Quadrunner/Building_Stable.cs:226-232",
    "Source/Quadrunner/Building_Stable.cs:289-299",
    "Source/Quadrunner/Network.cs:200-248"
   ]
  },
  {
   "id": "e.net.stable",
   "from": "comp.net-node",
   "to": "comp.stable",
   "label": "undock for work, deploy",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "Docked dogs that are needed step out at the interaction cell: workers when not at war, a medic with a patient, a Combatdog in war or with targets; the decision repeats each dispatch.",
   "evidence": [
    "Source/Quadrunner/Network.cs:319-331"
   ]
  },
  {
   "id": "e.retrieval.stable",
   "from": "comp.retrieval",
   "to": "comp.stable",
   "label": "CarryToStable docks",
   "kind": "data",
   "protocol": "vanilla job",
   "mechanism": "The nearest stable that takes the dog (its own or one with a free slot) and that the carrier can reach; on arrival the dog is docked, else set down with a log line.",
   "evidence": [
    "Source/Quadrunner/Retrieval.cs:20-50",
    "Source/Quadrunner/Retrieval.cs:100-112"
   ]
  },
  {
   "id": "e.fieldfix.battery",
   "from": "comp.fieldfix",
   "to": "comp.battery",
   "label": "use battery / repair kit",
   "kind": "data",
   "protocol": "vanilla job",
   "mechanism": "The battery charges to full (SetBattery(1)); the kit removes every injury; the kit is chosen first when health is below 70%.",
   "evidence": [
    "Source/Quadrunner/FieldFix.cs:24-48",
    "Source/Quadrunner/FieldFix.cs:88-118"
   ]
  },
  {
   "id": "e.tiers.stable",
   "from": "comp.tiers",
   "to": "comp.stable",
   "label": "upgrade bill, rebuild dog",
   "kind": "control",
   "protocol": "bill + in-process",
   "mechanism": "On completion a new pawn of the next kind inherits name, stable, battery, build order and gun picks; the old pawn is destroyed and the dock slot reused.",
   "evidence": [
    "Source/Quadrunner/Tiers.cs:136-153",
    "Source/Quadrunner/Building_Stable.cs:137-158"
   ]
  },
  {
   "id": "e.net.discovery",
   "from": "comp.net-node",
   "to": "comp.discovery",
   "label": "request a local scan",
   "kind": "control",
   "protocol": "network tick",
   "mechanism": "Every scanInterval (120 ticks) or on a forced tick, each active dog scans around itself; nothing outside 15 cells or in fog is ever seen.",
   "evidence": [
    "Source/Quadrunner/Network.cs:332-340",
    "Source/Quadrunner/Work.cs:63-95"
   ]
  },
  {
   "id": "e.discovery.net",
   "from": "comp.discovery",
   "to": "comp.net-node",
   "label": "Report(kind, target, giver)",
   "kind": "data",
   "protocol": "in-process",
   "mechanism": "Work kinds go to the normal queue, rescue/combat to the war queue; a duplicate kind+ThingID is refused; fogged, destroyed or off-map targets are refused; task ids come from nextId.",
   "evidence": [
    "Source/Quadrunner/Network.cs:250-262",
    "Source/Quadrunner/Work.cs:87-92"
   ]
  },
  {
   "id": "e.net.survey",
   "from": "comp.net-node",
   "to": "comp.survey",
   "label": "survey job for an idle worker",
   "kind": "control",
   "protocol": "think node",
   "mechanism": "Home-first area categories, vanilla returned route-cost entry ranking, retained within-area coverage and bounded 80-cell legs; 600-tick expiry permits work preemption; unavailable goals remain unobserved.",
   "evidence": [
    "Source/Quadrunner/Network.cs",
    "Source/Quadrunner/Survey.cs"
   ]
  },
  {
   "id": "e.net.workjob",
   "from": "comp.net-node",
   "to": "comp.work-job",
   "label": "JobFor(dog, task)",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "One candidate job per capable, unassigned dog; path cost by FindPathNow; unreachable = MaxValue; ties go to the lower thingID; the winner is started with InterruptForced and its loadID saved.",
   "evidence": [
    "Source/Quadrunner/Network.cs:488-535"
   ]
  },
  {
   "id": "e.net.startjob",
   "from": "comp.net-node",
   "to": "ext.vanilla",
   "label": "StartJob, reservations, path cost",
   "kind": "sync",
   "protocol": "vanilla jobs",
   "mechanism": "Vanilla reservations and pathfinder do the work; assignee, job loadID and routeCost are saved with the task; the scheduler refreshes every 60 ticks.",
   "evidence": [
    "Source/Quadrunner/Network.cs:514-534",
    "Source/Quadrunner/Work.cs:137-167"
   ]
  },
  {
   "id": "e.net.engage",
   "from": "comp.net-node",
   "to": "comp.engage",
   "label": "war job for a Combatdog",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "Only combat-capable dogs; TakeOrder re-asks Combat.JobFor for the assigned target, continuing a capture or carry already under way rather than restarting it.",
   "evidence": [
    "Source/Quadrunner/Network.cs:481-493",
    "Source/Quadrunner/Network.cs:544-561"
   ]
  },
  {
   "id": "e.net.rescue",
   "from": "comp.net-node",
   "to": "comp.rescue-job",
   "label": "rescue job for a Medirunner",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "Capable() requires the race's rescue flag; patient tasks are sorted by rank, then bleed rate, then task id, and the nearest available medic gets each one.",
   "evidence": [
    "Source/Quadrunner/Network.cs:370-385",
    "Source/Quadrunner/Network.cs:481-493"
   ]
  },
  {
   "id": "e.net.retrieval",
   "from": "comp.net-node",
   "to": "comp.retrieval",
   "label": "carry a powered-down dog",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "Retrieve tasks come from scans; JobFor refuses when a field fix is pending, the dog is forbidden/reserved/unreachable, or no stable will take it, and the task then stays queued.",
   "evidence": [
    "Source/Quadrunner/Network.cs:490-492",
    "Source/Quadrunner/Retrieval.cs:57-69"
   ]
  },
  {
   "id": "e.net.triage",
   "from": "comp.net-node",
   "to": "comp.triage",
   "label": "AnyPatient / Patients",
   "kind": "sync",
   "protocol": "in-process",
   "mechanism": "The network asks whether any patient exists to decide whether a docked Medirunner deploys, and reports each revealed patient every scan.",
   "evidence": [
    "Source/Quadrunner/Network.cs:319-342"
   ]
  },
  {
   "id": "e.net.raid",
   "from": "comp.net-node",
   "to": "comp.raid-part",
   "label": "Raid.Active(map)",
   "kind": "sync",
   "protocol": "in-process",
   "mechanism": "War = an active threat or a live queued hostile; the answer is cached one second, and war flips warActive, suspends normal work and deploys Combatdogs.",
   "evidence": [
    "Source/Quadrunner/Network.cs:310-315",
    "Source/Quadrunner/Work.cs:214-232"
   ]
  },
  {
   "id": "e.net.patrol",
   "from": "comp.net-node",
   "to": "comp.patrol",
   "label": "patrol/guard job in war",
   "kind": "control",
   "protocol": "think node",
   "mechanism": "With nothing assigned while war is on or targets remain, the dog patrols the stable's waypoints in a loop, else guards within 4 cells; jobs expire every 60 ticks; clear map means dock.",
   "evidence": [
    "Source/Quadrunner/Network.cs:598-600",
    "Source/Quadrunner/Combat.cs:64-78"
   ]
  },
  {
   "id": "e.charge.spots",
   "from": "comp.charge-decision",
   "to": "comp.charge-spots",
   "label": "Best(dog)",
   "kind": "sync",
   "protocol": "in-process",
   "mechanism": "Cached spots filtered by UsableBy, grouped by region, traversed from the dog with its TraverseParms (forbidden doors block), first 5 candidates scored and the lowest taken.",
   "evidence": [
    "Source/Quadrunner/Charging.cs:29-36",
    "Source/Quadrunner/ChargeSpots.cs:161-195"
   ]
  },
  {
   "id": "e.spots.decision",
   "from": "comp.charge-spots",
   "to": "comp.charge-decision",
   "label": "spot or none",
   "kind": "data",
   "protocol": "in-process",
   "mechanism": "Score = straight distance + 15 x dogs already charging there - 10 x min(stored Wd/1000, 1); none when no reachable region has a usable spot.",
   "evidence": [
    "Source/Quadrunner/ChargeSpots.cs:188-195"
   ]
  },
  {
   "id": "e.charge.stable",
   "from": "comp.charge-decision",
   "to": "comp.stable",
   "label": "dock and charge",
   "kind": "control",
   "protocol": "QR_Dock job",
   "mechanism": "Chosen when stableTicks <= spotTicks: straight-line distance x TicksPerMoveCardinal + missing/0.0012; an unpowered stable counts only when no spot is usable; the dock adds charge and repair.",
   "evidence": [
    "Source/Quadrunner/Charging.cs:23-43",
    "Source/Quadrunner/Building_Stable.cs:239-248"
   ]
  },
  {
   "id": "e.charge.conduit",
   "from": "comp.charge-decision",
   "to": "comp.conduit-charge",
   "label": "charge in the room",
   "kind": "control",
   "protocol": "QR_ChargeAtConduit",
   "mechanism": "Chosen when a spot fills the dog sooner; the job targets the spot cell and the conduit. From 10% a conduit wins when the stable is about 120 cells further away.",
   "evidence": [
    "Source/Quadrunner/Charging.cs:40-42",
    "Defs/ThingDefs_Races/QR_Races.xml:77-79"
   ]
  },
  {
   "id": "e.conduit.battery",
   "from": "comp.conduit-charge",
   "to": "comp.battery",
   "label": "Charge(got)",
   "kind": "data",
   "protocol": "job driver",
   "mechanism": "Per tick min(0.0004 x delta, missing); Wd drawn = fraction x 100; the job ends at full, on a dry net, or when the spot stops qualifying (recheck every 250 ticks).",
   "evidence": [
    "Source/Quadrunner/Charging.cs:79-107"
   ]
  },
  {
   "id": "e.conduit.vanilla",
   "from": "comp.conduit-charge",
   "to": "ext.vanilla",
   "label": "draw PowerNet batteries",
   "kind": "data",
   "protocol": "PowerNet",
   "mechanism": "Batteries ordered by StoredEnergy descending; DrawPower takes up to 100 Wd per full charge; no net or empty batteries ends the job and the dog searches again.",
   "evidence": [
    "Source/Quadrunner/Charging.cs:110-123"
   ]
  },
  {
   "id": "e.workjob.vanilla",
   "from": "comp.work-job",
   "to": "ext.vanilla",
   "label": "scanner checks + JobOnThing",
   "kind": "sync",
   "protocol": "scanner API",
   "interface": "iface.vanilla-scanner",
   "mechanism": "The target must be live, allowed, reachable and reservable; the built job's def must be in the race's jobs list; haulOpportunisticDuplicates is forced off.",
   "evidence": [
    "Source/Quadrunner/Work.cs:143-167"
   ]
  },
  {
   "id": "e.triage.vanilla",
   "from": "comp.triage",
   "to": "ext.vanilla",
   "label": "downed pawns, WantsToBeRescued",
   "kind": "sync",
   "protocol": "Core queries",
   "mechanism": "Rank 0 colonist, 1 guest, 2 prisoner, 3 colony animal; -1 for enemies, robodogs and wild animals; NoCare patients are skipped as vanilla's float menu does.",
   "evidence": [
    "Source/Quadrunner/Rescue.cs:19-36"
   ]
  },
  {
   "id": "e.rideorder.driver",
   "from": "comp.ride-order",
   "to": "comp.ride-driver",
   "label": "QR_RideTo job",
   "kind": "control",
   "protocol": "in-process",
   "mechanism": "The rider reserves the dog; Notify_Starting starts QR_BeingRidden on the dog; if the ride job ends for any reason the rider dismounts and the dog is free.",
   "evidence": [
    "Source/Quadrunner/Riding.cs:76-99",
    "Source/Quadrunner/Riding.cs:138-142",
    "Source/Quadrunner/Riding.cs:144-165"
   ]
  },
  {
   "id": "e.driver.battery",
   "from": "comp.ride-driver",
   "to": "comp.battery",
   "label": "movement drains the dog",
   "kind": "data",
   "protocol": "CompTick",
   "mechanism": "Per-cell drain as walking with no raid factor while ridden; at 0% the dog powers down, the rider is put down, and recovery starts.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:60-68",
    "Source/Quadrunner/Riding.cs:165-186"
   ]
  },
  {
   "id": "e.driver.vanilla",
   "from": "comp.ride-driver",
   "to": "ext.vanilla",
   "label": "GotoCell + pather",
   "kind": "sync",
   "protocol": "vanilla jobs",
   "mechanism": "The rider's job walks to the destination while the dog mirrors position and rotation; the rider dismounts before an indoor room's next cell or door.",
   "evidence": [
    "Source/Quadrunner/Riding.cs:101-116",
    "Source/Quadrunner/Riding.cs:167-195",
    "Source/Quadrunner/Riding.cs:197-226"
   ]
  },
  {
   "id": "e.vanilla.ping",
   "from": "ext.vanilla",
   "to": "comp.ping",
   "label": "PostPostApplyDamage",
   "kind": "event",
   "protocol": "ThingComp",
   "mechanism": "Called when a hostile pawn damages a spawned, living dog; friendly fire and lethal hits do not ping.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:181-187"
   ]
  },
  {
   "id": "e.ping.net",
   "from": "comp.ping",
   "to": "comp.net-node",
   "label": "Ping(attacker)",
   "kind": "event",
   "protocol": "in-process",
   "mechanism": "Rejects non-hostile/off-map attackers; reports Combat into the war queue, rechecks the raid cache and forces a tick, so one Combatdog answers in the same tick with or without a stable.",
   "evidence": [
    "Source/Quadrunner/Network.cs:283-289"
   ]
  },
  {
   "id": "e.engage.tiers",
   "from": "comp.engage",
   "to": "comp.tiers",
   "label": "Arm(GunFor(target))",
   "kind": "sync",
   "protocol": "in-process",
   "mechanism": "Slot 2's weapon when the target is within its range, else slot 1, else claws; the gun is re-armed before each attack and nothing is ever dropped.",
   "evidence": [
    "Source/Quadrunner/Combat.cs:85-110",
    "Source/Quadrunner/Tiers.cs:52-74"
   ]
  },
  {
   "id": "e.battery.engage",
   "from": "comp.battery",
   "to": "comp.engage",
   "label": "retreat interrupts the attack",
   "kind": "control",
   "protocol": "comp latch",
   "mechanism": "At the latch an AttackMelee/AttackStatic/Wait/Goto job ends at once; a Capture or QR_CarryToRoom already under way finishes; a drafted dog is undrafted instead.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:86-104"
   ]
  },
  {
   "id": "e.patrol.stable",
   "from": "comp.patrol",
   "to": "comp.stable",
   "label": "patrol waypoints",
   "kind": "data",
   "protocol": "stable field",
   "mechanism": "Waypoints are placed one click at a time and drawn yellow while the stable is selected; a per-dog counter picks the next one, looping.",
   "evidence": [
    "Source/Quadrunner/Combat.cs:151-174",
    "Source/Quadrunner/Building_Stable.cs:161-221"
   ]
  },
  {
   "id": "e.pose.tree",
   "from": "comp.pose",
   "to": "comp.render-tree",
   "label": "root pose node",
   "kind": "data",
   "protocol": "render worker",
   "mechanism": "The root properties move the whole dog from vanilla state each frame; tunables are in XML (pounce 24 ticks, recoil 10, brace 8, scoop 45).",
   "evidence": [
    "Source/Quadrunner/Rendering.cs:148-283",
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:11-34"
   ]
  },
  {
   "id": "e.gait.tree",
   "from": "comp.gait",
   "to": "comp.render-tree",
   "label": "body + five moving parts",
   "kind": "data",
   "protocol": "render worker",
   "mechanism": "The sprite is split into body and four leg textures in Art/; the gait cycle comes from the pather, and hind legs draw only in profile; the Whole node serves icons.",
   "evidence": [
    "Source/Quadrunner/Rendering.cs:67-145",
    "Defs/PawnRenderTreeDefs/QR_RenderTrees.xml:35-121"
   ]
  },
  {
   "id": "e.render.battery",
   "from": "comp.render-tree",
   "to": "comp.battery",
   "label": "carried, carriedSince",
   "kind": "data",
   "protocol": "comp state",
   "mechanism": "The mount worker draws carried pawns and the pickup flip; a dog spawned or loaded with a pawn on its back has carriedSince reset, so no flip replays.",
   "evidence": [
    "Source/Quadrunner/Rendering.cs:9-49",
    "Source/Quadrunner/Robodog.cs:43-46",
    "Source/Quadrunner/Robodog.cs:121-135"
   ]
  },
  {
   "id": "e.battery.siren",
   "from": "comp.battery",
   "to": "comp.siren",
   "label": "Retreating / Rescuing",
   "kind": "data",
   "protocol": "comp flags",
   "mechanism": "A yellow lamp at 2 Hz while a Combatdog walks off to charge; a red tail dome while a Medirunner runs Rescue/TakeWoundedPrisonerToBed.",
   "evidence": [
    "Source/Quadrunner/Robodog.cs:74-79",
    "Source/Quadrunner/Robodog.cs:205-218"
   ]
  }
 ],
 "interfaces": [
  {
   "id": "iface.network-state",
   "name": "NetworkState (saved queues)",
   "provider": "comp.net-node",
   "consumers": [
    "comp.stable",
    "comp.battery"
   ],
   "contract": "NetworkState via Scribe_Deep: normal, war, nextId, warActive; exploration is transient and old arrival tags are ignored",
   "description": "One saved queue owner per map. Exactly one stable or dog carries it; it is transferred on election and cleared when the last eligible node goes, so removing the mod leaves no map component behind."
  },
  {
   "id": "iface.autotest-files",
   "name": "Test file protocol",
   "provider": "ext.harness",
   "consumers": [
    "comp.stable",
    "comp.net-node"
   ],
   "contract": "<autotest>/heartbeat, command, events.jsonl",
   "description": "The only link between the Python harness and the game process: one command at a time (<id> <verb> [arg]), JSON-line events, a heartbeat each second and a watchdog on the other side."
  },
  {
   "id": "iface.vanilla-scanner",
   "name": "WorkGiver_Scanner contract",
   "provider": "ext.vanilla",
   "consumers": [
    "comp.work-job"
   ],
   "contract": "WorkGiver_Scanner: ShouldSkip / HasJobOnThing / JobOnThing / PathEndMode",
   "description": "The vanilla interface the job builder uses to turn one queued target into one job; our code never constructs a job without asking the target's scanner first."
  }
 ],
 "requirements": [
  {
   "id": "REQ-SYS-001",
   "level": 0,
   "text": "A player can build one powered stable that houses 15 dogs, assemble every dog model from bills worked at that stable, and read each assigned dog's type, battery, health and current job in the stable's Overview tab.",
   "allocated_to": [
    "sub.stable"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T1: one stable as the crafting station, 15 shared slots, Overview tab rows."
  },
  {
   "id": "REQ-SYS-002",
   "level": 0,
   "text": "Idle Quadrunners find work by their own local observation and a per-map network assigns each discovered task to the nearest available capable dog; no stable service radius or map-wide work search bounds what is hauled, cleaned, supplied or retrieved.",
   "allocated_to": [
    "sub.network",
    "sub.work"
   ],
   "inferred": true,
   "rationale": "Plan 0003 Goal and N1; supersedes plan 0001's 40-cell stable dispatch."
  },
  {
   "id": "REQ-SYS-003",
   "level": 0,
   "text": "A dog never dies of an empty battery: at 0% it powers down as a downed condition, can be revived in the field or carried to a stable, and works again after charging.",
   "allocated_to": [
    "sub.charging",
    "sub.stable"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T2/T4: powered down is a condition, not destruction."
  },
  {
   "id": "REQ-SYS-004",
   "level": 0,
   "text": "A Medirunner rescues downed colonists, guests, prisoners and colony animals map-wide, under fire, fastest-bleeding first within a rank and never enemies or robodogs.",
   "allocated_to": [
    "sub.rescue"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T5 and the Rescue rules decision."
  },
  {
   "id": "REQ-SYS-005",
   "level": 0,
   "text": "A colonist can ride a Quadrunner to a chosen cell at about 1.5x walking speed and is put down safely at the destination, before entering a building, when drafted or downed, or when the dog powers down.",
   "allocated_to": [
    "sub.riding"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T6 and the riding decisions."
  },
  {
   "id": "REQ-SYS-006",
   "level": 0,
   "text": "Combatdogs answer any hostile that damages a dog, deploy for raids, capture downed hostiles, patrol their stable and upgrade through four tiers with built-in guns that are never dropped.",
   "allocated_to": [
    "sub.combat"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T7/T8."
  },
  {
   "id": "REQ-SYS-007",
   "level": 0,
   "text": "Every dog model is one robot race on a custom non-organic flesh type (neither animal nor mechanoid, no food or rest needs), drawn from body-and-leg art, and the mod runs on Core plus Harmony with no DLC.",
   "allocated_to": [
    "sub.pawn",
    "sub.render"
   ],
   "inferred": true,
   "rationale": "Plan 0001 decisions: flesh type, Core-only release, custom render tree."
  },
  {
   "id": "REQ-SYS-008",
   "level": 0,
   "text": "A colony can be prepared for mod removal: an untouched backup plus a cleaned save that loads, ticks, saves and reloads without the Quadrunner assembly while riders, patients, injuries, vanilla cargo and battle history survive.",
   "allocated_to": [
    "comp.mod-core"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T9 clean removal."
  },
  {
   "id": "REQ-SYS-009",
   "level": 0,
   "text": "Every in-game behaviour the mod ships is exercised by an automated scenario running in an isolated game instance, with a save/load step where state is involved, and a run passes only with zero new errors against a no-mod baseline.",
   "allocated_to": [
    "comp.mod-core"
   ],
   "inferred": true,
   "rationale": "Plan 0001 T0 and the Testing decision: fully automated, driven by the agent; the 55-scenario suite compiled into the DLL is the executable specification, and the external harness supplies the private game instance and watchdog."
  },
  {
   "id": "REQ-STB-001",
   "level": 1,
   "text": "A stable owns up to 15 dogs whether docked, out or carried; docking despawns a dog into the stable's container and both the docked dogs and the network state survive save/load.",
   "allocated_to": [
    "comp.stable"
   ],
   "inferred": true,
   "parent": "REQ-SYS-001",
   "rationale": "Plan 0001 T1 and the Dog ownership decision."
  },
  {
   "id": "REQ-STB-004",
   "level": 1,
   "text": "Destroying a stable unassigns its dogs; uninstalling it keeps their affiliation and they walk back and dock once it is reinstalled elsewhere.",
   "allocated_to": [
    "comp.stable"
   ],
   "inferred": true,
   "parent": "REQ-SYS-003",
   "rationale": "Plan 0001 Stable destroyed or uninstalled decision."
  },
  {
   "id": "REQ-STB-006",
   "level": 1,
   "text": "A powered-down dog is recovered to the nearest stable with a free slot and docked there, by a network worker or by a colonist's Carry to stable order.",
   "allocated_to": [
    "comp.retrieval"
   ],
   "inferred": true,
   "parent": "REQ-SYS-003",
   "rationale": "Plan 0001 T4 retrieval rules."
  },
  {
   "id": "REQ-STB-009",
   "level": 1,
   "text": "A powered-down dog that no stable can reach raises a named alert, is not assigned, and is recovered once a path is opened.",
   "allocated_to": [
    "comp.retrieval"
   ],
   "inferred": true,
   "parent": "REQ-SYS-003",
   "rationale": "Plan 0001 Fire and out of reach decision."
  },
  {
   "id": "REQ-STB-010",
   "level": 1,
   "text": "Prepare save for removing Quadrunner writes an untouched backup and a separate cleaned save; the export contains no mod pawns, buildings, items, jobs or shots, and keeps vanilla cargo, riders, patients, injuries and battle-log records.",
   "allocated_to": [
    "comp.mod-core"
   ],
   "inferred": true,
   "parent": "REQ-SYS-008",
   "rationale": "Plan 0001 T9; the independent removal check is the proof."
  },
  {
   "id": "REQ-PAW-001",
   "level": 1,
   "text": "Every robodog is an Animal-intelligence, non-flesh, non-mechanoid race with no food, rest or mood need, trainability None, psychic sensitivity 0, immunity to disease and rot, and no wild or manhunter spawns.",
   "allocated_to": [
    "comp.race"
   ],
   "inferred": true,
   "parent": "REQ-SYS-007",
   "rationale": "Plan 0001 Robodog kind decision."
  },
  {
   "id": "REQ-PAW-002",
   "level": 1,
   "text": "Walking drains 1/3500 of a full charge per cell (doubled for a Quadrunner in a raid, not while ridden); at or below 10% the dog latches 'wants a charge' until full and at 0% it powers down.",
   "allocated_to": [
    "comp.battery"
   ],
   "inferred": true,
   "parent": "REQ-SYS-003",
   "rationale": "Plan 0001 Battery and Battery tunables decisions."
  },
  {
   "id": "REQ-NET-001",
   "level": 1,
   "text": "Each map runs one dispatch network whose persistent productive queues are stored on exactly one elected node - the oldest spawned stable, else the newest-built active dog - and losing the node transfers the same tasks to the next.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-SYS-002",
   "rationale": "Plan 0003 Coordination and Persistence decisions."
  },
  {
   "id": "REQ-NET-002",
   "level": 1,
   "text": "Work enters the queues only from a dog's local observation (a 15-cell radial scan every 120 ticks, or a Combatdog's line-of-sight spot); fogged, forbidden, non-Home cleaning and ineligible targets are never reported.",
   "allocated_to": [
    "comp.discovery"
   ],
   "inferred": true,
   "parent": "REQ-SYS-002",
   "rationale": "Plan 0003 Discovery: no work search, no discovery through fog."
  },
  {
   "id": "REQ-NET-009",
   "level": 1,
   "text": "An active hostile threat suspends normal work immediately: assigned jobs stop, carried cargo is dropped safely, workers wait, the normal queue is retained, and work resumes when the threat clears.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-SYS-002",
   "rationale": "Plan 0003 War; supersedes plan 0001's finish-before-standby raid rule."
  },
  {
   "id": "REQ-CHG-001",
   "level": 1,
   "text": "A dog that wants a charge goes to whichever of its stable or the best conduit spot fills it soonest, travel included; with no usable spot it goes to the stable even when unpowered.",
   "allocated_to": [
    "comp.charge-decision"
   ],
   "inferred": true,
   "parent": "REQ-SYS-003",
   "rationale": "Plan 0001 Charging and Stable or conduit."
  },
  {
   "id": "REQ-CHG-002",
   "level": 1,
   "text": "A conduit charge spot is a standable cell in a roofed, indoor, non-prison room whose conduit sits on a net with stored energy, at least N cells (default 6) from a wind turbine or its wind cells; bedrooms and barracks only when empty; freezers allowed; allowed areas, fire and hostiles respected.",
   "allocated_to": [
    "comp.charge-spots"
   ],
   "inferred": true,
   "parent": "REQ-SYS-003",
   "rationale": "Plan 0001 Charge-spot search decision."
  },
  {
   "id": "REQ-WRK-001",
   "level": 1,
   "text": "For one assigned task a dog takes exactly one vanilla scanner job for that target (HaulToCell, HaulToContainer or Clean), with opportunistic duplicate hauling off and vanilla reservations intact.",
   "allocated_to": [
    "comp.work-job"
   ],
   "inferred": true,
   "parent": "REQ-SYS-002",
   "rationale": "Plan 0001 Robodog work and plan 0003 Scope."
  },
  {
   "id": "REQ-WRK-004",
   "level": 1,
   "text": "While a raid is active, an out working dog takes no new work and moves at 3x speed with 2x battery drain until the raid ends.",
   "allocated_to": [
    "comp.raid-part"
   ],
   "inferred": true,
   "parent": "REQ-SYS-002",
   "rationale": "Plan 0001 Raid decision."
  },
  {
   "id": "REQ-RES-001",
   "level": 1,
   "text": "A Medirunner rescues in rank order (colonist, guest, prisoner, colony animal), fastest-bleeding first within a rank, skipping enemies, robodogs and NoCare patients, map-wide and under fire, with one medic per patient by reservation.",
   "allocated_to": [
    "comp.triage"
   ],
   "inferred": true,
   "parent": "REQ-SYS-004",
   "rationale": "Plan 0001 T5 and Rescue rules decision."
  },
  {
   "id": "REQ-RID-001",
   "level": 1,
   "text": "A colonist can order a ride on a Quadrunner that is out, or on its stable (which undocks its fullest idle rideable), pick a destination and mount.",
   "allocated_to": [
    "comp.ride-order"
   ],
   "inferred": true,
   "parent": "REQ-SYS-005",
   "rationale": "Plan 0001 Riding: mounting and Ride to... decisions."
  },
  {
   "id": "REQ-CMB-001",
   "level": 1,
   "text": "Any dog damaged by a hostile pings its network: the attacker joins the war queue, one Combatdog is sent at once even without a stable and beyond 40 cells, and an already-queued attacker sends none.",
   "allocated_to": [
    "comp.ping"
   ],
   "inferred": true,
   "parent": "REQ-SYS-006",
   "rationale": "Plan 0001 Combatdog: queue and pings decision."
  },
  {
   "id": "REQ-CMB-008",
   "level": 1,
   "text": "An upgrade ordered for one Combatdog adds one bill; a colonist brings the next tier's cost and works it only while the dog is docked; on completion the dog is rebuilt as the next tier keeping name, stable, battery and gun picks, and no built-in gun is ever dropped.",
   "allocated_to": [
    "comp.tiers"
   ],
   "inferred": true,
   "parent": "REQ-SYS-006",
   "rationale": "Plan 0001 Combatdog: upgrades and slot guns decisions."
  },
  {
   "id": "REQ-REN-001",
   "level": 1,
   "text": "Every dog is drawn from one render tree: a whole sprite for icons, a body plus four separately animated leg layers (hind legs only in profile), a gait cycle, built-in guns on the back and carried pawns on the back, with action poses for pounce, recoil/brace and scoop/flip.",
   "allocated_to": [
    "comp.render-tree",
    "comp.pose",
    "comp.gait"
   ],
   "inferred": true,
   "parent": "REQ-SYS-007",
   "rationale": "Plan 0001 Gun rendering and Action animations decisions."
  },
  {
   "id": "REQ-STB-002",
   "level": 2,
   "text": "A finished assemble bill generates a dog of the recipe's kind and docks it at once; a dog bill cannot start while all 15 slots are taken, and a dog finished anyway waits beside the stable unassigned.",
   "allocated_to": [
    "comp.bills"
   ],
   "inferred": true,
   "parent": "REQ-STB-001",
   "rationale": "Plan 0001 Full stable and Stable bills decisions."
  },
  {
   "id": "REQ-STB-003",
   "level": 2,
   "text": "A powered stable charges docked dogs at 0.0012/tick (3x the conduit rate) and heals one injury at 0.02 HP/tick; with the stable switched off neither happens while dispatch continues.",
   "allocated_to": [
    "comp.stable"
   ],
   "inferred": true,
   "parent": "REQ-STB-001",
   "rationale": "Plan 0001 Stable repair decision and Battery tunables."
  },
  {
   "id": "REQ-STB-005",
   "level": 2,
   "text": "The Overview tab lists every assigned dog (docked first) with type, battery, health and current job, and for Combatdogs its tier, a per-slot gun picker and one upgrade button per dog.",
   "allocated_to": [
    "comp.overview"
   ],
   "inferred": true,
   "parent": "REQ-STB-001",
   "rationale": "Plan 0001 T1/T8 and the Overview tab design."
  },
  {
   "id": "REQ-STB-007",
   "level": 2,
   "text": "A colonist brings a repair kit to a field dog below 70% health, then a robodog battery to a powered-down dog, from items made at the stable; the kit is chosen first.",
   "allocated_to": [
    "comp.fieldfix"
   ],
   "inferred": true,
   "parent": "REQ-STB-006",
   "rationale": "Plan 0001 Field fixes decision."
  },
  {
   "id": "REQ-STB-008",
   "level": 2,
   "text": "A destroyed dog's wreck remains as its corpse and a rebuild bill resurrects the same pawn (name, stable, tier) at about half the assemble cost, docked or beside a full stable.",
   "allocated_to": [
    "comp.bills"
   ],
   "inferred": true,
   "parent": "REQ-STB-001",
   "rationale": "Plan 0001 Wrecks decision."
  },
  {
   "id": "REQ-REC-001",
   "level": 2,
   "text": "Before a powered-down dog is carried home, a fire within 1.5 cells of it is beaten out first.",
   "allocated_to": [
    "comp.retrieval"
   ],
   "inferred": true,
   "parent": "REQ-STB-006",
   "rationale": "Plan 0001 Fire and out of reach decision."
  },
  {
   "id": "REQ-REC-002",
   "level": 2,
   "text": "When vanilla finds no humanlike kidnap victim, a hostile kidnapper can take the nearest reachable powered-down player robodog, and the stable keeps its slot.",
   "allocated_to": [
    "comp.retrieval"
   ],
   "inferred": true,
   "parent": "REQ-STB-006",
   "rationale": "Plan 0001 Harmony patch 2."
  },
  {
   "id": "REQ-REC-003",
   "level": 2,
   "text": "A carrier at its seek threshold mid-carry finishes the delivery when the stable wins the charge comparison, otherwise sets the dog down, charges and returns for it.",
   "allocated_to": [
    "comp.retrieval",
    "comp.charge-decision"
   ],
   "inferred": true,
   "parent": "REQ-STB-006",
   "rationale": "Plan 0001 Carrier on low battery decision."
  },
  {
   "id": "REQ-REC-004",
   "level": 2,
   "text": "A powered-down dog with its field fix in stock is left to the colonists for 2500 ticks before a network dog carries it home.",
   "allocated_to": [
    "comp.retrieval"
   ],
   "inferred": true,
   "parent": "REQ-STB-006",
   "rationale": "Plan 0001 Retrieval dispatch decision."
  },
  {
   "id": "REQ-PAW-003",
   "level": 2,
   "text": "At 0% the dog gains QR_PoweredDown (Moving 0), is downed, refuses assignments and keeps the condition and want-charge across save/load; above 0% it powers up.",
   "allocated_to": [
    "comp.battery"
   ],
   "inferred": true,
   "parent": "REQ-PAW-002",
   "rationale": "Plan 0001 T2 and Battery tunables."
  },
  {
   "id": "REQ-PAW-004",
   "level": 2,
   "text": "The think tree gives every dog, in order: drafted orders, charge search while it wants a charge, a network order, dock, wait-in-room, wander; no dog searches the map for work on its own.",
   "allocated_to": [
    "comp.think-tree"
   ],
   "inferred": true,
   "parent": "REQ-PAW-001",
   "rationale": "Plan 0003 Protocol; plan 0001 Harmony budget note that vanilla assumptions stay untouched."
  },
  {
   "id": "REQ-NET-003",
   "level": 2,
   "text": "A target is queued at most once however many dogs or scans report it, and queued tasks survive save/load without duplication.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-NET-001",
   "rationale": "Plan 0003 N1 and Persistence."
  },
  {
   "id": "REQ-NET-004",
   "level": 2,
   "text": "Each unassigned task goes to the nearest available capable dog by actual path cost; unreachable dogs never win, ties break by thingID, and stable affiliation does not restrict assignment.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-NET-001",
   "rationale": "Plan 0003 Assignment and N1."
  },
  {
   "id": "REQ-NET-005",
   "level": 2,
   "text": "A dead, interrupted or re-assigned dog's unfinished task returns to the queue with no duplicate execution, and an acknowledged assignment survives save/load.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-NET-001",
   "rationale": "Plan 0003 Protocol and N2."
  },
  {
   "id": "REQ-NET-006",
   "level": 2,
   "text": "A task whose assignee shows no progress for 2500 ticks is stopped, released and made assignable again after a scan interval.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-NET-001",
   "rationale": "Plan 0003 Discovery: 2500 ticks of lost progress before release."
  },
  {
   "id": "REQ-NET-007",
   "level": 2,
   "text": "When the last eligible node disappears, both productive queues and transient exploration clear and are not resurrected by a new stable or dog after reload.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-NET-001",
   "rationale": "Plan 0003 Coordination and N2."
  },
  {
   "id": "REQ-NET-008",
   "level": 2,
   "text": "Idle workers survey Home-first native areas by vanilla returned entry-route cost, retain within-area coverage over bounded legs, and credit only actual local scans in finite shared sweeps; productive work preempts travel.",
   "allocated_to": [
    "comp.survey"
   ],
   "inferred": true,
   "parent": "REQ-NET-002",
   "rationale": "Plan 0003 Exploration."
  },
  {
   "id": "REQ-NET-010",
   "level": 2,
   "text": "Rescue and combat tasks enter the war queue from any dog's scan or sight (map-wide for emergency rescue), independent of the reporting dog's own work list.",
   "allocated_to": [
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-NET-001",
   "rationale": "Plan 0003 Discovery and War."
  },
  {
   "id": "REQ-CHG-003",
   "level": 2,
   "text": "Rules that change without a building change (roof, prisoners, sleepers, stored energy, allowed area, fire, hostiles, reservations) are rechecked for the charging dog, which gets up and searches again when a spot stops qualifying.",
   "allocated_to": [
    "comp.charge-spots"
   ],
   "inferred": true,
   "parent": "REQ-CHG-002",
   "rationale": "Plan 0001 Charge-spot search decision."
  },
  {
   "id": "REQ-CHG-004",
   "level": 2,
   "text": "The spot cache is rebuilt when the colony's building signature changes or after 2500 ticks.",
   "allocated_to": [
    "comp.charge-spots"
   ],
   "inferred": true,
   "parent": "REQ-CHG-002",
   "rationale": "Plan 0001 Charge-spot search decision."
  },
  {
   "id": "REQ-CHG-005",
   "level": 2,
   "text": "A conduit charge draws at most 100 Wd per full charge from the net's batteries, fullest first; a dry net or a spot that stops qualifying ends the job and the dog searches again.",
   "allocated_to": [
    "comp.conduit-charge"
   ],
   "inferred": true,
   "parent": "REQ-CHG-001",
   "rationale": "Plan 0001 Battery tunables and T2 edge cases."
  },
  {
   "id": "REQ-WRK-002",
   "level": 2,
   "text": "A vanilla job that queues extra targets (Clean batches nearby filth) is trimmed to targets the network discovered and no other dog holds.",
   "allocated_to": [
    "comp.work-job"
   ],
   "inferred": true,
   "parent": "REQ-WRK-001",
   "rationale": "Plan 0003 Scope: no dog works targets it never saw."
  },
  {
   "id": "REQ-WRK-003",
   "level": 2,
   "text": "A robodog passes vanilla construction checks without work settings or skills and only ever delivers materials to blueprints and frames.",
   "allocated_to": [
    "comp.construct-patch"
   ],
   "inferred": true,
   "parent": "REQ-WRK-001",
   "rationale": "Plan 0001 Harmony patch 1."
  },
  {
   "id": "REQ-RES-002",
   "level": 2,
   "text": "A Medirunner uses vanilla Rescue, or TakeWoundedPrisonerToBed for a prisoner, to a bed vanilla finds; a patient with no med care, no bed or no reach is skipped and does not hold the workers back.",
   "allocated_to": [
    "comp.rescue-job"
   ],
   "inferred": true,
   "parent": "REQ-RES-001",
   "rationale": "Plan 0001 Rescue rules and Medirunner treatment."
  },
  {
   "id": "REQ-RES-003",
   "level": 2,
   "text": "A docked Medirunner is sent whenever a patient exists, raid or not, while workers stay suspended in war.",
   "allocated_to": [
    "comp.rescue-job",
    "comp.net-node"
   ],
   "inferred": true,
   "parent": "REQ-RES-001",
   "rationale": "Plan 0001 Rescue rules and plan 0003 War."
  },
  {
   "id": "REQ-RID-002",
   "level": 2,
   "text": "While mounted the rider moves at the dog's ridden speed (7 cells/s) in the dog's cell, and the dog drains per cell as when walking, with no raid factor.",
   "allocated_to": [
    "comp.ride-stats",
    "comp.ride-driver"
   ],
   "inferred": true,
   "parent": "REQ-RID-001",
   "rationale": "Plan 0001 Ridden speed and Riding defaults."
  },
  {
   "id": "REQ-RID-003",
   "level": 2,
   "text": "The rider dismounts at the destination, before a door into an indoor room, when drafted or downed, and when the dog powers down; the ride job ending for any reason frees the dog.",
   "allocated_to": [
    "comp.ride-driver"
   ],
   "inferred": true,
   "parent": "REQ-RID-001",
   "rationale": "Plan 0001 Riding: trip and defaults."
  },
  {
   "id": "REQ-RID-004",
   "level": 2,
   "text": "A mounted rider is drawn at the dog's tweened position plus per-facing XML offsets, one half altitude step above pawns, with the dog drawn under the rider.",
   "allocated_to": [
    "comp.ride-stats"
   ],
   "inferred": true,
   "parent": "REQ-RID-001",
   "rationale": "Plan 0001 Harmony patch 3."
  },
  {
   "id": "REQ-RID-005",
   "level": 2,
   "text": "After a ride the dog is free and returns to work or docks; a ridden dog that powers down is recovered like any powered-down dog.",
   "allocated_to": [
    "comp.ride-driver"
   ],
   "inferred": true,
   "parent": "REQ-RID-001",
   "rationale": "Plan 0001 Riding defaults."
  },
  {
   "id": "REQ-CMB-002",
   "level": 2,
   "text": "A spawned Combatdog reports standing hostiles in line of sight within 15 cells into the war queue and engages the assigned one.",
   "allocated_to": [
    "comp.engage"
   ],
   "inferred": true,
   "parent": "REQ-CMB-001",
   "rationale": "Plan 0001 Combatdog: engage range."
  },
  {
   "id": "REQ-CMB-003",
   "level": 2,
   "text": "A standing target is shot from where the dog stands when its gun can hit, else from a found cast position, else meleed; a downed humanlike goes to a free prisoner bed, else any downed hostile is carried to an empty non-bedroom room and left.",
   "allocated_to": [
    "comp.engage"
   ],
   "inferred": true,
   "parent": "REQ-CMB-001",
   "rationale": "Plan 0001 Combatdog: capture decision."
  },
  {
   "id": "REQ-CMB-005",
   "level": 2,
   "text": "With nothing to fight during a raid, a deployed Combatdog walks its stable's waypoints in a loop, else guards within 4 cells of the stable (or coordinator); it docks when the map is clear.",
   "allocated_to": [
    "comp.patrol"
   ],
   "inferred": true,
   "parent": "REQ-CMB-001",
   "rationale": "Plan 0001 Combatdog: patrol and guard decisions."
  },
  {
   "id": "REQ-CMB-006",
   "level": 2,
   "text": "At 10% a Combatdog breaks off, retreats with a yellow flashing siren, ignores pings until fully charged, then returns to the queued target; a drafted one is undrafted.",
   "allocated_to": [
    "comp.battery",
    "comp.siren"
   ],
   "inferred": true,
   "parent": "REQ-CMB-001",
   "rationale": "Plan 0001 Combatdog: siren and drafting decisions."
  },
  {
   "id": "REQ-CMB-007",
   "level": 2,
   "text": "A player Combatdog can be drafted, holds position between orders, takes vanilla move and melee attack orders ahead of the war queue, ignores the queue while drafted, and stays drafted across save/load.",
   "allocated_to": [
    "comp.drafting"
   ],
   "inferred": true,
   "parent": "REQ-CMB-001",
   "rationale": "Plan 0001 Combatdog: drafting and Harmony patches 4-5."
  },
  {
   "id": "REQ-CMB-009",
   "level": 2,
   "text": "Combatdog weapon slots are filled for free from the mod's built-in guns: slot 1 from Tier 2 (assault rifle default or LMG), slot 2's flamethrower on Tier 3 used within its range, and Tier 4's minigun with its move-speed penalty; one gun is held at a time.",
   "allocated_to": [
    "comp.tiers"
   ],
   "inferred": true,
   "parent": "REQ-CMB-008",
   "rationale": "Plan 0001 Combatdog: slot guns and Tier 3 fire."
  },
  {
   "id": "REQ-CMB-010",
   "level": 2,
   "text": "Each tier has its own armor, health scale and upgrade cost, and a Tier 1 Combatdog survives repeated ordinary melee hits.",
   "allocated_to": [
    "comp.race",
    "comp.tiers"
   ],
   "inferred": true,
   "parent": "REQ-CMB-008",
   "rationale": "Plan 0001 Combatdog: tier details and T9 balance direction."
  },
  {
   "id": "REQ-REN-002",
   "level": 2,
   "text": "A retreating Combatdog carries a yellow lamp flashing at 2 Hz; a rescuing Medirunner flashes a red tail dome; both are drawn above everything.",
   "allocated_to": [
    "comp.siren"
   ],
   "inferred": true,
   "parent": "REQ-REN-001",
   "rationale": "Plan 0001 Combatdog: siren and Liveries decisions."
  },
  {
   "id": "REQ-REN-003",
   "level": 2,
   "text": "A pawn carried by a robodog lies along its back and turns over during the scoop.",
   "allocated_to": [
    "comp.pose"
   ],
   "inferred": true,
   "parent": "REQ-REN-001",
   "rationale": "Plan 0001 Harmony patch 6."
  }
 ],
 "verifications": [
  {
   "id": "VER-SYS-001",
   "level": 0,
   "method": "demonstration",
   "name": "Packaged Run all (55-scenario suite)",
   "verifies": [
    "REQ-SYS-001",
    "REQ-SYS-002",
    "REQ-SYS-003",
    "REQ-SYS-004",
    "REQ-SYS-005",
    "REQ-SYS-006",
    "REQ-SYS-007"
   ],
   "procedure": "Run tools/autotest.py run against the actual package; every scenario must PASS with zero new errors against the no-mod baseline, with save/load steps in the suite. Recorded result: 54/55 in the full packaged run, the one failure a stale fixture assumption, corrected and that scenario re-proven 1/1 with 0 new errors (docs/test-runs.md:14-17); no full-suite rerun followed the fix.",
   "status": "passing",
   "evidence": [
    "docs/test-runs.md:14",
    "docs/test-runs.md:16",
    "tools/autotest.py:556",
    "Source/Quadrunner/Scenario.cs:114-156"
   ]
  },
  {
   "id": "VER-SYS-002",
   "level": 0,
   "method": "test",
   "name": "Independent removal check (no Quadrunner assembly)",
   "verifies": [
    "REQ-SYS-008"
   ],
   "procedure": "Load the cleaned export in a second game process without Quadrunner; tick 600, save/reload, tick 600, and assert the keeper list (riders, patients, injuries, vanilla cargo, battle-log records).",
   "status": "passing",
   "evidence": [
    "docs/test-runs.md:17",
    "tools/removal-probe/Probe.cs:1-40",
    "tools/autotest.py:599"
   ]
  },
  {
   "id": "VER-SYS-003",
   "level": 0,
   "method": "inspection",
   "name": "Core + Harmony only",
   "verifies": [
    "REQ-SYS-007"
   ],
   "procedure": "Inspect About.xml dependencies and the assembly for DLC checks; the test profile loads Core, Harmony and Quadrunner only, so every scenario doubles as a Core-only run.",
   "status": "passing",
   "evidence": [
    "About/About.xml:10-20",
    "Source/Quadrunner/QR.cs:15-21"
   ]
  },
  {
   "id": "VER-SYS-004",
   "level": 0,
   "method": "test",
   "name": "Smoke, Save and reload, and the harness self-test",
   "verifies": [
    "REQ-SYS-009"
   ],
   "procedure": "Smoke proves the runner starts, ticks and cleans up; Save and reload proves a scenario survives a reload with tracked ThingIDs; tools/selftest.sh proves the harness itself (baseline, restart, filters, failures, new errors, hang watchdog) against a fake game.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.cs:290 (Smoke)",
    "Source/Quadrunner/Scenarios.cs:301 (Save and reload)",
    "tools/selftest.sh",
    "docs/test-runs.md:16"
   ]
  },
  {
   "id": "VER-STB-DOCK",
   "level": 1,
   "method": "test",
   "name": "Dock and undock",
   "verifies": [
    "REQ-STB-001",
    "REQ-PAW-001",
    "REQ-PAW-004"
   ],
   "procedure": "Spawn a stable and a new Quadrunner: docked, race properties (not animal/mechanoid/flesh, untrainable, no needs, no wild spawns), undock, walk home, dock, two reloads.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.cs:162 (Dock and undock)"
   ]
  },
  {
   "id": "VER-STB-FULL",
   "level": 1,
   "method": "test",
   "name": "Stable full (16th dog)",
   "verifies": [
    "REQ-STB-001",
    "REQ-STB-002"
   ],
   "procedure": "14 dogs plus a bill; add the 15th: bill doable; add a 16th: bill not doable, the extra dog is out and unassigned; reload keeps 15 assigned and the 16th unassigned.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.cs:257 (Stable full (16th dog))"
   ]
  },
  {
   "id": "VER-STB-ASM",
   "level": 2,
   "method": "test",
   "name": "Assemble and dock / Assemble Medirunner / Assemble Combatdog",
   "verifies": [
    "REQ-STB-002"
   ],
   "procedure": "A colonist takes the bill; the finished dog of the recipe's kind joins and docks; the bill is done; reload keeps it with the stable.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.cs:224 (Assemble and dock, Assemble Medirunner, Assemble Combatdog)"
   ]
  },
  {
   "id": "VER-STB-CHARGE",
   "level": 1,
   "method": "test",
   "name": "Stable or conduit",
   "verifies": [
    "REQ-STB-003",
    "REQ-CHG-001",
    "REQ-PAW-004"
   ],
   "procedure": "At 10% the dog docks (stable fills it sooner); the measured rate tracks 0.0012/tick and is 2.5x+ the conduit rate; at 97% the adjacent conduit wins and it tops up and resumes surveying.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs:450 (Stable or conduit)"
   ]
  },
  {
   "id": "VER-STB-POWER",
   "level": 2,
   "method": "test",
   "name": "Stable power loss",
   "verifies": [
    "REQ-STB-003"
   ],
   "procedure": "Switch the stable off: dispatch continues and dirt is cleaned, but a docked low dog neither charges nor heals; after reload and power-on it charges (5% to 18%) and heals (8.0 to 5.9).",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs:368 (Stable power loss)"
   ]
  },
  {
   "id": "VER-STB-FIRE",
   "level": 1,
   "method": "test",
   "name": "Stable fire with docked dogs",
   "verifies": [
    "REQ-STB-004"
   ],
   "procedure": "Burn the stable: both dogs are out and unassigned, the newest coordinates, and both survey instead of waiting indoors; still true after reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs:420 (Stable fire with docked dogs)"
   ]
  },
  {
   "id": "VER-STB-UNINSTALL",
   "level": 1,
   "method": "test",
   "name": "Stable uninstall and reinstall",
   "verifies": [
    "REQ-STB-004"
   ],
   "procedure": "Minify the stable: the dog keeps affiliation, itself coordinates, surveys, waits on a low battery; after reload and reinstall 10+ cells away it walks over and docks.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs:462 (Stable uninstall and reinstall)"
   ]
  },
  {
   "id": "VER-STB-OVERVIEW",
   "level": 1,
   "method": "test",
   "name": "Tier upgrade",
   "verifies": [
    "REQ-STB-005",
    "REQ-CMB-008",
    "REQ-CMB-009"
   ],
   "procedure": "Rows show Tier 1/claws; the tab's upgrade button adds exactly one bill; a colonist works it; the dog returns as Tier 2 with its name and the default rifle; the row and gun pick update; reload preserves them.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Tiers.cs:30 (Tier upgrade)"
   ]
  },
  {
   "id": "VER-STB-CARRY",
   "level": 1,
   "method": "test",
   "name": "Carry home",
   "verifies": [
    "REQ-STB-006"
   ],
   "procedure": "Force dispatch after a powered-down dog appears 15-25 cells out: a network dog carries it and docks it, across a reload; then a colonist's real Carry to stable float option does the same.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs:50 (Carry home)"
   ]
  },
  {
   "id": "VER-STB-FIELD",
   "level": 2,
   "method": "test",
   "name": "Field battery and repair kit",
   "verifies": [
    "REQ-STB-007"
   ],
   "procedure": "Make both items at the stable; a damaged, powered-down field dog gets the kit first, then the battery; both items are consumed and health/injury checks pass across reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs:114 (Field battery and repair kit)"
   ]
  },
  {
   "id": "VER-STB-WRECK",
   "level": 2,
   "method": "test",
   "name": "Wreck rebuilt",
   "verifies": [
    "REQ-STB-008"
   ],
   "procedure": "Destroy a dog beside its stable; the wreck is not a dog; a colonist works the rebuild bill; the same pawn is resurrected alive, named and assigned, after reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs:354 (Wreck rebuilt)"
   ]
  },
  {
   "id": "VER-STB-REMOVAL",
   "level": 1,
   "method": "test",
   "name": "Prepare removal",
   "verifies": [
    "REQ-STB-010"
   ],
   "procedure": "Stage bills, docked and deployed dogs, a rider mid-ride and a powered-down dog; PrepareSave; assert riders/patients/cargo/injuries survive and every mod object and QR job is gone.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Removal.cs:24 (Prepare removal)"
   ]
  },
  {
   "id": "VER-STB-ALERT",
   "level": 1,
   "method": "test",
   "name": "Retrieval under fire",
   "verifies": [
    "REQ-STB-009",
    "REQ-REC-001"
   ],
   "procedure": "A dog at 0% with a fire beside it: the dog beats the fire out, then carries it home; a walled-in dog is alerted by name and not assigned, and after the wall opens it is carried home.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs:235 (Retrieval under fire)"
   ]
  },
  {
   "id": "VER-PAW-DRAIN",
   "level": 1,
   "method": "test",
   "name": "Battery drain and power-down",
   "verifies": [
    "REQ-PAW-002",
    "REQ-PAW-003"
   ],
   "procedure": "Walk measured distances at 50%, 10.5% and 0.3%: drain matches 1/3500 per cell; below 10% it wants a charge; at 0% it is downed with the hediff; reload keeps both; 50% powers it up.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs:168 (Battery drain and power-down)"
   ]
  },
  {
   "id": "VER-NET-COORD",
   "level": 1,
   "method": "test",
   "name": "Network: coordinator failover",
   "verifies": [
    "REQ-NET-001"
   ],
   "procedure": "Stable A coordinates; A burns, B takes the same task; B burns, the newest dog coordinates the same task across reload; the dog dies, the older dog coordinates and finishes the haul with no stable.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:315 (Network: coordinator failover)"
   ]
  },
  {
   "id": "VER-NET-DISC",
   "level": 1,
   "method": "test",
   "name": "Network: discovery beyond the boundary",
   "verifies": [
    "REQ-NET-002",
    "REQ-NET-008"
   ],
   "procedure": "A revealed narrow corridor with gold 60 cells out: the idle worker surveys, discovers the stack only after walking near it, exactly once, hauls it across the old 40-cell boundary; reload resumes surveying.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:60 (Network: discovery beyond the boundary)"
   ]
  },
  {
   "id": "VER-NET-SCAN",
   "level": 1,
   "method": "test",
   "name": "Network: scan eligibility",
   "verifies": [
    "REQ-NET-002",
    "REQ-WRK-001"
   ],
   "procedure": "Forbidden gold, fogged gold and dirt outside Home are ignored over several scans; after allowing/revealing/Home they execute; permitted work stays complete after reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:106 (Network: scan eligibility)"
   ]
  },
  {
   "id": "VER-NET-DEDUP",
   "level": 2,
   "method": "test",
   "name": "Network: dedup and queue persistence",
   "verifies": [
    "REQ-NET-003"
   ],
   "procedure": "Two stacks 30-40 cells out; both queued exactly once; a duplicate manual report changes nothing; rescans do not duplicate while hauling; a walled pocket stays queued exactly once across scans and reload, then completes.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:166 (Network: dedup and queue persistence)"
   ]
  },
  {
   "id": "VER-NET-NEAR",
   "level": 2,
   "method": "test",
   "name": "Network: nearest dog wins",
   "verifies": [
    "REQ-NET-004"
   ],
   "procedure": "A 41-cell wall with a stable north and south: the south dog wins the south stack despite a shorter beeline, then the north dog wins the north one; walling in the nearer north dog makes the reachable south dog win.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:232 (Network: nearest dog wins)"
   ]
  },
  {
   "id": "VER-NET-DEATH",
   "level": 2,
   "method": "test",
   "name": "Network: death and interruption reassignment",
   "verifies": [
    "REQ-NET-005"
   ],
   "procedure": "D1 is killed mid-task and D2 finishes the stack; D2's acknowledged job survives reload without duplication; a manual interruption is re-issued and the stack is delivered once.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:462 (Network: death and interruption reassignment)"
   ]
  },
  {
   "id": "VER-NET-CLEAR",
   "level": 2,
   "method": "test",
   "name": "Network: last node clears the backlog",
   "verifies": [
    "REQ-NET-007"
   ],
   "procedure": "Kill the last dog and burn the last stable: both queues clear; reload resurrects nothing; a new stable and dog never pick up the discarded backlog.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:399 (Network: last node clears the backlog)"
   ]
  },
  {
   "id": "VER-NET-WAR",
   "level": 1,
   "method": "test",
   "name": "Network: war suspends hauls",
   "verifies": [
    "REQ-NET-009"
   ],
   "procedure": "A raider appears while the dog carries a far stack: the haul stops at once, cargo is dropped safely, the worker stands by for 600 ticks and across reload, then finishes both stacks after the raider leaves.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:541 (Network: war suspends hauls)"
   ]
  },
  {
   "id": "VER-WRK-RAID",
   "level": 1,
   "method": "test",
   "name": "Raid: suspend and resume",
   "verifies": [
    "REQ-NET-009",
    "REQ-WRK-004",
    "REQ-PAW-002"
   ],
   "procedure": "Measure calm speed and drain, start a raid: the haul suspends and cargo is dropped, raid speed is 3x calm and drain factor 2; reload keeps the queue; the raid ending restores 1x speed and drain and completes both stacks.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs:251 (Raid: suspend and resume)"
   ]
  },
  {
   "id": "VER-NET-NOSTABLE",
   "level": 2,
   "method": "test",
   "name": "Network: combatdog without a stable",
   "verifies": [
    "REQ-NET-010",
    "REQ-CMB-002"
   ],
   "procedure": "A free worker and Combatdog and no stable: the newest dog coordinates in war, the Combatdog engages the raider while the worker stands by and the gold waits across reload; after the fight the worker delivers it.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:637 (Network: combatdog without a stable)"
   ]
  },
  {
   "id": "VER-NET-MEDIC",
   "level": 2,
   "method": "test",
   "name": "Network: medic keeps rescuing in war",
   "verifies": [
    "REQ-NET-010",
    "REQ-RES-003"
   ],
   "procedure": "From the first raid tick the worker stands by docked with its haul queued while the Medirunner rescues; reload mid-carry leaves the patient in bed and the queue waiting; after the raider dies the worker hauls.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Network.cs:704 (Network: medic keeps rescuing in war)"
   ]
  },
  {
   "id": "VER-CHG-SPOT",
   "level": 1,
   "method": "test",
   "name": "Conduit charging",
   "verifies": [
    "REQ-CHG-002",
    "REQ-CHG-005"
   ],
   "procedure": "A roofed room with a conduit under a wall and a full battery: a cached spot exists, the dog lies down and charges at 0.0004/tick, survives reload, completes and draws about 90 Wd.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs:215 (Conduit charging)"
   ]
  },
  {
   "id": "VER-CHG-MAZE",
   "level": 1,
   "method": "test",
   "name": "Charging maze",
   "verifies": [
    "REQ-CHG-001",
    "REQ-CHG-002"
   ],
   "procedure": "A room by a turbine, a near room behind a forbidden door, a far open room: no spot by the turbine (setting 0 gives one, back to 6 gives none), the dog charges in the far room, and with no reachable spot it goes to its stable and charges.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs:273 (Charging maze)"
   ]
  },
  {
   "id": "VER-CHG-ROOM",
   "level": 2,
   "method": "test",
   "name": "Charging room rules",
   "verifies": [
    "REQ-CHG-002",
    "REQ-CHG-003",
    "REQ-CHG-005"
   ],
   "procedure": "Room rules one by one: prisoner bed never, occupied bedroom no, empty bedroom yes, freezer yes, allowed-area exclusion no; charging then opening the roof makes the dog get up still wanting a charge.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs:343 (Charging room rules)"
   ]
  },
  {
   "id": "VER-CHG-DRAIN",
   "level": 2,
   "method": "test",
   "name": "Batteries drain mid-charge",
   "verifies": [
    "REQ-CHG-005"
   ],
   "procedure": "A near room holding 30 Wd and a far full one: the dog charges near, leaves when the battery runs dry at about 30-45%, searches again, charges far and completes.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Charging.cs:416 (Batteries drain mid-charge)"
   ]
  },
  {
   "id": "VER-WRK-HAUL",
   "level": 1,
   "method": "test",
   "name": "Haul and clean",
   "verifies": [
    "REQ-NET-008",
    "REQ-WRK-001",
    "REQ-WRK-003"
   ],
   "procedure": "Two dogs: the near blueprint and frame get steel, the near gold is stored, three filth are cleaned, idle workers survey; a survey finds gold 50-60 out and hauls it across the old boundary after reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs:111 (Haul and clean)"
   ]
  },
  {
   "id": "VER-WRK-TWO",
   "level": 1,
   "method": "test",
   "name": "Two stables",
   "verifies": [
    "REQ-NET-004",
    "REQ-WRK-001"
   ],
   "procedure": "Two stables share one queue across reload; separate cleaning targets are never claimed by both dogs at once; home-stable affiliation does not reserve nearby work.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Dispatch.cs:192 (Two stables)"
   ]
  },
  {
   "id": "VER-RES-MASS",
   "level": 1,
   "method": "test",
   "name": "Mass-casualty raid",
   "verifies": [
    "REQ-RES-001",
    "REQ-RES-002",
    "REQ-RES-003"
   ],
   "procedure": "Three colonists down 18-28 cells out with a raider walled in among them: the Medirunner goes for the fastest-bleeding first, carries each to a bed, then docks, across a reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Rescue.cs:61 (Mass-casualty raid)"
   ]
  },
  {
   "id": "VER-RES-ORDER",
   "level": 1,
   "method": "test",
   "name": "Rescue order",
   "verifies": [
    "REQ-RES-001",
    "REQ-RES-002"
   ],
   "procedure": "Two Medirunners, two medical beds, a prison cell, an animal spot and five downed pawns: rank order colonist/guest/prisoner/animal starts first, two patients at once, the prisoner in the prison cell, the enemy left.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Rescue.cs:123 (Rescue order)"
   ]
  },
  {
   "id": "VER-RID-EMPTY",
   "level": 1,
   "method": "test",
   "name": "Ride until 0%",
   "verifies": [
    "REQ-RID-001",
    "REQ-RID-002",
    "REQ-RID-003",
    "REQ-RID-005",
    "REQ-PAW-002"
   ],
   "procedure": "Ride from the stable 70-90 cells out with the dog at 1.5%: the rider moves at 7.0 c/s in the dog's cell, the battery drains, reload keeps the ride, at 0% the dog powers down and the rider walks on; the other dog carries it home.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Riding.cs:45 (Ride until 0%)"
   ]
  },
  {
   "id": "VER-RID-DISMOUNT",
   "level": 1,
   "method": "test",
   "name": "Ride and dismount",
   "verifies": [
    "REQ-RID-001",
    "REQ-RID-003",
    "REQ-RID-005"
   ],
   "procedure": "Ride on the dog and from the stable; dismount at the destination, before an indoor door, when drafted, when downed; reload mid-ride; the dog is free afterwards.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Riding.cs:105 (Ride and dismount)"
   ]
  },
  {
   "id": "VER-RID-POSE",
   "level": 2,
   "method": "test",
   "name": "Rider pose",
   "verifies": [
    "REQ-RID-004"
   ],
   "procedure": "Pause with a mounted rider, camera close, turn to each facing and capture ride-north/east/south/west.png; inspect the offsets in the summary.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Riding.cs:197 (Rider pose)"
   ]
  },
  {
   "id": "VER-CMB-PING",
   "level": 1,
   "method": "test",
   "name": "Ping chain",
   "verifies": [
    "REQ-CMB-001",
    "REQ-CMB-003"
   ],
   "procedure": "Two raiders hit a Quadrunner 50-70 cells out: the Combatdog takes a war assignment beyond 40 cells, both are dealt with across reload; a third raider pings another dog and the Combatdog turns round; it docks.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs:86 (Ping chain)"
   ]
  },
  {
   "id": "VER-CMB-SWEEP",
   "level": 2,
   "method": "test",
   "name": "Combatdog sweep",
   "verifies": [
    "REQ-CMB-003",
    "REQ-CMB-005"
   ],
   "procedure": "Four raiders: three down (one to the prisoner bed, two carried to an empty room past a nearer bedroom), the dead skipped, reload mid-carry; then three patrol waypoints across reload with a walled-in raider keeping the raid on.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs:166 (Combatdog sweep)"
   ]
  },
  {
   "id": "VER-CMB-RETREAT",
   "level": 2,
   "method": "test",
   "name": "Low-battery retreat",
   "verifies": [
    "REQ-CMB-006",
    "REQ-REN-002"
   ],
   "procedure": "At 10% the Combatdog breaks off, retreats with the siren lit (siren.png captured), ignores a new ping for 200 ticks, docks or charges, and after charging goes back for the raiders.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs:287 (Low-battery retreat)"
   ]
  },
  {
   "id": "VER-CMB-DRAFT",
   "level": 2,
   "method": "test",
   "name": "Drafted priority target",
   "verifies": [
    "REQ-CMB-006",
    "REQ-CMB-007"
   ],
   "procedure": "Draft the Combatdog with its toggle: it holds and takes a vanilla melee order on a farther, unpinned raider ahead of the queued one; reload keeps it drafted and idle for 300 ticks; at 10% it is undrafted and retreats, then returns.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Combat.cs:385 (Drafted priority target)"
   ]
  },
  {
   "id": "VER-CMB-T3",
   "level": 2,
   "method": "test",
   "name": "Tier 3 and 4 weapons",
   "verifies": [
    "REQ-CMB-009"
   ],
   "procedure": "Tier 3 holds rifle + flamethrower: it rifles a far raider, switches to the flamethrower for a 4-6 cell one, survives reload; upgraded to Tier 4 it holds the minigun and its deployed speed is 0.5 c/s lower.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Tiers.cs:121 (Tier 3 and 4 weapons)"
   ]
  },
  {
   "id": "VER-CMB-DEMO",
   "level": 2,
   "method": "demonstration",
   "name": "Tier 2/3/4 weapon demos",
   "verifies": [
    "REQ-CMB-009"
   ],
   "procedure": "Upgrade a Combatdog to each tier, arm each allowed gun and let it fight raiders at normal speed; a screenshot catches a burst in flight and the rounds are named in the demo output.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Demo.cs:47 (Tier 2/3/4 weapon demo)"
   ]
  },
  {
   "id": "VER-CMB-ENDURE",
   "level": 2,
   "method": "test",
   "name": "Combatdog endurance",
   "verifies": [
    "REQ-CMB-010"
   ],
   "procedure": "A fresh Tier 1 dog without a stable takes three then six ordinary fist hits and stays standing, with damage persisting across reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Balance.cs:13 (Combatdog endurance)"
   ]
  },
  {
   "id": "VER-REN-ART",
   "level": 1,
   "method": "test",
   "name": "Artwork showcase",
   "verifies": [
    "REQ-REN-001"
   ],
   "procedure": "Every model lined up on clear ground, captured from four facings at close range, plus stable facings, mounted guns and the overview tab; images inspected.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Art.cs:14 (Artwork showcase)"
   ]
  },
  {
   "id": "VER-REN-WALK",
   "level": 1,
   "method": "test",
   "name": "Walk animation",
   "verifies": [
    "REQ-REN-001"
   ],
   "procedure": "A Quadrunner and a Tier 4 Combatdog walk 16 cells: layer check (whole + body + legs), legs swing and lift while moving and rest when stopped, across a reload.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Gait.cs:29 (Walk animation)"
   ]
  },
  {
   "id": "VER-REN-RESCUE",
   "level": 1,
   "method": "test",
   "name": "Rescue animation",
   "verifies": [
    "REQ-REN-001",
    "REQ-REN-002",
    "REQ-REN-003"
   ],
   "procedure": "A Medirunner runs to a downed colonist at normal speed: siren frames, head dip/duck at the pickup, 180-degree patient flip, then delivery to bed; captures inspected.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Animation.cs:60 (Rescue animation)"
   ]
  },
  {
   "id": "VER-REN-COMBAT",
   "level": 1,
   "method": "test",
   "name": "Combat animation",
   "verifies": [
    "REQ-REN-001"
   ],
   "procedure": "A Combatdog fights at normal speed: pounce on melee strikes, braced crouch and recoil kick per shot, measured and captured.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Animation.cs:184 (Combat animation)"
   ]
  },
  {
   "id": "VER-REC-KIDNAP",
   "level": 2,
   "method": "test",
   "name": "Raider carries off a dog",
   "verifies": [
    "REQ-REC-002"
   ],
   "procedure": "A raider 6-10 cells from a powered-down dog: the kidnap search finds the dog, the raider picks it up and leaves the map with it, and the stable keeps its docked dog.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs:302 (Raider carries off a dog)"
   ]
  },
  {
   "id": "VER-REC-CARRIER",
   "level": 2,
   "method": "test",
   "name": "Carrier runs low",
   "verifies": [
    "REQ-REC-003"
   ],
   "procedure": "A carrier drops to 10% mid-carry: with a powered stable it finishes and docks the dog; with the stable off and a charge room near, it sets the dog down, charges and then carries it home.",
   "status": "passing",
   "evidence": [
    "Source/Quadrunner/Scenarios.Retrieval.cs:186 (Carrier runs low)"
   ]
  }
 ],
 "flows": [
  {
   "id": "flow.dispatch",
   "name": "Dispatch a haul",
   "trigger": "An idle Quadrunner is out of a job and a revealed item lies within its scan",
   "description": "The paying path: local observation, deduplicated queue, nearest-capable assignment by real path cost, a vanilla job, then completion or reassignment.",
   "steps": [
    {
     "n": 1,
     "from": "comp.net-node",
     "to": "comp.discovery",
     "action": "Request a local scan",
     "condition": "every 120 ticks, for each active dog",
     "mechanism": "15-cell radial scan per active dog; fogged and distant cells are never seen."
    },
    {
     "n": 2,
     "from": "comp.discovery",
     "to": "comp.net-node",
     "action": "Report the task",
     "mechanism": "Item, frame/blueprint materials, Home filth, dog; fogged, forbidden, off-map refused; dedup."
    },
    {
     "n": 3,
     "from": "comp.net-node",
     "to": "comp.work-job",
     "action": "Build one candidate job",
     "mechanism": "The task's own scanner is asked; reservations and reach checked; 3 job defs."
    },
    {
     "n": 4,
     "from": "comp.work-job",
     "to": "comp.net-node",
     "action": "Candidate job or null",
     "mechanism": "Null when forbidden, reserved, unreachable or refused: that dog is no candidate."
    },
    {
     "n": 5,
     "from": "comp.net-node",
     "to": "ext.vanilla",
     "action": "Assign the nearest by path cost",
     "mechanism": "FindPathNow cost; unreachable = MaxValue; ties to lower thingID; assignee saved."
    },
    {
     "n": 6,
     "from": "comp.think-tree",
     "to": "comp.net-node",
     "action": "Ask for the next order",
     "mechanism": "Assigned job continues; finished tasks drop; else survey, dock or wait."
    },
    {
     "n": 7,
     "from": "comp.net-node",
     "to": "ext.vanilla",
     "action": "Reconcile and complete",
     "mechanism": "Each 60 ticks: job, toil and position tracked; 2500 idle ticks release it."
    },
    {
     "n": 8,
     "from": "comp.net-node",
     "to": "comp.survey",
     "action": "Survey when there is nothing",
     "condition": "no task assignable to this dog",
     "mechanism": "Home-first scan-certified area sweep; 600-tick expiry so work preempts."
    }
   ],
   "failure_modes": [
    {
     "when": "No dog can reach the target",
     "then": "The task stays queued with retryAt = now + 120 ticks; it is dropped only when Finished() says it is moot or a dog can take it.",
     "detected_by": "VER-NET-NEAR"
    },
    {
     "when": "The assignee dies or its job is interrupted",
     "then": "The assignment resets and the same task is re-queued; an acknowledged job reloads without duplicate execution.",
     "detected_by": "VER-NET-DEATH"
    },
    {
     "when": "The assignee stops making progress",
     "then": "After 2500 ticks StopSafely, drop any carried cargo, release the task and retry after a scan interval. Implemented but unproven: no scenario forces a stall, so REQ-NET-006 is a reported traceability gap."
    },
    {
     "when": "A raid starts mid-haul",
     "then": "Normal work is suspended at once, cargo is dropped safely and the worker waits; the queue is retained and resumes after the threat (see the combat flow).",
     "detected_by": "VER-NET-WAR"
    }
   ]
  },
  {
   "id": "flow.charge",
   "name": "The charge decision",
   "trigger": "A dog's battery falls to 10% and it latches 'wants a charge'",
   "description": "How a low dog picks stable or conduit, what it draws, and what happens when it reaches 0% and has to be recovered.",
   "steps": [
    {
     "n": 1,
     "from": "comp.battery",
     "to": "comp.charge-decision",
     "action": "Latch want-charge",
     "condition": "battery <= 10%",
     "mechanism": "Latch at 10%, cleared only at 100%; a drafted dog is undrafted at once."
    },
    {
     "n": 2,
     "from": "comp.think-tree",
     "to": "comp.charge-decision",
     "action": "Take the charge branch",
     "mechanism": "Seek-charge sits before the work giver, so charging outranks work."
    },
    {
     "n": 3,
     "from": "comp.charge-decision",
     "to": "comp.charge-spots",
     "action": "Find the best spot",
     "mechanism": "Cached spots, UsableBy per dog, region traversal; forbidden doors block."
    },
    {
     "n": 4,
     "from": "comp.charge-spots",
     "to": "comp.charge-decision",
     "action": "Spot or none",
     "mechanism": "Score = distance + 15 x dogs there - 10 x min(stored Wd / 1000, 1); none if no spot qualifies."
    },
    {
     "n": 5,
     "from": "comp.charge-decision",
     "to": "comp.stable",
     "action": "Dock and charge",
     "condition": "stable fills it sooner, travel included",
     "mechanism": "Travel + missing/0.0012 vs missing/0.0004; unpowered stable only if no spot."
    },
    {
     "n": 6,
     "from": "comp.charge-decision",
     "to": "comp.conduit-charge",
     "action": "Charge in the room",
     "condition": "a conduit spot fills it sooner",
     "mechanism": "QR_ChargeAtConduit to the spot; from 10% a spot wins only ~120 cells closer."
    },
    {
     "n": 7,
     "from": "comp.conduit-charge",
     "to": "comp.battery",
     "action": "Draw from the net",
     "mechanism": "Lie down; +0.0004/tick; 100 Wd per full charge, fullest battery first."
    },
    {
     "n": 8,
     "from": "comp.battery",
     "to": "comp.net-node",
     "action": "Power down",
     "condition": "battery reaches 0%",
     "mechanism": "Moving 0: downed, off the roster, never assigned; above 0% powers up."
    },
    {
     "n": 9,
     "from": "comp.net-node",
     "to": "comp.retrieval",
     "action": "Send a dog to carry it home",
     "mechanism": "Retrieve task from any scan; fire at 1.5 cells first; nearest stable docks it."
    }
   ],
   "failure_modes": [
    {
     "when": "No stable and no spot is reachable",
     "then": "The dog searches again while it still wants a charge and eventually powers down where it stands; recovery then applies.",
     "detected_by": "VER-CHG-MAZE"
    },
    {
     "when": "The net's batteries run dry mid-charge",
     "then": "The job ends Incompletable, the dog gets up and searches again; it moves to a room with a fuller battery.",
     "detected_by": "VER-CHG-DRAIN"
    },
    {
     "when": "The spot stops qualifying (roof opened, prisoner bed, fire, hostile)",
     "then": "The 250-tick re-check ends the job; the dog gets up still wanting a charge and searches again.",
     "detected_by": "VER-CHG-ROOM"
    },
    {
     "when": "A carrier hits 10% while carrying a dog",
     "then": "It compares stable versus spot: finish when the stable wins, otherwise set the dog down, charge and come back for it.",
     "detected_by": "VER-REC-CARRIER"
    }
   ]
  },
  {
   "id": "flow.combat",
   "name": "The combat ping chain",
   "trigger": "A hostile pawn damages one of the player's dogs",
   "description": "From a single hit to a Combatdog answering, the war queue and work suspension, engagement, capture, and the low-battery retreat.",
   "steps": [
    {
     "n": 1,
     "from": "ext.vanilla",
     "to": "comp.ping",
     "action": "Hostile damages a dog",
     "mechanism": "Hostile pawn, dog spawned and alive; friendly damage and a killing hit do not."
    },
    {
     "n": 2,
     "from": "comp.ping",
     "to": "comp.net-node",
     "action": "Ping the attacker",
     "mechanism": "War-queue report (dedup), Raid.Recheck, forced tick: answered the same tick."
    },
    {
     "n": 3,
     "from": "comp.net-node",
     "to": "comp.engage",
     "action": "Assign the war target",
     "condition": "attacker queued and a combat-capable dog is available",
     "mechanism": "Nearest combat dog by path cost; a queued attacker sends no second dog."
    },
    {
     "n": 4,
     "from": "comp.net-node",
     "to": "comp.think-tree",
     "action": "Suspend normal work",
     "mechanism": "War stops normal jobs, drops cargo safely, workers Wait; queue retained."
    },
    {
     "n": 5,
     "from": "comp.engage",
     "to": "comp.tiers",
     "action": "Arm the gun for the range",
     "mechanism": "Slot 2 within range, else slot 1, else claws; re-armed before each attack."
    },
    {
     "n": 6,
     "from": "comp.engage",
     "to": "ext.vanilla",
     "action": "Attack or capture",
     "mechanism": "AttackStatic, else cast-position Goto, else melee; downed: bed or empty room."
    },
    {
     "n": 7,
     "from": "comp.battery",
     "to": "comp.engage",
     "action": "Break off at 10%",
     "condition": "battery <= 10%",
     "mechanism": "Job ends at once (a capture finishes); undrafted; siren; pings ignored."
    },
    {
     "n": 8,
     "from": "comp.net-node",
     "to": "comp.patrol",
     "action": "Patrol or guard",
     "condition": "war on, nothing to fight",
     "mechanism": "Waypoints in a loop, else guard within 4 cells; 60-tick jobs; clear = dock."
    }
   ],
   "failure_modes": [
    {
     "when": "There is no Combatdog on the map",
     "then": "Workers stand by; no worker takes combat; the queue holds until a Combatdog exists or the threat clears.",
     "detected_by": "VER-NET-WAR"
    },
    {
     "when": "The target dies, is downed or becomes a prisoner",
     "then": "Finished() drops the task; a capture already under way completes and the queue empties.",
     "detected_by": "VER-CMB-SWEEP"
    },
    {
     "when": "No prisoner bed is free",
     "then": "A downed humanlike is carried to the nearest empty non-bedroom room and left downed, not a prisoner; with no such room it stays queued.",
     "detected_by": "VER-CMB-SWEEP"
    },
    {
     "when": "The Combatdog hits 10% mid-fight",
     "then": "It retreats with the siren, ignores further pings, charges, then returns to the queued target.",
     "detected_by": "VER-CMB-RETREAT"
    }
   ]
  }
 ],
 "annotations": [
  {
   "id": "ann.1",
   "target": "ext.harness",
   "kind": "decision",
   "text": "Boundary call: the harness is modelled outside the product. tools/autotest.py is not shipped; Source/Quadrunner/AutoTest.cs and the scenario suite are compiled into the shipped DLL but dormant unless the game is launched with -quadrunner-autotest=. The command/event files are the only link.",
   "author": "brownfield reconstruction 2026-10-21"
  },
  {
   "id": "ann.2",
   "target": "VER-SYS-001",
   "kind": "note",
   "text": "Newest results: the packaged Run all was 54/55 PASS with 0 new errors; the one failure was a fresh-filth fixture assumption. After the fixture fix the affected scenario passed alone (1/1) from the refreshed package, but no full 55-scenario rerun followed that fix.",
   "author": "docs/test-runs.md 2026-10-21 03:55 and 04:03"
  },
  {
   "id": "ann.3",
   "target": "REQ-NET-006",
   "kind": "risk",
   "text": "Progress-timeout release (2500 ticks without progress) is implemented but no scenario forces a stall; VER-NET-TIMEOUT is planned, not passing.",
   "author": "traceability review"
  },
  {
   "id": "ann.4",
   "target": "REQ-WRK-002",
   "kind": "risk",
   "text": "Trimming a vanilla job's extra targets is inspected in code only; no scenario asserts it.",
   "author": "traceability review"
  },
  {
   "id": "ann.5",
   "target": "REQ-CHG-004",
   "kind": "risk",
   "text": "Cache rebuild on a building-signature change is untested; the charging scenarios clear the cache by hand instead.",
   "author": "traceability review"
  },
  {
   "id": "ann.6",
   "target": "REQ-REC-004",
   "kind": "risk",
   "text": "The 2500-tick deferral of a downed dog to colonists with a field fix in stock is untested; only the colonist delivery itself is.",
   "author": "traceability review"
  },
  {
   "id": "ann.7",
   "target": "comp.stable",
   "kind": "note",
   "text": "JobGiver_WaitInRoom (standby in the nearest non-bedroom room) is only reachable when no survey area exists; Stable fire observes surveying instead, so the room choice has no scenario.",
   "author": "brownfield reconstruction"
  },
  {
   "id": "ann.8",
   "target": "comp.mod-core",
   "kind": "decision",
   "text": "Parented under Stable & Fabrication for want of a mod-level page: this is mod-wide plumbing (startup, settings, removal) that happens to dismantle stable and dog content.",
   "author": "brownfield reconstruction"
  },
  {
   "id": "ann.9",
   "target": "ext.harness",
   "kind": "note",
   "text": "A seventh Harmony patch is test-only (Root_Play.SetupForQuickTestPlay: fixture tile and map size) and active only with the launch argument; the gameplay patch count stays at six plus two XML StatParts.",
   "author": "plan 0001 Test map size decision"
  },
  {
   "id": "ann.10",
   "target": "comp.net-node",
   "kind": "decision",
   "text": "State lives on an elected stable or dog, not a saved map component: removing the mod leaves no orphan map state, which is what makes the independent removal check clean.",
   "author": "plan 0003 Persistence; AGENTS.md architecture note"
  }
 ],
 "index": {
  "reqs_for_node": {
   "sub.stable": [
    "REQ-SYS-001",
    "REQ-SYS-003"
   ],
   "sub.network": [
    "REQ-SYS-002"
   ],
   "sub.work": [
    "REQ-SYS-002"
   ],
   "sub.charging": [
    "REQ-SYS-003"
   ],
   "sub.rescue": [
    "REQ-SYS-004"
   ],
   "sub.riding": [
    "REQ-SYS-005"
   ],
   "sub.combat": [
    "REQ-SYS-006"
   ],
   "sub.pawn": [
    "REQ-SYS-007"
   ],
   "sub.render": [
    "REQ-SYS-007"
   ],
   "comp.mod-core": [
    "REQ-SYS-008",
    "REQ-SYS-009",
    "REQ-STB-010"
   ],
   "comp.stable": [
    "REQ-STB-001",
    "REQ-STB-004",
    "REQ-STB-003"
   ],
   "comp.retrieval": [
    "REQ-STB-006",
    "REQ-STB-009",
    "REQ-REC-001",
    "REQ-REC-002",
    "REQ-REC-003",
    "REQ-REC-004"
   ],
   "comp.race": [
    "REQ-PAW-001",
    "REQ-CMB-010"
   ],
   "comp.battery": [
    "REQ-PAW-002",
    "REQ-PAW-003",
    "REQ-CMB-006"
   ],
   "comp.net-node": [
    "REQ-NET-001",
    "REQ-NET-009",
    "REQ-NET-003",
    "REQ-NET-004",
    "REQ-NET-005",
    "REQ-NET-006",
    "REQ-NET-007",
    "REQ-NET-010",
    "REQ-RES-003"
   ],
   "comp.discovery": [
    "REQ-NET-002"
   ],
   "comp.charge-decision": [
    "REQ-CHG-001",
    "REQ-REC-003"
   ],
   "comp.charge-spots": [
    "REQ-CHG-002",
    "REQ-CHG-003",
    "REQ-CHG-004"
   ],
   "comp.work-job": [
    "REQ-WRK-001",
    "REQ-WRK-002"
   ],
   "comp.raid-part": [
    "REQ-WRK-004"
   ],
   "comp.triage": [
    "REQ-RES-001"
   ],
   "comp.ride-order": [
    "REQ-RID-001"
   ],
   "comp.ping": [
    "REQ-CMB-001"
   ],
   "comp.tiers": [
    "REQ-CMB-008",
    "REQ-CMB-009",
    "REQ-CMB-010"
   ],
   "comp.render-tree": [
    "REQ-REN-001"
   ],
   "comp.pose": [
    "REQ-REN-001",
    "REQ-REN-003"
   ],
   "comp.gait": [
    "REQ-REN-001"
   ],
   "comp.bills": [
    "REQ-STB-002",
    "REQ-STB-008"
   ],
   "comp.overview": [
    "REQ-STB-005"
   ],
   "comp.fieldfix": [
    "REQ-STB-007"
   ],
   "comp.think-tree": [
    "REQ-PAW-004"
   ],
   "comp.survey": [
    "REQ-NET-008"
   ],
   "comp.conduit-charge": [
    "REQ-CHG-005"
   ],
   "comp.construct-patch": [
    "REQ-WRK-003"
   ],
   "comp.rescue-job": [
    "REQ-RES-002",
    "REQ-RES-003"
   ],
   "comp.ride-stats": [
    "REQ-RID-002",
    "REQ-RID-004"
   ],
   "comp.ride-driver": [
    "REQ-RID-002",
    "REQ-RID-003",
    "REQ-RID-005"
   ],
   "comp.engage": [
    "REQ-CMB-002",
    "REQ-CMB-003"
   ],
   "comp.patrol": [
    "REQ-CMB-005"
   ],
   "comp.siren": [
    "REQ-CMB-006",
    "REQ-REN-002"
   ],
   "comp.drafting": [
    "REQ-CMB-007"
   ]
  },
  "vers_for_req": {
   "REQ-SYS-001": [
    "VER-SYS-001"
   ],
   "REQ-SYS-002": [
    "VER-SYS-001"
   ],
   "REQ-SYS-003": [
    "VER-SYS-001"
   ],
   "REQ-SYS-004": [
    "VER-SYS-001"
   ],
   "REQ-SYS-005": [
    "VER-SYS-001"
   ],
   "REQ-SYS-006": [
    "VER-SYS-001"
   ],
   "REQ-SYS-007": [
    "VER-SYS-001",
    "VER-SYS-003"
   ],
   "REQ-SYS-008": [
    "VER-SYS-002"
   ],
   "REQ-SYS-009": [
    "VER-SYS-004"
   ],
   "REQ-STB-001": [
    "VER-STB-DOCK",
    "VER-STB-FULL"
   ],
   "REQ-PAW-001": [
    "VER-STB-DOCK"
   ],
   "REQ-PAW-004": [
    "VER-STB-DOCK",
    "VER-STB-CHARGE"
   ],
   "REQ-STB-002": [
    "VER-STB-FULL",
    "VER-STB-ASM"
   ],
   "REQ-STB-003": [
    "VER-STB-CHARGE",
    "VER-STB-POWER"
   ],
   "REQ-CHG-001": [
    "VER-STB-CHARGE",
    "VER-CHG-MAZE"
   ],
   "REQ-STB-004": [
    "VER-STB-FIRE",
    "VER-STB-UNINSTALL"
   ],
   "REQ-STB-005": [
    "VER-STB-OVERVIEW"
   ],
   "REQ-CMB-008": [
    "VER-STB-OVERVIEW"
   ],
   "REQ-CMB-009": [
    "VER-STB-OVERVIEW",
    "VER-CMB-T3",
    "VER-CMB-DEMO"
   ],
   "REQ-STB-006": [
    "VER-STB-CARRY"
   ],
   "REQ-STB-007": [
    "VER-STB-FIELD"
   ],
   "REQ-STB-008": [
    "VER-STB-WRECK"
   ],
   "REQ-STB-010": [
    "VER-STB-REMOVAL"
   ],
   "REQ-STB-009": [
    "VER-STB-ALERT"
   ],
   "REQ-REC-001": [
    "VER-STB-ALERT"
   ],
   "REQ-PAW-002": [
    "VER-PAW-DRAIN",
    "VER-WRK-RAID",
    "VER-RID-EMPTY"
   ],
   "REQ-PAW-003": [
    "VER-PAW-DRAIN"
   ],
   "REQ-NET-001": [
    "VER-NET-COORD"
   ],
   "REQ-NET-002": [
    "VER-NET-DISC",
    "VER-NET-SCAN"
   ],
   "REQ-NET-008": [
    "VER-NET-DISC",
    "VER-WRK-HAUL"
   ],
   "REQ-WRK-001": [
    "VER-NET-SCAN",
    "VER-WRK-HAUL",
    "VER-WRK-TWO"
   ],
   "REQ-NET-003": [
    "VER-NET-DEDUP"
   ],
   "REQ-NET-004": [
    "VER-NET-NEAR",
    "VER-WRK-TWO"
   ],
   "REQ-NET-005": [
    "VER-NET-DEATH"
   ],
   "REQ-NET-007": [
    "VER-NET-CLEAR"
   ],
   "REQ-NET-009": [
    "VER-NET-WAR",
    "VER-WRK-RAID"
   ],
   "REQ-WRK-004": [
    "VER-WRK-RAID"
   ],
   "REQ-NET-010": [
    "VER-NET-NOSTABLE",
    "VER-NET-MEDIC"
   ],
   "REQ-CMB-002": [
    "VER-NET-NOSTABLE"
   ],
   "REQ-RES-003": [
    "VER-NET-MEDIC",
    "VER-RES-MASS"
   ],
   "REQ-CHG-002": [
    "VER-CHG-SPOT",
    "VER-CHG-MAZE",
    "VER-CHG-ROOM"
   ],
   "REQ-CHG-005": [
    "VER-CHG-SPOT",
    "VER-CHG-ROOM",
    "VER-CHG-DRAIN"
   ],
   "REQ-CHG-003": [
    "VER-CHG-ROOM"
   ],
   "REQ-WRK-003": [
    "VER-WRK-HAUL"
   ],
   "REQ-RES-001": [
    "VER-RES-MASS",
    "VER-RES-ORDER"
   ],
   "REQ-RES-002": [
    "VER-RES-MASS",
    "VER-RES-ORDER"
   ],
   "REQ-RID-001": [
    "VER-RID-EMPTY",
    "VER-RID-DISMOUNT"
   ],
   "REQ-RID-002": [
    "VER-RID-EMPTY"
   ],
   "REQ-RID-003": [
    "VER-RID-EMPTY",
    "VER-RID-DISMOUNT"
   ],
   "REQ-RID-005": [
    "VER-RID-EMPTY",
    "VER-RID-DISMOUNT"
   ],
   "REQ-RID-004": [
    "VER-RID-POSE"
   ],
   "REQ-CMB-001": [
    "VER-CMB-PING"
   ],
   "REQ-CMB-003": [
    "VER-CMB-PING",
    "VER-CMB-SWEEP"
   ],
   "REQ-CMB-005": [
    "VER-CMB-SWEEP"
   ],
   "REQ-CMB-006": [
    "VER-CMB-RETREAT",
    "VER-CMB-DRAFT"
   ],
   "REQ-REN-002": [
    "VER-CMB-RETREAT",
    "VER-REN-RESCUE"
   ],
   "REQ-CMB-007": [
    "VER-CMB-DRAFT"
   ],
   "REQ-CMB-010": [
    "VER-CMB-ENDURE"
   ],
   "REQ-REN-001": [
    "VER-REN-ART",
    "VER-REN-WALK",
    "VER-REN-RESCUE",
    "VER-REN-COMBAT"
   ],
   "REQ-REN-003": [
    "VER-REN-RESCUE"
   ],
   "REQ-REC-002": [
    "VER-REC-KIDNAP"
   ],
   "REQ-REC-003": [
    "VER-REC-CARRIER"
   ]
  },
  "flows_for_node": {
   "comp.net-node": [
    "flow.dispatch",
    "flow.charge",
    "flow.combat"
   ],
   "comp.discovery": [
    "flow.dispatch"
   ],
   "comp.work-job": [
    "flow.dispatch"
   ],
   "ext.vanilla": [
    "flow.dispatch",
    "flow.combat"
   ],
   "comp.think-tree": [
    "flow.dispatch",
    "flow.charge",
    "flow.combat"
   ],
   "comp.survey": [
    "flow.dispatch"
   ],
   "comp.battery": [
    "flow.charge",
    "flow.combat"
   ],
   "comp.charge-decision": [
    "flow.charge"
   ],
   "comp.charge-spots": [
    "flow.charge"
   ],
   "comp.stable": [
    "flow.charge"
   ],
   "comp.conduit-charge": [
    "flow.charge"
   ],
   "comp.retrieval": [
    "flow.charge"
   ],
   "comp.ping": [
    "flow.combat"
   ],
   "comp.engage": [
    "flow.combat"
   ],
   "comp.tiers": [
    "flow.combat"
   ],
   "comp.patrol": [
    "flow.combat"
   ]
  },
  "children": {
   "sys.quadrunner": [
    "sub.stable",
    "sub.pawn",
    "sub.network",
    "sub.charging",
    "sub.work",
    "sub.rescue",
    "sub.riding",
    "sub.combat",
    "sub.render"
   ],
   "sub.stable": [
    "comp.stable",
    "comp.bills",
    "comp.overview",
    "comp.retrieval",
    "comp.fieldfix",
    "comp.mod-core"
   ],
   "sub.pawn": [
    "comp.race",
    "comp.battery",
    "comp.think-tree"
   ],
   "sub.network": [
    "comp.net-node",
    "comp.discovery",
    "comp.survey"
   ],
   "sub.charging": [
    "comp.charge-decision",
    "comp.charge-spots",
    "comp.conduit-charge"
   ],
   "sub.work": [
    "comp.work-job",
    "comp.construct-patch",
    "comp.raid-part"
   ],
   "sub.rescue": [
    "comp.triage",
    "comp.rescue-job"
   ],
   "sub.riding": [
    "comp.ride-order",
    "comp.ride-driver",
    "comp.ride-stats"
   ],
   "sub.combat": [
    "comp.ping",
    "comp.engage",
    "comp.patrol",
    "comp.drafting",
    "comp.tiers"
   ],
   "sub.render": [
    "comp.render-tree",
    "comp.pose",
    "comp.gait",
    "comp.siren"
   ]
  },
  "page_for": {
   "sys.quadrunner": "index.html",
   "sub.stable": "sub--sub-stable.html",
   "sub.pawn": "sub--sub-pawn.html",
   "sub.network": "sub--sub-network.html",
   "sub.charging": "sub--sub-charging.html",
   "sub.work": "sub--sub-work.html",
   "sub.rescue": "sub--sub-rescue.html",
   "sub.riding": "sub--sub-riding.html",
   "sub.combat": "sub--sub-combat.html",
   "sub.render": "sub--sub-render.html",
   "comp.stable": "sub--sub-stable.html",
   "comp.bills": "sub--sub-stable.html",
   "comp.overview": "sub--sub-stable.html",
   "comp.retrieval": "sub--sub-stable.html",
   "comp.fieldfix": "sub--sub-stable.html",
   "comp.mod-core": "sub--sub-stable.html",
   "comp.race": "sub--sub-pawn.html",
   "comp.battery": "sub--sub-pawn.html",
   "comp.think-tree": "sub--sub-pawn.html",
   "comp.net-node": "sub--sub-network.html",
   "comp.discovery": "sub--sub-network.html",
   "comp.survey": "sub--sub-network.html",
   "comp.charge-decision": "sub--sub-charging.html",
   "comp.charge-spots": "sub--sub-charging.html",
   "comp.conduit-charge": "sub--sub-charging.html",
   "comp.work-job": "sub--sub-work.html",
   "comp.construct-patch": "sub--sub-work.html",
   "comp.raid-part": "sub--sub-work.html",
   "comp.triage": "sub--sub-rescue.html",
   "comp.rescue-job": "sub--sub-rescue.html",
   "comp.ride-order": "sub--sub-riding.html",
   "comp.ride-driver": "sub--sub-riding.html",
   "comp.ride-stats": "sub--sub-riding.html",
   "comp.ping": "sub--sub-combat.html",
   "comp.engage": "sub--sub-combat.html",
   "comp.patrol": "sub--sub-combat.html",
   "comp.drafting": "sub--sub-combat.html",
   "comp.tiers": "sub--sub-combat.html",
   "comp.render-tree": "sub--sub-render.html",
   "comp.pose": "sub--sub-render.html",
   "comp.gait": "sub--sub-render.html",
   "comp.siren": "sub--sub-render.html",
   "actor.player": "index.html",
   "ext.vanilla": "index.html",
   "ext.harmony": "index.html",
   "ext.harness": "index.html",
   "REQ-SYS-001": "traceability.html",
   "REQ-SYS-002": "traceability.html",
   "REQ-SYS-003": "traceability.html",
   "REQ-SYS-004": "traceability.html",
   "REQ-SYS-005": "traceability.html",
   "REQ-SYS-006": "traceability.html",
   "REQ-SYS-007": "traceability.html",
   "REQ-SYS-008": "traceability.html",
   "REQ-SYS-009": "traceability.html",
   "REQ-STB-001": "traceability.html",
   "REQ-STB-004": "traceability.html",
   "REQ-STB-006": "traceability.html",
   "REQ-STB-009": "traceability.html",
   "REQ-STB-010": "traceability.html",
   "REQ-PAW-001": "traceability.html",
   "REQ-PAW-002": "traceability.html",
   "REQ-NET-001": "traceability.html",
   "REQ-NET-002": "traceability.html",
   "REQ-NET-009": "traceability.html",
   "REQ-CHG-001": "traceability.html",
   "REQ-CHG-002": "traceability.html",
   "REQ-WRK-001": "traceability.html",
   "REQ-WRK-004": "traceability.html",
   "REQ-RES-001": "traceability.html",
   "REQ-RID-001": "traceability.html",
   "REQ-CMB-001": "traceability.html",
   "REQ-CMB-008": "traceability.html",
   "REQ-REN-001": "traceability.html",
   "REQ-STB-002": "traceability.html",
   "REQ-STB-003": "traceability.html",
   "REQ-STB-005": "traceability.html",
   "REQ-STB-007": "traceability.html",
   "REQ-STB-008": "traceability.html",
   "REQ-REC-001": "traceability.html",
   "REQ-REC-002": "traceability.html",
   "REQ-REC-003": "traceability.html",
   "REQ-REC-004": "traceability.html",
   "REQ-PAW-003": "traceability.html",
   "REQ-PAW-004": "traceability.html",
   "REQ-NET-003": "traceability.html",
   "REQ-NET-004": "traceability.html",
   "REQ-NET-005": "traceability.html",
   "REQ-NET-006": "traceability.html",
   "REQ-NET-007": "traceability.html",
   "REQ-NET-008": "traceability.html",
   "REQ-NET-010": "traceability.html",
   "REQ-CHG-003": "traceability.html",
   "REQ-CHG-004": "traceability.html",
   "REQ-CHG-005": "traceability.html",
   "REQ-WRK-002": "traceability.html",
   "REQ-WRK-003": "traceability.html",
   "REQ-RES-002": "traceability.html",
   "REQ-RES-003": "traceability.html",
   "REQ-RID-002": "traceability.html",
   "REQ-RID-003": "traceability.html",
   "REQ-RID-004": "traceability.html",
   "REQ-RID-005": "traceability.html",
   "REQ-CMB-002": "traceability.html",
   "REQ-CMB-003": "traceability.html",
   "REQ-CMB-005": "traceability.html",
   "REQ-CMB-006": "traceability.html",
   "REQ-CMB-007": "traceability.html",
   "REQ-CMB-009": "traceability.html",
   "REQ-CMB-010": "traceability.html",
   "REQ-REN-002": "traceability.html",
   "REQ-REN-003": "traceability.html",
   "VER-SYS-001": "traceability.html",
   "VER-SYS-002": "traceability.html",
   "VER-SYS-003": "traceability.html",
   "VER-SYS-004": "traceability.html",
   "VER-STB-DOCK": "traceability.html",
   "VER-STB-FULL": "traceability.html",
   "VER-STB-ASM": "traceability.html",
   "VER-STB-CHARGE": "traceability.html",
   "VER-STB-POWER": "traceability.html",
   "VER-STB-FIRE": "traceability.html",
   "VER-STB-UNINSTALL": "traceability.html",
   "VER-STB-OVERVIEW": "traceability.html",
   "VER-STB-CARRY": "traceability.html",
   "VER-STB-FIELD": "traceability.html",
   "VER-STB-WRECK": "traceability.html",
   "VER-STB-REMOVAL": "traceability.html",
   "VER-STB-ALERT": "traceability.html",
   "VER-PAW-DRAIN": "traceability.html",
   "VER-NET-COORD": "traceability.html",
   "VER-NET-DISC": "traceability.html",
   "VER-NET-SCAN": "traceability.html",
   "VER-NET-DEDUP": "traceability.html",
   "VER-NET-NEAR": "traceability.html",
   "VER-NET-DEATH": "traceability.html",
   "VER-NET-CLEAR": "traceability.html",
   "VER-NET-WAR": "traceability.html",
   "VER-WRK-RAID": "traceability.html",
   "VER-NET-NOSTABLE": "traceability.html",
   "VER-NET-MEDIC": "traceability.html",
   "VER-CHG-SPOT": "traceability.html",
   "VER-CHG-MAZE": "traceability.html",
   "VER-CHG-ROOM": "traceability.html",
   "VER-CHG-DRAIN": "traceability.html",
   "VER-WRK-HAUL": "traceability.html",
   "VER-WRK-TWO": "traceability.html",
   "VER-RES-MASS": "traceability.html",
   "VER-RES-ORDER": "traceability.html",
   "VER-RID-EMPTY": "traceability.html",
   "VER-RID-DISMOUNT": "traceability.html",
   "VER-RID-POSE": "traceability.html",
   "VER-CMB-PING": "traceability.html",
   "VER-CMB-SWEEP": "traceability.html",
   "VER-CMB-RETREAT": "traceability.html",
   "VER-CMB-DRAFT": "traceability.html",
   "VER-CMB-T3": "traceability.html",
   "VER-CMB-DEMO": "traceability.html",
   "VER-CMB-ENDURE": "traceability.html",
   "VER-REN-ART": "traceability.html",
   "VER-REN-WALK": "traceability.html",
   "VER-REN-RESCUE": "traceability.html",
   "VER-REN-COMBAT": "traceability.html",
   "VER-REC-KIDNAP": "traceability.html",
   "VER-REC-CARRIER": "traceability.html",
   "flow.dispatch": "flow--flow-dispatch.html",
   "flow.charge": "flow--flow-charge.html",
   "flow.combat": "flow--flow-combat.html"
  }
 }
};
