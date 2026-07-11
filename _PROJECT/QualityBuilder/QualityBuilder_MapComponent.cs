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
        // Fresh maps follow the live "Default settings" page until the player explicitly opts
        // this specific map into its own independent copy. (The ExposeData default below stays
        // true so existing saves keep their historical per-map behavior on load.)
        bool useMapSettingsInternal = false;

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
            // Default true: saves from versions without this flag (and fresh maps) keep the
            // historical behavior of using per-map settings.
            Scribe_Values.Look<bool>(ref this.useMapSettingsInternal, "qbUseMapSettings", true);
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
