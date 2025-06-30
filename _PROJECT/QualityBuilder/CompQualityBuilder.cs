using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace QualityBuilder
{
	public class CompQualityBuilder : ThingComp
	{
        bool skilled = false;
        QualityCategory desiredMinQualityRef = QualityCategory.Awful;

        bool desiredMinQualityReached;
        public bool isDesiredMinQualityReached
        {
            get { return desiredMinQualityReached; }
            set { this.desiredMinQualityReached = value; }
        }

        public bool isSkilled
        {
            get { return skilled; }
            set { this.skilled = value; }
        }

        public QualityCategory desiredMinQuality
        {
            get { return desiredMinQualityRef; }
            set { this.desiredMinQualityRef = value; }
        }

		public override void Initialize(CompProperties props)
		{
			base.Initialize(props);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && parent.def.IsBlueprint && !parent.def.IsFrame && !QualityBuilder.hasDesignation(parent) && QualityBuilderModSettings.getDefaultUseQualityBuilder(parent.Map))
            {
                desiredMinQualityRef = QualityBuilderModSettings.getDefaultMinQualitySetting(parent.Map);
                skilled = QualityBuilderModSettings.getDefaultUseQualityBuilder(parent.Map);
                QualityBuilder.setSkilled(parent, this.desiredMinQuality, true);
            }
            else if (respawningAfterLoad && (parent.def.IsBlueprint || parent.def.IsFrame) &&QualityBuilder.hasDesignation(parent))
            {
                QualityBuilder.setSkilled(parent, this.desiredMinQuality, skilled);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<bool>(ref this.skilled, "Quality", QualityBuilderModSettings.getDefaultUseQualityBuilder(parent.Map), false);
            Scribe_Values.Look<QualityCategory>(ref this.desiredMinQualityRef, "desiredMinQuality", QualityBuilderModSettings.getDefaultMinQualitySetting(parent.Map), false);
            Scribe_Values.Look<bool>(ref this.desiredMinQualityReached, "desiredMinQualityReaced", true, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
            if (parent.def.IsBlueprint || parent.def.IsFrame)
                yield return this.GetCommandButton();
			yield break;
		}

        private Command GetCommandButton()
		{
            ToggleCommand command_Toggle = new ToggleCommand();
            command_Toggle.hotKey = KeyBindingDefOf.Misc1;
			command_Toggle.icon = ContentFinder<Texture2D>.Get("Skilled", true);
			command_Toggle.isActive = new Func<bool>(this.IsSkilledActive);
			command_Toggle.toggleAction = new Action(this.ToggleSkilled);
            command_Toggle.defaultLabel = Translator.Translate("QualityBuilderCommand.Label");
            String qualityName = Translator.Translate("QualityCategory_" + Enum.GetName(typeof(QualityCategory), desiredMinQualityRef));
            NamedArgument arg = NamedArgumentUtility.Named(qualityName, "qualityName");
            if (skilled)
                command_Toggle.defaultDesc = "QualityBuilderOff.Desc".Translate(arg);
            else
                command_Toggle.defaultDesc = "QualityBuilderOn.Desc".Translate(arg);

			return command_Toggle;
		}

		private bool IsSkilledActive()
		{
			return this.isSkilled;
		}

		private void ToggleSkilled()
		{
            QualityBuilder.setSkilled(parent, this.desiredMinQuality, !isSkilled);
        }


        internal class ToggleCommand : Command_Toggle
        {
            internal ToggleCommand()
            {
            }

            public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
            {
                get
                {
                    return QualityBuilder.getFloatingOptions(item => {
                            List<object> selected = Find.Selector.SelectedObjects.FindAll(o => typeof(ThingWithComps).IsAssignableFrom(o.GetType()));
                            foreach (object curSelection in selected)
                            {
                                CompQualityBuilder cmp = QualityBuilder.getCompQualityBuilder(curSelection as ThingWithComps);
                                if (cmp != null)
                                    QualityBuilder.setSkilled(curSelection as ThingWithComps, item, true);
                            }
                    });
                }
            }
        }
	}
}
