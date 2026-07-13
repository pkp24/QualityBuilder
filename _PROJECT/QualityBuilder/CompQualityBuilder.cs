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
        // Set only when QualityBuilder itself designates a deconstruct-for-quality redo.
        // Gates the auto-rebuild in _JobDriver_Deconstruct so player-ordered deconstructions
        // are never hijacked into a rebuild.
        bool pendingQualityRebuildInternal;
        // How many quality redos QB has already started for this building; carried across
        // the deconstruct->blueprint->frame->building cycle so an unreachable min quality
        // can't burn materials forever.
        int qualityRebuildAttemptsInternal;

        public bool isDesiredMinQualityReached
        {
            get { return desiredMinQualityReached; }
            set { this.desiredMinQualityReached = value; }
        }

        public bool pendingQualityRebuild
        {
            get { return pendingQualityRebuildInternal; }
            set { this.pendingQualityRebuildInternal = value; }
        }

        public int qualityRebuildAttempts
        {
            get { return qualityRebuildAttemptsInternal; }
            set { this.qualityRebuildAttemptsInternal = value; }
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
            else if (respawningAfterLoad && (parent.def.IsBlueprint || parent.def.IsFrame))
            {
                // On load the designation manager (scribed with the map) is the source of
                // truth: old saves scribed skilled/desiredMinQuality against dynamic defaults,
                // so those values can be wrong. Adopt an existing QB designation instead of
                // clobbering it from possibly-wrong scribed values.
                Designation des = QualityBuilder.getDesignationOnThing(parent);
                if (des != null)
                {
                    skilled = true;
                    QualityCategory? desCat = QualityBuilder.getQualityForDesignationDef(des.def);
                    if (desCat.HasValue)
                        desiredMinQualityRef = desCat.Value;
                }
                else if (skilled)
                {
                    // Scribed as skilled but the designation is missing — restore it.
                    QualityBuilder.setSkilled(parent, this.desiredMinQuality, true);
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Constant defaults only: parent.Map is non-null on save but null on load, so a
            // dynamic default omits values on save and reinterprets them against a different
            // default on load (skilled could flip, desiredMinQuality silently reset).
            Scribe_Values.Look<bool>(ref this.skilled, "Quality", false, false);
            Scribe_Values.Look<QualityCategory>(ref this.desiredMinQualityRef, "desiredMinQuality", QualityCategory.Awful, false);
            Scribe_Values.Look<bool>(ref this.desiredMinQualityReached, "desiredMinQualityReaced", true, false);
            Scribe_Values.Look<bool>(ref this.pendingQualityRebuildInternal, "pendingQualityRebuild", false, false);
            Scribe_Values.Look<int>(ref this.qualityRebuildAttemptsInternal, "qualityRebuildAttempts", 0, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
            if (parent.def.IsBlueprint || parent.def.IsFrame)
            {
                yield return this.GetCommandButton();
            }
            else if (parent is Building building && !(building is Frame))
            {
                // Finished buildings: keep offering the toggle as long as there's quality
                // headroom left to build up to. Gating this on `skilled` instead would strand
                // the player once they toggle off, since the only way back on is this same
                // gizmo's right-click menu.
                QualityCategory curQuality;
                if (!building.TryGetQuality(out curQuality) || curQuality < QualityCategory.Legendary)
                    yield return this.GetCommandButton();
            }
			yield break;
		}

        private Command GetCommandButton()
		{
            ToggleCommand command_Toggle = new ToggleCommand();
            command_Toggle.hotKey = KeyBindingDefOf.Misc1;
			command_Toggle.icon = skilled ? QualityBuilderStartup.SkilledTex : QualityBuilderStartup.UnSkilledTex;
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
                    List<object> selected = Find.Selector.SelectedObjects.FindAll(o => typeof(ThingWithComps).IsAssignableFrom(o.GetType()));
                    // Never offer a quality at or below what a selected finished building
                    // already has — retargeting to its current (or a lower) quality isn't a
                    // meaningful choice. Blueprints/frames have no current quality yet, so
                    // they keep the full Awful..Legendary range.
                    QualityCategory minQuality = QualityCategory.Awful;
                    bool hasFinishedBuilding = false;
                    foreach (object curSelection in selected)
                    {
                        if (curSelection is Building building && !(building is Frame) && building.TryGetQuality(out QualityCategory curQuality))
                        {
                            hasFinishedBuilding = true;
                            if (curQuality > minQuality)
                                minQuality = curQuality;
                        }
                    }
                    return QualityBuilder.getFloatingOptions(item => {
                            foreach (object curSelection in selected)
                            {
                                CompQualityBuilder cmp = QualityBuilder.getCompQualityBuilder(curSelection as ThingWithComps);
                                if (cmp != null)
                                    QualityBuilder.setSkilled(curSelection as ThingWithComps, item, true);
                            }
                    }, minQuality, hasFinishedBuilding);
                }
            }
        }
	}
}
