namespace InventoryWalker.Tests;

/// <summary>Which movement keys are held down this frame.</summary>
public readonly record struct Held(bool W = false, bool A = false, bool S = false, bool D = false)
{
    public static readonly Held None = new();
    public static readonly Held Forward = new(W: true);
    public static readonly Held Back = new(S: true);
    public static readonly Held Left = new(A: true);
    public static readonly Held Right = new(D: true);
    public static readonly Held ForwardLeft = new(W: true, A: true);
    public static readonly Held ForwardRight = new(W: true, D: true);
    public static readonly Held BackLeft = new(S: true, A: true);
    public static readonly Held BackRight = new(S: true, D: true);
}

/// <summary>What the mouse is doing this frame.</summary>
public readonly record struct Mouse(float X = 0f, bool LeftDown = false, bool RightDown = false, float Y = 0f)
{
    public static readonly Mouse Still = new();
    public static readonly Mouse Moving = new(X: 12f, Y: -8f);
    public static readonly Mouse Clicking = new(LeftDown: true);
    public static readonly Mouse Dragging = new(X: 6f, Y: 3f, LeftDown: true);
    public static readonly Mouse RightClicking = new(RightDown: true);
}

/// <summary>
/// The commands this model cares about, with the values read out of
/// <c>EFT.InputSystem.ECommand</c>.
///
/// Note that the plugin itself contains none of this. It patches the axis path and touches the
/// command path nowhere, which is precisely the claim these tests exist to hold: firing, aiming
/// and sprinting stay blocked because the game blocks them, and the mod must not change that.
/// </summary>
public static class GameCommand
{
    public const int ToggleShooting = 1;
    public const int EndShooting = 2;
    public const int ToggleAlternativeShooting = 3; // aim down sights
    public const int ToggleDuck = 22;
    public const int ToggleSprinting = 23;
    public const int ToggleInventory = 36;
    public const int Jump = 39;
    public const int Escape = 55;
    public const int EndAlternativeShooting = 58;
    public const int MakeScreenshot = 87;
    public const int ShowConsole = 88;
    public const int ToggleTalk = 122;
    public const int StopTalk = 123;

    /// <summary>
    /// The one global allow-list, built in <c>InputNode</c>'s static constructor. Any command not
    /// in it is removed by <c>GetDefaultBlockResult</c> before a lower priority node sees it.
    /// </summary>
    public static readonly int[] AlwaysAllowed =
        [MakeScreenshot, ShowConsole, ToggleInventory, ToggleTalk, StopTalk];
}

/// <summary>Values of <c>EFT.InputSystem.ECursorResult</c>.</summary>
public static class CursorResult
{
    public const int Ignore = 0;
    public const int LockCursor = 1;
    public const int ShowCursor = 2;
}

/// <summary>What the player was actually told to do on a given frame.</summary>
public readonly record struct PlayerInput(bool Delivered, float MoveX, float MoveY, float TurnX, float TurnY, float Lean)
{
    public static readonly PlayerInput Blocked = new(false, 0, 0, 0, 0, 0);

    /// <summary>The player is standing still, whether because it was told to or told nothing.</summary>
    public bool IsStationary => !Delivered || (MoveX == 0f && MoveY == 0f);

    /// <summary>The player's camera moved this frame.</summary>
    public bool CameraTurned => Delivered && (TurnX != 0f || TurnY != 0f);
}

/// <summary>
/// A model of EFT's per frame input delivery, written against the client's IL rather than
/// against the mod, so that the cases in the brief can be asserted rather than argued.
///
/// What it mirrors, and where each step came from:
///
///   1. The input source zeroes the entire axes array and refills it from the current state of
///      each bound axis, every frame. Read out of the source's UpdateInput: a loop writing 0f
///      into every slot, then a loop writing each axis combination's GetValue() back in. This is
///      the step the whole design rests on, because it means held keys are sampled fresh each
///      frame and nothing anywhere is latched. Note that mouse buttons are not axes; they raise
///      commands, which travel in a separate list.
///   2. Both are threaded by reference through the input tree, whose nodes are visited newest
///      first, so a screen opened in raid is reached before the player's own node.
///   3. For each node, InputNode.TranslateInput does three things in order:
///        a. TranslateCommand per command. Result 1 removes that command from the shared list,
///           result 2 clears the list outright.
///        b. TranslateAxes, but only if (axes != null).
///        c. ShouldLockCursor, ALWAYS, outside that null check. The running result is combined
///           by taking the maximum, so ShowCursor (2) beats LockCursor (1) whatever the order.
///   4. UIScreen.TranslateAxes assigns null to the array. That is the first half of the freeze, and
///      what InventoryAxisPatch changes.
///   5. The player's node, GamePlayerOwner.TranslateAxes, opens with `if (flag) return;`. The flag
///      is the owner's static ignore-input bool, which EftScreenController.PrepareEnvironment
///      sets from the screen's IgnorePlayerInput, Enabled by default and not overridden by the
///      inventory. So opening the inventory sets it. 0.1.0's model left this step out, which is
///      how 97 tests passed for a mod that did nothing in game. OwnerAxesPatch lifts it.
///   6. Whatever axes survive reach MoveInputTranslator.TranslateAxes, which calls
///      Player.Move(axes[MoveX], axes[MoveY]) and Player.Rotate(axes[TurnX], axes[TurnY]).
///
/// This is a model, not the game. It proves the mod's logic behaves correctly against the
/// pipeline as read; it cannot prove the pipeline was read correctly.
/// </summary>
public sealed class InputPipeline
{
    private readonly float[] _axes = new float[GameAxis.Count];

    /// <summary>Whether the in raid inventory screen is on top of the input tree.</summary>
    public bool InventoryOpen { get; set; }

    /// <summary>The config toggle.</summary>
    public bool ModEnabled { get; set; } = true;

    /// <summary>
    /// Whether GamePlayerOwner.MyPlayer resolves to a live player. False out of raid, which is
    /// what keeps the mod off the character screen, that being the same InventoryScreen type.
    /// </summary>
    public bool RaidPlayerPresent { get; set; } = true;

    /// <summary>
    /// The tab currently selected inside the inventory screen. The pipeline ignores it, and that
    /// is the point: every top tab lives inside the one InventoryScreen, and the panel hosting
    /// them is a UIElement rather than an input node, so changing tabs cannot add, remove or
    /// reorder anything in the input tree.
    /// </summary>
    public string Tab { get; set; } = "Gear";

    /// <summary>
    /// An item inspection window, <c>EFT.UI.InfoWindow</c>, which derives from
    /// <c>Window&lt;T&gt;</c> and so is a real input node stacked above the inventory.
    ///
    /// It is transparent to movement, and that is luck rather than design: <c>Window.TranslateAxes</c>
    /// is a bare <c>ret</c>, so unlike a screen it never nulls the array. Had InfoWindow derived
    /// from UIScreen instead, inspecting an item would freeze the player and the mod's instance
    /// check would not have covered it.
    /// </summary>
    public bool InspectWindowOpen { get; set; }

    /// <summary>
    /// A right click context menu, <c>EFT.UI.SimpleContextMenu</c>. It derives from
    /// <c>UIElement</c>, not from <c>InputNode</c>, so it is not in the input tree at all and
    /// cannot affect axes, commands or the cursor. The pipeline ignores it for that reason.
    /// </summary>
    public bool ContextMenuOpen { get; set; }

    /// <summary>
    /// Some other screen stacked on top, a modal dialog for instance. A screen nulls the axes,
    /// and this mod deliberately does not override that: something that takes over the screen
    /// should still stop the player.
    /// </summary>
    public bool BlockingScreenOpen { get; set; }

    /// <summary>
    /// The owner's ignore-input flag set by something other than a screen: a cutscene or an
    /// IgnorePlayerInputZone, the flag's two other writers. The mod must not lift these.
    /// </summary>
    public bool ScriptedIgnoreInput { get; set; }

    /// <summary>
    /// Whether OwnerAxesPatch is applied. Off reproduces 0.1.0, which patched the screen alone.
    /// </summary>
    public bool OwnerPatchApplied { get; set; } = true;

    /// <summary>The cursor state the last frame settled on.</summary>
    public int Cursor { get; private set; } = CursorResult.Ignore;

    /// <summary>Whether an inspect window closed itself this frame, from Escape.</summary>
    public bool InspectWindowClosedThisFrame { get; private set; }

    /// <summary>The commands that survived to reach the player's node on the last frame.</summary>
    public IReadOnlyList<int> CommandsReachingPlayer { get; private set; } = [];

    /// <summary>Whether the screen closed itself this frame, from Escape or Tab.</summary>
    public bool ScreenClosedThisFrame { get; private set; }

    /// <summary>Runs one frame and returns what the player was told to do.</summary>
    public PlayerInput Frame(Held held, float mouseX = 0f, float mouseY = 0f, float lean = 0f)
    {
        return Frame(held, new Mouse(X: mouseX, Y: mouseY), lean, []);
    }

    /// <summary>Runs one frame, with the mouse and any extra commands bound to keys.</summary>
    public PlayerInput Frame(Held held, Mouse mouse, float lean = 0f, params int[] alsoPressed)
    {
        // 1. axes: zeroed and refilled from current state, every frame.
        Array.Clear(_axes, 0, _axes.Length);
        _axes[GameAxis.MoveX] = (held.D ? 1f : 0f) - (held.A ? 1f : 0f);
        _axes[GameAxis.MoveY] = (held.W ? 1f : 0f) - (held.S ? 1f : 0f);
        _axes[GameAxis.TurnX] = mouse.X;
        _axes[GameAxis.TurnY] = mouse.Y;
        _axes[GameAxis.LeanX] = lean;

        // 1b. commands: a separate list, and the only thing a mouse button produces.
        List<int> commands = [];
        if (mouse.LeftDown) { commands.Add(GameCommand.ToggleShooting); }
        if (mouse.RightDown) { commands.Add(GameCommand.ToggleAlternativeShooting); }
        commands.AddRange(alsoPressed);

        float[]? threaded = _axes;
        bool passedThroughInventory = false;
        int cursor = CursorResult.Ignore;
        ScreenClosedThisFrame = false;
        InspectWindowClosedThisFrame = false;

        // 2. nodes stacked above the inventory, visited first because the tree runs newest first.

        // A modal screen behaves like any screen: it nulls, and everything below is skipped.
        if (BlockingScreenOpen)
        {
            TranslateCommandsAtTheScreen(commands);
            threaded = null;
            cursor = Math.Max(cursor, CursorResult.ShowCursor);
        }

        // An inspect window. Window<T>.TranslateCommand consumes Escape to close itself and
        // passes everything else; Window<T>.TranslateAxes is a bare ret, so the array survives.
        if (InspectWindowOpen && threaded is not null)
        {
            if (commands.Remove(GameCommand.Escape))
            {
                InspectWindowClosedThisFrame = true;
            }

            cursor = Math.Max(cursor, CursorResult.ShowCursor);
        }

        // ContextMenuOpen is deliberately not consulted: a UIElement is not in the input tree.

        // 3. the inventory screen node. Its commands and its cursor answer run whatever the axes
        // are doing; only TranslateAxes sits behind the null check, which is the shape of
        // InputNode.TranslateInput and the reason the mod cannot reach the cursor.
        if (InventoryOpen)
        {
            TranslateCommandsAtTheScreen(commands);

            if (threaded is not null)
            {
                if (AxisGate.ShouldPassThrough(ModEnabled, isInventoryScreen: true, RaidPlayerPresent))
                {
                    AxisGate.KeepMovementOnly(threaded);
                    passedThroughInventory = true;
                }
                else
                {
                    threaded = null; // vanilla UIScreen.TranslateAxes
                }
            }

            cursor = Math.Max(cursor, CursorResult.ShowCursor);
        }

        // 4. the player's node.
        cursor = Math.Max(cursor, CursorResult.LockCursor);
        Cursor = cursor;
        CommandsReachingPlayer = commands;

        if (threaded is null)
        {
            return PlayerInput.Blocked;
        }

        // 5. GamePlayerOwner.TranslateAxes: `if (flag) return;`. Any open screen (the inventory
        // included) sets the flag, and so do the scripted writers. Only OwnerAxesPatch lifts it.
        bool ignoreInput = InventoryOpen || BlockingScreenOpen || ScriptedIgnoreInput;
        bool lifted = OwnerPatchApplied && AxisGate.ShouldLiftIgnoreInput(passedThroughInventory, ignoreInput);
        if (ignoreInput && !lifted)
        {
            return PlayerInput.Blocked;
        }

        return new PlayerInput(
            true,
            threaded[GameAxis.MoveX],
            threaded[GameAxis.MoveY],
            threaded[GameAxis.TurnX],
            threaded[GameAxis.TurnY],
            threaded[GameAxis.LeanX]);
    }

    /// <summary>
    /// InventoryScreen.TranslateCommand: Escape and ToggleInventory close the screen and return 2,
    /// which clears the list; everything else falls through to GetDefaultBlockResult, which keeps
    /// only the five always-allowed commands and removes the rest.
    /// </summary>
    private void TranslateCommandsAtTheScreen(List<int> commands)
    {
        for (int i = 0; i < commands.Count;)
        {
            int command = commands[i];

            if (command is GameCommand.Escape or GameCommand.ToggleInventory)
            {
                ScreenClosedThisFrame = true;
                commands.Clear();
                return;
            }

            if (GameCommand.AlwaysAllowed.Contains(command))
            {
                i++;
            }
            else
            {
                commands.RemoveAt(i);
            }
        }
    }
}
