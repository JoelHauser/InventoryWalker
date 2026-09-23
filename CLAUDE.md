# InventoryWalker -- working notes for Claude

Lets the player walk with WASD while the in raid inventory is open. One BepInEx plugin, **two**
patches, one per gate. No server half, nothing written to the profile.

**0.1.0 was run in game on 2026-09-22 and did not work.** The prefix fired (its log line is in
`LogOutput.log`) and the player stood still. There are two gates, and 0.1.0 opened only the
first. See "The second gate, found by running it" below. 0.2.0 opens both and has **not** been
run yet. The sibling repos' hardest lesson applied here in full: *an IL trace that predicts
success is not evidence of success.* 97 tests passed for a mod that did nothing, because the test
model was written from the same incomplete reading.

## The second gate, found by running it

Read off the **patched** `H:\SPT4.1.X` assembly with `ilspycmd` (in `~/.dotnet/tools`). That
assembly has readable names, which makes it far quicker than the `\uXXXX` scripts below.

1. Opening any screen runs `EftScreenManager.EftScreenController<,>.PrepareEnvironment`, which does
   `Switch(IgnorePlayerInput, GamePlayerOwner.SetIgnoreInput)`.
2. `IgnorePlayerInput` defaults to `EStateSwitcher.Enabled` (`LastState 0, Enabled 1, Disabled 2`).
   Only three controllers override it: `TraderDialogScreen` and `EftBattleUIScreen` return
   `Disabled`, and the hideout returns `LastState`. `InventoryScreenController` does not override it, so
   opening the inventory calls `SetIgnoreInput(true)`. Going back to the battle UI sets it false.
3. `SetIgnoreInput` is `ldarg.0; stsfld bool_0; ret`. `GamePlayerOwner.TranslateAxes` begins
   `ldsfld bool_0; brfalse.s; ret`. So the axes 0.1.0 let through arrived at the player's node and
   stopped on its first instruction.

`OwnerAxesPatch` is a prefix and finalizer on `GamePlayerOwner.TranslateAxes`. It clears the flag
for the length of that one call and puts it back in the finalizer, and it only does so when
`InventoryAxisPatch` let axes through this frame or the previous one. Everything else that reads the flag,
the owner's command handling included, still sees it set. The flag is found through the
`stsfld` in `SetIgnoreInput`'s IL rather than by the name `bool_0`.

What the 0.1.0 reading got wrong, beyond missing the flag:

- The translator is `EFT.MoveInputTranslator` in the live client. `EFT.PlayerInputTranslator.TranslateAxes`
  is empty. `PlayerOwner.TranslateAxes` calls the move translator, then the hands translator, then the player translator.
- `InputNode.TranslateInput` calls `base.TranslateInput` (the **children**) **before** its own
  `TranslateCommand`/`TranslateAxes`. Visit order is post-order, not just "newest first". The
  one-frame slack in `InventoryAxisPatch.PassedRecently` exists because the relative order of the
  screen and the owner has still not been seen live.

**Known edge:** a cutscene or `IgnorePlayerInputZone` sets the same flag. If one fires *while the
inventory is open*, the lift overrides it. With the inventory shut nothing is lifted.

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

## The first gate

This section was once titled "the finding the whole mod rests on" and said the freeze "is not a
flag". That was wrong: the flag is the second gate (top of this file). This one is one line of
one method.

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

## The translator's own gates, which could have killed this and did not

`PlayerInputTranslator.TranslateAxes` has an early return **before** `Player.Move`:

```
if (MovementContext.IsAxesIgnored) return;
if (MovementContext.BlindFire == 0) Player.Move(...)
```

So there was a live possibility that opening the inventory sets one of those and the passthrough
achieves nothing. It does not, and both halves were checked rather than assumed:

- **`IsAxesIgnored`** is written in exactly three places in the whole assembly, all of them found
  by sweeping every instruction operand for the name: `NarrateGame`'s coroutine and two `BTRView`
  coroutines. Cutscenes and riding the BTR. Nothing to do with the inventory, and both of those
  *should* still stop the player, so leave them alone.
- **`BlindFire`** is set to 0 by `Player.SetInventoryOpened` itself (IL_00A2), which is the call
  the inventory makes on open. So the guard is satisfied on exactly the path this mod cares about.

`SetInventoryOpened` otherwise exits any mounted state, sets `_isInventoryOpened`, tells the hands
controller, force stops animated interactions and starts `_waitInventoryCoroutine`. That coroutine
waits on the hands animator's state and then calls `PlayerAnimator.SetInventory(true)` and
`OnInventoryInteraction(true, false)`. It is the **opening animation** and nothing else; there is
no movement check anywhere in it, so moving does not cancel the inventory.

## The cursor and the clicks are on paths this mod is not on

Both halves of "keep the inventory usable while walking" are vanilla behaviour that the mod must
avoid breaking, rather than behaviour it provides. Worth having read, because "the mod does
nothing here" only reassures once it has been checked.

### The cursor

`ECursorResult` is `0 Ignore, 1 LockCursor, 2 ShowCursor`. `UIScreen.ShouldLockCursor` returns 2,
`PlayerOwner.ShouldLockCursor` returns 1, and `InputNode.TranslateInput` combines by taking the
**maximum** (IL_005F..IL_0071: `if (*shouldLockCursor < mine) *shouldLockCursor = mine`). So
ShowCursor wins whatever the visit order.

The load bearing detail: `ShouldLockCursor()` is called at **IL_0053, outside** the
`if (axes != null)` branch at IL_0042. The mod's prefix lives inside that branch. So making the
player's node reachable for axes cannot change the cursor, and the cursor is the same with the mod
on or off. `InputManager.Update` then computes `visible = !(result == 1)`, so result 2 shows it.

### The clicks

Mouse buttons raise **commands**, not axes, and commands travel in a separate list that this mod
never touches. `InventoryScreen.TranslateCommand` closes the screen on `Escape` (55) and
`ToggleInventory` (36) returning 2, and sends everything else to `GetDefaultBlockResult`, which
removes anything outside the five item allow-list. `ToggleShooting` (1) and
`ToggleAlternativeShooting` (3) are therefore gone before the player's node is reached.

Note the closing path returns 2, which clears the **command** list and never touches the axes
array. That is one more reason closing cannot interrupt a held direction.

Also checked, because it was the remaining way a click could reach the weapon: `PlayerOwner`
calls a hands input translator's `TranslateAxes` as well as the player's. Every concrete hands
translator (`` through ``) has a **one instruction** `TranslateAxes`, a bare `ret`.
Only `` does anything with axes. So nothing in the hands chain can act on what we pass.

## What stacks on top of the inventory, and why it mattered

Context menus and Inspect windows were the first requirement whose answer was **not** obvious in
advance, and one of them came down to a coin flip BSG made years ago.

| What the player opens | Type | In the input tree? | Effect on axes |
| --- | --- | --- | --- |
| Right click menu | `EFT.UI.SimpleContextMenu` | **No**, it is a `UIElement` | none, it cannot see input |
| Inspect window | `EFT.UI.InfoWindow : Window'1` | yes | **none**, `Window.TranslateAxes` is a bare `ret` |
| A modal dialog | some `UIScreen` | yes | nulls, and *should* |

**The Inspect window is transparent by luck, not by design.** `InfoWindow` derives from
`Window<T>`, and `Window<T>.TranslateAxes` is one instruction, `ret`. Had it derived from
`UIScreen` instead, whose `TranslateAxes` is `axes = null`, inspecting an item would stop the
player dead -- and the mod's `IsInventoryScreen` instance check would **not** have covered it,
because the prefix would have been running against a different type. That failure would have
looked like "movement randomly stops sometimes", which is the worst shape of bug to diagnose.

So when a new stacked window is reported as freezing the player, the question to ask first is
**which base class it has**, not what it does.

Two supporting details, both read rather than assumed:

- `Window<T>.TranslateCommand` returns `1` for `Escape` (55), after playing a UI sound and
  closing itself, and `0` for everything else. So Escape closes just the inspect window, the
  inventory screen underneath never sees it, and commands otherwise pass down to the inventory
  screen which removes the weapon ones as usual.
- `Window<T>.ShouldLockCursor` returns `2` (ShowCursor), so the cursor stays available.

### The one that looked dangerous and was not

`EFT.UI.ItemObserveScreen'2` overrides `TranslateAxes` with 25 instructions and **is** a
`UIScreen`, so it looked like a screen that would both block movement and dodge the patch. It
does neither:

```
if (!rotatingPreview) return;                                  // array untouched
_weaponPreview.Rotate(axes[TurnX] * 2, -axes[TurnY] * 2, -30, 30);
UpdatePositions();
```

It never nulls. It reads the turn axes to spin the 3D preview and passes everything on. And it is
not the Inspect window anyway -- only `EditBuildScreen` and `WeaponModdingScreen` derive from it.
Worth keeping because it is a good example of an override that looks like a block and is not.

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
  AxisGate.cs             PURE: the axis map, the filter, both gates' decisions. No game type.
  GameTypes.cs            every game member, resolved by name at runtime, in one place
  InventoryAxisPatch.cs   gate 1: prefix on UIScreen.TranslateAxes, and the engaged log line
  OwnerAxesPatch.cs       gate 2: prefix+finalizer lifting GamePlayerOwner's ignore-input flag
  InventoryWalkerPlugin.cs  BepInPlugin, config binding, manual patches (both or neither)

tests/InventoryWalker.Tests/   xunit, 109 tests
  OwnerGateTests.cs       gate 2, including the 0.1.0 regression
  InputPipeline.cs        a model of the per frame delivery, written from the IL: axes, the
                          command list, and the cursor combination
  AxisGateTests.cs        the axis map, the filter, the gate truth table
  TransitionTests.cs      the brief's six cases, plus no-latch and the vanilla fallbacks
  InteractionTests.cs     cursor, clicks, drags and context menus while walking
  InspectAndContextMenuTests.cs   the windows that stack on top, and the one that still blocks
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

- **Finding one block is not proof there is only one.** 0.1.0's notes said `SetIgnoreInput` and
  its flags were "not the freeze", and that was **wrong**. `bool_0` is the second half of the freeze. The
  mistake was stopping at the first block that explained the symptom and not following the axes
  all the way to `Player.Move` once it was out of the way. Follow the data to the end.
- **`\uXXXX` arguments vanish between Bash and PowerShell.** See above.
- **`Grid`-style reasoning about "which node consumes the input" was the wrong model.** Nothing
  consumes anything; a reference is set to null and every later node opts out. Read
  `TranslateInput` before theorising about priority.

## Untested, and what to look for

In rough order of risk:

1. **Do both patches fire?** On each opening in raid, expect `Movement passing through to the
   player (inventory open in raid)` and then `Lifting the screen's ignore-input flag for the
   player's movement.` The first line alone is exactly 0.1.0's failure. `nothing to lift` means the
   flag was not the block after all.
2. **Does the player actually move?** The whole point. 0.1.0: no.
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
- **There is one GitHub release, and it is edited in place.** On 2026-09-22 the user had the
  0.1.0 release overwritten with 0.3.0, rather than a new release alongside it. The same release
  object was retagged `v0.3.0` and retitled, its notes were rewritten, and its asset was replaced.
  The bare `v0.1.0` git tag was left pointing at `95efa28` as history. Ask before assuming the
  next version gets the same treatment.

## Where this was left off

2026-09-22: **0.1.0**, first cut. Built clean at 0 warnings, 35 tests, references clean, packed
to `releases\InventoryWalker_V0.1.0.zip`. Never launched. Version deliberately not 1.0.0 -- in
this family of repos that is earned by a live run, not by a build.

Then the user added a requirement: the inventory must stay fully interactive while walking, with
the cursor usable, clicks landing on items rather than the trigger, and drags not disturbing a
held key. **It needed no code.** Every part of it was already true, for reasons on paths the mod
is not on, and the work was verifying that rather than building it: the cursor combination, the
command allow-list, the empty hands translators, and the two gates in the translator that could
have stopped `Player.Move` and do not. All four are written up above.

What did change is the test model, which previously represented only the axes and so left those
claims living in prose. `InputPipeline` now carries the command list and the cursor as well, and
`InteractionTests` holds the new requirement. Widened afterwards to run every direction through
start-from-standstill, close-while-held and drag-while-moving rather than a representative one or
two. 77 tests.

Then context menus and Inspect windows, which is the section above. Same outcome again, but the
closest call of the four: a right click menu is not an input node at all, and the Inspect window
is one that happens to pass input through because of which base class it has.

The lesson worth keeping: **a requirement that turns out to need no code still needs the reading.**
Four of the five checks had a plausible failure mode. `IsAxesIgnored` would have made the whole
mod silently do nothing, and an `InfoWindow` deriving from `UIScreen` would have frozen the player
on Inspect in a way the patch does not cover.

### The state as it stands

0.1.0: 97 tests, 0 warnings, published as a GitHub release, which 0.3.0 later overwrote (see
Publishing). **Run in game 2026-09-22: the prefix fired and the player did not move.** The second gate is the cause (see the top of this file).

0.2.0: adds `OwnerAxesPatch`. The test model now has the owner's flag, and
`OwnerGateTests.TheScreenPatchAloneLeavesThePlayerStandingStill` reproduces the 0.1.0 result. 109
tests, 0 warnings, references unchanged, packed to `releases\InventoryWalker_V0.2.0.zip` and
installed on `H:\SPT4.1.X`. **Run in game 2026-09-22: works** (user report: "it works great").

0.3.0: adds the loot range (`LootRange.cs`), at the user's request. Looting a bag, body or container
and walking more than 3 m (config **Loot range in metres**, 0 = off) from it closes the screen.
118 tests, 0 warnings. It is the published GitHub release (`v0.3.0`, which replaced 0.1.0), with walking
confirmed in game. **The loot range is not yet run in game.**

How it is built, all read off the live assembly:

- Every raid loot source opens through `EftGamePlayerOwner.ShowInventoryScreenLoot(CompoundItem loot,
  Action callback, bool isFakeContainer)`. The callers are in `InteractionContextHelper`:
  `LootItem` "Search" and armor "MODDING", and `OnContainerOpen` for a `LootableContainer`.
  `Corpse : LootItem`. The Tab inventory never goes through this method.
  `HideoutPlayerOwner : EftGamePlayerOwner` does, for the stash, so it is filtered out by type.
- The anchor is `Player.InteractableObject`, accepted only if its `ItemOwner.RootItem` **is** `loot`.
  `ClientPlayer` overrides `Interact`, so the callback may be delayed, and the player may have
  looked away by then. If nothing matches, the range falls back to the player's position when they
  opened it, and a log line says which was used.
- The session ends when the game calls `callback`. The method's own exit action calls it on every
  close path (Tab, Esc, death, and the "another screen is up" bail), so nothing polls to find out
  whether the screen is still open.
- The close is `InventoryScreen.TranslateCommand(ToggleInventory)`, the screen's own Tab handler:
  `ScreenController.CloseScreen()`. It is invoked on the instance `InventoryAxisPatch` last passed
  axes through, and only while `PassedRecently()`, so it never fires from under a dialog.
  `GamePlayerOwner.CloseInventoryIfOpen` is `ToggleScreen(Inventory)`. Nothing in the client calls
  it, and a toggle could open the screen instead of closing it, so it is not used.

Log lines to check in a raid: `Loot opened; measuring the range from <object>.` on opening, and
`Walked X m from the loot (range 3.0 m); closing it.` on walking away. The fallback wording
(`no world object matched`) on a bag or body means the anchor match needs another look.
