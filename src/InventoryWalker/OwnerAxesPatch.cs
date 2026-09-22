using System;
using BepInEx.Logging;

namespace InventoryWalker
{
    /// <summary>
    /// The second patch. A prefix and finalizer on <c>GamePlayerOwner.TranslateAxes</c>.
    ///
    /// The second half of the freeze, which 0.1.0 missed and which stopped it working in game:
    ///
    ///   1. Opening a screen runs <c>EftScreenController.PrepareEnvironment</c>, which calls
    ///      <c>Switch(IgnorePlayerInput, GamePlayerOwner.SetIgnoreInput)</c>.
    ///   2. <c>IgnorePlayerInput</c> defaults to <c>EStateSwitcher.Enabled</c>, and the inventory's
    ///      controller does not override it (only the trader dialog, the battle UI and the
    ///      hideout do). So opening the inventory sets the owner's static ignore flag to true, and
    ///      the battle UI, whose controller returns <c>Disabled</c>, sets it back on close.
    ///   3. <c>GamePlayerOwner.TranslateAxes</c> begins <c>if (flag) return;</c>. So once the first
    ///      patch lets the axes through, they arrive at the player's node and stop on its first
    ///      line.
    ///
    /// The fix lifts the flag for the length of this one call and puts it back in a finalizer,
    /// which runs even if the original throws. Every other reader of the flag, the owner's
    /// command handling included, still sees it set, so commands stay exactly as blocked as in
    /// vanilla. It never lifts unless the axes came through the inventory gate this frame, which
    /// is what keeps cutscenes and scripted no-input zones, the flag's other writers, in charge.
    /// </summary>
    internal static class OwnerAxesPatch
    {
        /// <summary>The <see cref="InventoryAxisPatch.Engagement"/> last logged about.</summary>
        private static int _loggedEngagement;

        private static ManualLogSource _log;

        internal static void SetLogger(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>
        /// Clears the flag if it should be lifted, and says so in <paramref name="__state"/> so
        /// the finalizer knows to put it back.
        /// </summary>
        internal static void Prefix(out bool __state)
        {
            __state = false;

            try
            {
                if (!InventoryAxisPatch.PassedRecently())
                {
                    return;
                }

                ref bool ignoreInput = ref GameTypes.IgnoreInput;
                bool lift = AxisGate.ShouldLiftIgnoreInput(true, ignoreInput);
                Note(lift);

                if (lift)
                {
                    ignoreInput = false;
                    __state = true;
                }
            }
            catch (Exception e)
            {
                if (_log != null)
                {
                    _log.LogError("Owner prefix failed, falling back to vanilla for this frame: " + e);
                }
            }
        }

        /// <summary>Puts the flag back, whatever happened in between.</summary>
        internal static void Finalizer(bool __state)
        {
            if (__state)
            {
                GameTypes.IgnoreInput = true;
            }
        }

        /// <summary>
        /// One line per inventory opening, saying whether the flag was there to lift. The pair of
        /// lines is what settles a live test from the log alone.
        /// </summary>
        private static void Note(bool lifted)
        {
            if (_log == null || _loggedEngagement == InventoryAxisPatch.Engagement)
            {
                return;
            }

            _loggedEngagement = InventoryAxisPatch.Engagement;
            _log.LogInfo(lifted
                ? "Lifting the screen's ignore-input flag for the player's movement."
                : "The ignore-input flag was not set, so there was nothing to lift.");
        }
    }
}
