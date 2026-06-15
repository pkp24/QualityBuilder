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
            if (!QualityBuilder.hasDesignation(t))
                return;
            if (!forced && !isPawnGoodEnoughToBuild(pawn))
            {
                __result = null;
                return;
            }
            // The pawn is allowed to finish this quality frame (qualified, or player forced).
            // A low-skill pawn builds much slower, so if one is already constructing this same
            // frame (job started before the designation, or via a race), kick them off so the
            // better builder takes over. Player-forced builders are never kicked.
            if (__result != null)
                kickUnqualifiedBuilders(t, pawn);
		}

        private static void kickUnqualifiedBuilders(Thing frame, Pawn replacement)
        {
            Map map = frame.Map;
            if (map == null)
                return;
            List<Pawn> pawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
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
                p.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
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
            Pawn overridePawn = QualityBuilderModSettings.getBestConstructorOverride(curMap);
            // A dead/gone/incapable override must not block all quality construction
            if (overridePawn != null && !overridePawn.Dead && !overridePawn.Destroyed && QualityBuilder.pawnCanConstruct(overridePawn))
                return pawn == overridePawn;
            int curPawnLevel = QualityBuilder.getPawnConstructionSkill(pawn);
            if (QualityBuilderModSettings.getIgnoreQualityBuilderAtSkill(curMap) > curPawnLevel)
            {
                int bestConstructorSkill = QualityBuilderModSettings.getBestConstructionSkill(curMap);
                if (bestConstructorSkill <= 0)
                {
#if DEBUG
                    Log.Message("Pawn " + pawn.Name.ToStringShort + " is good cause no best constructor could be determined");
#endif
                    return true;
                }
                int targetLevel = bestConstructorSkill - QualityBuilderModSettings.getSkillDifferenceFromBestBuilder(curMap);
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
