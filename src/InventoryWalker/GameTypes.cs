using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace InventoryWalker
{
    /// <summary>
    /// Every game member this mod touches, resolved by name at runtime, in one place.
    ///
    /// Nothing here references Assembly-CSharp at compile time, and that is deliberate. The
    /// Assembly-CSharp.dll sitting in Managed is not the assembly the game runs: the SPT Launcher
    /// applies a delta at startup which renames obfuscated types, so an install that has never
    /// been launched still holds the unpatched original. A plugin compiled against that copy does
    /// not load. Resolving by name costs a few lines and works against whichever assembly is
    /// actually in memory.
    ///
    /// The four names below are BSG's own, unobfuscated in both the patched and unpatched
    /// assemblies, so this is not a bet on the delta's renaming.
    /// </summary>
    internal static class GameTypes
    {
        internal const string UIScreenName = "EFT.UI.Screens.UIScreen";
        internal const string InventoryScreenName = "EFT.UI.InventoryScreen";
        internal const string GamePlayerOwnerName = "EFT.GamePlayerOwner";
        internal const string TranslateAxesName = "TranslateAxes";
        internal const string MyPlayerName = "MyPlayer";

        /// <summary>
        /// <c>UIScreen.TranslateAxes(ref float[])</c>. The method that nulls the axes, and the
        /// only thing this mod patches.
        /// </summary>
        internal static MethodInfo TranslateAxes { get; private set; }

        /// <summary>
        /// <c>EFT.UI.InventoryScreen</c>. The screen behind Tab in raid, and also the character
        /// screen out of raid, and also the window a container's loot opens in. All of its top
        /// tabs live inside this one screen, so matching the type covers every one of them.
        /// </summary>
        private static Type _inventoryScreen;

        /// <summary>
        /// The getter for the static <c>GamePlayerOwner.MyPlayer</c>, which is the raid player, or
        /// null when there is not one.
        /// </summary>
        private static Func<UnityEngine.Object> _myPlayer;

        /// <summary>
        /// Resolves everything, logging exactly which name failed if one does.
        ///
        /// Returns false rather than throwing, so a client update that renames something leaves
        /// the player with an unpatched, vanilla game and a clear line in the log, rather than a
        /// plugin that half loaded.
        /// </summary>
        internal static bool Resolve(ManualLogSource log)
        {
            Type uiScreen = AccessTools.TypeByName(UIScreenName);
            if (uiScreen == null)
            {
                log.LogError("Could not find the type " + UIScreenName + ". Not patching anything.");
                return false;
            }

            TranslateAxes = AccessTools.Method(uiScreen, TranslateAxesName);
            if (TranslateAxes == null)
            {
                log.LogError("Found " + UIScreenName + " but not its " + TranslateAxesName
                             + " method. Not patching anything.");
                return false;
            }

            _inventoryScreen = AccessTools.TypeByName(InventoryScreenName);
            if (_inventoryScreen == null)
            {
                log.LogError("Could not find the type " + InventoryScreenName + ". Not patching anything.");
                return false;
            }

            Type owner = AccessTools.TypeByName(GamePlayerOwnerName);
            MethodInfo getter = owner == null ? null : AccessTools.PropertyGetter(owner, MyPlayerName);
            if (getter == null)
            {
                log.LogError("Could not find " + GamePlayerOwnerName + "." + MyPlayerName
                             + ". Not patching anything.");
                return false;
            }

            _myPlayer = BindMyPlayer(getter, log);
            if (_myPlayer == null)
            {
                return false;
            }

            log.LogInfo("Resolved " + UIScreenName + "." + TranslateAxesName + ", "
                        + InventoryScreenName + " and " + GamePlayerOwnerName + "." + MyPlayerName + ".");
            return true;
        }

        /// <summary>
        /// Binds the MyPlayer getter to a delegate so the per frame call does not allocate.
        /// Falls back to plain reflection if the return type will not bind covariantly, which
        /// costs an allocation a frame and is still fine.
        /// </summary>
        private static Func<UnityEngine.Object> BindMyPlayer(MethodInfo getter, ManualLogSource log)
        {
            try
            {
                return (Func<UnityEngine.Object>)Delegate.CreateDelegate(typeof(Func<UnityEngine.Object>), getter);
            }
            catch (Exception e)
            {
                log.LogWarning("Could not bind " + MyPlayerName + " as a delegate (" + e.GetType().Name
                               + "), falling back to reflection.");
                return () => getter.Invoke(null, null) as UnityEngine.Object;
            }
        }

        /// <summary>Whether this screen instance is the inventory screen.</summary>
        internal static bool IsInventoryScreen(object screen)
        {
            return screen != null && _inventoryScreen != null && _inventoryScreen.IsInstanceOfType(screen);
        }

        /// <summary>
        /// Whether there is a raid player to move.
        ///
        /// The comparison is Unity's, not C#'s, on purpose: a Player whose GameObject has been
        /// destroyed at the end of a raid compares equal to null through
        /// <c>UnityEngine.Object.op_Equality</c> while a plain reference check would still see the
        /// stale static. That is what keeps this from engaging on the out of raid character
        /// screen, which is the same InventoryScreen type.
        /// </summary>
        internal static bool RaidPlayerPresent()
        {
            if (_myPlayer == null)
            {
                return false;
            }

            UnityEngine.Object player = _myPlayer();
            return player != null;
        }
    }
}
