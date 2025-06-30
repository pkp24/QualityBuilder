using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using System.Linq;

namespace QualityBuilder
{
	public class ModInitializerComponent : MonoBehaviour
	{
		private static readonly FieldInfo resolvedDesignatorsField = typeof(DesignationCategoryDef).GetField("resolvedDesignators", BindingFlags.Instance | BindingFlags.NonPublic);

		private static bool designatorInjectionPerformed;
        private static bool mapInitComplete;
        private static bool modActive = false;
        private static Action scheduledCallback;

		public static void ScheduleUpdateCallback(Action callback)
		{
			ModInitializerComponent.scheduledCallback = callback;
		}

		public void FixedUpdate()
		{
            if (Current.ProgramState == ProgramState.MapInitializing && !ModInitializerComponent.mapInitComplete)
            {
                modActive = modLoaded();
                if (!modActive)
                    return;
                int i = 0;
                List<ThingDef> list = DefDatabase<ThingDef>.AllDefs.ToList().FindAll( t => t.HasComp(typeof(CompQuality))  && t.building != null);
                list.ForEach(td =>
                {
                    ThingDef bpTD = td.blueprintDef;
                    if (bpTD != null)
                    {
                        List<CompProperties> comps = bpTD.comps;
                        if (!comps.OfType<CompProperties_QualityBuilderr>().Any())
                        {
                            comps.Add(new CompProperties_QualityBuilderr());
                            i++;
                        }
                    }
                    ThingDef frTD = td.frameDef;
                    if (frTD != null)
                    {
                        List<CompProperties> frComps = frTD.comps;
                        if (!frComps.OfType<CompProperties_QualityBuilderr>().Any())
                        {
                            frComps.Add(new CompProperties_QualityBuilderr());
                            i++;
                        }

                    }
                    if (td != null)
                    {
                        List<CompProperties> tdComps = td.comps;
                        if (!tdComps.OfType<CompProperties_QualityBuilderr>().Any())
                        {
                            tdComps.Add(new CompProperties_QualityBuilderr());
                            i++;
                        }

                    }
                });
                Log.Message("QualityBuilder added property to '" + i + "' things");
                mapInitComplete = true;
            }
            if (modActive && Current.ProgramState == ProgramState.Playing)
            {
                injectDesignators();
            }
		}

        private bool modLoaded()
        {
            return ModsConfig.ActiveModsInLoadOrder.ToList().Exists(mc => "QualityBuilder".EqualsIgnoreCase(mc.Name) && mc.Active);
        }

        private void injectDesignators()
        {
            if (ModInitializerComponent.designatorInjectionPerformed)
                return;
            foreach (DesignationCategoryDef current in DefDatabase<DesignationCategoryDef>.AllDefs)
            {
                if (!"Orders".EqualsIgnoreCase(current.defName))
                    continue;
                List<Designator> list = (List<Designator>)ModInitializerComponent.resolvedDesignatorsField.GetValue(current);
                if (list.Exists( d => d.GetType().Equals(typeof(_Designator_SkilledBuilder))))
                    break;
                list.Add(new _Designator_SkilledBuilder());
                list.Add(new _Designator_UnSkilledBuilder());
                Log.Message("QualityBuilder added to " + current.label + " category.");
            }
            designatorInjectionPerformed = true;
        }

        public void Update()
		{
			if (ModInitializerComponent.scheduledCallback != null)
			{
				Action action = ModInitializerComponent.scheduledCallback;
				ModInitializerComponent.scheduledCallback = null;
				action();
			}
		}

		public void OnLevelWasLoaded()
		{
			ModInitializerComponent.designatorInjectionPerformed = false;
            ModInitializerComponent.mapInitComplete = false;
        }
	}
}
