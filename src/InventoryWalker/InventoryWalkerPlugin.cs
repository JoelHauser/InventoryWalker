using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace InventoryWalker
{
    /// <summary>
    /// Lets the player walk with WASD while the in raid inventory is open.
    ///
    /// Two Harmony patches, both resolved by name, one for each of the two gates that stop the
    /// player with a screen open. See <see cref="InventoryAxisPatch"/> and
    /// <see cref="OwnerAxesPatch"/>. They go on together or not at all: either alone leaves the
    /// player standing still. A third, <see cref="LootRange"/>, closes a loot view once the
    /// player walks out of range; it is optional, and walking works without it.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class InventoryWalkerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.inventorywalker";
        public const string PluginName = "Inventory Walker";

        /// <summary>Must match the csproj's Version. Two places, and they have to agree.</summary>
        public const string PluginVersion = "0.3.0";

        /// <summary>
        /// Exposed so the patch can read it without a singleton lookup on every frame. Null until
        /// Awake has run, which the patch checks, because Harmony patches and BepInEx config
        /// binding are ordered by BepInEx and not by us.
        /// </summary>
        internal static ConfigEntry<bool> MoveWhileInventoryOpen;

        internal static ConfigEntry<float> LootRangeMetres;

        /// <summary>Whether the loot range resolved and patched; Update does nothing otherwise.</summary>
        private bool _lootRangeOn;

        private void Awake()
        {
            MoveWhileInventoryOpen = Config.Bind(
                "General",
                "Move while the inventory is open",
                true,
                "Walk with WASD while the in raid inventory is open. Mouse look, leaning and "
                + "sprinting stay blocked, as they are in vanilla. Turning this off restores "
                + "vanilla behaviour immediately, without a restart.");

            LootRangeMetres = Config.Bind(
                "General",
                "Loot range in metres",
                3f,
                new ConfigDescription(
                    "When looting a bag, a body or a container, walking further than this from it "
                    + "closes the loot view. 0 turns the limit off. The plain Tab inventory is never "
                    + "limited.",
                    new AcceptableValueRange<float>(0f, 50f)));

            if (!GameTypes.Resolve(Logger))
            {
                Logger.LogError(PluginName + " " + PluginVersion
                                + " could not resolve the client members it needs, so it is doing "
                                + "nothing. The game is unmodified.");
                return;
            }

            InventoryAxisPatch.SetLogger(Logger);
            OwnerAxesPatch.SetLogger(Logger);

            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                harmony.Patch(
                    GameTypes.TranslateAxes,
                    prefix: new HarmonyMethod(typeof(InventoryAxisPatch), nameof(InventoryAxisPatch.Prefix)));
                harmony.Patch(
                    GameTypes.OwnerTranslateAxes,
                    prefix: new HarmonyMethod(typeof(OwnerAxesPatch), nameof(OwnerAxesPatch.Prefix)),
                    finalizer: new HarmonyMethod(typeof(OwnerAxesPatch), nameof(OwnerAxesPatch.Finalizer)));
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Logger.LogError("Could not apply the patches, so the game is unmodified: " + e);
                return;
            }

            if (GameTypes.ResolveLootRange(Logger))
            {
                LootRange.SetLogger(Logger);
                try
                {
                    harmony.Patch(
                        GameTypes.ShowInventoryScreenLoot,
                        prefix: new HarmonyMethod(typeof(LootRange), nameof(LootRange.Prefix)));
                    _lootRangeOn = true;
                }
                catch (Exception e)
                {
                    Logger.LogError("Could not patch the loot view, so walking works but the loot range is off: " + e);
                }
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded"
                           + (_lootRangeOn ? ", with the loot range." : ", without the loot range."));
        }

        private void Update()
        {
            if (_lootRangeOn)
            {
                LootRange.Tick(LootRangeMetres.Value);
            }
        }
    }
}
