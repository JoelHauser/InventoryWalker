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
    /// The names below are BSG's own, unobfuscated in both the patched and unpatched assemblies,
    /// so this is not a bet on the delta's renaming. The one private member it needs, the
    /// ignore-input flag, is found through its public setter's IL rather than by name.
    /// </summary>
    internal static class GameTypes
    {
        internal const string UIScreenName = "EFT.UI.Screens.UIScreen";
        internal const string InventoryScreenName = "EFT.UI.InventoryScreen";
        internal const string GamePlayerOwnerName = "EFT.GamePlayerOwner";
        internal const string TranslateAxesName = "TranslateAxes";
        internal const string MyPlayerName = "MyPlayer";
        internal const string SetIgnoreInputName = "SetIgnoreInput";

        internal const string EftGamePlayerOwnerName = "EFT.EftGamePlayerOwner";
        internal const string HideoutPlayerOwnerName = "EFT.HideoutPlayerOwner";
        internal const string PlayerOwnerName = "EFT.PlayerOwner";
        internal const string PlayerName = "EFT.Player";
        internal const string LootItemName = "EFT.Interactive.LootItem";
        internal const string LootableContainerName = "EFT.Interactive.LootableContainer";
        internal const string ItemControllerName = "EFT.InventoryLogic.ItemController";
        internal const string ECommandName = "EFT.InputSystem.ECommand";
        internal const string ShowInventoryScreenLootName = "ShowInventoryScreenLoot";

        /// <summary>
        /// <c>EftGamePlayerOwner.ShowInventoryScreenLoot(CompoundItem loot, Action callback, bool)</c>.
        /// Every raid loot source opens through it: loose items and bags, bodies (a
        /// <c>Corpse</c> is a <c>LootItem</c>), and lootable containers. So does the hideout
        /// stash, because <c>HideoutPlayerOwner</c> derives from it; that one is filtered out.
        /// </summary>
        internal static MethodInfo ShowInventoryScreenLoot { get; private set; }

        private static Type _hideoutOwner;
        private static MethodInfo _ownerPlayer;
        private static MethodInfo _interactableObject;
        private static Type _lootItem;
        private static FieldInfo _lootItemOwner;
        private static Type _lootableContainer;
        private static FieldInfo _containerOwner;
        private static MethodInfo _rootItem;
        private static MethodInfo _translateCommand;
        private static object _toggleInventory;

        /// <summary>
        /// <c>UIScreen.TranslateAxes(ref float[])</c>. The method that nulls the axes. The first
        /// of the two gates.
        /// </summary>
        internal static MethodInfo TranslateAxes { get; private set; }

        /// <summary>
        /// <c>GamePlayerOwner.TranslateAxes(ref float[])</c>, the override declared on the owner
        /// itself. It returns on its first line while the ignore-input flag is set, which every
        /// screen opening sets by default. The second gate.
        /// </summary>
        internal static MethodInfo OwnerTranslateAxes { get; private set; }

        /// <summary>
        /// The static bool behind <c>GamePlayerOwner.SetIgnoreInput</c>. Private, and named
        /// <c>bool_0</c> in the patched 0.16.9 client, so it is found by reading the one field
        /// the public setter stores to rather than by that name.
        /// </summary>
        private static AccessTools.FieldRef<bool> _ignoreInput;

        /// <summary>The ignore-input flag, by reference, for reading and for putting back.</summary>
        internal static ref bool IgnoreInput => ref _ignoreInput();

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

            OwnerTranslateAxes = AccessTools.DeclaredMethod(owner, TranslateAxesName);
            if (OwnerTranslateAxes == null)
            {
                log.LogError("Could not find " + GamePlayerOwnerName + "." + TranslateAxesName
                             + ". Not patching anything.");
                return false;
            }

            FieldInfo ignoreInput = FindIgnoreInputField(owner, log);
            if (ignoreInput == null)
            {
                return false;
            }

            _ignoreInput = AccessTools.StaticFieldRefAccess<bool>(ignoreInput);

            log.LogInfo("Resolved " + UIScreenName + "." + TranslateAxesName + ", "
                        + InventoryScreenName + ", " + GamePlayerOwnerName + "." + MyPlayerName + ", "
                        + GamePlayerOwnerName + "." + TranslateAxesName + " and the ignore-input flag ("
                        + ignoreInput.Name + ").");
            return true;
        }

        /// <summary>
        /// Reads <c>GamePlayerOwner.SetIgnoreInput(bool)</c>, whose whole body is
        /// <c>ldarg.0; stsfld flag; ret</c>, and returns the field it stores to. Insists on
        /// exactly one static bool store, so a client that changes the setter fails here with a
        /// log line instead of the patch lifting the wrong flag.
        /// </summary>
        private static FieldInfo FindIgnoreInputField(Type owner, ManualLogSource log)
        {
            MethodInfo setter = AccessTools.Method(owner, SetIgnoreInputName, new[] { typeof(bool) });
            if (setter == null)
            {
                log.LogError("Could not find " + GamePlayerOwnerName + "." + SetIgnoreInputName
                             + "(bool). Not patching anything.");
                return null;
            }

            FieldInfo found = null;
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(setter))
            {
                if (instruction.opcode != System.Reflection.Emit.OpCodes.Stsfld)
                {
                    continue;
                }

                if (found != null || !(instruction.operand is FieldInfo field) || field.FieldType != typeof(bool))
                {
                    log.LogError(GamePlayerOwnerName + "." + SetIgnoreInputName
                                 + " no longer stores to exactly one static bool. Not patching anything.");
                    return null;
                }

                found = field;
            }

            if (found == null)
            {
                log.LogError(GamePlayerOwnerName + "." + SetIgnoreInputName
                             + " stores to no static field. Not patching anything.");
            }

            return found;
        }

        /// <summary>
        /// Resolves what the loot range needs. Separate from <see cref="Resolve"/> because the
        /// range is a guard on top of walking rather than part of it: if a client update breaks
        /// one of these, walking still works and the log says the range is off.
        /// </summary>
        internal static bool ResolveLootRange(ManualLogSource log)
        {
            Type eftOwner = AccessTools.TypeByName(EftGamePlayerOwnerName);
            ShowInventoryScreenLoot = eftOwner == null ? null : AccessTools.DeclaredMethod(eftOwner, ShowInventoryScreenLootName);
            _hideoutOwner = AccessTools.TypeByName(HideoutPlayerOwnerName);

            Type playerOwner = AccessTools.TypeByName(PlayerOwnerName);
            _ownerPlayer = playerOwner == null ? null : AccessTools.PropertyGetter(playerOwner, "Player");

            Type player = AccessTools.TypeByName(PlayerName);
            _interactableObject = player == null ? null : AccessTools.PropertyGetter(player, "InteractableObject");

            _lootItem = AccessTools.TypeByName(LootItemName);
            _lootItemOwner = _lootItem == null ? null : AccessTools.Field(_lootItem, "ItemOwner");
            _lootableContainer = AccessTools.TypeByName(LootableContainerName);
            _containerOwner = _lootableContainer == null ? null : AccessTools.Field(_lootableContainer, "ItemOwner");

            Type itemController = AccessTools.TypeByName(ItemControllerName);
            _rootItem = itemController == null ? null : AccessTools.PropertyGetter(itemController, "RootItem");

            // Closing goes through the screen's own Tab handler, so it is the same close as a key
            // press: InventoryScreen.TranslateCommand(ToggleInventory) -> ScreenController.CloseScreen().
            _translateCommand = _inventoryScreen == null ? null : AccessTools.DeclaredMethod(_inventoryScreen, "TranslateCommand");
            Type command = AccessTools.TypeByName(ECommandName);
            _toggleInventory = command != null && Enum.IsDefined(command, "ToggleInventory")
                ? Enum.Parse(command, "ToggleInventory")
                : null;

            string missing =
                ShowInventoryScreenLoot == null ? EftGamePlayerOwnerName + "." + ShowInventoryScreenLootName
                : _hideoutOwner == null ? HideoutPlayerOwnerName
                : _ownerPlayer == null ? PlayerOwnerName + ".Player"
                : _interactableObject == null ? PlayerName + ".InteractableObject"
                : _lootItemOwner == null ? LootItemName + ".ItemOwner"
                : _containerOwner == null ? LootableContainerName + ".ItemOwner"
                : _rootItem == null ? ItemControllerName + ".RootItem"
                : _translateCommand == null ? InventoryScreenName + ".TranslateCommand"
                : _toggleInventory == null ? ECommandName + ".ToggleInventory"
                : null;

            if (missing != null)
            {
                log.LogError("Could not find " + missing + ". Walking still works, but the loot range is off.");
                return false;
            }

            return true;
        }

        /// <summary>Whether this owner is the hideout's, whose stash has no place in the world.</summary>
        internal static bool IsHideoutOwner(object owner)
        {
            return owner != null && _hideoutOwner.IsInstanceOfType(owner);
        }

        /// <summary><c>PlayerOwner.Player</c>.</summary>
        internal static UnityEngine.Component OwnerPlayer(object owner)
        {
            return _ownerPlayer.Invoke(owner, null) as UnityEngine.Component;
        }

        /// <summary>Whether this player is the raid's own player, by Unity's equality.</summary>
        internal static bool IsRaidPlayer(UnityEngine.Component player)
        {
            return player != null && _myPlayer != null && _myPlayer() == player;
        }

        /// <summary>
        /// The world object whose loot this is, or null. Reads what the player is interacting
        /// with and accepts it only if its item owner's root item is the very item being shown,
        /// so a player who looked away while a networked interaction was in flight does not get
        /// measured against the wrong thing.
        /// </summary>
        internal static UnityEngine.Transform FindLootAnchor(UnityEngine.Component player, object loot)
        {
            if (!(_interactableObject.Invoke(player, null) is UnityEngine.Component interactive) || interactive == null)
            {
                return null;
            }

            FieldInfo ownerField = _lootItem.IsInstanceOfType(interactive) ? _lootItemOwner
                : _lootableContainer.IsInstanceOfType(interactive) ? _containerOwner
                : null;
            object itemOwner = ownerField?.GetValue(interactive);
            object root = itemOwner == null ? null : _rootItem.Invoke(itemOwner, null);

            return root != null && ReferenceEquals(root, loot) ? interactive.transform : null;
        }

        /// <summary>Closes the inventory screen exactly as pressing Tab would.</summary>
        internal static void CloseInventoryScreen(object screen)
        {
            _translateCommand.Invoke(screen, new[] { _toggleInventory });
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
