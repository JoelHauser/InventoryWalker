# Inventory Walker

Walk with WASD while your inventory is open in raid.

Vanilla freezes you in place the moment you press Tab. This lets you keep moving: hold W, open
your inventory, and you keep walking. Sort your rig while you reposition, keep drifting toward
cover while you decide what to drop, close the inventory and carry straight on.

Mouse look, leaning and sprinting stay blocked while the inventory is open, exactly as they are
in vanilla. Your mouse belongs to the cursor there, and the camera swinging while you drag an
item would be unusable.

## What it covers

- Your own in raid inventory, on every tab along the top: Overall, Gear, Health, Skills, Map,
  Notes, Achievements and Prestige. Switching tabs while you are moving does not interrupt you.
- Every direction and combination, diagonals included.
- Opening and closing while a key is already held. You never have to release and press again.
- The inventory's opening and closing animation is untouched.
- The inventory stays fully usable. Click items, drag and drop, manage equipment, open context
  menus, switch tabs, all while walking. None of it interrupts a held key.

Out of raid, nothing changes. The hideout character screen is the same screen internally, so the
mod checks for a live raid player before doing anything.

## Install

Unzip over your SPT folder, so that `InventoryWalker.dll` lands in `BepInEx\plugins`. Client side
only; there is no server half and nothing is written to your profile.

To turn it off, use the BepInEx configuration manager (F12) and untick
**Move while the inventory is open**. It takes effect on the next frame, without a restart.

## FIKA

Supported, and it needs nothing from FIKA to be so.

Movement is delivered through the client's ordinary input path, ending at `Player.Move` and the
player's `MovementContext`. That is the same state FIKA reads when it replicates you to everyone
else, so your movement synchronises the way it always did. The mod adds no packets, patches
nothing FIKA patches, and has no FIKA reference.

Worth saying plainly: this is a real advantage over a player who is not running it. In a co-op
session, either everyone has it or the people who do can reposition while the others cannot.

## How it works

The interesting part is that being frozen in the inventory is not a flag, a lock, or a disabled
character controller. It is one line.

EFT's input is a tree of nodes. Each frame the input system zeroes an array of axis values and
refills it from the current state of every bound axis, then threads that array by reference down
the tree. Nodes are visited newest first, so a screen opened during a raid is reached before the
player's own node. `InputNode.TranslateInput` will only hand the array to a node
`if (axes != null)` — and `UIScreen.TranslateAxes` is, in full:

```csharp
axes = null;
```

Everything visited after the screen, the player included, is skipped. That is the freeze.

So this mod does not unblock a flag and does not drive the player itself. It puts a Harmony
prefix on that one method, and when the screen is the in raid inventory it flattens the look,
turn and lean slots, leaves the two movement slots exactly as the input system sampled them, and
declines to null the array. The player then moves down its ordinary path:

```
GamePlayerOwner.TranslateAxes
  -> PlayerOwner.TranslateAxes
    -> PlayerInputTranslator.TranslateAxes
      -> Player.Move(new Vector2(axes[MoveX], axes[MoveY]))
        -> MovementContext
```

That the array is rebuilt from current key state on every frame is what makes the transitions
seamless, and it is not something the mod arranges. Opening or closing the inventory changes only
whether the array reaches the player, never what is in it. There is no key-down event in the
movement path to miss, so a held key needs no re-press; and because nothing is latched anywhere,
releasing a key stops you on that frame. The mod stores no key state at all, which is what makes
stuck movement structurally impossible rather than merely unlikely.

## Why the inventory still works normally

Your mouse is not shared with the camera, and your clicks are not shared with the trigger. Those
are two separate paths in the client, and the mod is on neither of them.

The cursor is decided by `ShouldLockCursor`, which every node answers on every frame and which the
game resolves by taking the highest answer. The screen asks to show the cursor, the player asks to
lock it, and showing wins. That question is asked outside the branch this mod patches, so the mod
cannot affect it even though it changes what the player's node receives.

Clicks are commands rather than axes, and travel in a different list. The inventory screen removes
every command that is not on a five item allow-list before the player's node is reached, so left
click never becomes a trigger pull and right click never becomes aim down sights. Sprint, jump and
crouch are removed by the same rule, which is why this is WASD only. The mod patches the axis path
and touches the command path nowhere, so all of that is the game's own behaviour, unchanged.

What that leaves is the thing worth saying to a player: you can drag a magazine across your rig
while walking, and the drag will not swing your camera, will not fire your gun, and will not drop
the key you are holding.

## Known limitations

- **Sprint, jump, crouch and lean stay blocked.** Those are commands rather than axes, and the
  screen still consumes them. WASD only, which is what was asked for.
- **Looting a container opens the same screen**, so movement works there too. Whether walking out
  of range closes the window gracefully is not verified.
- **Sorting a stash and walking at once is not free.** You are moving while your eyes are in a
  menu, in a game where that gets you killed. That is the point of the mod, but it is worth
  knowing.

## Building

```
scripts\pack.ps1 -SPTPath C:\HUH
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
dotnet test tests\InventoryWalker.Tests
```

Run those through PowerShell, not Bash.

The plugin deliberately does not reference `Assembly-CSharp`. The copy in `Managed` is not the
assembly the game runs: the SPT Launcher applies a delta at startup that renames obfuscated
types, so a plugin compiled against the on-disk copy does not load. Every game member is resolved
by name at runtime in `GameTypes.cs`, and `pack.ps1` asserts the built DLL carries no reference to
the game assembly.
