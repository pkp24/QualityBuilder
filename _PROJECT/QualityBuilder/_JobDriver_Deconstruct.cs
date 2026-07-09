using RimWorld;
using Verse;
using HarmonyLib;

namespace QualityBuilder
{
	[HarmonyPatch(typeof(JobDriver_Deconstruct), "FinishedRemoving")]
	public static class _JobDriver_Deconstruct_FinishedRemoving
	{
		// FinishedRemoving destroys the building before the Postfix runs (so thing.Map is null
		// by then). Capture the map in a Prefix while the building is still spawned, so a pawn
		// that despawns at job end doesn't cause the rebuild to be skipped and the building lost.
		// Game logic is single-threaded and FinishedRemoving isn't reentrant, so a static
		// handoff field between Prefix and Postfix is safe.
		private static Map capturedMap;

		public static void Prefix(JobDriver_Deconstruct __instance)
		{
			Thing thing = __instance.job?.targetA.Thing;
			capturedMap = thing?.Map ?? __instance.pawn?.Map;
		}

		public static void Postfix(JobDriver_Deconstruct __instance)
		{
			Map curMap = capturedMap ?? __instance.pawn?.Map;
			capturedMap = null;

			// Get the original building info before it was destroyed
			LocalTargetInfo target = __instance.job.targetA;
			Thing thing = target.Thing;

			// If the thing is null, it was already destroyed, so we can't get its info
			if (thing == null) return;

			CompQualityBuilder cmp = QualityBuilder.getCompQualityBuilder(thing);
			if (cmp == null || !cmp.isSkilled || cmp.isDesiredMinQualityReached)
				return;

			// Only rebuild when QB itself designated this deconstruction as a quality redo
			// (flag set in _JobDriver_ConstructFinishFrame.afterFinishToil). Player-ordered
			// deconstructions of a below-min building must never be hijacked into a rebuild.
			if (!cmp.pendingQualityRebuild)
				return;
			cmp.pendingQualityRebuild = false;

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

			// Clear any lingering reservations on the old building before placing the rebuild
			// blueprint, so a stale FinishFrame reservation can't survive into the rebuilt
			// frame and collide with vanilla's reserve in
			// Toils_Construct.MakeSolidThingFromBlueprintIfNecessary. Only the exact thing
			// being replaced is released — other things at the cell may be legitimately
			// reserved by other pawns' jobs.
			curMap.reservationManager.ReleaseAllForTarget(thing);

			// Create new blueprint with ideology style preserved
			Blueprint newBP = GenConstruct.PlaceBlueprintForBuild(buildingDef, center, curMap, rotation, Faction.OfPlayer, stuffDef);
			CompQualityBuilder newBPCmp = QualityBuilder.getCompQualityBuilder(newBP);
			if (newBPCmp == null)
			{
				Log.Error("QualityBuilder: new blueprint has no CompQualityBuilder");
				return;
			}
			newBPCmp.desiredMinQuality = cmp.desiredMinQuality;
			// Carry the redo counter through the rebuild cycle so an unreachable min quality
			// can't loop forever (see afterFinishToil's attempt cap).
			newBPCmp.qualityRebuildAttempts = cmp.qualityRebuildAttempts;
			QualityBuilder.setSkilled(newBP, cmp.desiredMinQuality, cmp.isSkilled);

			// Restore ideology settings to the new blueprint
			QualityBuilder.applyStyle(newBP, originalStyleSourcePrecept, originalStyleDef);
		}
	}
}
