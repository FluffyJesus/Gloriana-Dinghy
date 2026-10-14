namespace GlorianaDinghy
{
    // Coil of the hoisting tackle fall, click = hoist / launch
    public class HoistCoil : GoPointerButton
    {
        public override void OnActivate()
        {
            DeckStow.Toggle();
        }
    }
}