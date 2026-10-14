namespace GlorianaDinghy
{
    // Final tuned values. Change here and rebuild if something needs adjusting.
    internal static class S
    {
        // dinghy on deck
        public const float AftX = 1.0f, KeelOffset = -0.02f, CenterZ = 0f, Yaw = 180f;
        public const float ScaleLength = 0.87f, ScaleBeam = 0.92f, ScaleHeight = 0.92f;

        // timbers and chocks
        public const float SparD = 0.16f, SparLength = 3.3f, SparAft = 0.25f, SparFore = 0.72f;
        public const float ChockInset = 0.35f, ChockSink = 0.06f, ChockSizeX = 0.24f, ChockSizeZ = 0.14f;
        public const float TileU = 0.3f, TileV = 0.5f, OffsetU = 0f, OffsetV = 0f;
        public const string MaterialSource = "grate_1";

        // lashings
        public const float LashWidth = 0.03f, LashInset = 0.15f, LashClearance = 0.01f;

        // capstan
        public const float CapstanShift = 1.55f, CapstanScale = 0.8f;

        // fore sheet block
        public const string BlockName = "PULLEY_foremast";
        public const float BlockLift = 1.8f, BlockGap = -0.01f, BlockSpin = 90f, BlockTilt = 0f;

        // hoisting coil
        public const float CoilDiameter = 0.65f, CoilBehindFife = 0.45f, CoilSideZ = 0f, CoilSink = 0.02f;

        // launch
        public const float LaunchY = 0.5f, LaunchSide = 7f;
    }
}