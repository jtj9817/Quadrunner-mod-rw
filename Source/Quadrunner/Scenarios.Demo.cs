using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>Weapon demos: each upgraded Combatdog tier fights raiders with every gun it gets, at normal speed.</summary>
    public static partial class Scenarios
    {
        /// <summary>One wave of a demo: <see cref="raiders"/> raiders <see cref="minDist"/>–<see cref="maxDist"/> cells from the dog, fought with <see cref="gun"/>.</summary>
        private struct DemoRound
        {
            public ThingDef gun;
            public int raiders;
            public float minDist, maxDist;

            public DemoRound(ThingDef gun, int raiders, float minDist, float maxDist)
            {
                this.gun = gun;
                this.raiders = raiders;
                this.minDist = minDist;
                this.maxDist = maxDist;
            }
        }

        /// <summary>Raiders this far from the stable are beyond the drafted colonists' guns (about 25 cells).</summary>
        private const float OutOfColonistRange = 32f;

        /// <summary>The camera on <paramref name="dog"/> and <paramref name="target"/>, both on screen with a cell to spare.</summary>
        private static void Frame(Pawn dog, Thing target)
        {
            Vector3 a = dog.Position.ToVector3Shifted(), b = (target ?? dog).Position.ToVector3Shifted();
            // The root size is half the view's height in cells; the view is 16:9.
            float size = Mathf.Max(4f, Mathf.Abs(a.z - b.z) / 2f + 2f, Mathf.Abs(a.x - b.x) / 2f / (16f / 9f) + 2f);
            Find.CameraDriver.SetRootPosAndSize((a + b) / 2f, size);
        }

        /// <summary>
        /// A Combatdog upgraded from Tier 1 to <paramref name="tier"/> at its stable, out on a bare field 35–50 cells away
        /// (beyond the drafted colonists' guns). Each round puts it back in the field center, picks the round's gun for
        /// slot 1 if that is where it sits, spawns raiders beside it and pings the stable; the dog shoots every raider with that gun
        /// until all are down, and a screenshot catches a burst in flight.
        /// </summary>
        private static void WeaponDemo(int tier, params DemoRound[] rounds)
        {
            var s = new Scenario($"Tier {tier} weapon demo", 6000 * rounds.Length);
            string stableId = null, dogId = null, name = null;
            var raiderIds = new List<string>();
            var shotAt = new HashSet<string>();
            var times = new List<string>();
            CellRect field = default;
            int roundStart = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            IEnumerable<Pawn> Raiders() => raiderIds.Select(id => Scenario.ById(id) as Pawn).Where(p => p != null);
            Verb Firing() => Dog()?.equipment?.PrimaryEq?.PrimaryVerb is Verb v && v.Bursting ? v : null;
            void Sample(ThingDef gun)
            {
                Pawn dog = Dog();
                if (dog?.Spawned == true && dog.CurJobDef == JobDefOf.AttackStatic && Held(dog) == gun
                    && dog.CurJob.targetA.Thing is Pawn target && raiderIds.Contains(target.ThingID))
                    shotAt.Add(target.ThingID);
            }
            s.Do($"a powered stable with a Combatdog upgraded to Tier {tier}; a bare field 35–50 cells out", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn dog = stable.AddNewDog(QR_DefOf.QR_Combatdog);
                    name = dog.LabelShort;
                    for (int t = 1; t < tier; t++)
                        dog = stable.Upgrade(dog) ?? throw new InvalidOperationException($"couldn't upgrade past Tier {t}");
                    dogId = s.Track(dog).ThingID;
                    field = ClearRect(map, stable.InteractionCell, 15, 1, 35f, 50f);
                    if (!stable.TryUndock(dog))
                        throw new InvalidOperationException($"couldn't undock {dog}");
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    CloseUpCaptures(s);
                    // The flamethrower sets things alight: no fire outlives the scenario.
                    s.OnFinish(() =>
                    {
                        foreach (Thing fire in Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList())
                            fire.Destroy();
                    });
                })
                .Expect($"Tier {tier}, same name, holding its first gun", () =>
                    Dog() is Pawn dog && CombatTier.Of(dog)?.tier == tier && dog.LabelShort == name
                    && Held(dog) == Tiers.Gun(dog, 0));
            foreach (DemoRound round in rounds)
            {
                string gunName = round.gun.label.Replace("built-in ", "");
                s.Do($"{round.raiders} raider(s) {round.minDist:F0}–{round.maxDist:F0} cells from the dog, for its {gunName}", () =>
                    {
                        Map map = Find.CurrentMap;
                        Pawn dog = Dog();
                        Teleport(dog, field.CenterCell);
                        if (Tiers.Gun(dog, 1) != round.gun && Tiers.Gun(dog, 0) != round.gun)
                            Tiers.PickGun(dog, 0, round.gun);
                        raiderIds.Clear();
                        shotAt.Clear();
                        // Beside the dog, not above or below it: the view is wider than tall, so the camera can come closer.
                        IntVec3 first = OpenCell(map, dog.Position, round.minDist, round.maxDist,
                            c => c.DistanceTo(Stable().InteractionCell) > OutOfColonistRange && Math.Abs(c.z - dog.Position.z) <= 3);
                        for (int i = 0; i < round.raiders; i++)
                        {
                            IntVec3 at = i == 0 ? first : OpenCell(map, first, 1.5f, 3f, c =>
                                c.DistanceTo(Stable().InteractionCell) > OutOfColonistRange && c.DistanceTo(dog.Position) >= round.minDist
                                && c.GetFirstPawn(map) == null);
                            raiderIds.Add(SpawnRaider(s, at).ThingID);
                        }
                        Raid.Recheck(map);
                        foreach (Pawn raider in Raiders())
                            Net().Ping(raider, dog);
                        roundStart = Find.TickManager.TicksGame;
                        Frame(dog, Raiders().First());
                    });
                CaptureWhen(s, $"demo-t{tier}-{round.gun.defName.Replace("QR_Gun_", "").ToLowerInvariant()}", () =>
                {
                    Sample(round.gun);
                    return Firing() is Verb v && v.EquipmentSource?.def == round.gun
                        && Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile)
                            .Any(p => ((Projectile)p).Launcher == Dog());
                }, () => Frame(Dog(), Dog().CurJob?.targetA.Thing));
                s.Expect($"every raider shot with the {gunName} and down", () =>
                    {
                        Sample(round.gun);
                        return Raiders().All(r => r.Dead || r.Downed);
                    })
                    .Do($"checked: the {gunName} hit them all", () =>
                    {
                        // A raider downed by a burst aimed at its neighbour, or by the fire, was never targeted itself.
                        if (shotAt.Count == 0 || round.raiders == 1 && shotAt.Count != 1)
                            throw new InvalidOperationException($"shot at {shotAt.Count} of {round.raiders} with the {gunName}");
                        times.Add($"{gunName}: {round.raiders} raider(s) down in {(Find.TickManager.TicksGame - roundStart) / 60f:F1} s, " +
                            $"{shotAt.Count} targeted, {string.Join("/", raiderIds.Select(Fate))}");
                    });
            }
            s.Summary(() => $"{Dog()?.def.defName} {name}: {string.Join("; ", times)}; loose guns {LooseGuns()}; " +
                    "inspect .devlog/screens/demo-*.png")
                .Start();
        }
    }
}
