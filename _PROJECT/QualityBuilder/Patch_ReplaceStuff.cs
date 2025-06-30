using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace QualityBuilder
{
    class Patch_ReplaceStuff
    {
        public static void PostFix_PlaceReplaceFrame(ref Frame __result, Thing oldThing, ThingDef stuff)
        {
            if (__result == null)
                return;
            var newComp = QualityBuilder.getCompQualityBuilder(oldThing);
            if (newComp == null)
                return;
            QualityBuilder.setSkilled(__result, newComp.desiredMinQuality, newComp.isSkilled);
        }

    }
}
