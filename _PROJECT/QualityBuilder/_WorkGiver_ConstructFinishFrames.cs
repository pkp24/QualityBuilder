using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace QualityBuilder
{
    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), "JobOnThing")]
    public class _WorkGiver_ConstructFinishFrames
	{
        public static void Postfix(ref Job __result, Pawn pawn, Thing t, bool forced = false)
		{
            if (__result == null || !QualityBuilder.hasDesignation(t))
                return;
            // Only gate the actual FinishFrame job. JobOnThing can also return a
            // blocker-handling job (haul/deconstruct the thing blocking the frame), which any
            // pawn may do regardless of construction skill.
            if (__result.def != JobDefOf.FinishFrame)
                return;
            if (!forced && !isPawnGoodEnoughToBuild(pawn))
            {
                __result = null;
                return;
            }
            // The pawn is allowed to finish this quality frame. A low-skill pawn builds much
            // slower, so if one is already constructing this same frame (job started before
            // the designation, or via a race), kick them off so the better builder takes over.
            // Player-forced builders are never kicked. Never kick from the forced/float-menu
            // path (pure menu generation must not interrupt anyone), and only when the
            // scanning pawn is qualified (checked above) and plausibly able to take the job
            // right now — JobOnThing also runs on pure HasJobOnThing scans.
            if (!forced && pawn.Map == t.Map && !pawn.Downed && !pawn.Drafted)
                kickUnqualifiedBuilders(t, pawn);
		}

        // Per-frame kick cooldown so repeated WorkGiver scans don't re-kick the same frame
        // every scan tick. thingIDNumber -> last kick tick; cleared when it grows.
        private static readonly Dictionary<int, int> lastKickTick = new Dictionary<int, int>();
        private const int kickCooldownTicks = 600;

        private static void kickUnqualifiedBuilders(Thing frame, Pawn replacement)
        {
            Map map = frame.Map;
            if (map == null)
                return;
            int nowTick = Find.TickManager.TicksGame;
            int lastTick;
            if (lastKickTick.TryGetValue(frame.thingIDNumber, out lastTick) && nowTick - lastTick < kickCooldownTicks)
                return;
            // Snapshot: PawnsInFaction returns a shared buffer that gets cleared and refilled
            // by other calls (e.g. the best-constructor lookup in isPawnGoodEnoughToBuild
            // below), so copy it before iterating to avoid the list mutating mid-loop.
            List<Pawn> pawns = new List<Pawn>(map.mapPawns.PawnsInFaction(Faction.OfPlayer));
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == replacement || p.jobs == null)
                    continue;
                Job job = p.jobs.curJob;
                if (job == null || job.def != JobDefOf.FinishFrame || job.targetA.Thing != frame)
                    continue;
                if (job.playerForced || isPawnGoodEnoughToBuild(p))
                    continue;
                // startNewJob:false — don't synchronously re-run the kicked pawn's job search
                // from inside this WorkGiver scan; they'll pick up new work on the next tick.
                p.jobs.EndCurrentJob(JobCondition.InterruptForced, false, true);
                if (lastKickTick.Count > 256)
                    lastKickTick.Clear();
                lastKickTick[frame.thingIDNumber] = nowTick;
            }
        }

        public static bool isPawnGoodEnoughToBuild(Pawn pawn)
        {
            if (!QualityBuilder.pawnCanConstruct(pawn))
            {
#if DEBUG
                Log.Message("Pawn " + pawn.Name.ToStringShort + " cant construct");
#endif
                return false;
            }
            Map curMap = pawn.Map;
            // Resolve the map settings once instead of re-scanning the map's components per field.
            QualityBuilderModSettings settings = QualityBuilderModSettings.getSettings(curMap);
            Pawn overridePawn = settings.bestConstructorOverride;
            // A dead/gone/off-map/incapable override must not block all quality construction
            if (overridePawn != null && overridePawn.Spawned && overridePawn.Map == curMap && !overridePawn.Dead && QualityBuilder.pawnCanConstruct(overridePawn))
                return pawn == overridePawn;
            int curPawnLevel = QualityBuilder.getPawnConstructionSkill(pawn);
            if (settings.ignoreQualityBuilderAtSkill > curPawnLevel)
            {
                int bestConstructorSkill = settings.getBestConstructionSkillCached(curMap);
                if (bestConstructorSkill <= 0)
                {
#if DEBUG
                    Log.Message("Pawn " + pawn.Name.ToStringShort + " is good cause no best constructor could be determined");
#endif
                    return true;
                }
                int targetLevel = bestConstructorSkill - settings.skillDifferenceFromBestBuilder;
                if (targetLevel < 0)
                    targetLevel = 0;
                if (curPawnLevel < targetLevel)
                {
#if DEBUG
                    Log.Message("Pawn " + pawn.Name.ToStringShort + " is not good enough with skill " + curPawnLevel);
#endif
                    return false;
                }
            }
            return true;
        }
    }
}
