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
        Assert.False(AxisGate.ShouldCloseLoot(distance, distance, 3f));
    }

    [Theory]
    [InlineData(3.01f)]
    [InlineData(10f)]
    [InlineData(30.5f)]
    public void BeyondThreeMetresCloses(float distance)
    {
        Assert.True(AxisGate.ShouldCloseLoot(distance, distance, 3f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ARangeOfZeroOrLessNeverCloses(float range)
    {
        Assert.False(AxisGate.ShouldCloseLoot(1000f, 1000f, range));
    }

    /// <summary>
    /// Issue #1: looting an AI corpse opened the view and closed it on the same frame, because
    /// the anchor read 6.3 m for a player standing on the body. 0.3.0 measured from that one
    /// pivot, so a wrong one made the body unlootable outright.
    /// </summary>
    [Fact]
    public void AnAnchorAlreadyOutOfRangeCannotCloseTheLootOnOpening()
    {
        Assert.False(AxisGate.ShouldCloseLoot(6.3f, 0f, 3f));
    }

    [Theory]
    [InlineData(6.3f)]
    [InlineData(50f)]
    [InlineData(1000f)]
    public void AWrongAnchorCanOnlyBeLenientNeverEarly(float anchorOffset)
    {
        // Whatever the anchor says, the player has to have walked out of range of where they
        // opened the loot before anything closes.
        Assert.False(AxisGate.ShouldCloseLoot(anchorOffset, 2.9f, 3f));
        Assert.True(AxisGate.ShouldCloseLoot(anchorOffset, 3.1f, 3f));
    }

    /// <summary>
    /// The other half of the same asymmetry. The opening position is a fixed point in the world
    /// and cannot follow loot that moves; the anchor is what does that, and it is what keeps the
    /// view open for a player riding a moving platform with the body they are looting.
    /// </summary>
    [Fact]
    public void StandingOnTheAnchorKeepsTheLootOpenHoweverFarFromTheOpeningPosition()
    {
        Assert.False(AxisGate.ShouldCloseLoot(0.5f, 400f, 3f));
    }

    /// <summary>Both pivots out of range is the ordinary walk-away the feature is for.</summary>
    [Fact]
    public void WalkingAwayFromBothStillCloses()
    {
        Assert.True(AxisGate.ShouldCloseLoot(3.4f, 3.2f, 3f));
    }

    /// <summary>
    /// GameTypes resolves <c>TrackableTransform</c> on the base <c>InteractableObject</c> and
    /// invokes it against whatever the player is interacting with, relying on reflection
    /// dispatching to the override. That is the only reason a <c>Corpse</c> answers with its
    /// pelvis rather than <c>base.transform</c>, and the fix for issue #1 rests on it. The game's
    /// types cannot be loaded here, so the assumption is pinned on a stand-in pair.
    /// </summary>
    [Fact]
    public void AVirtualGetterResolvedOnTheBaseTypeStillReachesTheOverride()
    {
        var getter = typeof(AnchorBase).GetProperty(nameof(AnchorBase.TrackableThing))!.GetGetMethod()!;

        Assert.Equal("root", getter.Invoke(new AnchorBase(), null));
        Assert.Equal("pelvis", getter.Invoke(new AnchorOverride(), null));
    }

    private class AnchorBase
    {
        public virtual string TrackableThing => "root";
    }

    private class AnchorOverride : AnchorBase
    {
        public override string TrackableThing => "pelvis";
    }
}
