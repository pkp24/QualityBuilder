using System;
using System.Collections.Generic;
using System.Text;

namespace QualityBuilder
{
    class QualityBuilderGlobalModSettings : Verse.ModSettings
    {
        QualityBuilderModSettings settings = new QualityBuilderModSettings();

        public override void ExposeData()
        {
            base.ExposeData();
            if (settings == null)
                settings = new QualityBuilderModSettings();
            settings.ExposeData();
        }

        public static QualityBuilderModSettings getSettings()
        {
            QualityBuilderMod mod = QualityBuilder.getMod();
            if (mod == null)
                return null;
            QualityBuilderGlobalModSettings globalSettings = mod.GetSettings<QualityBuilderGlobalModSettings>();
            if (globalSettings == null)
                return null;
            return globalSettings.settings;
        }
    }
}
