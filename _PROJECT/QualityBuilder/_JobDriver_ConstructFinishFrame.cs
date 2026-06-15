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
				// Defensive: release this FinishFrame job's reservation on the (now-finished)
				// frame even if a reservation-system rewrite (e.g. PartialReservationSystem)
				// skips the normal job cleanup. A lingering maxPawns-1 frame reservation would
				// otherwise collide with the vanilla maxPawns-5 reserve when the same pawn later
				// delivers to / rebuilds at this cell.
				if (map != null && __instance.pawn != null && __instance.job != null)
					map.reservationManager.ReleaseClaimedBy(__instance.pawn, __instance.job);
				_JobDriver_ConstructFinishFrame.afterFinishToil(compQuality, map, localTargetInfo);
			});
			return list;
		}

		public static void afterFinishToil(CompQualityBuilder cmp, Map curMap, LocalTargetInfo targetInfo)
		{
			if (cmp == null || curMap == null)
				return;
			Building building = null;
			List<Thing> thingsAtCell = curMap.thingGrid.ThingsListAt(targetInfo.Cell);
			for (int i = 0; i < thingsAtCell.Count; i++)
			{
				if (QualityBuilder.getCompQualityBuilder(thingsAtCell[i]) != null)
				{
					building = thingsAtCell[i] as Building;
					break;
				}
			}
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
				QualityBuilder.setSkilled(newBP, cmp.desiredMinQuality, cmp.isSkilled);

				// Preserve ideology settings from the original building
				Building originalBuilding = cmp.parent as Building;
				if (originalBuilding != null)
				{
					CompStyleable newCompStyleable = newBP.GetComp<CompStyleable>();
					if (newCompStyleable != null)
					{
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

			// Quality too low: designate for deconstruction. The rebuild (and ideology style
			// restoration) happens in _JobDriver_Deconstruct_FinishedRemoving once the
			// building is actually removed.
			// Clear any reservations on the building / cell before deconstruct+rebuild churn
			// so nothing carries a stale reservation into the rebuilt frame.
			curMap.reservationManager.ReleaseAllForTarget(building);
			QualityBuilder.releaseConstructionReservations(curMap, targetInfo.Cell);
			curMap.designationManager.AddDesignation(new Designation(building, DesignationDefOf.Deconstruct));
		}
	}
}
