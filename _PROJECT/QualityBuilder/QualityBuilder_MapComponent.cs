using System;
using System.Collections.Generic;
using System.Text;
using Verse;
using RimWorld;

namespace QualityBuilder
{
    class QualityBuilder_MapComponent : Verse.MapComponent
    {
        public QualityBuilderModSettings settings { get; set; }
        bool useMapSettingsInternal = true;

        public bool useMapSettings
        {
            get { return useMapSettingsInternal; }
            set { useMapSettingsInternal = value; }
        }

        public QualityBuilder_MapComponent(Map map) : base(map)
        {
            settings = new QualityBuilderModSettings(QualityBuilderGlobalModSettings.getSettings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<bool>(ref this.useMapSettingsInternal, "qbUseMapSettings", false);
            if (settings == null)
                settings = new QualityBuilderModSettings(QualityBuilderGlobalModSettings.getSettings());
            settings.ExposeData();
        }

        public static QualityBuilder_MapComponent getAndEnsure(Map map)
        {
            if (map == null)
                return null;
            QualityBuilder_MapComponent comp = map.GetComponent<QualityBuilder_MapComponent>();
            if (comp == null)
            {
                comp = new QualityBuilder_MapComponent(map);
                comp.settings = new QualityBuilderModSettings(QualityBuilderGlobalModSettings.getSettings());
                map.components.Add(comp);
            }
            return comp;
        }
    }
}
