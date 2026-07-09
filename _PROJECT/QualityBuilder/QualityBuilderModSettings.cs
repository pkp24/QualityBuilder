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

        public void ExposeData()
        {
            Scribe_Values.Look<bool>(ref this.defaultUseQualityBuilderInternal, "defaultUseQBuilder", true);
            Scribe_Values.Look<int>(ref this.skillDifferenceFromBestBuilderInternal, "qBuilderSkillDiff", 0);
            Scribe_Values.Look<int>(ref this.ignoreQualityBuilderAtSkillInternal, "qBuilderIgnoAtSkill", 20);
            Scribe_Values.Look<QualityCategory>(ref this.defaultMinQualitySettingInternal, "desiredMinQuality", QualityCategory.Awful, false);
            Scribe_References.Look<Pawn>(ref this.bestConstructorOverrideInternal, "bestConstructorOverride");
        }

        public void resetToDefault()
        {
            this.defaultUseQualityBuilderInternal = true;
            this.skillDifferenceFromBestBuilderInternal = 0;
            this.ignoreQualityBuilderAtSkillInternal = 20;
            this.defaultMinQualitySettingInternal = QualityCategory.Awful;
            this.bestConstructorOverride = null;
            this.bestConstructionSkillInternal = 0;
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

        private static QualityBuilderModSettings fallBack = new QualityBuilderModSettings();
    }
}
