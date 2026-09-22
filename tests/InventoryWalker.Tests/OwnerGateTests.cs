namespace InventoryWalker.Tests;

/// <summary>
/// The second gate: GamePlayerOwner's ignore-input flag, which opening any screen sets and which
/// 0.1.0 did not know about. 0.1.0 was run in game on 2026-09-22; its log line confirmed the
/// screen patch fired, and the player still stood still. These pin why, and the fix.
/// </summary>
public class OwnerGateTests
{
    [Fact]
    public void TheLiftNeedsBothTheInventoryPassAndTheFlag()
    {
        Assert.True(AxisGate.ShouldLiftIgnoreInput(true, true));

        Assert.False(AxisGate.ShouldLiftIgnoreInput(false, true));
        Assert.False(AxisGate.ShouldLiftIgnoreInput(true, false));
        Assert.False(AxisGate.ShouldLiftIgnoreInput(false, false));
    }

    /// <summary>
    /// 0.1.0, reproduced: the screen patch alone lets the axes through and the owner still drops
    /// them on its first line. This is the in-game result the old model could not produce.
    /// </summary>
    [Fact]
    public void TheScreenPatchAloneLeavesThePlayerStandingStill()
    {
        InputPipeline pipe = new() { InventoryOpen = true, OwnerPatchApplied = false };

        PlayerInput input = pipe.Frame(Held.Forward);

        Assert.False(input.Delivered);
        Assert.True(input.IsStationary);
    }

    [Theory]
    [MemberData(nameof(EveryDirection))]
    public void WithBothPatchesEveryDirectionReachesThePlayer(Held held, float moveX, float moveY)
    {
        InputPipeline pipe = new() { InventoryOpen = true };

        PlayerInput input = pipe.Frame(held);

        Assert.True(input.Delivered);
        Assert.Equal(moveX, input.MoveX);
        Assert.Equal(moveY, input.MoveY);
    }

    /// <summary>
    /// A cutscene or no-input zone sets the same flag. With the inventory shut there is no pass
    /// through the screen, so nothing is lifted and the game keeps the player still.
    /// </summary>
    [Fact]
    public void AScriptedNoInputStateIsNotLifted()
    {
        InputPipeline pipe = new() { ScriptedIgnoreInput = true };

        PlayerInput input = pipe.Frame(Held.Forward);

        Assert.False(input.Delivered);
    }

    /// <summary>Turning the mod off puts both gates back, not just the first.</summary>
    [Fact]
    public void DisablingTheModBlocksAtTheScreenAndLiftsNothing()
    {
        InputPipeline pipe = new() { InventoryOpen = true, ModEnabled = false };

        PlayerInput input = pipe.Frame(Held.Forward);

        Assert.False(input.Delivered);
    }

    public static TheoryData<Held, float, float> EveryDirection => new()
    {
        { Held.Forward, 0f, 1f },
        { Held.Back, 0f, -1f },
        { Held.Left, -1f, 0f },
        { Held.Right, 1f, 0f },
        { Held.ForwardLeft, -1f, 1f },
        { Held.ForwardRight, 1f, 1f },
        { Held.BackLeft, -1f, -1f },
        { Held.BackRight, 1f, -1f },
    };
}
