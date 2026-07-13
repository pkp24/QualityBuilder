using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace QualityBuilder
{
	public static class QualityBuilder
	{
		// Index matches (int)QualityCategory: Awful..Legendary -> SkilledBuilder, SkilledBuilder2..7
		private static DesignationDef[] skilledDesignationDefs;

		private static DesignationDef[] getSkilledDesignationDefs()
		{
			if (skilledDesignationDefs == null)
			{
				skilledDesignationDefs = new DesignationDef[7];
				for (int i = 0; i < 7; i++)
				{
					String name = i == 0 ? "SkilledBuilder" : "SkilledBuilder" + (i + 1);
					skilledDesignationDefs[i] = DefDatabase<DesignationDef>.GetNamed(name, true);
				}
			}
			return skilledDesignationDefs;
		}

		public static Thing GetFirstBuildingBuildingOrFrame(Map map, IntVec3 c)
		{
			List<Thing> list = map.thingGrid.ThingsListAt(c);
			for (int i = 0; i < list.Count; i++)
			{
				var cur = list[i];
				if ((cur.def.IsFrame || cur.def.IsBlueprint) && getCompQualityBuilder(cur) != null)
					return cur;
			}
			return null;
		}

		public static Thing GetFirstBuildingBuildingOrFrame(IntVec3 c)
		{
			return GetFirstBuildingBuildingOrFrame(Find.CurrentMap, c);
		}

		public static DesignationDef getDesignationDef(QualityCategory cat)
		{
			DesignationDef[] defs = getSkilledDesignationDefs();
			int index = (int)cat;
			if (index < 0 || index >= defs.Length)
				index = 0;
			return defs[index];
		}

		// Reverse of getDesignationDef: which min quality a SkilledBuilder* designation stands
		// for. The designation manager is scribed with the map, so on load this is more
		// trustworthy than the comp's scribed values from old saves.
		public static QualityCategory? getQualityForDesignationDef(DesignationDef def)
		{
			if (def == null)
				return null;
			DesignationDef[] defs = getSkilledDesignationDefs();
			for (int i = 0; i < defs.Length; i++)
				if (defs[i] == def)
					return (QualityCategory)i;
			return null;
		}

		public static bool hasDesignation(Thing t)
		{
			return getDesignationOnThing(t) != null;
		}

		public static Designation getDesignationOnThing(Thing thing)
		{
			Map thingMap = getMapForThing(thing);
			if (thingMap == null)
				return null;
			List<Designation> all = thingMap.designationManager.AllDesignationsOn(thing);
			DesignationDef[] defs = getSkilledDesignationDefs();
			for (int i = 0; i < all.Count; i++)
			{
				for (int j = 0; j < defs.Length; j++)
					if (all[i].def == defs[j])
						return all[i];
			}
			return null;
		}

		public static void setSkilled(Thing thing, QualityCategory? cat, bool add)
		{
			Map thingMap = getMapForThing(thing);
			if (thingMap == null)
				return;
			// Preserve forbidden state
			bool wasForbidden = false;
			var forbiddable = (thing as ThingWithComps)?.GetComp<CompForbiddable>();
			if (forbiddable != null)
			{
				wasForbidden = forbiddable.Forbidden;
			}
			Designation desOnThing = getDesignationOnThing(thing);
			if (desOnThing != null)
				thingMap.designationManager.RemoveDesignation(desOnThing);
			QualityCategory curCat = cat.HasValue ? cat.Value : QualityBuilderModSettings.getDefaultMinQualitySetting(thing.Map);
			if (add)
				thingMap.designationManager.AddDesignation(new Designation(thing, getDesignationDef(curCat)));
			setSkilledInComp(thing, curCat, add);
			// Restore forbidden state
			if (forbiddable != null)
			{
				forbiddable.Forbidden = wasForbidden;
			}
			// Setting a quality target on an already-finished building (as opposed to a
			// blueprint/frame still under construction) needs its own check: the normal
			// deconstruct->rebuild designation only happens right after a frame completes
			// (_JobDriver_ConstructFinishFrame), so without this, right-clicking a quality
			// onto a finished building would silently do nothing.
			if (add && thing is Building building && !(building is Frame))
				checkAndDesignateForRebuild(building, getCompQualityBuilder(building));
		}

		// Compares a finished building's current quality against its desired minimum and, if
		// it falls short, designates it for deconstruction so the deconstruct->rebuild cycle
		// (_JobDriver_Deconstruct) replaces it with a higher-quality version. Shared by the
		// post-construction check and by setting a quality target on an already-finished
		// building (setSkilled, e.g. via the gizmo's right-click menu).
		public static void checkAndDesignateForRebuild(Building building, CompQualityBuilder buildingCmp)
		{
			if (building == null || buildingCmp == null)
				return;
			Map curMap = building.Map;
			if (curMap == null)
				return;
			QualityCategory finishedBuildingQuality;
			if (!building.TryGetQuality(out finishedBuildingQuality))
				return;
			if (finishedBuildingQuality >= buildingCmp.desiredMinQuality || !buildingCmp.isSkilled)
			{
				buildingCmp.isDesiredMinQualityReached = true;
				// Meeting (or no longer targeting) the min quality resets the redo counter.
				buildingCmp.qualityRebuildAttempts = 0;
				buildingCmp.pendingQualityRebuild = false;
				return;
			}
			// Already mid-cycle (or a Deconstruct designation is already pending) — don't
			// re-designate or double-count attempts.
			if (buildingCmp.pendingQualityRebuild || curMap.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
				return;

			// Loop-breaker: an unreachable min quality must not deconstruct->rebuild forever
			// (each cycle burns ~50% materials), unless the player explicitly asked for
			// unlimited attempts. After the cap, keep the building and tell the player once.
			int maxRebuildAttempts = QualityBuilderModSettings.getMaxQualityRebuildAttempts(curMap);
			if (buildingCmp.qualityRebuildAttempts >= maxRebuildAttempts)
			{
				buildingCmp.pendingQualityRebuild = false;
				Messages.Message("QualityBuilder.RebuildGaveUp".Translate(building.LabelShort, maxRebuildAttempts, finishedBuildingQuality.GetLabel()), building, MessageTypeDefOf.NegativeEvent);
				return;
			}
			buildingCmp.qualityRebuildAttempts++;

			// Quality too low: designate for deconstruction. pendingQualityRebuild marks this
			// deconstruction as QB-initiated so _JobDriver_Deconstruct_FinishedRemoving rebuilds
			// it — player-ordered deconstructions never set the flag and are never hijacked.
			buildingCmp.pendingQualityRebuild = true;
			curMap.designationManager.AddDesignation(new Designation(building, DesignationDefOf.Deconstruct));
		}

		private static void setSkilledInComp(Thing thing, QualityCategory curCat, bool add)
		{
			CompQualityBuilder cmp = getCompQualityBuilder(thing);
			if (cmp == null)
				return;
			cmp.isSkilled = add;
			cmp.desiredMinQuality = curCat;
			if (!add && cmp.pendingQualityRebuild)
			{
				// Opting out cancels a pending quality redo: clear the flag and remove QB's
				// own deconstruct order (only QB-initiated ones — the flag is never set for
				// player-ordered deconstructions).
				cmp.pendingQualityRebuild = false;
				Map map = getMapForThing(thing);
				Designation decon = map?.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct);
				if (decon != null)
					map.designationManager.RemoveDesignation(decon);
			}
		}

		public static CompQualityBuilder getCompQualityBuilder(Thing thing)
		{
			return (thing as ThingWithComps)?.GetComp<CompQualityBuilder>();
		}

		public static bool hasQuality(Thing thing, QualityCategory cat)
		{
			CompQualityBuilder cmp = getCompQualityBuilder(thing);
			if (cmp == null)
				return false;
			return cmp.desiredMinQuality == cat;
		}

		public static Map getMapForThing(Thing thing)
		{
			return thing?.Map;
		}

		// Copy ideology style (source precept + style def) onto a rebuilt blueprint/frame.
		// Prefers the CompStyleable component; falls back to the Thing-level style API
		// (and Blueprint.InheritStyle) when the comp is absent. Shared by the
		// construction-failed and deconstruct->rebuild paths.
		public static void applyStyle(ThingWithComps dest, Precept_ThingStyle sourcePrecept, ThingStyleDef sourceStyleDef)
		{
			if (dest == null)
				return;
			CompStyleable comp = dest.GetComp<CompStyleable>();
			if (comp != null)
			{
				if (sourcePrecept != null)
					comp.SourcePrecept = sourcePrecept;
				if (sourceStyleDef != null)
					comp.styleDef = sourceStyleDef;
			}
			else
			{
				if (sourcePrecept != null)
					dest.StyleSourcePrecept = sourcePrecept;
				if (sourceStyleDef != null)
					dest.StyleDef = sourceStyleDef;
				if (dest is Blueprint bp)
					bp.InheritStyle(sourcePrecept, sourceStyleDef);
			}
		}

		internal static QualityBuilderMod modInstance;

		public static QualityBuilderMod getMod()
		{
			if (modInstance == null)
				modInstance = LoadedModManager.GetMod<QualityBuilderMod>();
			return modInstance;
		}

		public delegate void SetQuality(QualityCategory cat);

		// exclusive=true excludes minQuality itself (used when minQuality is a finished
		// building's *current* quality — retargeting to the same quality it already has is
		// not a meaningful choice). exclusive=false (default) keeps minQuality as the floor,
		// for callers (e.g. blueprints/frames) with no current quality to exceed.
		public static IEnumerable<FloatMenuOption> getFloatingOptions(SetQuality action, QualityCategory minQuality = QualityCategory.Awful, bool exclusive = false)
		{
			List<FloatMenuOption> floatOptionList = new List<FloatMenuOption>();
			foreach (QualityCategory item in Enum.GetValues(typeof(QualityCategory)))
			{
				if (exclusive ? item <= minQuality : item < minQuality)
					continue;
				floatOptionList.Add(buildFloatMenuOption(action, item));
			}
			return floatOptionList;
		}

		public static FloatMenuOption buildFloatMenuOption(SetQuality action, QualityCategory cat)
		{
			return new FloatMenuOption(Translator.Translate("QualityCategory_" + Enum.GetName(typeof(QualityCategory), cat)), new Action(delegate
			{
				action.Invoke(cat);
			}));
		}

		public static int getPawnConstructionSkill(Pawn pawn)
		{
			if (pawn == null)
				return 0;
			if (pawn.IsColonyMech)
				return pawn.RaceProps.mechFixedSkillLevel;
			else if (pawn.IsColonist && pawn.skills != null)
				return pawn.skills.GetSkill(SkillDefOf.Construction).Level;
			return 0;
		}

		public static int getBestConstructorSkill(Map curMap)
		{
			int best = 0;
			List<Pawn> pawns = curMap.mapPawns.PawnsInFaction(Faction.OfPlayer);
			for (int i = 0; i < pawns.Count; i++)
			{
				Pawn p = pawns[i];
				// Only pawns actually available to build may set the quality threshold:
				// PawnsInFaction also returns unspawned held pawns (cryptosleep caskets,
				// carried) and imprisoned colonists, and counting a downed/broken best
				// builder would deadlock all quality construction. Drafted pawns are NOT
				// excluded — that's a temporary state and would cause churn.
				if (!p.Spawned || p.Downed || p.IsPrisoner || p.InMentalState)
					continue;
				if (!pawnCanConstruct(p))
					continue;
				int skill = getPawnConstructionSkill(p);
				if (skill > best)
					best = skill;
			}
			return best;
		}

		public static bool pawnCanConstruct(Pawn pawn)
		{
			if (pawn == null)
				return false;
			if (pawn.RaceProps.Humanlike && pawn.skills != null && pawn.workSettings != null && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction) && pawnCapable(pawn))
				return true;
			else if (pawn.IsColonyMech && pawn.GetOverseer() != null && pawn.RaceProps.mechEnabledWorkTypes.Contains(WorkTypeDefOf.Construction))
				return true;
			return false;
		}

		private static bool pawnCapable(Pawn p)
		{
			PawnCapacitiesHandler capacitiesHandler = p.health?.capacities;
			if (capacitiesHandler == null)
				return false;
			return capacitiesHandler.CapableOf(PawnCapacityDefOf.Manipulation) && capacitiesHandler.CapableOf(PawnCapacityDefOf.Moving);
		}
	}
}
