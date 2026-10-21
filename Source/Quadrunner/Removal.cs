using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>Explicit, confirmed cutover to a new save without Quadrunner content. Never runs on ordinary saves.</summary>
    internal static class Removal
    {
        internal static void Confirm()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Prepare this colony for removing Quadrunner?\n\nAn untouched backup is saved first. All Quadrunner dogs, "
                + "wrecks, stables, field-fix items and construction orders are then removed from every map and the world. "
                + "Riders, patients and other vanilla cargo are released safely; injuries are not healed. No materials are refunded.\n\n"
                + "A separate cleaned save is created. Quit without saving again, disable Quadrunner, then load that cleaned save. "
                + "Keep the backup with Quadrunner enabled if you change your mind.", () =>
                {
                    string name = "QR_removed_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff");
                    try
                    {
                        string backup = PrepareSave(name);
                        Find.WindowStack.Add(new Dialog_MessageBox($"Cleaned save: {name}\nBackup: {backup}\n\n"
                            + "Quit without saving again, disable Quadrunner, and load the cleaned save. Accept the expected mod-list warning."));
                    }
                    catch (Exception e)
                    {
                        QR.Error("removal preparation failed: " + e);
                        Find.WindowStack.Add(new Dialog_MessageBox("Removal preparation failed. Do not disable Quadrunner. "
                            + "Reload the QR_before_removal backup to restore the colony; see the log for details."));
                    }
                }, destructive: true, title: "Prepare for Quadrunner removal"));
        }

        internal static string PrepareSave(string cleanName)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            string backup = "QR_before_removal_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff");
            GameDataSaveLoader.SaveGame(backup);
            if (!File.Exists(GenFilePaths.FilePathForSavedGame(backup)))
                throw new IOException("The backup was not written; nothing has been removed.");

            List<Thing> things = AllThings();
            List<Pawn> pawns = things.OfType<Pawn>().ToList();
            foreach (Pawn pawn in pawns)
            {
                if (CompRobodog.IsRobodog(pawn) || pawn.jobs?.AllJobs().Any(UsesMod) == true
                    || IsModThing(pawn.carryTracker?.CarriedThing))
                    pawn.jobs?.StopAll();
                // A surviving wound may name a removed built-in gun or claw. Keep the wound, only forget its source.
                foreach (Hediff h in pawn.health.hediffSet.hediffs.ToList())
                {
                    if (Owned(h.def))
                        pawn.health.RemoveHediff(h);
                    else
                    {
                        if (Owned(h.sourceDef)) h.sourceDef = null;
                        if (Owned(h.sourceHediffDef)) h.sourceHediffDef = null;
                        if (Owned(h.sourceBodyPartGroup)) h.sourceBodyPartGroup = null;
                    }
                }
            }
            // Do not use DropAndForbidEverything here: a dock's typed pawn container cannot accept item cargo.
            foreach (Pawn dog in pawns.Where(CompRobodog.IsRobodog))
            {
                ReleaseContents(dog, dog.carryTracker?.innerContainer);
                ReleaseContents(dog, dog.inventory?.innerContainer);
                ReleaseContents(dog, dog.equipment?.GetDirectlyHeldThings());
            }
            foreach (Pawn dog in pawns.Where(CompRobodog.IsRobodog))
            {
                dog.holdingOwner?.Remove(dog);
                if (!dog.Destroyed) dog.Destroy();
                if (Find.WorldPawns.Contains(dog))
                    Find.WorldPawns.RemoveAndDiscardPawnViaGC(dog);
                else if (!dog.Discarded)
                    dog.Discard(silentlyRemoveReferences: true);
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans.ToList())
                if (caravan.PawnsListForReading.Count == 0) caravan.Destroy();
            foreach (Thing thing in things.Where(t => !(t is Pawn) && IsModThing(t)))
            {
                thing.holdingOwner?.Remove(thing);
                if (!thing.Destroyed) thing.Destroy();
            }
            ChargeSpots.ClearAll();
            DogNetwork.ClearAll();
            Find.Selector.ClearSelection();
            string path = GenFilePaths.FilePathForSavedGame(cleanName);
            // Only this explicitly requested export is overwritten, never the original colony save or its backup.
            if (File.Exists(path)) File.Delete(path);
            GameDataSaveLoader.SaveGame(cleanName);
            if (!File.Exists(path)) throw new IOException("The cleaned save was not written.");
            CleanExportReferences(path);
            QR.Log($"removal prepared: {cleanName}; untouched backup {backup}");
            return backup;
        }

        internal static List<Thing> AllThings()
        {
            var things = new HashSet<Thing>();
            var contents = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), contents);
                things.UnionWith(contents);
            }
            foreach (IThingHolder holder in Find.WorldObjects.AllWorldObjects.OfType<IThingHolder>())
            {
                ThingOwnerUtility.GetAllThingsRecursively(holder, contents);
                things.UnionWith(contents);
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToList())
            {
                things.Add(pawn);
                ThingOwnerUtility.GetAllThingsRecursively(pawn, contents);
                things.UnionWith(contents);
            }
            return things.ToList();
        }

        private static void ReleaseContents(Pawn dog, ThingOwner contents)
        {
            if (contents == null) return;
            foreach (Thing item in contents.ToList())
            {
                if (IsModThing(item)) continue;
                if (dog.MapHeld is Map map)
                {
                    if (!contents.TryDrop(item, dog.PositionHeld, map, ThingPlaceMode.Near, out _))
                        throw new InvalidOperationException($"Could not release {item.LabelCap} near {dog.LabelShort}.");
                    continue;
                }
                Caravan caravan = dog.GetCaravan();
                if (caravan != null && item is Pawn patient)
                {
                    contents.Remove(patient);
                    caravan.AddPawn(patient, addCarriedPawnToWorldPawnsIfAny: true);
                    if (!caravan.ContainsPawn(patient))
                        throw new InvalidOperationException($"Could not release {patient.LabelShort} into its caravan.");
                    if (!patient.IsWorldPawn()) Find.WorldPawns.PassToWorld(patient);
                    continue;
                }
                Pawn recipient = caravan?.PawnsListForReading.FirstOrDefault(p =>
                    !CompRobodog.IsRobodog(p) && p.inventory != null);
                if (recipient == null || !contents.TryTransferToContainer(item, recipient.inventory.innerContainer,
                    canMergeWithExistingStacks: false))
                    throw new InvalidOperationException($"Bring {dog.LabelShort}'s vanilla cargo onto a map before removing Quadrunner.");
            }
        }

        private static bool Owned(Def def) => def?.modContentPack?.PackageId == QR.PackageId;

        private static bool IsModThing(Thing thing) => thing != null && (Owned(thing.def)
            || thing is Corpse corpse && CompRobodog.IsRobodog(corpse.InnerPawn)
            || thing is MinifiedThing minified && IsModThing(minified.InnerThing)
            || thing is Blueprint blueprint && Owned(blueprint.def.entityDefToBuild)
            || thing is Frame frame && Owned(frame.def.entityDefToBuild)
            || thing is Projectile shot && (Owned(shot.EquipmentDef) || Owned(shot.Launcher?.def)
                || Owned(shot.usedTarget.Thing?.def) || Owned(shot.intendedTarget.Thing?.def)));

        private static bool UsesMod(Job job) => Owned(job.def) || IsModThing(job.targetA.Thing)
            || IsModThing(job.targetB.Thing) || IsModThing(job.targetC.Thing)
            || job.targetQueueA?.Any(t => IsModThing(t.Thing)) == true
            || job.targetQueueB?.Any(t => IsModThing(t.Thing)) == true;

        private static void CleanExportReferences(string path)
        {
            var names = new HashSet<string>(LoadedModManager.RunningMods
                .Where(m => m.PackageId == QR.PackageId).SelectMany(m => m.AllDefs).Select(d => d.defName));
            names.UnionWith(DefDatabase<ThingDef>.AllDefs.Where(Owned).Select(d => d.defName));
            var doc = new XmlDocument();
            doc.Load(path);
            // Vanilla filters serialize every allowed ThingDef, including mod items never owned by the colony.
            // Change only those list entries, not arbitrary XML values or surviving vanilla objects.
            foreach (XmlNode node in doc.SelectNodes("//allowedDefs/li").Cast<XmlNode>().ToList())
                if (names.Contains(node.InnerText)) node.ParentNode.RemoveChild(node);
            // Core battle records can outlive their launcher. Keep the record and its load ID (wounds may refer
            // to it), but clear optional ThingDef metadata whose content is being removed. Core handles these nulls.
            foreach (XmlNode node in doc.SelectNodes("//battleLog/battles/li/entries/li/*").Cast<XmlNode>())
                if ((node.Name == "weaponDef" || node.Name == "projectileDef" || node.Name == "coverDef"
                        || node.Name == "recipientThing" || node.Name == "originalTargetThing" || node.Name == "ownerDef")
                    && names.Contains(node.InnerText))
                    node.InnerText = "null";
            foreach (XmlNode node in doc.SelectNodes("//*").Cast<XmlNode>())
            {
                if (node.Attributes?["Class"]?.Value.StartsWith("Quadrunner.", StringComparison.Ordinal) == true
                    || !node.HasChildNodes && names.Contains(node.InnerText)
                    || node.ChildNodes.Count == 1 && node.FirstChild.NodeType == XmlNodeType.Text && names.Contains(node.InnerText))
                    throw new InvalidOperationException($"The export still contains Quadrunner data at {node.Name}; keep the mod enabled.");
            }
            string temp = path + ".cleaning";
            doc.Save(temp);
            File.Replace(temp, path, null);
        }
    }
}
