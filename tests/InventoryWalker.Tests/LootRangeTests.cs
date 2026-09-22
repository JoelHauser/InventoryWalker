namespace InventoryWalker.Tests;

/// <summary>
/// The loot range's decision. The game half (which object is the loot, when the session ends,
/// how it closes) is in LootRange.cs and GameTypes.cs and can only be checked in a raid.
/// </summary>
public class LootRangeTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(1.2f)]
    [InlineData(2.99f)]
    [InlineData(3f)]
    public void WithinThreeMetresStaysOpen(float distance)
    {
        Assert.False(AxisGate.ShouldCloseLoot(distance, 3f));
    }

    [Theory]
    [InlineData(3.01f)]
    [InlineData(10f)]
    [InlineData(30.5f)]
    public void BeyondThreeMetresCloses(float distance)
    {
        Assert.True(AxisGate.ShouldCloseLoot(distance, 3f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ARangeOfZeroOrLessNeverCloses(float range)
    {
        Assert.False(AxisGate.ShouldCloseLoot(1000f, range));
    }
}
