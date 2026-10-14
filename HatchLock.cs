using System;
using System.Linq;
using HarmonyLib;

namespace GlorianaDinghy
{
    public static class HatchLock
    {
        private static readonly string[] Names = { "grate_1", "grate_2" };

        public static bool IsCargoHatch(GPButtonTrapdoor t)
        {
            if (!Names.Contains(t.name)) return false;
            var p = t.transform.parent;
            return p != null && p.name == "gloriana";
        }
    }

    [HarmonyPatch(typeof(GPButtonTrapdoor), nameof(GPButtonTrapdoor.OnActivate), new Type[0])]
    internal static class TrapdoorPatch
    {
        private static bool Prefix(GPButtonTrapdoor __instance)
        {
            if (!DeckStow.IsStowed || !HatchLock.IsCargoHatch(__instance)) return true;
            if (__instance.IsOpen()) return true;   // closing is always allowed
            Plugin.Log.LogMessage("The dinghy is on the hatches.");
            return false;
        }
    }
}