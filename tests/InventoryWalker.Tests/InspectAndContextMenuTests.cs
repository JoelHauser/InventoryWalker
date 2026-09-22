namespace InventoryWalker.Tests;

/// <summary>
/// Right click menus and item inspection windows, which stack on top of the inventory.
///
/// This is the one area where the answer was not obvious in advance. A right click menu turned
/// out to be a <c>UIElement</c> and therefore not in the input tree at all, but the inspect
/// window is a real input node, and whether it froze the player came down to which base class
/// BSG happened to give it. <c>InfoWindow</c> derives from <c>Window&lt;T&gt;</c>, whose
/// <c>TranslateAxes</c> is a bare <c>ret</c>. Had it derived from <c>UIScreen</c>, whose
/// <c>TranslateAxes</c> is <c>axes = null</c>, inspecting an item would stop the player dead and
/// the mod's instance check would not have covered it.
/// </summary>
public class InspectAndContextMenuTests
{
    private static readonly (bool W, bool A, bool S, bool D, float X, float Y)[] AllDirections =
    [
        (true, false, false, false, 0, 1),
        (false, true, false, false, -1, 0),
        (false, false, true, false, 0, -1),
        (false, false, false, true, 1, 0),
        (true, true, false, false, -1, 1),
        (true, false, false, true, 1, 1),
        (false, true, true, false, -1, -1),
        (false, false, true, true, 1, -1),
    ];

    public static TheoryData<bool, bool, bool, bool, float, float> Directions()
    {
        TheoryData<bool, bool, bool, bool, float, float> data = [];
        foreach ((bool w, bool a, bool s, bool d, float x, float y) in AllDirections)
        {
            data.Add(w, a, s, d, x, y);
        }

        return data;
    }

    /// <summary>Every direction still moves with a context menu open.</summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryDirectionMovesWithAContextMenuOpen(bool w, bool a, bool s, bool d, float x, float y)
    {
        InputPipeline pipe = new() { InventoryOpen = true, ContextMenuOpen = true };

        PlayerInput input = pipe.Frame(new Held(w, a, s, d), Mouse.Still);

        Assert.True(input.Delivered);
        Assert.Equal(x, input.MoveX);
        Assert.Equal(y, input.MoveY);
        Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);
    }

    /// <summary>Every direction still moves with an inspect window open.</summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryDirectionMovesWithAnInspectWindowOpen(bool w, bool a, bool s, bool d, float x, float y)
    {
        InputPipeline pipe = new() { InventoryOpen = true, InspectWindowOpen = true };

        PlayerInput input = pipe.Frame(new Held(w, a, s, d), Mouse.Still);

        Assert.True(input.Delivered);
        Assert.Equal(x, input.MoveX);
        Assert.Equal(y, input.MoveY);
        Assert.False(input.CameraTurned);
        Assert.Equal(CursorResult.ShowCursor, pipe.Cursor);
    }

    /// <summary>
    /// The full right click to Inspect sequence, without ever releasing the key: walking, right
    /// click for the menu, pick Inspect, the window opens, walk on, change direction, close it,
    /// walk on again.
    /// </summary>
    [Fact]
    public void RightClickToInspectAndBackNeverInterruptsMovement()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput walking = pipe.Frame(Held.ForwardLeft, Mouse.Still);
        Assert.Equal(-1f, walking.MoveX);
        Assert.Equal(1f, walking.MoveY);

        // right click opens the context menu
        pipe.ContextMenuOpen = true;
        Assert.Equal(walking, pipe.Frame(Held.ForwardLeft, Mouse.RightClicking));
        Assert.Empty(pipe.CommandsReachingPlayer);

        // click Inspect: the menu closes, the window opens
        pipe.ContextMenuOpen = false;
        pipe.InspectWindowOpen = true;
        Assert.Equal(walking, pipe.Frame(Held.ForwardLeft, Mouse.Clicking));

        // change direction with the window open
        PlayerInput turned = pipe.Frame(Held.BackRight, Mouse.Still);
        Assert.Equal(1f, turned.MoveX);
        Assert.Equal(-1f, turned.MoveY);

        // stop, then start again, still with the window open
        Assert.True(pipe.Frame(Held.None, Mouse.Still).IsStationary);
        Assert.Equal(1f, pipe.Frame(Held.Right, Mouse.Still).MoveX);

        // close it with Escape: the window eats the command and nothing else sees it
        pipe.Frame(Held.Right, Mouse.Still, 0f, GameCommand.Escape);
        Assert.True(pipe.InspectWindowClosedThisFrame);
        Assert.False(pipe.ScreenClosedThisFrame);
        pipe.InspectWindowOpen = false;

        // and still walking
        PlayerInput after = pipe.Frame(Held.Right, Mouse.Still);
        Assert.Equal(1f, after.MoveX);
        Assert.True(after.Delivered);
    }

    /// <summary>Dragging with an inspect window open is still just a drag.</summary>
    [Fact]
    public void DraggingWithAnInspectWindowOpenKeepsMoving()
    {
        InputPipeline pipe = new() { InventoryOpen = true, InspectWindowOpen = true };

        PlayerInput reference = pipe.Frame(Held.ForwardRight, Mouse.Still);

        for (int frame = 0; frame < 60; frame++)
        {
            PlayerInput dragging = pipe.Frame(Held.ForwardRight, Mouse.Dragging);
            Assert.Equal(reference, dragging);
            Assert.False(dragging.CameraTurned);
        }

        Assert.Empty(pipe.CommandsReachingPlayer);
    }

    /// <summary>Clicks inside an inspect window still reach neither the trigger nor the sights.</summary>
    [Fact]
    public void ClicksInsideAnInspectWindowDoNotFireOrAim()
    {
        InputPipeline pipe = new() { InventoryOpen = true, InspectWindowOpen = true };

        pipe.Frame(Held.Forward, new Mouse(LeftDown: true, RightDown: true));

        Assert.Empty(pipe.CommandsReachingPlayer);
    }

    /// <summary>
    /// The deliberate non-goal. A screen stacked on top nulls the axes and this mod does not
    /// override that, because something that takes over the screen should still stop the player.
    /// Stated as a test so that a later change which "fixes" it has to argue with this first.
    /// </summary>
    [Fact]
    public void AScreenStackedOnTopStillStopsThePlayer()
    {
        InputPipeline pipe = new() { InventoryOpen = true, BlockingScreenOpen = true };

        Assert.False(pipe.Frame(Held.Forward, Mouse.Still).Delivered);

        pipe.BlockingScreenOpen = false;
        Assert.True(pipe.Frame(Held.Forward, Mouse.Still).Delivered);
    }
}
