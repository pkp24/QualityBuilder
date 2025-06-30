using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace QualityBuilder
{
    public class QualityBuilderMod : Verse.Mod
    {
        public static string REPLACESTUFF_MOD_NAME = "Replace Stuff";
        public static string RS_TYPE_GENREPLACE = "Replace_Stuff.GenReplace";

        private QualityBuilderGlobalModSettings settings;

        private QualityBuilderModSettings currentSelectedSetting;
        bool isCurentMapSelected = true;

        public QualityBuilderMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<QualityBuilderGlobalModSettings>();
            var harmony = new Harmony("de.hatti.rimworld.mod.qualitybuilder");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            patchReplaceStuff(harmony);
        }

        private void patchReplaceStuff(Harmony harmony)
        {
            var available = LoadedModManager.RunningMods.Any(mod => mod.Name == REPLACESTUFF_MOD_NAME);

            if (available)
            {
                try
                {
                    // get the assembly
                    var RS_assembly = LoadedModManager
                                        .RunningMods.First(mod => mod.Name == REPLACESTUFF_MOD_NAME)
                                        .assemblies.loadedAssemblies.First(a => !a.FullName.StartsWith("0"));
                    if (RS_assembly == null)
                        throw new Exception("Replace Stuff not available");
                    var rsGenReplaceType = RS_assembly.GetType(RS_TYPE_GENREPLACE);
                    if (rsGenReplaceType == null)
                        throw new Exception("GenReplace type is not available. Loaded assemblyName " + RS_assembly.FullName);
                    var rsMethod = rsGenReplaceType.GetMethod("PlaceReplaceFrame");
                    if (rsMethod == null)
                        throw new Exception("Replace Stuff method to patch not available");
                    var postFix = typeof(Patch_ReplaceStuff).GetMethod("PostFix_PlaceReplaceFrame");
                    harmony.Patch(rsMethod, null, new HarmonyMethod(postFix));
                    Log.Message("QualityBuilder successfully patched Replace Stuff");
                }
                catch
                {
                    Log.Error("QualityBuilder is unable to patch Replace Stuff");
                    throw;
                }

            }
        }

        public override string SettingsCategory()
        {
            return "QualityBuilder".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            if (isCurentMapSelected)
                currentSelectedSetting = QualityBuilder_MapComponent.getAndEnsure(Find.CurrentMap)?.settings;
            if (currentSelectedSetting == null || !isCurentMapSelected)
            {
                currentSelectedSetting = QualityBuilderGlobalModSettings.getSettings();
                isCurentMapSelected = false;
            }

            // HelpRow for first
            // First row
            Listing_Standard firstRowList = new Listing_Standard();
            firstRowList.Begin(inRect);

            if (firstRowList.ButtonTextLabeled("QualityBuilder.SelectedSettings".Translate(), isCurentMapSelected ? "QualityBuilder.CurrentMap".Translate() : "QualityBuilder.Global".Translate()))
                buildSelectedSettingsFloatMenu();
            firstRowList.Gap(12f);
            // defaultUseQualityBuilder
            bool defaultUseQualityBuilder = currentSelectedSetting.defaultUseQualityBuilder;
            firstRowList.CheckboxLabeled("QualityBuilder.EnableByDefault".Translate(), ref defaultUseQualityBuilder, null);
            currentSelectedSetting.defaultUseQualityBuilder = defaultUseQualityBuilder;
            // IgnoreAtSkill
            int ignoreAtSkill = currentSelectedSetting.ignoreQualityBuilderAtSkill;
            firstRowList.Label("QualityBuilder.IgnoreAtSkill".Translate(ignoreAtSkill.ToString()));
            int num = Mathf.RoundToInt(firstRowList.Slider((float)currentSelectedSetting.ignoreQualityBuilderAtSkill, 0f, 20f));
            currentSelectedSetting.ignoreQualityBuilderAtSkill = num;
            // Default quality
            if (firstRowList.ButtonTextLabeled("QualityBuilder.MinQuality".Translate(), getMinQualityName()))
                buildMinQualityFloatMenu();
            // Override best Builder
            if (isCurentMapSelected)
            {
                if (firstRowList.ButtonTextLabeled("QualityBuilder.OverrideBestBuilder".Translate(), getBestConstructorOverrideName()))
                    buildOverrideBestConstructorFloatMenu();
            }

            firstRowList.End();
            /*
            // HelpRpw for Snd
            float gap = (inRect.width / 100) * 20;
            Rect sndHelpRect = new Rect(firstRowRect);
            sndHelpRect.x += firstRowRect.width + gap;
            sndHelpRect.width = 45;
            // Snd Row
            Rect sndRowRect = new Rect(sndHelpRect);
            sndRowRect.x += sndHelpRect.width + 5;
            Listing_Standard sndRowList = new Listing_Standard();
            sndRowList.ColumnWidth = sndRowRect.width;
            sndRowList.Begin(sndRowRect);

            sndRowList.End();
            /*
            Vector2 vector = new Vector2(120f, 40f);
            if (Widgets.ButtonText(new Rect(0f, 0f, vector.x, vector.y), "ResetButton".Translate()))
            {

            }
            */
        }

        private string getBestConstructorOverrideName()
        {
            if (currentSelectedSetting.bestConstructorOverride == null)
                return "None".Translate();
            return currentSelectedSetting.bestConstructorOverride.Name.ToStringShort;
        }

        private string getMinQualityName()
        {
            return Translator.Translate("QualityCategory_" + Enum.GetName(typeof(QualityCategory), currentSelectedSetting.defaultMinQualitySetting));
        }

        public void buildMinQualityFloatMenu()
        {
            List<FloatMenuOption> floatOptionList = new List<FloatMenuOption>();
            foreach (QualityCategory item in Enum.GetValues(typeof(QualityCategory)))
                floatOptionList.Add(new FloatMenuOption(Translator.Translate("QualityCategory_" + Enum.GetName(typeof(QualityCategory), item)), new Action(delegate
                {
                    currentSelectedSetting.defaultMinQualitySetting = item;
                })));
            Find.WindowStack.Add(new FloatMenu(floatOptionList));
        }

        private void buildSelectedSettingsFloatMenu()
        {
            List<FloatMenuOption> floatOptionList = new List<FloatMenuOption>();
            List<String> selectableValues = new List<string>();
            Map currentMap = Find.CurrentMap;
            if (currentMap != null)
            {
                floatOptionList.Add(new FloatMenuOption("QualityBuilder.CurrentMap".Translate(), new Action(delegate
                {
                    isCurentMapSelected = true;
                })));
            }
            floatOptionList.Add(new FloatMenuOption("QualityBuilder.Global".Translate(), new Action(delegate
            {
                currentSelectedSetting = QualityBuilderGlobalModSettings.getSettings();
                isCurentMapSelected = false;
            })));
            Find.WindowStack.Add(new FloatMenu(floatOptionList));
        }

        private void buildOverrideBestConstructorFloatMenu()
        {
            List<FloatMenuOption> floatOptionList = new List<FloatMenuOption>();
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
                return;
            floatOptionList.Add(new FloatMenuOption("None".Translate(), new Action(delegate
            {
                currentSelectedSetting.bestConstructorOverride = null;
            })));
            IEnumerable<Pawn> bestConstructors = currentMap.mapPawns.AllPawns.Where(p => p.IsColonist && p.workSettings.WorkIsActive(WorkTypeDefOf.Construction));
            foreach (Pawn pawn in bestConstructors)
                floatOptionList.Add(new FloatMenuOption(pawn.Name.ToStringShort, new Action(delegate
                {
                    currentSelectedSetting.bestConstructorOverride = pawn;
                })));
            Find.WindowStack.Add(new FloatMenu(floatOptionList));
        }

        private void doFirstRow()
        {

        }

        private void doSelectedSettingsControl(Listing_Standard firstRowList)
        {
            /*
            if (firstRowList.ButtonTextLabeled("Selected settings".Translate(), currentSelectedSettingName))
            {

            }
            */
        }

        private String getCurrentSelectedSettingsName()
        {
            return null;
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
        }
    }
}
