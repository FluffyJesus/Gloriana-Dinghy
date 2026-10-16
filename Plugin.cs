using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace OldChronianDinghy
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "oldchroniandinghy";
        public const string NAME = "Old Chronian Dinghy";
        public const string VERSION = "1.1.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Harmony H;
        internal static Plugin Instance;

        internal static ConfigEntry<KeyCode> HoistKey;
        internal static ConfigEntry<float> MaxDistance;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            HoistKey = Config.Bind("Controls", "HoistKey", KeyCode.None,
                "Optional hotkey to hoist / launch the dinghy of the ship you stand on. The rope coil on deck does the same");
            MaxDistance = Config.Bind("Hoisting", "MaxDistance", 25f,
                "How close the dinghy must be to the ship to be hoisted (m)");

            ShipStow.All.Add(new ShipStow(ShipProfile.Gloriana));
            ShipStow.All.Add(new ShipStow(ShipProfile.Chronian));

            H = new Harmony(GUID);
            H.PatchAll();
            Log.LogInfo($"{NAME} {VERSION} loaded");
        }

        private void Update()
        {
            try { ShipStow.TickAll(); }
            catch (System.Exception e) { Log.LogError(e); }
        }
    }
}