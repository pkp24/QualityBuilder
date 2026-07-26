using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;

namespace QualityBuilder
{
    class QualityBuilderModSettings
    {
        bool defaultUseQualityBuilderInternal = true;
        int skillDifferenceFromBestBuilderInternal = 0;
        int ignoreQualityBuilderAtSkillInternal = 20;
        QualityCategory defaultMinQualitySettingInternal = QualityCategory.Awful;
        Pawn bestConstructorOverrideInternal;
        int bestConstructionSkillInternal = 0;
        Stopwatch bestConstructorCheckWatch;
        int maxQualityRebuildAttemptsInternal = DefaultMaxQualityRebuildAttempts;
        bool excludeMechsFromSkilledBuildsInternal = false;
        bool gizmoOnBlueprintsOnlyInternal = false;

        // How many QB-initiated deconstruct->rebuild cycles are allowed before giving up on a
        // building whose min quality the colony can't roll (each cycle burns ~50% materials).
        public const int DefaultMaxQualityRebuildAttempts = 3;
        // Sentinel stored/returned when the player drags the rebuild-attempts slider to its max
        // (displayed as "∞"): never give up retrying for the desired quality.
        public const int UnlimitedRebuildAttempts = int.MaxValue;

        public QualityBuilderModSettings()
        { }

        public QualityBuilderModSettings(QualityBuilderModSettings clone)
        {
            if (clone == null)
                clone = fallBack;
            this.bestConstructorOverride = clone.bestConstructorOverride;
            this.skillDifferenceFromBestBuilder = clone.skillDifferenceFromBestBuilder;
            this.ignoreQualityBuilderAtSkill = clone.ignoreQualityBuilderAtSkill;
            this.defaultUseQualityBuilder = clone.defaultUseQualityBuilder;
            this.defaultMinQualitySetting = clone.defaultMinQualitySetting;
            this.maxQualityRebuildAttempts = clone.maxQualityRebuildAttempts;
            this.excludeMechsFromSkilledBuilds = clone.excludeMechsFromSkilledBuilds;
            this.gizmoOnBlueprintsOnly = clone.gizmoOnBlueprintsOnly;
        }

        public Pawn bestConstructorOverride
        {
            get { return bestConstructorOverrideInternal; }
            set { bestConstructorOverrideInternal = value; }
        }

        public bool defaultUseQualityBuilder
        {
            get { return defaultUseQualityBuilderInternal; }
            set { defaultUseQualityBuilderInternal = value; }
        }

        public int skillDifferenceFromBestBuilder
        {
            get { return skillDifferenceFromBestBuilderInternal; }
            set { skillDifferenceFromBestBuilderInternal = value; }
        }

        public int ignoreQualityBuilderAtSkill
        {
            get { return ignoreQualityBuilderAtSkillInternal; }
            set { ignoreQualityBuilderAtSkillInternal = value; }
        }

        public QualityCategory defaultMinQualitySetting
        {
            get { return defaultMinQualitySettingInternal; }
            set { defaultMinQualitySettingInternal = value; }
        }

        public int maxQualityRebuildAttempts
        {
            get { return maxQualityRebuildAttemptsInternal; }
            set { maxQualityRebuildAttemptsInternal = value; }
        }

        public bool excludeMechsFromSkilledBuilds
        {
            get { return excludeMechsFromSkilledBuildsInternal; }
            set { excludeMechsFromSkilledBuildsInternal = value; }
        }

        public bool gizmoOnBlueprintsOnly
        {
            get { return gizmoOnBlueprintsOnlyInternal; }
            set { gizmoOnBlueprintsOnlyInternal = value; }
        }

        public void ExposeData()
        {
            Scribe_Values.Look<bool>(ref this.defaultUseQualityBuilderInternal, "defaultUseQBuilder", true);
            Scribe_Values.Look<int>(ref this.skillDifferenceFromBestBuilderInternal, "qBuilderSkillDiff", 0);
            Scribe_Values.Look<int>(ref this.ignoreQualityBuilderAtSkillInternal, "qBuilderIgnoAtSkill", 20);
            Scribe_Values.Look<QualityCategory>(ref this.defaultMinQualitySettingInternal, "desiredMinQuality", QualityCategory.Awful, false);
            Scribe_References.Look<Pawn>(ref this.bestConstructorOverrideInternal, "bestConstructorOverride");
            Scribe_Values.Look<int>(ref this.maxQualityRebuildAttemptsInternal, "qBuilderMaxRebuildAttempts", DefaultMaxQualityRebuildAttempts);
            Scribe_Values.Look<bool>(ref this.excludeMechsFromSkilledBuildsInternal, "excludeMechsFromSkilledBuilds", false);
            Scribe_Values.Look<bool>(ref this.gizmoOnBlueprintsOnlyInternal, "gizmoOnBlueprintsOnly", false);
        }

        public void resetToDefault()
        {
            this.defaultUseQualityBuilderInternal = true;
            this.skillDifferenceFromBestBuilderInternal = 0;
            this.ignoreQualityBuilderAtSkillInternal = 20;
            this.defaultMinQualitySettingInternal = QualityCategory.Awful;
            this.bestConstructorOverride = null;
            this.bestConstructionSkillInternal = 0;
            this.maxQualityRebuildAttemptsInternal = DefaultMaxQualityRebuildAttempts;
            this.excludeMechsFromSkilledBuildsInternal = false;
            this.gizmoOnBlueprintsOnlyInternal = false;
        }

        internal static QualityBuilderModSettings getSettings(Map map)
        {
            QualityBuilderModSettings settings = null;
            if (map != null)
            {
                QualityBuilder_MapComponent mapComp = QualityBuilder_MapComponent.getAndEnsure(map);
                // When the map opts out of per-map settings, fall through to the global
                // ("Default settings") page so it actually affects the current game.
                if (mapComp != null && mapComp.useMapSettings)
                    settings = mapComp.settings;
            }
            if (settings == null)
                settings = QualityBuilderGlobalModSettings.getSettings();
            if (settings == null)
                settings = QualityBuilderModSettings.fallBack;
            return settings;
        }

        public static bool getDefaultUseQualityBuilder(Map map)
        {
            return getSettings(map).defaultUseQualityBuilderInternal;
        }

        public static int getSkillDifferenceFromBestBuilder(Map map)
        {
            return getSettings(map).skillDifferenceFromBestBuilderInternal;
        }

        public static int getBestConstructionSkill(Map map)
        {
            return getSettings(map).getBestConstructionSkillCached(map);
        }

        public int getBestConstructionSkillCached(Map map)
        {
            if (bestConstructorCheckWatch == null)
                bestConstructorCheckWatch = new Stopwatch();
            if (bestConstructorCheckWatch.ElapsedMilliseconds > 10000 || !bestConstructorCheckWatch.IsRunning) // 10 seconds
            {
                bestConstructionSkillInternal = QualityBuilder.getBestConstructorSkill(map);
#if DEBUG
                Log.Message("QualityBuilder recalc best pawn with best skill " + bestConstructionSkillInternal);
#endif
                bestConstructorCheckWatch.Restart();
            }
            return bestConstructionSkillInternal;
        }

        public static int getIgnoreQualityBuilderAtSkill(Map map)
        {
            return getSettings(map).ignoreQualityBuilderAtSkillInternal;
        }

        public static QualityCategory getDefaultMinQualitySetting(Map map)
        {
            return getSettings(map).defaultMinQualitySettingInternal;
        }

        public static Pawn getBestConstructorOverride(Map map)
        {
            return getSettings(map).bestConstructorOverrideInternal;
        }

        public static int getMaxQualityRebuildAttempts(Map map)
        {
            return getSettings(map).maxQualityRebuildAttemptsInternal;
        }

        public static bool getExcludeMechsFromSkilledBuilds(Map map)
        {
            return getSettings(map).excludeMechsFromSkilledBuildsInternal;
        }

        public static bool getGizmoOnBlueprintsOnly(Map map)
        {
            return getSettings(map).gizmoOnBlueprintsOnlyInternal;
        }

        // --- "Default settings" status indicator (settings window) ------------------------------
        // Whether the global "Default settings" page is what actually drives QualityBuilder on
        // this map right now. Mirrors the branch in getSettings(Map) above: the map only escapes
        // the global page by explicitly opting into its own settings. Kept as a separate static
        // (rather than inline in the settings window) so the indicator's state is assertable
        // outside OnGUI. Read-only on purpose: GetComponent, not getAndEnsure, so merely drawing
        // the settings window never creates a map component. A map with no component yet has
        // never opted in, which is the same answer getAndEnsure would produce.
        public static bool areDefaultSettingsActive(Map map)
        {
            if (map == null)
                return true; // No map loaded (e.g. main menu): only the global page exists.
            QualityBuilder_MapComponent mapComp = map.GetComponent<QualityBuilder_MapComponent>();
            return mapComp == null || !mapComp.useMapSettings;
        }

        public static string getDefaultSettingsStatusLabel(Map map)
        {
            return areDefaultSettingsActive(map)
                ? "QualityBuilder.DefaultSettingsActive".Translate()
                : "QualityBuilder.DefaultSettingsInactive".Translate();
        }

        public static string getDefaultSettingsStatusNote(Map map)
        {
            // The settings window is reachable from the main menu, where the "Current map" scope
            // isn't offered at all -- telling the player to tick a checkbox that isn't there would
            // be worse than saying nothing, so that case gets its own note.
            if (map == null)
                return "QualityBuilder.DefaultSettingsNoMapNote".Translate();
            return areDefaultSettingsActive(map)
                ? "QualityBuilder.DefaultSettingsActiveNote".Translate()
                : "QualityBuilder.DefaultSettingsInactiveNote".Translate();
        }

        private static QualityBuilderModSettings fallBack = new QualityBuilderModSettings();
    }
}
