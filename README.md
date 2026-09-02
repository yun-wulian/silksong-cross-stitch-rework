# Cross Stitch Rework

A BepInEx 6 runtime rework of the Cross Stitch (`Parry` / `PARRY`) skill.

## Behaviour

- Cross Stitch guard access is visible in the inventory from the start, including on existing saves.
- If the current crest has an empty neutral skill slot, Cross Stitch is equipped without replacing another skill.
- The native `PlayerData.hasParry` flag is not forged. Defeating Phantom still performs the original acquisition and unlocks the counterattack.
- Guarding has no silk gate and costs no silk.
- A successful guard restores 3 silk.
- A successful guard no longer counters automatically.
- Press attack during the counter window to counter when `hasParry` is true and the current native `SilkSkillCost` can be paid; the same native cost is then consumed.
- Press Cross Stitch again after a successful guard to chain another guard.
- Attack and other skill inputs cancel immediately; held movement cancels after a short delay.
- The hero stays invulnerable for the complete successful-guard/counter-ready state; movement cancel carries a short additional invulnerability window.
- The success pose reuses the stable final guard frame, avoiding the apparent backward slide caused by the original `Parry Clash` sprite pivots.
- Debug mode can unlock the counterattack without changing `hasParry` or `defeatedPhantom`.
- The native `Parry Catch` backward velocity and deceleration are removed.
- Releasing attack lands the hero at the position where the counter began.
- Holding attack through the counter lands the hero at the forward end of the slash hitbox, clamped before solid terrain.

## Optional independent guard binding

When Better Bindings 0.3.0 or newer is installed, this plugin registers a localized
`Cross Stitch Guard` shortcut in its Advanced keyboard menu. The shortcut starts
the native `PARRY` FSM path without requiring Cross Stitch to be equipped. The
equipped skill input remains available as a secondary route if the player chooses
to keep Cross Stitch in a skill slot.

The shortcut supplies labels for every language currently supported by Better
Bindings and follows its live language-change notifications, so reopening the game
or rebuilding the menu is not required after changing languages.

The integration is a soft dependency loaded through the public API at runtime.
Without Better Bindings, Cross Stitch remains visible in the inventory and is
automatically placed into an otherwise empty neutral skill slot.

## Default timing

- Counter window and successful-guard invulnerability: 1.00 second
- Movement cancel delay: 0.10 seconds
- Movement cancel invulnerability carry: 0.50 seconds

The values are written to `BepInEx/config/modcraft.silksong.cross-stitch-rework.cfg` after the first launch.

Set `Debug.UnlockCounterWithoutPhantom = true` in that file to test the counterattack before defeating Phantom.

## Build and install

Set `SILKSONG_GAME_ROOT` and build from the repository root:

```powershell
$env:SILKSONG_GAME_ROOT = 'C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight Silksong'
dotnet build .\CrossStitchRework.csproj -c Release
```

The successful build copies `CrossStitchRework.dll` to `BepInEx\plugins`. Pass `-p:DeployMod=false` to build without installing it.
