namespace InventoryWalker.Tests;

/// <summary>
/// The inventory has to stay fully usable while the player walks: cursor available, clicks and
/// drags landing on items rather than on the trigger, and none of it disturbing a held key.
///
/// Almost all of this is vanilla behaviour that the mod must be careful not to break, rather than
/// behaviour the mod provides. That is worth stating, because "the mod does nothing here" is only
/// reassuring once it has been checked, and the checks are what these tests are.
/// </summary>
public class InteractionTests
{
    /// <summary>
    /// The cursor is decided on a path the mod does not touch.
    ///
    /// <c>ShouldLockCursor</c> is called on every node unconditionally, outside the
    /// <c>if (axes != null)</c> branch that the mod's prefix lives inside, and the results are
    /// combined by taking the maximum. The screen asks for ShowCursor (2) and the player asks for
    /// LockCursor (1), so the screen wins whatever order they are visited in, and it wins whether
    /// or not the axes reached the player.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCursorIsShownWhileTheInventoryIsOpenWhicheverWayTheModIsSet(bool modEnabled)
    {
        InputPipeline pipe = new() { InventoryOpen = true, ModEnabled = modEnabled };

        pipe.Frame(Held.Forward, Mouse.Moving);

        Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);
    }

    [Fact]
    public void TheCursorLocksAgainWhenTheInventoryCloses()
    {
        InputPipeline pipe = new() { InventoryOpen = true };
        pipe.Frame(Held.Forward, Mouse.Moving);
        Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);

        pipe.InventoryOpen = false;
        pipe.Frame(Held.Forward, Mouse.Moving);

        Assert.Equal(CursorResult.LockCursor, pipe.Cursor);
    }

    /// <summary>
    /// Mouse movement drives the cursor and not the camera. The turn axes are flattened by the
    /// mod; the cursor is untouched by it. Both halves of the requirement, on one frame.
    /// </summary>
    [Fact]
    public void MouseMovementDrivesTheCursorAndNotTheCamera()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput input = pipe.Frame(Held.Forward, Mouse.Moving);

        Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);
        Assert.False(input.CameraTurned);
        Assert.Equal(0f, input.TurnX);
        Assert.Equal(0f, input.TurnY);
        Assert.Equal(1f, input.MoveY); // and still walking
    }

    /// <summary>
    /// Clicking in the inventory must not fire or aim. Firing and ADS are commands, not axes, and
    /// the screen removes every command that is not on the five item allow-list before the
    /// player's node is reached. The mod patches only the axis path, so this stays true.
    /// </summary>
    [Theory]
    [InlineData(GameCommand.ToggleShooting)]
    [InlineData(GameCommand.EndShooting)]
    [InlineData(GameCommand.ToggleAlternativeShooting)]
    [InlineData(GameCommand.EndAlternativeShooting)]
    [InlineData(GameCommand.ToggleSprinting)]
    [InlineData(GameCommand.Jump)]
    [InlineData(GameCommand.ToggleDuck)]
    public void NoWeaponOrStanceCommandReachesThePlayerWhileTheInventoryIsOpen(int command)
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        pipe.Frame(Held.Forward, Mouse.Still, 0f, command);

        Assert.DoesNotContain(command, pipe.CommandsReachingPlayer);
        Assert.Empty(pipe.CommandsReachingPlayer);
    }

    [Fact]
    public void LeftAndRightClickingReachNeitherTheTriggerNorTheSights()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        pipe.Frame(Held.Forward, new Mouse(LeftDown: true, RightDown: true));

        Assert.Empty(pipe.CommandsReachingPlayer);
    }

    /// <summary>With the inventory closed those same clicks do reach the player, which is what
    /// makes the test above mean something.</summary>
    [Fact]
    public void TheSameClicksDoReachThePlayerWithTheInventoryClosed()
    {
        InputPipeline pipe = new() { InventoryOpen = false };

        pipe.Frame(Held.Forward, new Mouse(LeftDown: true, RightDown: true));

        Assert.Contains(GameCommand.ToggleShooting, pipe.CommandsReachingPlayer);
        Assert.Contains(GameCommand.ToggleAlternativeShooting, pipe.CommandsReachingPlayer);
    }

    /// <summary>Clicking an item does not interrupt a held key, on the click frame or after it.</summary>
    [Fact]
    public void ClickingAnItemDoesNotInterruptHeldMovement()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput before = pipe.Frame(Held.ForwardRight, Mouse.Still);
        PlayerInput onClick = pipe.Frame(Held.ForwardRight, Mouse.Clicking);
        PlayerInput after = pipe.Frame(Held.ForwardRight, Mouse.Still);

        Assert.Equal(before, onClick);
        Assert.Equal(before, after);
        Assert.Equal(1f, onClick.MoveX);
        Assert.Equal(1f, onClick.MoveY);
    }

    /// <summary>
    /// A drag is many frames of held button and moving mouse. Movement has to be identical on
    /// every one of them, and the camera still must not move.
    /// </summary>
    [Fact]
    public void DraggingAnItemAcrossManyFramesDoesNotInterruptHeldMovement()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput reference = pipe.Frame(Held.ForwardLeft, Mouse.Still);

        for (int frame = 0; frame < 120; frame++)
        {
            PlayerInput dragging = pipe.Frame(Held.ForwardLeft, Mouse.Dragging);
            Assert.Equal(reference, dragging);
            Assert.False(dragging.CameraTurned);
            Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);
        }

        // and the drop
        Assert.Equal(reference, pipe.Frame(Held.ForwardLeft, Mouse.Still));
    }

    /// <summary>
    /// Dragging while moving in each of the eight directions, not just the two the tests above
    /// happen to use. A drag holds a mouse button and moves the mouse at once, which is the
    /// densest input the inventory produces, so it is the one worth running against every
    /// direction rather than a representative one.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, false, 0, 1)]
    [InlineData(false, true, false, false, -1, 0)]
    [InlineData(false, false, true, false, 0, -1)]
    [InlineData(false, false, false, true, 1, 0)]
    [InlineData(true, true, false, false, -1, 1)]
    [InlineData(true, false, false, true, 1, 1)]
    [InlineData(false, true, true, false, -1, -1)]
    [InlineData(false, false, true, true, 1, -1)]
    public void DraggingWhileMovingInAnyDirectionKeepsThatDirection(
        bool w, bool a, bool s, bool d, float x, float y)
    {
        Held held = new(w, a, s, d);
        InputPipeline pipe = new() { InventoryOpen = true };

        // pick the item up, move it, drop it, all while walking
        PlayerInput pickUp = pipe.Frame(held, Mouse.Clicking);
        PlayerInput move = pipe.Frame(held, Mouse.Dragging);
        PlayerInput drop = pipe.Frame(held, Mouse.Still);

        foreach (PlayerInput input in new[] { pickUp, move, drop })
        {
            Assert.True(input.Delivered);
            Assert.Equal(x, input.MoveX);
            Assert.Equal(y, input.MoveY);
            Assert.False(input.CameraTurned);
        }

        Assert.Empty(pipe.CommandsReachingPlayer);
        Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);
    }

    /// <summary>A right click opens a context menu, and is otherwise the same story.</summary>
    [Fact]
    public void OpeningAContextMenuDoesNotInterruptHeldMovement()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput before = pipe.Frame(Held.Back, Mouse.Still);
        PlayerInput onRightClick = pipe.Frame(Held.Back, Mouse.RightClicking);

        Assert.Equal(before, onRightClick);
        Assert.Empty(pipe.CommandsReachingPlayer);
    }

    /// <summary>
    /// The whole sequence the brief describes, end to end on one pipeline: walk in, open, click,
    /// drag, switch tabs, release, press again, close, keep walking.
    /// </summary>
    [Fact]
    public void TheWholeSequenceHoldsTogether()
    {
        InputPipeline pipe = new();

        // walking up to it
        Assert.Equal(1f, pipe.Frame(Held.Forward, Mouse.Still).MoveY);

        // Tab, without releasing W
        pipe.InventoryOpen = true;
        Assert.Equal(1f, pipe.Frame(Held.Forward, Mouse.Still).MoveY);

        // click an item, then drag it, still walking
        Assert.Equal(1f, pipe.Frame(Held.Forward, Mouse.Clicking).MoveY);
        Assert.Equal(1f, pipe.Frame(Held.Forward, Mouse.Dragging).MoveY);
        Assert.Empty(pipe.CommandsReachingPlayer);

        // change direction mid drag
        PlayerInput diagonal = pipe.Frame(Held.ForwardRight, Mouse.Dragging);
        Assert.Equal(1f, diagonal.MoveX);
        Assert.Equal(1f, diagonal.MoveY);

        // switch to the Health tab while still holding it
        pipe.Tab = "Health";
        Assert.Equal(diagonal, pipe.Frame(Held.ForwardRight, Mouse.Dragging));

        // let go, and stop on that frame
        Assert.True(pipe.Frame(Held.None, Mouse.Still).IsStationary);

        // press again inside the inventory
        Assert.Equal(1f, pipe.Frame(Held.Forward, Mouse.Still).MoveY);

        // Tab again to close: the command closes the screen and clears the list, and never
        // touches the axes, so the held key carries straight through
        pipe.Frame(Held.Forward, Mouse.Still, 0f, GameCommand.ToggleInventory);
        Assert.True(pipe.ScreenClosedThisFrame);

        pipe.InventoryOpen = false;
        PlayerInput out_ = pipe.Frame(Held.Forward, Mouse.Still);
        Assert.Equal(1f, out_.MoveY);
        Assert.Equal(CursorResult.LockCursor, pipe.Cursor);
    }

    /// <summary>
    /// Pins the allow-list. If a client update adds something to it, a command this mod's users
    /// do not expect starts reaching the player from inside the inventory.
    /// </summary>
    [Fact]
    public void TheAllowListIsTheFiveCommandsTheClientDeclares()
    {
        Assert.Equal(
            [GameCommand.MakeScreenshot, GameCommand.ShowConsole, GameCommand.ToggleInventory,
             GameCommand.ToggleTalk, GameCommand.StopTalk],
            GameCommand.AlwaysAllowed);
    }
}
