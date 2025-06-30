using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse.AI;
using Verse;
using HarmonyLib;

namespace QualityBuilder
{
    [StaticConstructorOnStartup]
    public static class QualityBuilder_StartupLogger
    {
        static QualityBuilder_StartupLogger()
        {
            if (JobDefOf.Deconstruct != null)
            {
                Log.Message($"QualityBuilder: JobDefOf.Deconstruct driverClass = {JobDefOf.Deconstruct.driverClass?.Name ?? "null"}");
            }
            else
            {
                Log.Message("QualityBuilder: JobDefOf.Deconstruct is null");
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_Deconstruct), "FinishedRemoving")]
    public static class _JobDriver_Deconstruct_FinishedRemoving
    {
        public static void Postfix(JobDriver_Deconstruct __instance)
        {
            // Get the original building info before it was destroyed
            LocalTargetInfo target = __instance.job.targetA;
            Thing thing = target.Thing;
            
            // If the thing is null, it was already destroyed, so we can't get its info
            if (thing == null) return;
            
            CompQualityBuilder cmp = QualityBuilder.getCompQualityBuilder(thing);
            if (cmp == null || !cmp.isSkilled || cmp.isDesiredMinQualityReached)
                return;
            
            // Store ideology settings from the original building before deconstructing
            Precept_ThingStyle originalStyleSourcePrecept = thing.StyleSourcePrecept;
            ThingStyleDef originalStyleDef = thing.StyleDef;
            
            // Get building info
            IntVec3 center = target.Cell;
            Rot4 rotation = thing.Rotation;
            ThingDef stuffDef = thing.Stuff;
            ThingDef buildingDef = thing.def;
            
            // Create new blueprint with ideology style preserved
            Blueprint newBP = GenConstruct.PlaceBlueprintForBuild(buildingDef, center, __instance.pawn.Map, rotation, Faction.OfPlayer, stuffDef);
            CompQualityBuilder newBPCmp = QualityBuilder.getCompQualityBuilder(newBP);
            if (newBPCmp == null)
            {
                Log.Error("New BP has no compQualityBuilder");
                return;
            }
            newBPCmp.desiredMinQuality = cmp.desiredMinQuality;
            QualityBuilder.setSkilled(newBP, cmp.desiredMinQuality, cmp.isSkilled);
            
            // Restore ideology settings to the new blueprint
            if (newBP != null)
            {
                // Get the CompStyleable component
                CompStyleable compStyleable = newBP.GetComp<CompStyleable>();
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
                        newBP.StyleSourcePrecept = originalStyleSourcePrecept;
                    }
                    
                    if (originalStyleDef != null)
                    {
                        newBP.StyleDef = originalStyleDef;
                    }
                    
                    // Try using InheritStyle method if available
                    newBP.InheritStyle(originalStyleSourcePrecept, originalStyleDef);
                }
            }
        }
    }
}
