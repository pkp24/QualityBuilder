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

		// Defensive cleanup for QB's deconstruct->rebuild churn. Releases any lingering
		// reservations on the construction thing(s) at a cell (blueprint/frame/QB-managed
		// building) before a replacement blueprint is placed there. Without this, a stale
		// FinishFrame reservation (maxPawns 1) left on the old construction can collide with
		// vanilla's reserve of the rebuilt frame in
		// Toils_Construct.MakeSolidThingFromBlueprintIfNecessary (which reserves the new frame
		// with maxPawns 5), producing a harmless-but-noisy "Could not reserve" red error.
		public static void releaseConstructionReservations(Map map, IntVec3 cell)
		{
			if (map == null || !cell.InBounds(map))
				return;
			List<Thing> things = map.thingGrid.ThingsListAt(cell);
			for (int i = things.Count - 1; i >= 0; i--)
			{
				Thing t = things[i];
				if (t == null)
					continue;
				if (t.def.IsBlueprint || t.def.IsFrame || getCompQualityBuilder(t) != null)
					map.reservationManager.ReleaseAllForTarget(t);
			}
		}

		public static DesignationDef getDesignationDef(QualityCategory cat)
		{
			DesignationDef[] defs = getSkilledDesignationDefs();
			int index = (int)cat;
			if (index < 0 || index >= defs.Length)
				index = 0;
			return defs[index];
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
		}

		private static void setSkilledInComp(Thing thing, QualityCategory curCat, bool add)
		{
			CompQualityBuilder cmp = getCompQualityBuilder(thing);
			if (cmp == null)
				return;
			cmp.isSkilled = add;
			cmp.desiredMinQuality = curCat;
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
			if (thing == null)
				return null;
			Map curMap = thing.Map;
			if (curMap == null)
				curMap = Find.CurrentMap;
			return curMap;
		}

		internal static QualityBuilderMod modInstance;

		public static QualityBuilderMod getMod()
		{
			if (modInstance == null)
				modInstance = LoadedModManager.GetMod<QualityBuilderMod>();
			return modInstance;
		}

		public delegate void SetQuality(QualityCategory cat);

		public static IEnumerable<FloatMenuOption> getFloatingOptions(SetQuality action)
		{
			List<FloatMenuOption> floatOptionList = new List<FloatMenuOption>();
			foreach (QualityCategory item in Enum.GetValues(typeof(QualityCategory)))
				floatOptionList.Add(buildFloatMenuOption(action, item));
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
