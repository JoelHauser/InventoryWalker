using System;
using BepInEx.Logging;
using UnityEngine;

namespace InventoryWalker
{
    /// <summary>
    /// Closes a loot view once the player walks out of range of what they are looting.
    ///
    /// Vanilla never needed this: nobody could move with the screen open. Walking makes it
    /// possible to open a bag and wander off with its grid still on screen, so this puts the
    /// distance back.
    ///
    /// A loot session starts in a prefix on <c>EftGamePlayerOwner.ShowInventoryScreenLoot</c>,
    /// the one method every raid loot source opens through, and it ends when the game calls that
    /// method's own <c>callback</c>. The game calls it on every way the screen closes: Tab, Escape,
    /// death, and the early exit when another screen is up. So nothing here polls to find out
    /// whether the screen is still open. The plain Tab inventory never goes through that method,
    /// so it is never range limited.
    ///
    /// The distance is measured from the world object whose loot it is (a bag or loose item, a
    /// body, a container). If that cannot be matched, it is measured from where the player stood
    /// when they opened it, which is within arm's reach of the loot anyway.
    /// </summary>
    internal static class LootRange
    {
        private static ManualLogSource _log;

        /// <summary>Bumped per session, so a stale close callback cannot end a newer one.</summary>
        private static int _session;

        private static bool _active;
        private static Component _player;
        private static Transform _anchor;
        private static Vector3 _anchorPosition;

        internal static void SetLogger(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>
        /// Starts a session and wraps <paramref name="callback"/> to end it. Never blocks the
        /// original.
        /// </summary>
        internal static void Prefix(object __instance, object loot, ref Action callback)
        {
            try
            {
                if (GameTypes.IsHideoutOwner(__instance))
                {
                    return;
                }

                Component player = GameTypes.OwnerPlayer(__instance);
                if (!GameTypes.IsRaidPlayer(player))
                {
                    return;
                }

                int session = ++_session;
                _active = true;
                _player = player;
                _anchor = GameTypes.FindLootAnchor(player, loot);
                _anchorPosition = _anchor != null ? _anchor.position : player.transform.position;

                if (_log != null)
                {
                    _log.LogInfo(_anchor != null
                        ? "Loot opened; measuring the range from " + _anchor.name + "."
                        : "Loot opened; no world object matched it, measuring the range from where it was opened.");
                }

                Action original = callback;
                callback = () =>
                {
                    End(session);
                    original?.Invoke();
                };
            }
            catch (Exception e)
            {
                _active = false;
                if (_log != null)
                {
                    _log.LogError("Loot range prefix failed, so this loot has no range: " + e);
                }
            }
        }

        /// <summary>Called every frame from the plugin's Update.</summary>
        internal static void Tick(float range)
        {
            if (!_active)
            {
                return;
            }

            try
            {
                if (_player == null)
                {
                    _active = false;
                    return;
                }

                // Only while this mod is what is letting the player move. With a dialog on top,
                // or the mod turned off, the player cannot be walking away, and the close below
                // should not fire from underneath something else.
                if (!InventoryAxisPatch.PassedRecently() || InventoryAxisPatch.LastScreen == null)
                {
                    return;
                }

                // Follow the object while it exists; keep the last place it was if it goes away,
                // as a bag does when it is picked up.
                if (_anchor != null)
                {
                    _anchorPosition = _anchor.position;
                }

                float distance = Vector3.Distance(_player.transform.position, _anchorPosition);
                if (!AxisGate.ShouldCloseLoot(distance, range))
                {
                    return;
                }

                _active = false;
                if (_log != null)
                {
                    _log.LogInfo("Walked " + distance.ToString("0.0") + " m from the loot (range "
                                 + range.ToString("0.0") + " m); closing it.");
                }

                GameTypes.CloseInventoryScreen(InventoryAxisPatch.LastScreen);
            }
            catch (Exception e)
            {
                _active = false;
                if (_log != null)
                {
                    _log.LogError("Loot range check failed, so this loot has no range: " + e);
                }
            }
        }

        private static void End(int session)
        {
            if (session == _session)
            {
                _active = false;
                _player = null;
                _anchor = null;
            }
        }
    }
}
