using HarmonyLib;
using RimWorld;
using Verse;

namespace QualityBuilder
{
    [HarmonyPatch(typeof(ThingUtility), "CheckAutoRebuildOnDestroyed")]
    class Patch_ThingUtillity
    {
        // CheckAutoRebuildOnDestroyed returns the auto-rebuild blueprint it placed (or null),
        // so use __result directly instead of re-deriving vanilla's placement condition and
        // scanning the cell.
        public static void Postfix(Thing thing, Blueprint_Build __result)
        {
            if (__result == null)
                return;
            var cmp = QualityBuilder.getCompQualityBuilder(thing);
            if (cmp == null)
                return;
            var newComp = QualityBuilder.getCompQualityBuilder(__result);
            if (newComp == null)
                return;
            QualityBuilder.setSkilled(__result, cmp.desiredMinQuality, cmp.isSkilled);
        }
    }
}
