using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GlorianaDinghy
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "glorianadinghy";
        public const string NAME = "Gloriana Dinghy";
        public const string VERSION = "1.0.0";

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
                "Optional hotkey to hoist / launch the dinghy. The rope coil behind the main mast does the same");
            MaxDistance = Config.Bind("Hoisting", "MaxDistance", 25f,
                "How close the dinghy must be to Gloriana to be hoisted (m)");

            H = new Harmony(GUID);
            H.PatchAll();
            Log.LogInfo($"{NAME} {VERSION} loaded");
        }

        private void Update()
        {
            try { DeckStow.Tick(); }
            catch (System.Exception e) { Log.LogError(e); }
        }
    }
}