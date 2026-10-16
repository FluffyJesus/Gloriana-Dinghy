using System;
using UnityEngine;

namespace OldChronianDinghy
{
    internal enum SupportKind { HatchSpars, DeckPlanks }

    // Everything that differs between ships. WALK colliders share the local axes of the ship model.
    internal sealed class ShipProfile
    {
        public string Id, DisplayName, BoatPrefix, ModelName;
        public string[] HatchNames = new string[0];      // empty = hatches are not locked
        public SupportKind Supports;
        public Action<Transform, Transform> Tweaks;       // one-time changes on the ship model (model, walk)

        public string DavitsName => "davits_0_" + Id;    // stored in saves by Dinghies, never change

        // dinghy on deck
        public float StowX;                              // aft end of the dinghy, or its middle if StowXIsCenter
        public bool StowXIsCenter;
        public float CenterZ, Yaw = 180f, KeelOffset;
        public float ScaleLength = 1f, ScaleBeam = 1f, ScaleHeight = 1f;

        // wood
        public string MaterialSource;
        public float TileU = 0.3f, TileV = 0.5f;
        public bool WoodFlat;                            // true = no textures, plain colour below
        public float WoodR = 0.36f, WoodG = 0.25f, WoodB = 0.18f;
        public float ChockSizeX = 0.24f, ChockSizeZ = 0.14f;

        // HatchSpars: two timbers on chocks over the hatches
        public float SparD, SparLength, SparAft, SparFore, ChockInset, ChockSink;

        // DeckPlanks: planks across the ship on beams along the gunwales
        public int PlankCount = 3;
        public float PlankY, PlankZ, PlankW = 0.28f, PlankT = 0.07f, PlankAft = 0.2f, PlankFore = 0.8f;   // PlankY = underside
        public float BilgeZ = 0.6f, ChockMaxH = 0.5f;
        public float BeamW = 0.18f, BeamT = 0.11f, BeamOver = 0.02f, BeamEnd = 0.15f;   // BeamOver = plank overhang past the beam
        public float PostOver = 0.03f, PostSink = 0.02f, PostMaxH = 0.5f;   // bigger gap = no gangway under the beam

        // lashings
        public float LashWidth = 0.03f, LashInset = 0.15f, LashClearance = 0.01f, LashOut = 0.12f;

        // hoisting coil
        public float CoilDiameter = 0.65f, CoilX, CoilZ, CoilProbeY, CoilY, CoilSink = 0.02f;
        public string CoilFrom;                          // if set: x = (min x of this object) - CoilX

        // launch
        public float LaunchY = 0.5f, LaunchSide = 7f;

        public static readonly ShipProfile Gloriana = new ShipProfile
        {
            Id = "gloriana", DisplayName = "Gloriana", BoatPrefix = "BOAT GLORIANA", ModelName = "gloriana",
            HatchNames = new[] { "grate_1", "grate_2" },
            Supports = SupportKind.HatchSpars,
            Tweaks = ShipTweaks.Apply,
            StowX = 1.0f, StowXIsCenter = false, KeelOffset = -0.02f, CenterZ = 0f, Yaw = 180f,
            ScaleLength = 0.87f, ScaleBeam = 0.92f, ScaleHeight = 0.92f,
            MaterialSource = "grate_1",
            SparD = 0.16f, SparLength = 3.3f, SparAft = 0.25f, SparFore = 0.72f, ChockInset = 0.35f, ChockSink = 0.06f,
            CoilFrom = "structure_container/FIFE_MAIN", CoilX = 0.45f, CoilZ = 0f, CoilProbeY = 2.5f, CoilY = 1.0f,
            LaunchY = 0.5f, LaunchSide = 7f,
        };

        public static readonly ShipProfile Chronian = new ShipProfile
        {
            Id = "chronian", DisplayName = "Chronian", BoatPrefix = "BOAT CHRONIAN", ModelName = "chronian",
            Supports = SupportKind.DeckPlanks,
            StowX = 4.67f, StowXIsCenter = true, KeelOffset = 0.125f, CenterZ = 0f, Yaw = 0f,
            MaterialSource = "1_GRATES_RIGHT", WoodFlat = true,
            PlankY = 4.125f, PlankZ = 3.79f, PlankAft = 0.2f, PlankFore = 0.8f, BilgeZ = 0.6f,
            LashOut = 0.53f, LashClearance = -0.01f,
            CoilX = 0.31f, CoilZ = 0f, CoilProbeY = 2.77f, CoilY = 2.21f,
            LaunchY = 0.5f, LaunchSide = 7.5f,
        };
    }
}