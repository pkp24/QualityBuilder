using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace QualityBuilder
{
	[StaticConstructorOnStartup]
	public static class QualityBuilderStartup
	{
		public static readonly Texture2D SkilledTex = ContentFinder<Texture2D>.Get("Skilled", true);
		public static readonly Texture2D UnSkilledTex = ContentFinder<Texture2D>.Get("UnSkilled", true);

		static QualityBuilderStartup()
		{
			InjectComps();
		}

		// Add CompQualityBuilder to every quality building plus its blueprint and frame defs.
		// Runs once after all defs are loaded; defs never change afterwards.
		private static void InjectComps()
		{
			int added = 0;
			List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
			for (int i = 0; i < defs.Count; i++)
			{
				ThingDef td = defs[i];
				if (td.building == null || !td.HasComp(typeof(CompQuality)))
					continue;
				added += AddCompTo(td);
				added += AddCompTo(td.blueprintDef);
				added += AddCompTo(td.frameDef);
			}
#if DEBUG
			Log.Message("QualityBuilder added property to '" + added + "' things");
#endif
		}

		private static int AddCompTo(ThingDef td)
		{
			if (td == null || td.comps == null)
				return 0;
			for (int i = 0; i < td.comps.Count; i++)
				if (td.comps[i] is CompProperties_QualityBuilderr)
					return 0;
			td.comps.Add(new CompProperties_QualityBuilderr());
			return 1;
		}
	}

	// ResolveDesignators clears and rebuilds resolvedDesignators (and the game re-resolves
	// via DirtyCache, e.g. on ideology style changes), so the designators must be re-added
	// after every resolve rather than injected once.
	[HarmonyPatch(typeof(DesignationCategoryDef), "ResolveDesignators")]
	public static class _DesignationCategoryDef_ResolveDesignators
	{
		public static void Postfix(DesignationCategoryDef __instance, List<Designator> ___resolvedDesignators)
		{
			if (!"Orders".EqualsIgnoreCase(__instance.defName))
				return;
			// Guard against double-injection if the list is re-resolved without being cleared
			// first (vanilla ResolveDesignators clears, but another mod's patch might not).
			for (int i = 0; i < ___resolvedDesignators.Count; i++)
				if (___resolvedDesignators[i] is _Designator_SkilledBuilder)
					return;
			___resolvedDesignators.Add(new _Designator_SkilledBuilder());
			___resolvedDesignators.Add(new _Designator_UnSkilledBuilder());
		}
	}
}
