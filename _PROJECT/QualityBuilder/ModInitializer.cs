using UnityEngine;
using Verse;

namespace QualityBuilder
{
    [StaticConstructorOnStartup]
    public class ModInitializer
	{
		private static GameObject obj;

		static ModInitializer()
		{
            if (!(ModInitializer.obj != null))
			{
                LongEventHandler.ExecuteWhenFinished(mau);
            }
        }

        public static void mau()
        {
            ModInitializer.obj = new GameObject("QualityBuilderLoader");
            ModInitializer.obj.AddComponent<ModInitializerComponent>();
            UnityEngine.Object.DontDestroyOnLoad(ModInitializer.obj);
        }
	}
}
