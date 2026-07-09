using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace QualityBuilder
{
	// Wraps the vanilla toil's initAction instead of replacing the method, so other mods'
	// patches on the vanilla body (e.g. Achtung, Replace Stuff) still apply. After vanilla
	// turns the blueprint into a frame, the QualityBuilder settings are copied onto the
	// frame so the designation survives the swap.
	//
	// No skill gating happens here: this toil only runs in delivery (JobDriver_HaulToContainer)
	// and frame placement (JobDriver_PlaceNoCostFrame) jobs, neither of which rolls quality.
	// Any construction pawn may deliver resources and place frames; finishing the frame is
	// gated in _WorkGiver_ConstructFinishFrames where the quality roll actually happens.
	[HarmonyPatch(typeof(Toils_Construct), "MakeSolidThingFromBlueprintIfNecessary")]
	public static class _Toils_Construct
	{
		public static void Postfix(Toil __result, TargetIndex blueTarget, TargetIndex targetToUpdate = TargetIndex.None)
		{
			Toil toil = __result;
			if (toil == null)
				return;
			Action vanillaInit = toil.initAction;
			toil.initAction = delegate
			{
				Pawn actor = toil.actor;
				Job curJob = actor.jobs.curJob;
				Blueprint blueprint = curJob.GetTarget(blueTarget).Thing as Blueprint;

				vanillaInit?.Invoke();

				if (blueprint == null)
					return;
				try
				{
					Thing created = curJob.GetTarget(blueTarget).Thing;
					if (created == null || created == blueprint)
						return; // vanilla did not replace the blueprint

					CompQualityBuilder cmpBlueprint = QualityBuilder.getCompQualityBuilder(blueprint);
					if (cmpBlueprint == null)
						return;
					CompQualityBuilder cmpCreated = QualityBuilder.getCompQualityBuilder(created);
					if (cmpCreated == null)
						return;
					cmpCreated.desiredMinQuality = cmpBlueprint.desiredMinQuality;
					// Carry the quality-redo counter across the blueprint->frame swap so the
					// deconstruct->rebuild loop-breaker keeps counting.
					cmpCreated.qualityRebuildAttempts = cmpBlueprint.qualityRebuildAttempts;
					if (!QualityBuilder.hasDesignation(created))
						QualityBuilder.setSkilled(created, cmpCreated.desiredMinQuality, cmpBlueprint.isSkilled);
				}
				catch (Exception ex)
				{
					Log.Error("QualityBuilder: Error in construct toil.");
					Log.Error(ex.ToString());
				}
			};
		}
	}
}
