using HarmonyLib;
using RimWorld;
using Verse;

namespace QualityBuilder
{
    [HarmonyPatch(typeof(ThingUtility), "CheckAutoRebuildOnDestroyed")]
    class Patch_ThingUtillity
    {
        public static void Postfix(Thing thing, DestroyMode mode, Map map, BuildableDef buildingDef)
        {
            if (!(Find.PlaySettings.autoRebuild && mode == DestroyMode.KillFinalize && thing.Faction == Faction.OfPlayer && buildingDef.blueprintDef != null && buildingDef.IsResearchFinished && map.areaManager.Home[thing.Position]))
                return;
            var cmp = QualityBuilder.getCompQualityBuilder(thing);
            if (cmp == null)
                return;
            var newBuilding = QualityBuilder.GetFirstBuildingBuildingOrFrame(map, thing.Position);
            if (newBuilding == null)
                return;
            var newComp = QualityBuilder.getCompQualityBuilder(newBuilding);
            if (newComp == null)
                return;
            QualityBuilder.setSkilled(newBuilding, cmp.desiredMinQuality, cmp.isSkilled);
        }
    }
}
