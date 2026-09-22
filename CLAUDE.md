# InventoryWalker -- working notes for Claude

Lets the player walk with WASD while the in raid inventory is open. One BepInEx plugin, one
Harmony prefix, one patched method. No server half, nothing written to the profile.

**Nothing in this repo has executed in the game.** Everything below was read off the client's IL
on 2026-09-22. The sibling repos' hardest lesson applies here in full: *an IL trace that predicts
success is not evidence of success.* UltrawideStash flipped its Sorting Table conclusion four
times, three of them confident and wrong, and the thing that settled it was starting the game.

## The box this was built on

| | development | live |
| --- | --- | --- |
| SPT install | `C:\HUH` -- never launched | `H:\SPT4.1.X` |
| EFT client | `0.16.9.5.40743` | same |

`C:\HUH` does not exist on the live machine and `H:` does not exist on the development one. Pass
`-SPTPath` explicitly.

```
scripts\pack.ps1 -SPTPath C:\HUH
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
dotnet test tests\InventoryWalker.Tests
```

**Run those through PowerShell, not Bash** -- the `C:HUH` mangling trap, same as the siblings.

## The finding the whole mod rests on

Being frozen with the inventory open is not a flag, a lock or a disabled character controller.
It is one line of one method.

`EFT.UI.Screens.UIScreen.TranslateAxes(ref float[] axes)`, in its entirety:

```
IL_0000  ldarg.1
IL_0001  ldnull
IL_0002  stind.ref
IL_0003  ret
```

`axes = null;`. And `EFT.InputSystem.InputNode.TranslateInput` only calls a node's `TranslateAxes`
`if (axes != null)` (IL_0042, `ldarg.2; ldind.ref; brfalse.s`). The array is threaded **by
reference** through the tree, so once a screen nulls it every node visited afterwards is skipped,
the player's own among them.

`EFT.InputSystem.InputNodeAbstract.TranslateInput` visits children **newest first** -- it copies
`_children` into a scratch list by `Insert(0, child)`, which reverses it. So a screen opened
during a raid is reached before `GamePlayerOwner`, which was added at raid start.

`EFT.UI.InventoryScreen` does not override `TranslateAxes`. Its chain is
`InventoryScreen -> EftScreen'2 -> BaseScreen'3 -> UIScreen -> UIInputNode -> InputNode`, and
`UIScreen` is the only ancestor that declares it. So patching `UIScreen.TranslateAxes` and
filtering on the instance type is enough, and it leaves every other screen nulling as before.

## Why nothing needs to latch, and why that is the whole answer to the brief

The brief's central requirement is uninterrupted movement across opening and closing, with a
warning not to latch movement on. **Both fall out of the architecture and neither needs any code.**

`\uEAB1.UpdateInput(List<ECommand> commands, float[] axis, float deltaTime)` -- the input source
`InputManager.Update` calls every frame -- does this:

```
for (j = 0; j < axis.Length; j++) axis[j] = 0f;              // IL_00A9..IL_00BD
foreach (combination in _axes)
    if (Mathf.Abs(axis[c.IntAxis]) < 0.0001f)
        axis[c.IntAxis] = c.GetValue();                      // IL_00CF..IL_0113
```

The array is **zeroed and refilled from current axis state on every single frame**. Movement is
continuous state, not key events. There is no key-down edge anywhere in the movement path.

Therefore: opening or closing the inventory changes only *whether* the array reaches the player,
never *what is in it*. A held W is re-sampled as 1.0 on the frame after the screen opens exactly
as on the frame before. A released W is re-sampled as 0.0. The mod stores no key state at all,
which is what makes stuck movement structurally impossible rather than merely unlikely.

**Do not add key tracking to this mod.** It would be strictly worse than the thing it replaced.

## The axis map, and why it is load bearing

`EFT.InputSystem.EAxis`:

| | |
| --- | --- |
| 0 | `MoveX` -- strafe, A/D |
| 1 | `MoveY` -- forward/back, W/S |
| 2 | `TurnX` -- mouse look horizontal |
| 3 | `TurnY` -- mouse look vertical |
| 4 | `LookX` -- free look (Alt) horizontal |
| 5 | `LookY` -- free look vertical |
| 6 | `LeanX` -- lean |

`PlayerInputTranslator.TranslateAxes` (obfuscated as `\uEA50`) consumes the array **by position**:

```
Player.Look(axes[4] * sensX, axes[5] * sensY, true)
if (MovementContext.IsAxesIgnored) return;
if (MovementContext.BlindFire == 0)
    Player.Move(new Vector2(axes[0], axes[1]))
Player.Rotate(new Vector2(axes[2] * sensX, axes[3] * sensY), false)
Player.SlowLean(axes[6])
```

Read by index, so a slot moving would route WASD into the look handler rather than fail loudly.
`AxisGateTests.TheAxisMapMatchesTheClient` pins all seven. **Write the assertion even when the
claim feels obvious** -- UltrawideStash found a confident wrong constant exactly this way.

`MovementContext.IsAxesIgnored` is a separate gate the game still owns, so scripted states that
stop the player still stop the player. Good; leave it alone.

## Why this is a passthrough and not a `Player.Move` call

The first design considered was to read WASD in the plugin's `Update` and call `Player.Move`
directly, bypassing the block entirely. **That would have been worse**, and the reason is FIKA:

Movement arriving through the ordinary path lands in `MovementContext`, which is the state any
co-op replication reads. Calling `Player.Move` from outside might well do the same, but it would
be a second writer racing the first, and it would need verifying against a mod this box does not
have installed. The passthrough adds no packets, patches nothing FIKA patches, and needs no FIKA
reference. **Prefer the vanilla path for anything another mod might be reading.**

## Sprint, jump and crouch stay blocked -- verified, not assumed

"WASD only" is not something this mod enforces beyond the axes. The commands are blocked by the
game, and it is worth having read it rather than assuming it.

`InputNode`'s static ctor builds the one global allow-list, and it holds exactly five commands:
`MakeScreenshot` (87), `ShowConsole` (88), `ToggleInventory` (36), `ToggleTalk` (122) and
`StopTalk` (123). `GetDefaultBlockResult` returns `1` (remove the command) for anything not in it.

`InventoryScreen.TranslateCommand` special cases two: `Escape` (55) and `ToggleInventory` (36)
both call `ScreenController.CloseScreen()` and return `2`, which clears the whole command list for
that frame. Everything else falls through to `GetDefaultBlockResult`.

So `ToggleSprinting` (23), `Jump` (39), `ToggleDuck` (22), `ToggleProne` (27) and the lean
commands (65, 66) are all removed while the screen is open, and this mod does nothing to change
that. Note also that the closing path returns `2` and clears *commands*; it never touches the
axes array, which is one more reason closing cannot interrupt a held direction.

## The tabs question

Every tab along the top of the inventory is `EFT.UI.EInventoryTab`: `Overall, Gear, Health,
Skills, Map, Notes, Achievements, Prestige`. All of them live inside the single `InventoryScreen`.
`EFT.UI.ItemsPanel` (which carries the narrower `EItemsTab` of `Gear, Health`) derives from
`EFT.UI.UIElement`, **not** from `InputNode`.

So switching tabs cannot add, remove or reorder anything in the input tree, and movement
continues across a tab change for structural reasons rather than because anything handles it.
`TransitionTests.SwitchingTabsWhileHoldingADirectionChangesNothing` writes the claim down but
cannot catch it breaking -- if this is ever in doubt, recheck the hierarchy, not the test.

## How this is put together

```
src/InventoryWalker/
  AxisGate.cs             PURE: the axis map, the filter, the three way gate. No game type.
  GameTypes.cs            every game member, resolved by name at runtime, in one place
  InventoryAxisPatch.cs   the one Harmony prefix, and the engaged/disengaged log line
  InventoryWalkerPlugin.cs  BepInPlugin, config binding, manual patch

tests/InventoryWalker.Tests/   xunit, 35 tests
  InputPipeline.cs        a model of the per frame delivery, written from the IL
  AxisGateTests.cs        the axis map, the filter, the gate truth table
  TransitionTests.cs      the brief's six cases, plus no-latch and the vanilla fallbacks
```

`AxisGate.cs` is the only file with no game dependency, and the test project **links it** rather
than referencing the project, because the plugin targets net472 against BepInEx and Unity which a
net10 test host cannot load. Keep it dependency free or the tests stop building.

`InputPipeline` is a model. It proves the mod's logic against the pipeline **as read**; it cannot
prove the pipeline was read correctly. Only the game can do that.

### Why the plugin references no game assembly

Same reason as the siblings: the `Assembly-CSharp.dll` in `Managed` is **not** the one the game
runs. The Launcher applies `SPT_Runtime\SPT_Data\Launcher\Patches\SPT-core\...\.dll.delta` at
startup and that delta **renames obfuscated types**. `C:\HUH` has never been launched, so it holds
the unpatched original (15,994,432 bytes; patched is 16,233,472).

`pack.ps1` asserts the built DLL carries no `Assembly-CSharp` and no `spt-*`. As of 0.1.0 it
references only `mscorlib`, `UnityEngine.CoreModule`, `BepInEx` and `0Harmony`. `UnityEngine.dll`
(the facade) is referenced at compile time because `BaseUnityPlugin` derives from `MonoBehaviour`
as typed against it, but it type forwards and does not survive into the output's reference list.
Do not remove it; the build fails with CS0012 without it.

### Reading the assembly on this box

There is **no `hpatchz.exe` anywhere on this machine** and no patched assembly copy, so the
unpatched `C:\HUH` DLL is what was read. That was sufficient: every type this mod touches
(`UIScreen`, `InventoryScreen`, `GamePlayerOwner`, `PlayerOwner`, `InputNode`, `InputManager`,
`EAxis`, `EInventoryTab`) is **BSG's own unobfuscated name and identical in both assemblies**.
Obfuscated names render as unprintable characters; the scratchpad scripts escape them as
`\uE0A1` and address them back by hex.

**A `\uXXXX` argument does not survive the shell into PowerShell** -- it arrives empty and the
script reports "not found" for a type that exists. The scratchpad scripts take `-TypeHex EA50`
instead. Half an hour went into that one; do not re-debug it.

## Traps hit while building this

- **A method named for the thing it blocks may not be where the block lives.** The obvious
  suspects were `GamePlayerOwner.SetIgnoreInput`, its three static ignore flags, and
  `TranslateInventoryScreenInput`. None of them is the freeze. The freeze is a four instruction
  base method on `UIScreen` that nobody would grep for. Follow the data, which here was the axes
  array, rather than the names.
- **`\uXXXX` arguments vanish between Bash and PowerShell.** See above.
- **`Grid`-style reasoning about "which node consumes the input" was the wrong model.** Nothing
  consumes anything; a reference is set to null and every later node opts out. Read
  `TranslateInput` before theorising about priority.

## Untested, and what to look for

In rough order of risk:

1. **Does the prefix fire at all?** The log line `Movement passing through to the player
   (inventory open in raid)` prints on the transition, once, not per frame. No line means the
   patch did not take -- check the startup line that names the three resolved members.
2. **Does the player actually move?** The whole point.
3. **FIKA.** Not installed on this box, so the co-op claim is reasoned, not observed. Watch for
   the local player moving smoothly while other clients see them stuttering or standing still,
   which would mean `MovementContext` is not being replicated from where this assumes.
4. **Container looting** opens the same `InventoryScreen`, so movement works there too. Whether
   walking out of range closes the window gracefully is unverified.
5. **The inventory open/close animation.** Nothing patches `Player.SetInventoryOpened` or the
   screen's `Show`/`Close`, so it should be untouched, but that is an assumption about what
   drives the animation rather than a reading of it.
6. **Stamina and pose.** Walking with the inventory open uses whatever pose the player was in.
   Not checked against the stamina drain path at all.
7. **Other mods that patch `UIScreen.TranslateAxes`.** None known. A second prefix returning
   false would win or lose depending on Harmony priority, and the symptom would be intermittent.

## Publishing

- Remote **https://github.com/JoelHauser/InventoryWalker.git**, on `main`.
- GUID `com.mybutthasarash.inventorywalker`, following the prefix the sibling repos use.
- Version lives in the csproj `<Version>` and in `InventoryWalkerPlugin.PluginVersion`.
  `pack.ps1` refuses to pack if they disagree.
- Commit as `Joel Hauser <jhauser@bostonlightsource.com>` -- **not** the gmail address.
- **Write commit messages to a file and use `git commit -F <file>`** with `UTF8Encoding($false)`.
  Piping a here-string adds a BOM in Windows PowerShell 5.1.
- **The Forge forbids mods substantially written by AI agents.** The user has acknowledged this
  for their other repos; do not re-raise it.

## Where this was left off

2026-09-22: **0.1.0**, first cut. Built clean at 0 warnings, 35 tests, references clean, packed
to `releases\InventoryWalker_V0.1.0.zip`. Never launched. Version deliberately not 1.0.0 -- in
this family of repos that is earned by a live run, not by a build.
