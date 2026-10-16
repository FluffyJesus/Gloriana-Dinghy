namespace OldChronianDinghy
{
    // Coil of the hoisting tackle fall, click = hoist / launch
    public class HoistCoil : GoPointerButton
    {
        internal ShipStow Owner;

        public override void OnActivate()
        {
            Owner?.Toggle();
        }
    }
}