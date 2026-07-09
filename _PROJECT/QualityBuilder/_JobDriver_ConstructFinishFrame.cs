using System;
using System.Collections.Generic;
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
			CompQualityBuilder compQuality = QualityBuilder.getCompQualityBuilder(thing);
			if (compQuality == null)
			{
				return __result;
			}
			List<Toil> list = new List<Toil>(__result);
			Toil toil = list.Last<Toil>();
			toil.AddFinishAction(delegate
			{
				// Vanilla's job cleanup already releases this job's frame reservation on every
				// job end; the deconstruct->rebuild reservation handoff is released once in
				// _JobDriver_Deconstruct_FinishedRemoving. afterFinishToil self-guards on
				// non-success (a still-present frame has no quality), so it's safe to run here.
				_JobDriver_ConstructFinishFrame.afterFinishToil(compQuality, map, localTargetInfo);
			});
			return list;
		}

		// How many QB-initiated deconstruct->rebuild cycles are allowed before giving up on a
		// building whose min quality the colony can't roll (each cycle burns ~50% materials).
		private const int MaxQualityRebuildAttempts = 3;

		public static void afterFinishToil(CompQualityBuilder cmp, Map curMap, LocalTargetInfo targetInfo)
		{
			if (cmp == null || curMap == null)
				return;
			// Identify the just-built thing properly: several comp-bearing buildings can share
			// the cell (e.g. a wall-attached lamp finishing on a quality wall's cell), so match
			// the def the frame was actually building instead of grabbing the first comp-bearer
			// — otherwise the WALL could get quality-checked and designated for deconstruction.
			ThingDef builtDef = (cmp.parent as Frame)?.def.entityDefToBuild as ThingDef;
			Building building = null;
			Building onlyCandidate = null;
			int candidateCount = 0;
			List<Thing> thingsAtCell = curMap.thingGrid.ThingsListAt(targetInfo.Cell);
			for (int i = 0; i < thingsAtCell.Count; i++)
			{
				Building cur = thingsAtCell[i] as Building;
				if (cur == null || QualityBuilder.getCompQualityBuilder(cur) == null)
					continue;
				if (builtDef != null && cur.def == builtDef)
				{
					building = cur;
					break;
				}
				candidateCount++;
				onlyCandidate = cur;
			}
			// Fall back to the old first-comp-bearer behavior only when the built def could
			// not be determined and there is no ambiguity. If builtDef is known but absent,
			// the build failed (blueprint placed) and the null-building branch handles it.
			if (building == null && builtDef == null && candidateCount == 1)
				building = onlyCandidate;
			if (building == null) // Construction failed: the frame is gone and a blueprint was placed instead
			{
				ThingWithComps newBP = QualityBuilder.GetFirstBuildingBuildingOrFrame(curMap, targetInfo.Cell) as ThingWithComps;
				if (newBP == null)
				{
					return;
				}
				CompQualityBuilder newBPCmp = QualityBuilder.getCompQualityBuilder(newBP);
				if (newBPCmp == null)
				{
					return;
				}
				newBPCmp.desiredMinQuality = cmp.desiredMinQuality;
				// Keep the redo counter across a failed construction so the loop-breaker
				// cap isn't reset by failures mid-cycle.
				newBPCmp.qualityRebuildAttempts = cmp.qualityRebuildAttempts;
				QualityBuilder.setSkilled(newBP, cmp.desiredMinQuality, cmp.isSkilled);

				// Preserve ideology settings from the original building
				Building originalBuilding = cmp.parent as Building;
				if (originalBuilding != null)
					QualityBuilder.applyStyle(newBP, originalBuilding.StyleSourcePrecept, originalBuilding.StyleDef);
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
			// Carry the redo counter across the frame->building comp handoff.
			buildingCmp.qualityRebuildAttempts = cmp.qualityRebuildAttempts;
			if (finishedBuildingQuality >= cmp.desiredMinQuality || !cmp.isSkilled)
			{
				buildingCmp.isDesiredMinQualityReached = true;
				// A successful build meeting the min quality resets the redo counter.
				buildingCmp.qualityRebuildAttempts = 0;
				buildingCmp.pendingQualityRebuild = false;
				return;
			}

			// Loop-breaker: an unreachable min quality must not deconstruct->rebuild forever
			// (each cycle burns ~50% materials). After the cap, keep the building and tell the
			// player once.
			if (buildingCmp.qualityRebuildAttempts >= MaxQualityRebuildAttempts)
			{
				buildingCmp.pendingQualityRebuild = false;
				Messages.Message("QualityBuilder.RebuildGaveUp".Translate(building.LabelShort, MaxQualityRebuildAttempts, finishedBuildingQuality.GetLabel()), building, MessageTypeDefOf.NegativeEvent);
				return;
			}
			buildingCmp.qualityRebuildAttempts++;

			// Quality too low: designate for deconstruction. pendingQualityRebuild marks this
			// deconstruction as QB-initiated so _JobDriver_Deconstruct_FinishedRemoving rebuilds
			// it — player-ordered deconstructions never set the flag and are never hijacked.
			// The rebuild, ideology-style restoration, and the single reservation release for
			// the deconstruct->rebuild handoff all happen there once the building is actually
			// removed (releasing here would be premature — nothing is rebuilt yet).
			buildingCmp.pendingQualityRebuild = true;
			curMap.designationManager.AddDesignation(new Designation(building, DesignationDefOf.Deconstruct));
		}
	}
}
