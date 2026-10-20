using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        /// <summary>
        /// Six successful ordinary fist hits to the previously fragile thorax, without repairs, across a reload.
        /// This is controlled damage pressure, not a claim that a dog wins every three-fight encounter.
        /// </summary>
        private static void CombatdogEndurance()
        {
            var s = new Scenario("Combatdog endurance", 6000);
            string dogId = null, attackerId = null;
            float injuryBeforeLoad = 0f;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            void Strike(int index)
            {
                Pawn dog = Dog();
                if (dog == null || dog.Dead)
                    return;
                Pawn attacker = (Pawn)Scenario.ById(attackerId);
                float power = attacker.def.tools.First(t => t.label == "left fist").power;
                Rand.PushState(9070 + index);
                try
                {
                    dog.TakeDamage(new DamageInfo(DamageDefOf.Blunt, power, angle: 0f, instigator: attacker,
                        hitPart: dog.RaceProps.body.corePart, weapon: attacker.def));
                }
                finally
                {
                    Rand.PopState();
                }
                QR.Log($"endurance hit {index + 1}: {power:F1} raw blunt; "
                    + $"thorax {dog.health.hediffSet.GetPartHealth(dog.RaceProps.body.corePart):F1}, alive {!dog.Dead}");
                if (dog.Dead && dog.Corpse != null)
                    s.Track(dog.Corpse);
            }
            s.Do("a fresh Tier 1 dog, no stable to repair it, and a human attacker", () =>
                {
                    DraftColonists(s);
                    Pawn dog = SpawnPawn(s, QR_DefOf.QR_Combatdog, Faction.OfPlayer, FreeCellNearCenter(Find.CurrentMap));
                    dogId = dog.ThingID;
                    Pawn attacker = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First(p => !p.Downed);
                    attackerId = attacker.ThingID;
                    Strike(0);
                    Strike(1);
                    Strike(2);
                    injuryBeforeLoad = InjurySeverity(dog);
                })
                .Expect("standing after the first three hits", () => Dog() is Pawn dog && !dog.Dead && !dog.Downed)
                .SaveAndReload()
                .Expect("damage persisted without a repair", () => Dog() is Pawn dog && InjurySeverity(dog) >= injuryBeforeLoad - 0.01f)
                .Do("three more fist hits without repair", () =>
                {
                    Strike(3);
                    Strike(4);
                    Strike(5);
                })
                .Expect("still standing after six successful ordinary fist hits", () => Dog() is Pawn dog && !dog.Dead && !dog.Downed)
                .Summary(() => Dog() is Pawn dog ? $"health {dog.health.summaryHealth.SummaryHealthPercent:P0}, "
                    + $"thorax {dog.health.hediffSet.GetPartHealth(dog.RaceProps.body.corePart):F1}; no healing between hits"
                    : "dog killed by the controlled six-hit pressure; no healing between hits")
                .Start();
        }
    }
}
