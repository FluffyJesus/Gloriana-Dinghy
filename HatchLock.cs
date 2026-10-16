using System;
using System.Linq;
using HarmonyLib;

namespace OldChronianDinghy
{
    internal static class HatchLock
    {
        public static bool IsCargoHatch(ShipProfile p, GPButtonTrapdoor t)
        {
            if (p.HatchNames.Length == 0 || !p.HatchNames.Contains(t.name)) return false;
            var par = t.transform.parent;
            return par != null && par.name == p.ModelName;
        }
    }

    [HarmonyPatch(typeof(GPButtonTrapdoor), nameof(GPButtonTrapdoor.OnActivate), new Type[0])]
    internal static class TrapdoorPatch
    {
        private static bool Prefix(GPButtonTrapdoor __instance)
        {
            if (__instance.IsOpen()) return true;   // closing is always allowed
            foreach (var s in ShipStow.All)
                if (s.IsStowed && HatchLock.IsCargoHatch(s.P, __instance))
                {
                    Plugin.Log.LogMessage("The dinghy is on the hatches.");
                    return false;
                }
            return true;
        }
    }
}