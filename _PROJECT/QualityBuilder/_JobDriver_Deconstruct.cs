using RimWorld;
using Verse;
using HarmonyLib;

namespace QualityBuilder
{
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

			Map curMap = __instance.pawn?.Map;
			if (curMap == null)
				return;

			// Store ideology settings from the original building before deconstructing
			Precept_ThingStyle originalStyleSourcePrecept = thing.StyleSourcePrecept;
			ThingStyleDef originalStyleDef = thing.StyleDef;

			// Get building info
			IntVec3 center = target.Cell;
			Rot4 rotation = thing.Rotation;
			ThingDef stuffDef = thing.Stuff;
			ThingDef buildingDef = thing.def;

			// Clear any lingering reservations on the old building and the cell before placing
			// the rebuild blueprint, so a stale FinishFrame reservation can't survive into the
			// rebuilt frame and collide with vanilla's reserve in
			// Toils_Construct.MakeSolidThingFromBlueprintIfNecessary.
			curMap.reservationManager.ReleaseAllForTarget(thing);
			QualityBuilder.releaseConstructionReservations(curMap, center);

			// Create new blueprint with ideology style preserved
			Blueprint newBP = GenConstruct.PlaceBlueprintForBuild(buildingDef, center, curMap, rotation, Faction.OfPlayer, stuffDef);
			CompQualityBuilder newBPCmp = QualityBuilder.getCompQualityBuilder(newBP);
			if (newBPCmp == null)
			{
				Log.Error("QualityBuilder: new blueprint has no CompQualityBuilder");
				return;
			}
			newBPCmp.desiredMinQuality = cmp.desiredMinQuality;
			QualityBuilder.setSkilled(newBP, cmp.desiredMinQuality, cmp.isSkilled);

			// Restore ideology settings to the new blueprint
			CompStyleable compStyleable = newBP.GetComp<CompStyleable>();
			if (compStyleable != null)
			{
				if (originalStyleSourcePrecept != null)
				{
					compStyleable.SourcePrecept = originalStyleSourcePrecept;
				}

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

				newBP.InheritStyle(originalStyleSourcePrecept, originalStyleDef);
			}
		}
	}
}
