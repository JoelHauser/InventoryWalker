namespace InventoryWalker
{
    /// <summary>
    /// The axis slots EFT's input system uses, read out of <c>EFT.InputSystem.EAxis</c> in the
    /// patched client assembly.
    ///
    /// These are pinned as constants rather than resolved at runtime because they are load
    /// bearing: <c>PlayerInputTranslator.TranslateAxes</c> indexes the array by these exact
    /// numbers, so a slot moving would silently send WASD into the look handler. A test asserts
    /// the mapping, so a future client change fails the suite instead of the player.
    ///
    /// From the translator's IL, the array is consumed as:
    ///   Player.Look(axes[LookX] * sensX, axes[LookY] * sensY, true)
    ///   Player.Move(new Vector2(axes[MoveX], axes[MoveY]))
    ///   Player.Rotate(new Vector2(axes[TurnX] * sensX, axes[TurnY] * sensY), false)
    ///   Player.SlowLean(axes[LeanX])
    /// </summary>
    public static class GameAxis
    {
        /// <summary>Strafe. Left/right on the movement axis, driven by A and D.</summary>
        public const int MoveX = 0;

        /// <summary>Forward/back on the movement axis, driven by W and S.</summary>
        public const int MoveY = 1;

        /// <summary>Mouse look, horizontal. Feeds Player.Rotate.</summary>
        public const int TurnX = 2;

        /// <summary>Mouse look, vertical. Feeds Player.Rotate.</summary>
        public const int TurnY = 3;

        /// <summary>Free look (held Alt), horizontal. Feeds Player.Look.</summary>
        public const int LookX = 4;

        /// <summary>Free look (held Alt), vertical. Feeds Player.Look.</summary>
        public const int LookY = 5;

        /// <summary>Lean. Feeds Player.SlowLean.</summary>
        public const int LeanX = 6;

        /// <summary>How many slots the enum declares.</summary>
        public const int Count = 7;
    }

    /// <summary>
    /// The whole decision this mod makes, with no game type anywhere near it.
    ///
    /// Vanilla stops the player moving with the inventory open by having
    /// <c>UIScreen.TranslateAxes</c> assign <c>null</c> to the axes array, which every input node
    /// visited afterwards (the player's own <c>GamePlayerOwner</c> among them) takes as "skip
    /// axes entirely". This class is what replaces that null: keep the two movement slots exactly
    /// as the input system sampled them this frame, and flatten everything else.
    /// </summary>
    public static class AxisGate
    {
        /// <summary>
        /// Whether the axes should reach the player rather than being nulled.
        ///
        /// Deliberately a pure function of three booleans. It holds no key state and no memory of
        /// previous frames, which is what makes stuck movement structurally impossible: there is
        /// nothing latched to get stuck.
        /// </summary>
        public static bool ShouldPassThrough(bool enabled, bool isInventoryScreen, bool raidPlayerPresent)
        {
            return enabled && isInventoryScreen && raidPlayerPresent;
        }

        /// <summary>
        /// Whether to lift <c>GamePlayerOwner</c>'s ignore-input flag around its own
        /// <c>TranslateAxes</c> call.
        ///
        /// The second gate. Opening any screen runs <c>EftScreenController.PrepareEnvironment</c>,
        /// which calls <c>GamePlayerOwner.SetIgnoreInput(IgnorePlayerInput)</c>; the inventory's
        /// controller inherits the default <c>Enabled</c>, and <c>GamePlayerOwner.TranslateAxes</c>
        /// opens with <c>if (flag) return;</c>. So passing the axes through the screen reaches the
        /// player's node and then stops at its first line. Lift only when the axes came through
        /// the inventory gate, and only if the flag is actually set, so there is nothing to put
        /// back otherwise.
        /// </summary>
        public static bool ShouldLiftIgnoreInput(bool passedThroughInventory, bool ignoreInputSet)
        {
            return passedThroughInventory && ignoreInputSet;
        }

        /// <summary>
        /// Whether the player has walked far enough from the loot to close it. A range of zero or
        /// less turns the limit off. Strictly beyond, so standing exactly on the line keeps it open.
        /// </summary>
        public static bool ShouldCloseLoot(float distance, float range)
        {
            return range > 0f && distance > range;
        }

        /// <summary>
        /// Zeroes every axis except the two movement slots, in place.
        ///
        /// In place rather than on a copy, because the array threaded through the input tree is
        /// the input manager's own buffer and the nodes we want to affect are the ones visited
        /// after us. The game does the same thing to the same array in
        /// <c>GamePlayerOwner.TranslateAxes</c> when it suppresses movement during NPC dialog, and
        /// the buffer is zeroed and refilled from scratch at the top of every frame, so nothing
        /// written here survives into the next one.
        ///
        /// Length is read off the array rather than assumed, so a client that grows or shrinks
        /// the axis list cannot make this throw.
        /// </summary>
        public static void KeepMovementOnly(float[] axes)
        {
            if (axes == null)
            {
                return;
            }

            for (int i = 0; i < axes.Length; i++)
            {
                if (i == GameAxis.MoveX || i == GameAxis.MoveY)
                {
                    continue;
                }

                axes[i] = 0f;
            }
        }
    }
}
