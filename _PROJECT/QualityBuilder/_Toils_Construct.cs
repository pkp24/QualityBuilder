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
    [HarmonyPatch(typeof(Toils_Construct), "MakeSolidThingFromBlueprintIfNecessary")]
    public static class _Toils_Construct
    {
        public static bool Prefix(ref Toil __result, TargetIndex blueTarget, TargetIndex targetToUpdate = TargetIndex.None)
        {
            Toil result = new Toil();
            result.initAction = delegate
            {
                Pawn actor = result.actor;
                Job curJob = actor.jobs.curJob;
                Blueprint blueprint = curJob.GetTarget(blueTarget).Thing as Blueprint;
                if (blueprint == null)
                    return;
                try
                {
                    bool flag = targetToUpdate != TargetIndex.None && curJob.GetTarget(targetToUpdate).Thing == blueprint;
                    Thing thing;
                    bool flag2;
                    if (!blueprint.TryReplaceWithSolidThing(actor, out thing, out flag2))
                        return;

                    CompQualityBuilder cmpBlueprint = QualityBuilder.getCompQualityBuilder(blueprint);
                    bool hasDesignation = QualityBuilder.hasDesignation(curJob.GetTarget(blueTarget).Thing);
                    if (cmpBlueprint != null)
                    {
                        CompQualityBuilder cmpFrame = QualityBuilder.getCompQualityBuilder(thing);
                        cmpFrame.desiredMinQuality = cmpBlueprint.desiredMinQuality;
                        if (!QualityBuilder.hasDesignation(thing))
                            QualityBuilder.setSkilled(thing, cmpFrame.desiredMinQuality, cmpBlueprint.isSkilled);
                    }
                    if (!hasDesignation || _WorkGiver_ConstructFinishFrames.isPawnGoodEnoughToBuild(actor))
                    {
                        curJob.SetTarget(blueTarget, thing);
                        if (flag)
                            curJob.SetTarget(targetToUpdate, thing);
                        if (thing is Frame)
                            actor.Reserve(thing, curJob, 1, -1, null);
                    }
                }catch(Exception ex)
                {
                    Log.Error("QualityBuilder: Error in construct toil.");
                    Log.Error(ex.ToString());
                }
            };
            __result = result;
            return false;
        }
    }
}
