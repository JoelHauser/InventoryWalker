namespace InventoryWalker.Tests;

/// <summary>
/// The pure half: the axis map, the filter and the three way gate.
/// </summary>
public class AxisGateTests
{
    /// <summary>
    /// Pins EFT.InputSystem.EAxis. These numbers are indexes into an array the client reads by
    /// position, so a slot moving would route WASD into the look handler rather than fail. The
    /// sibling repo's lesson applies: write the assertion even when the claim feels obvious.
    /// </summary>
    [Fact]
    public void TheAxisMapMatchesTheClient()
    {
        Assert.Equal(0, GameAxis.MoveX);
        Assert.Equal(1, GameAxis.MoveY);
        Assert.Equal(2, GameAxis.TurnX);
        Assert.Equal(3, GameAxis.TurnY);
        Assert.Equal(4, GameAxis.LookX);
        Assert.Equal(5, GameAxis.LookY);
        Assert.Equal(6, GameAxis.LeanX);
        Assert.Equal(7, GameAxis.Count);
    }

    [Fact]
    public void KeepMovementOnlyLeavesBothMovementSlotsExactlyAsSampled()
    {
        float[] axes = [0.5f, -0.25f, 9f, 9f, 9f, 9f, 9f];

        AxisGate.KeepMovementOnly(axes);

        Assert.Equal(0.5f, axes[GameAxis.MoveX]);
        Assert.Equal(-0.25f, axes[GameAxis.MoveY]);
    }

    [Fact]
    public void KeepMovementOnlyFlattensLookTurnAndLean()
    {
        float[] axes = [1f, 1f, 4f, -4f, 7f, -7f, 1f];

        AxisGate.KeepMovementOnly(axes);

        Assert.Equal(0f, axes[GameAxis.TurnX]);
        Assert.Equal(0f, axes[GameAxis.TurnY]);
        Assert.Equal(0f, axes[GameAxis.LookX]);
        Assert.Equal(0f, axes[GameAxis.LookY]);
        Assert.Equal(0f, axes[GameAxis.LeanX]);
    }

    [Fact]
    public void KeepMovementOnlySurvivesNull()
    {
        AxisGate.KeepMovementOnly(null!);
    }

    /// <summary>
    /// The length is read off the array rather than assumed to be Count, so a client that adds
    /// or drops an axis cannot make the prefix throw on every frame.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(12)]
    public void KeepMovementOnlyHandlesAnyLength(int length)
    {
        float[] axes = new float[length];
        for (int i = 0; i < length; i++)
        {
            axes[i] = 3f;
        }

        AxisGate.KeepMovementOnly(axes);

        for (int i = 0; i < length; i++)
        {
            float expected = i is GameAxis.MoveX or GameAxis.MoveY ? 3f : 0f;
            Assert.Equal(expected, axes[i]);
        }
    }

    [Fact]
    public void ThePassThroughGateNeedsAllThree()
    {
        Assert.True(AxisGate.ShouldPassThrough(true, true, true));

        Assert.False(AxisGate.ShouldPassThrough(false, true, true));
        Assert.False(AxisGate.ShouldPassThrough(true, false, true));
        Assert.False(AxisGate.ShouldPassThrough(true, true, false));
    }
}
