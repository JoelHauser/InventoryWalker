using System;
using BepInEx.Logging;

namespace InventoryWalker
{
    /// <summary>
    /// The first of two patches. A prefix on <c>UIScreen.TranslateAxes(ref float[] axes)</c>.
    /// The second, <see cref="OwnerAxesPatch"/>, deals with the flag that stops the player's own
    /// node once the axes get there; 0.1.0 shipped with this one alone and the player stood still.
    ///
    /// How the first half of the freeze works in vanilla, read out of the client's IL:
    ///
    ///   1. <c>InputManager.Update</c> asks the input source to fill the axes array. That source
    ///      zeroes the whole array and refills it from the current state of every bound axis,
    ///      every frame. Movement is continuous state, not key events.
    ///   2. The array is threaded by reference down the input tree.
    ///      <c>InputNodeAbstract.TranslateInput</c> visits children newest first, so a screen
    ///      opened during a raid is reached before the player's own node.
    ///   3. <c>InputNode.TranslateInput</c> calls its own <c>TranslateAxes</c> only
    ///      <c>if (axes != null)</c>.
    ///   4. <c>UIScreen.TranslateAxes</c> is, in its entirety, <c>axes = null;</c>. Every node
    ///      after it, <c>GamePlayerOwner</c> included, is therefore skipped.
    ///
    /// So the fix here is not to drive the player ourselves. It is to decline to null the array
    /// for this one screen, after flattening the axes we do not want. With the second patch
    /// lifting the owner's flag, the player then moves down its ordinary path:
    /// <c>GamePlayerOwner.TranslateAxes</c> ->
    /// <c>PlayerOwner.TranslateAxes</c> -> <c>PlayerInputTranslator.TranslateAxes</c> ->
    /// <c>Player.Move</c> -> <c>MovementContext</c>, which is also what any co-op replication
    /// reads, so movement synchronises without this mod knowing anything about it.
    ///
    /// Nothing here remembers a key. Held state lives where it already lived, in the input
    /// system's per frame sample, which is why opening or closing the inventory cannot interrupt
    /// a held direction and why releasing a key still stops the player.
    /// </summary>
    internal static class InventoryAxisPatch
    {
        /// <summary>
        /// Whether the gate was open on the previous frame, so the log can record the two
        /// transitions rather than a line every frame.
        /// </summary>
        private static bool _engaged;

        /// <summary>
        /// The frame the axes last passed through the inventory screen. What
        /// <see cref="OwnerAxesPatch"/> checks before lifting anything, so the flag is only ever
        /// lifted for axes this gate let through, never for a cutscene or a scripted zone.
        /// </summary>
        private static int _passedFrame = int.MinValue;

        /// <summary>
        /// Bumped each time the gate opens, so the second patch can log once per opening.
        /// </summary>
        internal static int Engagement { get; private set; }

        /// <summary>
        /// The inventory screen the axes last passed through. What <see cref="LootRange"/> closes;
        /// only trusted together with <see cref="PassedRecently"/>.
        /// </summary>
        internal static object LastScreen { get; private set; }

        private static ManualLogSource _log;

        /// <summary>
        /// Whether the axes came through the inventory gate this frame or the one before. One
        /// frame of slack because the order the owner and the screen are visited in has not been
        /// seen live; if the owner runs first, the stamp it sees is the previous frame's. Nothing
        /// lingers past that: closing the inventory stops the stamp, and the flag goes back to
        /// false when the battle UI's own controller runs anyway.
        /// </summary>
        internal static bool PassedRecently()
        {
            return UnityEngine.Time.frameCount - _passedFrame <= 1;
        }

        internal static void SetLogger(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>
        /// Returns false to skip the original, which is the whole point: the original is the
        /// assignment of null.
        /// </summary>
        internal static bool Prefix(object __instance, ref float[] axes)
        {
            try
            {
                if (axes == null)
                {
                    // Something with higher priority already blocked, a modal dialog over the
                    // inventory for instance. Leave that alone; it should still stop the player.
                    return true;
                }

                bool enabled = InventoryWalkerPlugin.MoveWhileInventoryOpen != null
                               && InventoryWalkerPlugin.MoveWhileInventoryOpen.Value;

                bool pass = AxisGate.ShouldPassThrough(
                    enabled,
                    GameTypes.IsInventoryScreen(__instance),
                    GameTypes.RaidPlayerPresent());

                if (!pass)
                {
                    Note(false);
                    return true;
                }

                AxisGate.KeepMovementOnly(axes);
                _passedFrame = UnityEngine.Time.frameCount;
                LastScreen = __instance;
                Note(true);
                return false;
            }
            catch (Exception e)
            {
                // A throwing prefix would be swallowed by Harmony and leave the player stuck in a
                // half applied state every frame. Fail back to vanilla loudly instead.
                if (_log != null)
                {
                    _log.LogError("Prefix failed, falling back to vanilla for this frame: " + e);
                }

                return true;
            }
        }

        private static void Note(bool engaged)
        {
            if (engaged == _engaged || _log == null)
            {
                return;
            }

            _engaged = engaged;
            if (engaged)
            {
                Engagement++;
            }

            _log.LogInfo(engaged
                ? "Movement passing through to the player (inventory open in raid)."
                : "Movement back on the vanilla path.");
        }
    }
}
