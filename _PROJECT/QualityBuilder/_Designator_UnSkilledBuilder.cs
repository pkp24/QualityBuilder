using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace QualityBuilder
{
	public class _Designator_UnSkilledBuilder : Designator
	{
		public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

		public _Designator_UnSkilledBuilder()
		{
			this.defaultLabel = Translator.Translate("QualityBuilderOff.Label");
			this.defaultDesc = Translator.Translate("QualityBuilderOff.Desc");
			LongEventHandler.ExecuteWhenFinished(delegate
			{
				this.icon = ContentFinder<Texture2D>.Get("UnSkilled", true);
			});
			this.soundDragSustain = SoundDefOf.Designate_DragStandard;
			this.soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
			this.useMouseIcon = true;
			this.soundSucceeded = SoundDefOf.Designate_Haul;
		}

		public override AcceptanceReport CanDesignateCell(IntVec3 c)
		{
            if (!GenGrid.InBounds(c, Find.CurrentMap) || GridsUtility.Fogged(c, Find.CurrentMap))
                return false;
			Thing firstBuilding = QualityBuilder.GetFirstBuildingBuildingOrFrame(c);
            return CanDesignateThing(firstBuilding);
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (t == null || !QualityBuilder.hasDesignation(t))
                return false;
            return AcceptanceReport.WasAccepted;
        }

        public override void DesignateThing(Thing t)
        {
            QualityBuilder.setSkilled(t, null, false);
        }

        public override void DesignateSingleCell(IntVec3 c)
		{
			Thing firstBuilding = QualityBuilder.GetFirstBuildingBuildingOrFrame(c);
            QualityBuilder.setSkilled(firstBuilding, null, false);
		}

		public override void SelectedUpdate()
		{
			GenUI.RenderMouseoverBracket();
		}
	}
}
