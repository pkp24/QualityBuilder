using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace QualityBuilder
{
    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), "JobOnThing")]
    public class _WorkGiver_ConstructFinishFrames
	{
        public static void Postfix(ref Job __result, Pawn pawn, Thing t, bool forced = false)
		{
            if (!forced && QualityBuilder.hasDesignation(t))
            {
                if (!isPawnGoodEnoughToBuild(pawn))
                    __result = null;
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
            if (overridePawn != null)
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
