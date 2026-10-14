# Gloriana Dinghy

A small add-on for **Sailwind** that lets you carry the cutter from the **Dinghies** mod
on board the **Gloriana** from the **Old Chronian** ship pack.

Instead of davits (which are an anachronism on an Elizabethan ship), the dinghy is
stowed the period way: in the ship's waist, on spare timbers laid across the cargo
hatches and lashed down with ropes.

## Features

- **Deck stowage** The cutter is hoisted aboard and rests on two timbers across the
  cargo hatches, following the slope of the deck, with chocks under the timbers and
  lashings over the tarp.
- **In-world control** Click the rope coil (the fall of the hoisting tackle) lying on
  the deck just aft of the main mast to hoist or launch the dinghy. Clicking the tarp
  with empty hands launches it too, like with the regular Dinghies davits.
- **Cargo hatches are locked** while the dinghy sits on them. You can always close an
  open hatch, but you can't open one. The dinghy can't be hoisted while a hatch is open.
- **Sensible launching** The dinghy goes into the water on the free side of the ship
  (away from a quay or another boat), level and at its normal floating height.
- **Saving works** Stowage is saved by Dinghies itself, so the dinghy is still on deck
  after you reload.
- **Small layout changes to make room**
  - the capstan is moved 1.55 m forward and scaled to 80 % (the anchor rope and the
    player collision follow it);
  - the fore sheet lead block is moved up the main mast and seized to it, so the sheet
    runs above the dinghy.

## Requirements

- [BepInEx 5](https://github.com/BepInEx/BepInEx)
- [Dinghies](https://github.com/alesparise/Dinghies-Sailwind-Mod) by Pr0SkyNesis
- Old Chronian ships by Aquilartz

## Installation

1. Install BepInEx, Dinghies and Old Chronian, and check that they work.
2. Put `GlorianaDinghy.dll` into `BepInEx/plugins/GlorianaDinghy/`.
3. Start the game.

## How to use

1. Bring the cutter alongside the Gloriana (within 25 m).
2. Get on board the Gloriana and close the cargo hatches.
3. Click the rope coil behind the main mast: the dinghy is hoisted aboard.
4. To launch it, click the coil again (or the tarp).

## Configuration

`BepInEx/config/glorianadinghy.cfg`:

| Option        | Default | Description                                              |
|---------------|---------|----------------------------------------------------------|
| `HoistKey`    | `None`  | Optional hotkey that does the same as clicking the coil. |
| `MaxDistance` | `25`    | How close the dinghy must be to be hoisted (m).          |

The layout values (dinghy position, timbers, capstan, etc.) are compiled into the mod.
To change them, edit `Settings.cs` and rebuild.

## ⚠️ Before uninstalling

**Launch the dinghy before removing this mod.** If you save with the dinghy on deck and
then remove the mod, Dinghies will look for the Gloriana's stowage on load, won't find
it, and the real cutter will stay hidden high above the ship.

## Known issues and notes

- **The stowed dinghy is shown slightly smaller** (87 % length, 92 % beam and height) so
  that it fits between the main mast fife rail and the capstan. Once launched, it is
  full size. This is a deliberate visual trade-off.
- **A dinghy may list heavily after you step from it straight onto another ship.** This
  is a vanilla Sailwind bug, not caused by this mod: when the player moves directly from
  one boat to another, the player's weight (about 160 kg) is not removed from the boat
  they left. To avoid it, step into the water first and then climb aboard. If it
  happens, hoisting the dinghy and launching it again fixes it.
- **Gloriana only.** The other Old Chronian ships are not affected.
- Tested with Old Chronian asset bundles modified for crew colliders and with the
  original bundles.
