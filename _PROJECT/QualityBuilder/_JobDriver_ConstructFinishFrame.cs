using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using RimWorld;
using Verse.AI;
using Verse;
using HarmonyLib;

namespace QualityBuilder
{
    [HarmonyPatch(typeof(JobDriver_ConstructFinishFrame), "MakeNewToils")]
    public class _JobDriver_ConstructFinishFrame
    {
        public static IEnumerable<Toil> Postfix(IEnumerable<Toil> __result, JobDriver_ConstructFinishFrame __instance)
        {
            Thing thing = __instance.job.targetA.Thing;
            if (thing == null)
            {
                return __result;
            }
            Map map = thing.Map;
            LocalTargetInfo localTargetInfo = __instance.job.targetA;
            CompQualityBuilder compQuality = null;
            try
            {
                compQuality = QualityBuilder.getCompQualityBuilder(thing);
            }
            catch (Exception ex)
            {
                Log.Warning("QualityBuilder: cant enhance constuctFinishFrame toil cause thing is not compatible");
            }
            if (compQuality == null)
            {
                return __result;
            }
            List<Toil> list = new List<Toil>(__result);
            Toil toil = list.Last<Toil>();
            toil.AddFinishAction(delegate
            {
                _JobDriver_ConstructFinishFrame.afterFinishToil(compQuality, map, localTargetInfo);
            });
            return list;
        }

        private static CompQualityBuilder getComp(Toil toil, ref LocalTargetInfo targetInfo)
        {
            Pawn actor = toil.actor;
            Job curJob = actor.jobs.curJob;
            targetInfo = curJob.targetA;
            ThingWithComps target = targetInfo.Thing as ThingWithComps;
            if (target == null)
            {
                Log.Error("QualityBuilder: Target not available to get QualityBuilder settings");
                return null;
            }
            return QualityBuilder.getCompQualityBuilder(target);
        }

        public static void afterFinishToil(CompQualityBuilder cmp, Map curMap, LocalTargetInfo targetInfo)
        {
            if (cmp == null)
                return;
            String targetDefName = targetInfo.Thing.def.defName;
            if (targetDefName.EndsWith("_ReplaceStuff"))
                targetDefName = targetDefName.Replace("_ReplaceStuff", "");
            Building building = null;
            try
            {
                building = curMap.thingGrid.ThingsListAt(targetInfo.Cell).First(t => QualityBuilder.getCompQualityBuilder(t) != null) as Building;
            }catch(Exception e)
            {
                
            }
            if (building == null) // Possible it got butchered
            {
                ThingWithComps newBP = QualityBuilder.GetFirstBuildingBuildingOrFrame(curMap, targetInfo.Cell) as ThingWithComps;
                if (newBP == null)
                {
                    return;
                }
                if (cmp == null)
                {
                    return;
                }
                CompQualityBuilder newBPCmp = QualityBuilder.getCompQualityBuilder(newBP);
                newBPCmp.desiredMinQuality = cmp.desiredMinQuality;
                QualityBuilder.setSkilled(newBP, cmp.desiredMinQuality, cmp.isSkilled);
                
                // Preserve ideology settings from the original building
                Building originalBuilding = cmp.parent as Building;
                if (originalBuilding != null)
                {
                    // Get the CompStyleable component from the new thing
                    CompStyleable newCompStyleable = newBP.GetComp<CompStyleable>();
                    if (newCompStyleable != null)
                    {
                        // Get the CompStyleable component from the original building
                        CompStyleable originalCompStyleable = originalBuilding.GetComp<CompStyleable>();
                        if (originalCompStyleable != null)
                        {
                            if (originalCompStyleable.SourcePrecept != null)
                            {
                                newCompStyleable.SourcePrecept = originalCompStyleable.SourcePrecept;
                            }
                            
                            if (originalCompStyleable.styleDef != null)
                            {
                                newCompStyleable.styleDef = originalCompStyleable.styleDef;
                            }
                        }
                    }
                    else
                    {
                        // Fallback to the old method
                        if (originalBuilding.StyleSourcePrecept != null)
                        {
                            newBP.StyleSourcePrecept = originalBuilding.StyleSourcePrecept;
                        }
                        
                        if (originalBuilding.StyleDef != null)
                        {
                            newBP.StyleDef = originalBuilding.StyleDef;
                        }
                        
                        // Try using InheritStyle method if available
                        if (newBP is Blueprint blueprint)
                        {
                            blueprint.InheritStyle(originalBuilding.StyleSourcePrecept, originalBuilding.StyleDef);
                        }
                    }
                }
                return;
            }
            QualityCategory finishedBuildingQuality;
            if (!building.TryGetQuality(out finishedBuildingQuality))
            {
                return;
            }
            CompQualityBuilder buildingCmp = QualityBuilder.getCompQualityBuilder(building);
            buildingCmp.isSkilled = cmp.isSkilled;
            buildingCmp.desiredMinQuality = cmp.desiredMinQuality;
            if (finishedBuildingQuality >= cmp.desiredMinQuality || !cmp.isSkilled)
            {
                buildingCmp.isDesiredMinQualityReached = true;
                return;
            }
            
            // Store ideology settings before deconstructing
            Precept_ThingStyle originalStyleSourcePrecept = building.StyleSourcePrecept;
            ThingStyleDef originalStyleDef = building.StyleDef;
            
            // Add deconstruction designation
            curMap.designationManager.AddDesignation(new Designation(building, DesignationDefOf.Deconstruct));
            
            // Immediately look for the new blueprint and restore ideology settings
            ThingWithComps newBlueprint = QualityBuilder.GetFirstBuildingBuildingOrFrame(curMap, targetInfo.Cell) as ThingWithComps;
            if (newBlueprint != null)
            {
                // Get the CompStyleable component
                CompStyleable compStyleable = newBlueprint.GetComp<CompStyleable>();
                if (compStyleable != null)
                {
                    // Directly set the sourcePrecept property
                    if (originalStyleSourcePrecept != null)
                    {
                        compStyleable.SourcePrecept = originalStyleSourcePrecept;
                    }
                    
                    // Directly set the styleDef property
                    if (originalStyleDef != null)
                    {
                        compStyleable.styleDef = originalStyleDef;
                    }
                }
                else
                {
                    // Fallback to the old method
                    if (originalStyleSourcePrecept != null)
                    {
                        newBlueprint.StyleSourcePrecept = originalStyleSourcePrecept;
                    }
                    
                    if (originalStyleDef != null)
                    {
                        newBlueprint.StyleDef = originalStyleDef;
                    }
                    
                    // Try using InheritStyle method if available
                    if (newBlueprint is Blueprint blueprint)
                    {
                        blueprint.InheritStyle(originalStyleSourcePrecept, originalStyleDef);
                    }
                }
            }
        }
    }
}
