using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace InventoryWalker
{
    /// <summary>
    /// Lets the player walk with WASD while the in raid inventory is open.
    ///
    /// One Harmony prefix, on one method, resolved by name. See <see cref="InventoryAxisPatch"/>
    /// for why that method and not another.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class InventoryWalkerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.inventorywalker";
        public const string PluginName = "Inventory Walker";

        /// <summary>Must match the csproj's Version. Two places, and they have to agree.</summary>
        public const string PluginVersion = "0.1.0";

        /// <summary>
        /// Exposed so the patch can read it without a singleton lookup on every frame. Null until
        /// Awake has run, which the patch checks, because Harmony patches and BepInEx config
        /// binding are ordered by BepInEx and not by us.
        /// </summary>
        internal static ConfigEntry<bool> MoveWhileInventoryOpen;

        private void Awake()
        {
            MoveWhileInventoryOpen = Config.Bind(
                "General",
                "Move while the inventory is open",
                true,
                "Walk with WASD while the in raid inventory is open. Mouse look, leaning and "
                + "sprinting stay blocked, as they are in vanilla. Turning this off restores "
                + "vanilla behaviour immediately, without a restart.");

            if (!GameTypes.Resolve(Logger))
            {
                Logger.LogError(PluginName + " " + PluginVersion
                                + " could not resolve the client members it needs, so it is doing "
                                + "nothing. The game is unmodified.");
                return;
            }

            InventoryAxisPatch.SetLogger(Logger);

            try
            {
                new Harmony(PluginGuid).Patch(
                    GameTypes.TranslateAxes,
                    prefix: new HarmonyMethod(typeof(InventoryAxisPatch), nameof(InventoryAxisPatch.Prefix)));
            }
            catch (Exception e)
            {
                Logger.LogError("Could not apply the patch, so the game is unmodified: " + e);
                return;
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }
    }
}
