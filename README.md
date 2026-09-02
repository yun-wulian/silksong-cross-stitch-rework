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
- A successful guard consumes only the overlapping damage source that triggered it. That source cannot damage or be guarded again during the same contact lifecycle.
- Separation must be observed for two consecutive physics steps outside an enlarged release box, preventing hurtbox changes, small guard movement, or one-frame contact jitter from rearming the same hit.
- Attack and other skill inputs cancel immediately; held movement cancels after a short delay.
- The hero stays invulnerable for the complete successful-guard/counter-ready state. Any attack, counter, skill, tool, or movement action used to cancel that state carries a short additional invulnerability window; only a chained guard deliberately drops it.
- The success pose reuses the stable final guard frame, avoiding the apparent backward slide caused by the original `Parry Clash` sprite pivots.
- Debug mode can unlock the counterattack without changing `hasParry` or `defeatedPhantom`.
- The native `Parry Catch` backward velocity and deceleration are removed.
- Releasing attack lands the hero at the position where the counter began.
- Holding attack through the counter lands the hero 75% of the way toward the forward end of the slash hitbox, clamped before solid terrain.

The consumed-contact rule uses physics overlap rather than hero invulnerability,
enemy FSM state, or the current animation's mutable hurtbox. The native parry box
triggers a guard; release uses a `2.18 x 3.2498` local-space box that covers both the
native parry and grounded hurtboxes with another `0.5` units on every side. Enemy
bodies, melee hitboxes, and returning projectiles all become valid again only after
they have genuinely left that larger box.

## Optional independent guard binding

When Better Bindings 0.3.0 or newer is installed, this plugin registers a localized
`Cross Stitch Guard` shortcut in its Advanced keyboard menu. The shortcut starts
the native `PARRY` FSM path without requiring Cross Stitch to be equipped. The
equipped skill input remains available as a secondary route if the player chooses
to keep Cross Stitch in a skill slot. The independent shortcut has defensive input
priority and cancels an in-progress normal attack, including its recovery.

The shortcut supplies labels for every language currently supported by Better
Bindings and follows its live language-change notifications, so reopening the game
or rebuilding the menu is not required after changing languages.

The integration is a soft dependency loaded through the public API at runtime.
Without Better Bindings, Cross Stitch remains visible in the inventory and is
automatically placed into an otherwise empty neutral skill slot.

## Default timing

- Counter window and successful-guard invulnerability: 1.00 second
- Movement cancel delay: 0.10 seconds
- Action cancel invulnerability carry: 0.50 seconds

The values are written to `BepInEx/config/modcraft.silksong.cross-stitch-rework.cfg` after the first launch.

Set `Debug.UnlockCounterWithoutPhantom = true` in that file to test the counterattack before defeating Phantom.

## Build and install

Set `SILKSONG_GAME_ROOT` and build from the repository root:

```powershell
$env:SILKSONG_GAME_ROOT = 'C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight Silksong'
dotnet build .\CrossStitchRework.csproj -c Release
```

The successful build copies `CrossStitchRework.dll` to `BepInEx\plugins`. Pass `-p:DeployMod=false` to build without installing it.
