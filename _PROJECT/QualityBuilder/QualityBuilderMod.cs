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
        public static string REPLACESTUFF_PACKAGE_ID = "Uuugggg.ReplaceStuff";
        public static string RS_TYPE_GENREPLACE = "Replace_Stuff.GenReplace";

        private QualityBuilderGlobalModSettings settings;

        private QualityBuilderModSettings currentSelectedSetting;
        // Opens on the default (global) settings page; the player can click the "Selected
        // settings" box to switch to the current map's own settings instead.
        bool isCurentMapSelected = false;

        // Slider position (not a stored value) that represents "unlimited" rebuild attempts.
        private const int UnlimitedRebuildAttemptsSliderPos = 21;

        // Layout of the "Selected settings" row: the left share holds the label + scope button,
        // the remainder holds the "Default settings" status light.
        private const float SelectedSettingsRowHeight = 30f;
        private const float SelectedSettingsSelectorPct = 0.72f;
        private const float StatusLightSize = 12f;
        private static readonly Color DefaultSettingsActiveColor = new Color(0.4f, 0.85f, 0.4f);
        private static readonly Color DefaultSettingsInactiveColor = new Color(0.9f, 0.42f, 0.35f);
        private static readonly Color StatusLightOutlineColor = new Color(0f, 0f, 0f, 0.6f);

        public QualityBuilderMod(ModContentPack content) : base(content)
        {
            QualityBuilder.modInstance = this;
            settings = GetSettings<QualityBuilderGlobalModSettings>();
            var harmony = new Harmony("de.hatti.rimworld.mod.qualitybuilder");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            patchReplaceStuff(harmony);
        }

        private void patchReplaceStuff(Harmony harmony)
        {
            // Detect by packageId first (stable across renames/translations), display name as
            // fallback for very old Replace Stuff releases.
            var rsMod = LoadedModManager.RunningMods.FirstOrDefault(mod =>
                REPLACESTUFF_PACKAGE_ID.Equals(mod.PackageId, StringComparison.OrdinalIgnoreCase)
                || mod.Name == REPLACESTUFF_MOD_NAME);
            if (rsMod == null)
                return;

            try
            {
                // Pick the assembly that actually contains the expected type instead of
                // guessing by name.
                var rsGenReplaceType = rsMod.assemblies.loadedAssemblies
                                            .Select(a => a.GetType(RS_TYPE_GENREPLACE))
                                            .FirstOrDefault(t => t != null);
                if (rsGenReplaceType == null)
                    throw new Exception("GenReplace type is not available in any Replace Stuff assembly");
                var rsMethod = rsGenReplaceType.GetMethod("PlaceReplaceFrame");
                if (rsMethod == null)
                    throw new Exception("Replace Stuff method to patch not available");
                var postFix = typeof(Patch_ReplaceStuff).GetMethod("PostFix_PlaceReplaceFrame");
                harmony.Patch(rsMethod, null, new HarmonyMethod(postFix));
                Log.Message("QualityBuilder successfully patched Replace Stuff");
            }
            catch (Exception ex)
            {
                // Don't rethrow: throwing out of the Mod constructor unregisters the settings
                // handle. QualityBuilder works without the Replace Stuff integration.
                Log.Warning("QualityBuilder is unable to patch Replace Stuff; continuing without Replace Stuff integration. " + ex);
            }
        }

        public override string SettingsCategory()
        {
            // Not a translation key: it's the mod's own display name/version, sourced from
            // About.xml. Calling .Translate() on it looked up a nonexistent key, which in Dev
            // Mode renders as pseudo-translated gibberish (Verse.Translator.PseudoTranslated).
            return Content.Name;
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

            doSelectedSettingsRow(firstRowList);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            firstRowList.Label("QualityBuilder.SelectedSettingsHint".Translate());
            // Spells out what the status light means, so the player doesn't have to guess why the
            // page they're editing may not be reaching their colony.
            firstRowList.Label(QualityBuilderModSettings.getDefaultSettingsStatusNote(Find.CurrentMap));
            GUI.color = Color.white;
            firstRowList.Gap(12f);
            // Whether this map uses its own settings; when off, the global "Default settings"
            // page drives QualityBuilder on this map (see QualityBuilderModSettings.getSettings).
            if (isCurentMapSelected)
            {
                QualityBuilder_MapComponent mapComp = QualityBuilder_MapComponent.getAndEnsure(Find.CurrentMap);
                if (mapComp != null)
                {
                    bool useMapSettings = mapComp.useMapSettings;
                    firstRowList.CheckboxLabeled("QualityBuilder.UseMapSettings".Translate(), ref useMapSettings, null);
                    mapComp.useMapSettings = useMapSettings;
                }
            }
            // defaultUseQualityBuilder
            bool defaultUseQualityBuilder = currentSelectedSetting.defaultUseQualityBuilder;
            firstRowList.CheckboxLabeled("QualityBuilder.EnableByDefault".Translate(), ref defaultUseQualityBuilder, null);
            currentSelectedSetting.defaultUseQualityBuilder = defaultUseQualityBuilder;
            // IgnoreAtSkill
            int ignoreAtSkill = currentSelectedSetting.ignoreQualityBuilderAtSkill;
            firstRowList.Label("QualityBuilder.IgnoreAtSkill".Translate(ignoreAtSkill.ToString()));
            int num = Mathf.RoundToInt(firstRowList.Slider((float)currentSelectedSetting.ignoreQualityBuilderAtSkill, 0f, 20f));
            currentSelectedSetting.ignoreQualityBuilderAtSkill = num;
            // Minimum construction skill for quality builds, anchored to the best constructor.
            // Stored as a difference from the best so it keeps tracking level-ups.
            int bestSkill = 0;
            if (isCurentMapSelected && Find.CurrentMap != null)
                bestSkill = QualityBuilderModSettings.getBestConstructionSkill(Find.CurrentMap);
            if (bestSkill > 0)
            {
                int curMinSkill = Mathf.Clamp(bestSkill - currentSelectedSetting.skillDifferenceFromBestBuilder, 0, bestSkill);
                firstRowList.Label("QualityBuilder.MinSkill".Translate(curMinSkill, bestSkill));
                int newMinSkill = Mathf.RoundToInt(firstRowList.Slider((float)curMinSkill, 0f, (float)bestSkill));
                currentSelectedSetting.skillDifferenceFromBestBuilder = bestSkill - newMinSkill;
            }
            else
            {
                int diff = Mathf.Clamp(currentSelectedSetting.skillDifferenceFromBestBuilder, 0, 20);
                firstRowList.Label("QualityBuilder.MinSkillDiff".Translate(diff));
                currentSelectedSetting.skillDifferenceFromBestBuilder = Mathf.RoundToInt(firstRowList.Slider((float)diff, 0f, 20f));
            }
            // Default quality
            if (firstRowList.ButtonTextLabeled("QualityBuilder.MinQuality".Translate(), getMinQualityName()))
                buildMinQualityFloatMenu();
            // Override best Builder
            if (isCurentMapSelected)
            {
                if (firstRowList.ButtonTextLabeled("QualityBuilder.OverrideBestBuilder".Translate(), getBestConstructorOverrideName()))
                    buildOverrideBestConstructorFloatMenu();
            }
            // How many deconstruct->rebuild cycles QB may spend chasing the min quality before
            // giving up on a building. Slider maxes out at "unlimited" (never give up).
            int curMaxRebuildAttempts = currentSelectedSetting.maxQualityRebuildAttempts;
            int rebuildSliderPos = curMaxRebuildAttempts == QualityBuilderModSettings.UnlimitedRebuildAttempts
                ? UnlimitedRebuildAttemptsSliderPos
                : Mathf.Clamp(curMaxRebuildAttempts, 1, UnlimitedRebuildAttemptsSliderPos - 1);
            string rebuildAttemptsLabel = curMaxRebuildAttempts == QualityBuilderModSettings.UnlimitedRebuildAttempts
                ? "∞"
                : curMaxRebuildAttempts.ToString();
            firstRowList.Label("QualityBuilder.MaxRebuildAttempts".Translate(rebuildAttemptsLabel));
            int newRebuildSliderPos = Mathf.RoundToInt(firstRowList.Slider((float)rebuildSliderPos, 1f, (float)UnlimitedRebuildAttemptsSliderPos));
            currentSelectedSetting.maxQualityRebuildAttempts = newRebuildSliderPos >= UnlimitedRebuildAttemptsSliderPos
                ? QualityBuilderModSettings.UnlimitedRebuildAttempts
                : newRebuildSliderPos;
            // Mechanoids never do skilled builds (both scopes resolved via currentSelectedSetting).
            bool excludeMechs = currentSelectedSetting.excludeMechsFromSkilledBuilds;
            firstRowList.CheckboxLabeled("QualityBuilder.ExcludeMechs".Translate(), ref excludeMechs, "QualityBuilder.ExcludeMechsTooltip".Translate());
            currentSelectedSetting.excludeMechsFromSkilledBuilds = excludeMechs;
            // Show the quality gizmo only on blueprints/frames (hide it on finished buildings).
            bool gizmoOnBlueprintsOnly = currentSelectedSetting.gizmoOnBlueprintsOnly;
            firstRowList.CheckboxLabeled("QualityBuilder.GizmoOnBlueprintsOnly".Translate(), ref gizmoOnBlueprintsOnly, "QualityBuilder.GizmoOnBlueprintsOnlyTooltip".Translate());
            currentSelectedSetting.gizmoOnBlueprintsOnly = gizmoOnBlueprintsOnly;

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

        // "Selected settings [ scope ]" plus the status light beside it. Laid out by hand instead
        // of via Listing_Standard.ButtonTextLabeled because that helper always consumes the full
        // column width (label 0-50%, button 50-100%), leaving nowhere to put the light.
        private void doSelectedSettingsRow(Listing_Standard list)
        {
            Rect row = list.GetRect(SelectedSettingsRowHeight);
            Rect selectorRect = row.LeftPart(SelectedSettingsSelectorPct);
            Rect labelRect = selectorRect.LeftHalf();
            Rect buttonRect = selectorRect.RightHalf();

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, "QualityBuilder.SelectedSettings".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            string selectedScope = isCurentMapSelected
                ? "QualityBuilder.CurrentMap".Translate()
                : "QualityBuilder.Global".Translate();
            if (Widgets.ButtonText(buttonRect, selectedScope.Truncate(buttonRect.width - 20f)))
                buildSelectedSettingsFloatMenu();

            doDefaultSettingsLight(row.RightPart(1f - SelectedSettingsSelectorPct));
            list.Gap(list.verticalSpacing);
        }

        // Reports whether the global "Default settings" page is what actually drives the CURRENT
        // MAP. Deliberately reflects the map's real state rather than whichever page is being
        // viewed: "I'm editing a page that isn't reaching my colony" is the situation this exists
        // to surface, and that only shows up while the Default page is selected.
        private void doDefaultSettingsLight(Rect rect)
        {
            Map map = Find.CurrentMap;
            bool active = QualityBuilderModSettings.areDefaultSettingsActive(map);
            Color statusColor = active ? DefaultSettingsActiveColor : DefaultSettingsInactiveColor;

            Rect lightRect = new Rect(rect.x + 8f, rect.y + (rect.height - StatusLightSize) / 2f,
                                      StatusLightSize, StatusLightSize);
            Widgets.DrawBoxSolidWithOutline(lightRect, statusColor, StatusLightOutlineColor, 1);

            Rect textRect = new Rect(lightRect.xMax + 6f, rect.y, rect.xMax - lightRect.xMax - 6f, rect.height);
            Color previousColor = GUI.color;
            GUI.color = statusColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(textRect, QualityBuilderModSettings.getDefaultSettingsStatusLabel(map));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previousColor;

            TooltipHandler.TipRegion(rect, QualityBuilderModSettings.getDefaultSettingsStatusNote(map));
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
