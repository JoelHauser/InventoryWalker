namespace InventoryWalker.Tests;

/// <summary>
/// The six cases from the brief, run against <see cref="InputPipeline"/>.
///
/// Every one of them is really the same assertion from a different angle: the axes array is
/// rebuilt from current key state on every frame, so opening or closing the inventory changes
/// only whether that array reaches the player, never what is in it. There is no edge to miss and
/// no state to carry across the transition.
/// </summary>
public class TransitionTests
{
    /// <summary>Case 1. Hold W, open the inventory, keep walking.</summary>
    [Fact]
    public void HoldingForwardThroughOpeningTheInventoryKeepsWalking()
    {
        InputPipeline pipe = new();

        PlayerInput before = pipe.Frame(Held.Forward);
        Assert.True(before.Delivered);
        Assert.Equal(1f, before.MoveY);

        pipe.InventoryOpen = true; // the player pressed Tab; W was never released

        PlayerInput after = pipe.Frame(Held.Forward);
        Assert.True(after.Delivered);
        Assert.Equal(1f, after.MoveY);
        Assert.Equal(before.MoveY, after.MoveY);
    }

    /// <summary>Case 2. Walking with the inventory open, close it, keep walking.</summary>
    [Fact]
    public void HoldingForwardThroughClosingTheInventoryKeepsWalking()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput inside = pipe.Frame(Held.Forward);
        Assert.Equal(1f, inside.MoveY);

        pipe.InventoryOpen = false;

        PlayerInput outside = pipe.Frame(Held.Forward);
        Assert.True(outside.Delivered);
        Assert.Equal(1f, outside.MoveY);
    }

    /// <summary>
    /// Case 3. Release W inside the inventory and stop on that frame.
    ///
    /// This is the one that would fail if movement were latched on rather than passed through,
    /// which is why it is worth stating separately from the rest.
    /// </summary>
    [Fact]
    public void ReleasingForwardInsideTheInventoryStopsImmediately()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        Assert.Equal(1f, pipe.Frame(Held.Forward).MoveY);

        PlayerInput released = pipe.Frame(Held.None);
        Assert.True(released.Delivered);
        Assert.Equal(0f, released.MoveX);
        Assert.Equal(0f, released.MoveY);
        Assert.True(released.IsStationary);
    }

    /// <summary>Case 4. Start moving, and change direction, from a standstill inside the inventory.</summary>
    [Fact]
    public void StartingAndChangingDirectionInsideTheInventoryWorks()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        Assert.True(pipe.Frame(Held.None).IsStationary);

        PlayerInput forward = pipe.Frame(Held.Forward);
        Assert.Equal(0f, forward.MoveX);
        Assert.Equal(1f, forward.MoveY);

        PlayerInput right = pipe.Frame(Held.Right);
        Assert.Equal(1f, right.MoveX);
        Assert.Equal(0f, right.MoveY);

        PlayerInput back = pipe.Frame(Held.Back);
        Assert.Equal(-1f, back.MoveY);

        Assert.True(pipe.Frame(Held.None).IsStationary);
    }

    /// <summary>
    /// Case 4, widened. Every direction can be started from a standstill with the inventory
    /// already open, not only the three the case above happens to walk through.
    ///
    /// Worth its own test because "open it while stationary, then start moving" and "open it
    /// while already moving" are different orderings of the same two events, and a design that
    /// carried any state across the transition could easily get one right and the other wrong.
    /// This one has no state to get wrong, and this is what says so.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, false, 0, 1)]
    [InlineData(false, false, true, false, 0, -1)]
    [InlineData(false, true, false, false, -1, 0)]
    [InlineData(false, false, false, true, 1, 0)]
    [InlineData(true, true, false, false, -1, 1)]
    [InlineData(true, false, false, true, 1, 1)]
    [InlineData(false, true, true, false, -1, -1)]
    [InlineData(false, false, true, true, 1, -1)]
    public void EveryDirectionCanBeStartedFromAStandstillInsideTheInventory(
        bool w, bool a, bool s, bool d, float x, float y)
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        // standing still, inventory already open, nothing held
        Assert.True(pipe.Frame(Held.None).IsStationary);

        // press the direction without ever having moved beforehand
        PlayerInput moving = pipe.Frame(new Held(w, a, s, d));
        Assert.True(moving.Delivered);
        Assert.Equal(x, moving.MoveX);
        Assert.Equal(y, moving.MoveY);

        // and release, still inside the inventory
        Assert.True(pipe.Frame(Held.None).IsStationary);
    }

    /// <summary>
    /// Closing while holding any direction, where the movement began inside the inventory rather
    /// than before it opened. The direction has to survive the close without a re-press.
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
    public void ADirectionStartedInsideTheInventorySurvivesClosingIt(
        bool w, bool a, bool s, bool d, float x, float y)
    {
        Held held = new(w, a, s, d);
        InputPipeline pipe = new() { InventoryOpen = true };

        Assert.True(pipe.Frame(Held.None).IsStationary);
        PlayerInput inside = pipe.Frame(held);

        // Tab closes the screen: the command clears the command list and never touches the axes
        pipe.Frame(held, Mouse.Still, 0f, GameCommand.ToggleInventory);
        Assert.True(pipe.ScreenClosedThisFrame);
        pipe.InventoryOpen = false;

        PlayerInput outside = pipe.Frame(held);
        Assert.True(outside.Delivered);
        Assert.Equal(x, outside.MoveX);
        Assert.Equal(y, outside.MoveY);
        Assert.Equal(inside.MoveX, outside.MoveX);
        Assert.Equal(inside.MoveY, outside.MoveY);
    }

    /// <summary>
    /// Case 5. Switch tabs while holding a direction.
    ///
    /// Weak as a test and honest about it: the pipeline ignores the tab entirely, so this asserts
    /// the model rather than discovering anything. It earns its place as the written form of the
    /// structural claim it stands on, which is that every top tab (Overall, Gear, Health, Skills,
    /// Map, Notes, Achievements, Prestige) lives inside the single InventoryScreen, and the panel
    /// that hosts them is a UIElement rather than an input node. Changing tabs therefore cannot
    /// touch the input tree. If that ever stops being true, this test will not catch it; the
    /// hierarchy is what has to be rechecked.
    /// </summary>
    [Theory]
    [InlineData("Overall")]
    [InlineData("Gear")]
    [InlineData("Health")]
    [InlineData("Skills")]
    [InlineData("Map")]
    [InlineData("Notes")]
    [InlineData("Achievements")]
    [InlineData("Prestige")]
    public void SwitchingTabsWhileHoldingADirectionChangesNothing(string tab)
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput before = pipe.Frame(Held.ForwardLeft);

        pipe.Tab = tab;

        PlayerInput after = pipe.Frame(Held.ForwardLeft);
        Assert.Equal(before, after);
        Assert.Equal(-1f, after.MoveX);
        Assert.Equal(1f, after.MoveY);
    }

    /// <summary>Case 6, the diagonal half. Every direction and combination, across both transitions.</summary>
    [Theory]
    [InlineData(false, false, false, false, 0, 0)]
    [InlineData(true, false, false, false, 0, 1)]
    [InlineData(false, false, true, false, 0, -1)]
    [InlineData(false, true, false, false, -1, 0)]
    [InlineData(false, false, false, true, 1, 0)]
    [InlineData(true, true, false, false, -1, 1)]
    [InlineData(true, false, false, true, 1, 1)]
    [InlineData(false, true, true, false, -1, -1)]
    [InlineData(false, false, true, true, 1, -1)]
    [InlineData(true, false, true, false, 0, 0)]
    [InlineData(false, true, false, true, 0, 0)]
    public void EveryDirectionSurvivesBothTransitions(bool w, bool a, bool s, bool d, float x, float y)
    {
        Held held = new(w, a, s, d);
        InputPipeline pipe = new();

        PlayerInput closed = pipe.Frame(held);
        Assert.Equal(x, closed.MoveX);
        Assert.Equal(y, closed.MoveY);

        pipe.InventoryOpen = true;
        PlayerInput opened = pipe.Frame(held);
        Assert.Equal(closed, opened);

        pipe.InventoryOpen = false;
        PlayerInput reclosed = pipe.Frame(held);
        Assert.Equal(closed, reclosed);
    }

    /// <summary>
    /// The brief's warning, stated as a test: nothing may latch. Held for many frames across
    /// repeated open and close, then released once, and the player must stop on that frame.
    /// </summary>
    [Fact]
    public void NothingLatchesAcrossARunOfFramesAndTransitions()
    {
        InputPipeline pipe = new();

        for (int frame = 0; frame < 200; frame++)
        {
            pipe.InventoryOpen = frame % 7 < 3; // opened and closed repeatedly under a held key
            PlayerInput input = pipe.Frame(Held.ForwardRight);
            Assert.True(input.Delivered);
            Assert.Equal(1f, input.MoveX);
            Assert.Equal(1f, input.MoveY);
        }

        pipe.InventoryOpen = true;
        Assert.True(pipe.Frame(Held.None).IsStationary);

        pipe.InventoryOpen = false;
        Assert.True(pipe.Frame(Held.None).IsStationary);
    }

    /// <summary>
    /// WASD only. Mouse look and lean are flattened while the inventory is open, so dragging an
    /// item cannot swing the camera, and they come straight back when it closes.
    /// </summary>
    [Fact]
    public void MouseLookAndLeanAreBlockedOnlyWhileTheInventoryIsOpen()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput inside = pipe.Frame(Held.Forward, mouseX: 12f, mouseY: -8f, lean: 1f);
        Assert.Equal(1f, inside.MoveY);
        Assert.Equal(0f, inside.TurnX);
        Assert.Equal(0f, inside.TurnY);
        Assert.Equal(0f, inside.Lean);

        pipe.InventoryOpen = false;

        PlayerInput outside = pipe.Frame(Held.Forward, mouseX: 12f, mouseY: -8f, lean: 1f);
        Assert.Equal(12f, outside.TurnX);
        Assert.Equal(-8f, outside.TurnY);
        Assert.Equal(1f, outside.Lean);
    }

    /// <summary>Turning the toggle off restores the vanilla freeze, on the next frame, with no restart.</summary>
    [Fact]
    public void DisablingTheModRestoresTheVanillaFreeze()
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        Assert.True(pipe.Frame(Held.Forward).Delivered);

        pipe.ModEnabled = false;

        PlayerInput blocked = pipe.Frame(Held.Forward);
        Assert.False(blocked.Delivered);
        Assert.True(blocked.IsStationary);
    }

    /// <summary>
    /// Out of raid the character screen is the same InventoryScreen type, so the gate leans on
    /// there being a live raid player. Without one, vanilla behaviour is untouched.
    /// </summary>
    [Fact]
    public void TheOutOfRaidCharacterScreenIsUntouched()
    {
        InputPipeline pipe = new() { InventoryOpen = true, RaidPlayerPresent = false };

        Assert.False(pipe.Frame(Held.Forward).Delivered);
    }
}
