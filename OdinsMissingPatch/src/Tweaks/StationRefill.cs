using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Filling a station by hand reaches into the chests around you, in two ways, each with its
    /// own switch.
    ///
    /// SingleFromChests: press Use on a fire, a smelter's or an oven's fuel switch or a shield
    /// generator as ever, and one unit goes in, out of your backpack if you carry the fuel and out
    /// of the nearest chest that holds it if you do not. The shared reach (see NearbyChests) is
    /// open while one of those four add-fuel interactions runs for the local player, so the game's
    /// own "have any?" and "take one" see the chests, backpack first. Nothing refuels itself; a
    /// station only ever takes fuel when you give it some.
    ///
    /// AddAll: Shift + Use on anything that takes items up to a cap puts in everything that fits,
    /// in one go: the fuel of a fire, a smelter, an oven or a shield generator, the ore of a
    /// smelter, the food of a cooking station, the bolts of a ballista. It comes out of the
    /// backpack first and then out of the chests around the player: the reach is open while a
    /// plan is drawn up and while its items are paid for, so both see the same items. Shift + Use
    /// is handed back to the game when the game would do exactly the same, so its own messages
    /// explain a full station or nothing to put in. The hover text names what a Shift + Use would
    /// add. Every unit goes in through the station's own RPC, one call per unit, so the station's
    /// owner applies each exactly as it applies a single Use; a fire has an amount RPC and gets
    /// one call. The count is bounded here, before the RPCs go out, because a client that does
    /// not own the station would not see its cap move until the owner had answered.
    /// </summary>
    internal sealed class StationRefill : Tweak
    {
        internal static readonly StationRefill Instance = new StationRefill();

        private StationRefill() { }

        private ConfigEntry<bool> singleFromChests;
        private ConfigEntry<bool> addAll;

        internal override string Section => "Station Refill";

        protected override string Summary =>
            "Fires, smelters, ovens, cooking stations, shield generators and ballistas take what " +
            "you give them from the chests around you as well as from your backpack, and Shift + " +
            "Use fills them up in one go. Only chests placed by a player within General.ChestRange, " +
            "and not chests switched off with the Nearby use button in their panel.";

        protected override void Bind(ConfigFile config)
        {
            singleFromChests = config.Bind(Section, "SingleFromChests", true,
                "Use on a fire, or on the fuel switch of a smelter, an oven or a shield generator, " +
                "takes the one unit it adds from a chest around you when your backpack has none.");
            addAll = config.Bind(Section, "AddAll", true,
                "Shift + Use on a fire, smelter, oven, cooking station, shield generator or " +
                "ballista adds everything that fits instead of one item: out of your backpack " +
                "first and then out of the chests around you. The hover text shows what it would add.");
        }

        // ---- SingleFromChests: the game's own Use, with the chests in reach ------------------

        private static bool EnterSingle(Humanoid user)
        {
            return Instance.singleFromChests.Value && NearbyChests.EnterReach(Instance, user);
        }

        /// <summary>Use on a campfire, hearth, torch, brazier or hot tub.</summary>
        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private static class FireScope
        {
            private static void Prefix(Humanoid user, out bool __state) => __state = EnterSingle(user);

            private static void Finalizer(bool __state) => NearbyChests.LeaveReach(__state);
        }

        /// <summary>The fuel switch of a smelter or blast furnace; a kiln has none.</summary>
        [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddFuel))]
        private static class SmelterScope
        {
            private static void Prefix(Humanoid user, out bool __state) => __state = EnterSingle(user);

            private static void Finalizer(bool __state) => NearbyChests.LeaveReach(__state);
        }

        /// <summary>The fuel switch of a cooking station that burns fuel, i.e. an oven.</summary>
        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.OnAddFuelSwitch))]
        private static class OvenScope
        {
            private static void Prefix(Humanoid user, out bool __state) => __state = EnterSingle(user);

            private static void Finalizer(bool __state) => NearbyChests.LeaveReach(__state);
        }

        /// <summary>The fuel switch of a shield generator, which takes any of several fuels.</summary>
        [HarmonyPatch(typeof(ShieldGenerator), nameof(ShieldGenerator.OnAddFuel))]
        private static class ShieldGeneratorScope
        {
            private static void Prefix(Humanoid user, out bool __state) => __state = EnterSingle(user);

            private static void Finalizer(bool __state) => NearbyChests.LeaveReach(__state);
        }

        // ---- AddAll: the plan, what one Shift + Use would put in -----------------------------

        /// <summary>One kind of item and how many of it a Shift + Use would put in.</summary>
        private struct Batch
        {
            public string Name;
            public string Prefab;
            public bool Cheated;
            public int Count;
        }

        private static readonly List<Batch> Plan = new List<Batch>();

        private static readonly StringBuilder Text = new StringBuilder();

        /// <summary>The Shift + Use of the local player, on the frame the key goes down.</summary>
        private static bool Wants(Humanoid user, bool hold, bool alt)
        {
            return Instance.On && Instance.addAll.Value && alt && !hold && user != null && user == Player.m_localPlayer;
        }

        private static bool Hovering => Instance.On && Instance.addAll.Value && Player.m_localPlayer != null;

        /// <summary>What a Shift + Use goes to; each draws up its plan and sends its units its own way.</summary>
        private enum Kind
        {
            None,
            Fire,
            SmelterFuel,
            SmelterOre,
            OvenFuel,
            Food,
            ShieldFuel,
            Ammo,
        }

        /// <summary>
        /// A station and the one thing a Use on it adds, resolved once from what was used, so the
        /// Use and the hover line draw up the same plan. None for anything this does not fill,
        /// and for a station that is not (or no longer) networked.
        /// </summary>
        private readonly struct Target
        {
            private readonly Kind kind;
            private readonly MonoBehaviour station;
            private readonly ZNetView nview;

            private Target(Kind kind, MonoBehaviour station, ZNetView nview)
            {
                bool valid = nview != null && nview.IsValid();
                this.kind = valid ? kind : Kind.None;
                this.station = station;
                this.nview = nview;
            }

            internal bool IsNone => kind == Kind.None;

            internal static Target Of(Fireplace fire) => new Target(Kind.Fire, fire, fire.m_nview);

            internal static Target Of(Turret turret) => new Target(Kind.Ammo, turret, turret.m_nview);

            /// <summary>A cooking station used directly: only one without a food switch, i.e. the spit over a fire.</summary>
            internal static Target Of(CookingStation station)
            {
                return station.m_addFoodSwitch != null ? default : new Target(Kind.Food, station, station.m_nview);
            }

            /// <summary>The switches: a smelter's ore and fuel, an oven's fuel and food, a shield generator's fuel.</summary>
            internal static Target Of(Switch sw)
            {
                Smelter smelter = sw.GetComponentInParent<Smelter>();
                if (smelter != null)
                {
                    Kind kind = sw == smelter.m_addWoodSwitch ? Kind.SmelterFuel
                        : sw == smelter.m_addOreSwitch ? Kind.SmelterOre : Kind.None;
                    return new Target(kind, smelter, smelter.m_nview);
                }
                CookingStation station = sw.GetComponentInParent<CookingStation>();
                if (station != null)
                {
                    Kind kind = sw == station.m_addFuelSwitch ? Kind.OvenFuel
                        : sw == station.m_addFoodSwitch ? Kind.Food : Kind.None;
                    return new Target(kind, station, station.m_nview);
                }
                ShieldGenerator generator = sw.GetComponentInParent<ShieldGenerator>();
                if (generator != null && sw == generator.m_addFuelSwitch)
                {
                    return new Target(Kind.ShieldFuel, generator, generator.m_nview);
                }
                return default;
            }

            /// <summary>
            /// Fills <see cref="Plan"/>, with the chests in reach so the counts see them. Fuel is
            /// bounded by what the station burns, conversions by their free room, filled in the
            /// order of the station's conversion list as the game's own lookup goes.
            /// </summary>
            internal void DrawUp(Humanoid user)
            {
                Plan.Clear();
                using (new NearbyChests.Scope(Instance, user))
                {
                    int room;
                    switch (kind)
                    {
                        case Kind.Fire:
                            Fireplace fire = (Fireplace)station;
                            if (!fire.m_canRefill || fire.m_infiniteFuel)
                            {
                                return;
                            }
                            room = (int)fire.m_maxFuel - Mathf.CeilToInt(nview.GetZDO().GetFloat(ZDOVars.s_fuel));
                            Fill(user, fire.m_fuelItem, false, ref room);
                            break;
                        case Kind.SmelterFuel:
                            Smelter furnace = (Smelter)station;
                            room = FuelRoom(furnace.GetFuel(), furnace.m_maxFuel);
                            Fill(user, furnace.m_fuelItem, false, ref room);
                            break;
                        case Kind.OvenFuel:
                            CookingStation oven = (CookingStation)station;
                            if (!oven.m_useFuel)
                            {
                                return;
                            }
                            room = FuelRoom(oven.GetFuel(), oven.m_maxFuel);
                            Fill(user, oven.m_fuelItem, false, ref room);
                            break;
                        case Kind.ShieldFuel:
                            ShieldGenerator generator = (ShieldGenerator)station;
                            room = FuelRoom(generator.GetFuel(), generator.m_maxFuel);
                            foreach (ItemDrop fuel in generator.m_fuelItems)
                            {
                                Fill(user, fuel, false, ref room);
                            }
                            break;
                        case Kind.SmelterOre:
                            Smelter smelter = (Smelter)station;
                            room = smelter.m_maxOre - smelter.GetQueueSize();
                            foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                            {
                                Fill(user, conversion.m_from, true, ref room);
                            }
                            break;
                        case Kind.Food:
                            CookingStation cooker = (CookingStation)station;
                            room = FoodRoom(cooker, nview);
                            foreach (CookingStation.ItemConversion conversion in cooker.m_conversion)
                            {
                                Fill(user, conversion.m_from, true, ref room);
                            }
                            break;
                        case Kind.Ammo:
                            Turret turret = (Turret)station;
                            if (turret.m_maxAmmo <= 0)
                            {
                                return;
                            }
                            room = turret.m_maxAmmo - turret.GetAmmo();
                            ItemDrop.ItemData item = room > 0 ? AmmoFor(turret, user) : null;
                            if (item != null && item.m_dropPrefab != null)
                            {
                                Fill(user, item.m_shared.m_name, item.m_dropPrefab.name, false, ref room);
                            }
                            break;
                    }
                }
            }

            /// <summary>
            /// The Shift + Use: true when this took over; false hands it to the game, which adds
            /// one or says why not.
            /// </summary>
            internal bool Use(Humanoid user)
            {
                if (kind == Kind.None)
                {
                    return false;
                }
                DrawUp(user);
                if (LeaveToGame(user))
                {
                    return false;
                }
                if ((kind == Kind.Fire || kind == Kind.Food) && !nview.HasOwner())
                {
                    nview.ClaimOwnership();
                }
                foreach (Batch batch in Plan)
                {
                    using (new NearbyChests.Scope(Instance, user))
                    {
                        user.GetInventory().RemoveItem(batch.Name, batch.Count);
                    }
                    Send(batch);
                }
                if (kind == Kind.SmelterOre)
                {
                    Smelter smelter = (Smelter)station;
                    smelter.m_addedOreTime = Time.time;
                    if (smelter.m_addOreAnimationDuration > 0f)
                    {
                        smelter.SetAnimation(active: true);
                    }
                }
                user.Message(MessageHud.MessageType.Center, "$msg_added " + Describe());
                return true;
            }

            /// <summary>
            /// The units of one batch, through the station's own RPC: one call per unit, except
            /// the fire's amount RPC, which the owner clamps.
            /// </summary>
            private void Send(Batch batch)
            {
                if (kind == Kind.Fire)
                {
                    nview.InvokeRPC("RPC_AddFuelAmount", (float)batch.Count);
                    return;
                }
                for (int i = 0; i < batch.Count; i++)
                {
                    switch (kind)
                    {
                        case Kind.SmelterFuel:
                        case Kind.OvenFuel:
                        case Kind.ShieldFuel:
                            nview.InvokeRPC("RPC_AddFuel");
                            break;
                        case Kind.SmelterOre:
                            nview.InvokeRPC("RPC_AddOre", batch.Prefab, batch.Cheated);
                            break;
                        case Kind.Food:
                            nview.InvokeRPC("RPC_AddItem", batch.Prefab, batch.Cheated);
                            Skills.SkillType skill = ((CookingStation)station).m_skill;
                            if (skill != Skills.SkillType.None)
                            {
                                Player.m_localPlayer.RaiseSkill(skill, 0.4f);
                            }
                            break;
                        case Kind.Ammo:
                            Game.instance.IncrementPlayerStat(PlayerStatType.TurretAmmoAdded);
                            nview.InvokeRPC("RPC_AddAmmo", batch.Prefab);
                            break;
                    }
                }
            }

            /// <summary>The line a hover adds: what a Shift + Use would put in, or nothing.</summary>
            internal string HoverLine()
            {
                if (kind == Kind.None)
                {
                    return "";
                }
                DrawUp(Player.m_localPlayer);
                if (Total() <= 0)
                {
                    return "";
                }
                string alt = ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive() ? "$KEY_AltKeys" : "$KEY_AltPlace";
                // The list is translated first: what goes in as $1 is put there after the lookup,
                // so an item name left in it would never be looked up itself.
                string items = Localization.instance.Localize(Describe());
                return Localization.instance.Localize(
                    "\n[<color=yellow><b>" + alt + " + $KEY_Use</b></color>] $omp_add_all", items);
            }
        }

        /// <summary>Adds up to the room left of one item, as much as the reach holds, to the plan.</summary>
        private static void Fill(Humanoid user, ItemDrop item, bool keepKind, ref int room)
        {
            if (item != null)
            {
                Fill(user, item.m_itemData.m_shared.m_name, item.gameObject.name, keepKind, ref room);
            }
        }

        /// <summary>
        /// <paramref name="keepKind"/> is for a conversion, whose RPC carries the item's prefab
        /// and cheat flag: the flag is copied off a unit of it in reach.
        /// </summary>
        private static void Fill(Humanoid user, string name, string prefab, bool keepKind, ref int room)
        {
            if (room <= 0)
            {
                return;
            }
            int count = Mathf.Min(room, user.GetInventory().CountItems(name));
            if (count <= 0)
            {
                return;
            }
            bool cheated = false;
            if (keepKind)
            {
                ItemDrop.ItemData sample = Sample(user, name);
                cheated = sample != null && sample.m_cheated;
            }
            Plan.Add(new Batch { Name = name, Prefab = prefab, Cheated = cheated, Count = count });
            room -= count;
        }

        /// <summary>
        /// A unit of the item to copy the cheat flag off: the backpack's first, else the first a
        /// chest in reach holds. Only ever asked about an item a count has already found, so the
        /// chest walk is never paid for something nobody has.
        /// </summary>
        private static ItemDrop.ItemData Sample(Humanoid user, string name)
        {
            ItemDrop.ItemData item = user.GetInventory().GetItem(name);
            if (item != null || Player.m_localPlayer == null)
            {
                return item;
            }
            foreach (Container chest in NearbyChests.Find(Player.m_localPlayer.transform.position))
            {
                item = chest.GetInventory().GetItem(name);
                if (item != null)
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>
        /// The ammunition the ballista would load: the game's own lookup on the backpack, and
        /// failing that the same lookup on each chest in reach, nearest first. It picks the type,
        /// so an empty ballista loads whatever the nearest chest has and a loaded one only ever
        /// gets more of what is already in it.
        /// </summary>
        private static ItemDrop.ItemData AmmoFor(Turret turret, Humanoid user)
        {
            ItemDrop.ItemData item = turret.FindAmmoItem(user.GetInventory(), onlyCurrentlyLoadableType: true);
            if (item != null || Player.m_localPlayer == null)
            {
                return item;
            }
            foreach (Container chest in NearbyChests.Find(Player.m_localPlayer.transform.position))
            {
                item = turret.FindAmmoItem(chest.GetInventory(), onlyCurrentlyLoadableType: true);
                if (item != null)
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>
        /// How many units a smelter, an oven or a shield generator still takes: the game adds
        /// while the fuel is at most one below the cap, so a fraction burnt off makes room for
        /// one more.
        /// </summary>
        private static int FuelRoom(float fuel, int maxFuel)
        {
            if (fuel > maxFuel - 1)
            {
                return 0;
            }
            return Mathf.FloorToInt(maxFuel - 1 - fuel) + 1;
        }

        /// <summary>
        /// The free slots of a cooking station. None while something is done, since Use then
        /// takes that off first, and none while the fire below is out.
        /// </summary>
        private static int FoodRoom(CookingStation station, ZNetView nview)
        {
            if (station.HaveDoneItem() || (station.m_requireFire && !station.IsFireLit()))
            {
                return 0;
            }
            int room = 0;
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                if (nview.GetZDO().GetString("slot" + i) == "")
                {
                    room++;
                }
            }
            return room;
        }

        /// <summary>
        /// True when the game's own Use would do exactly what the plan says: nothing at all, or
        /// the single unit the backpack already pays for. The Use is handed back then, so the
        /// vanilla message, effect and skill explain a full station or an empty backpack.
        /// </summary>
        private static bool LeaveToGame(Humanoid user)
        {
            int total = Total();
            return total == 0
                || (total == 1 && NearbyChests.Carried(user.GetInventory(), Plan[0].Name, -1, true) > 0);
        }

        private static int Total()
        {
            int total = 0;
            foreach (Batch batch in Plan)
            {
                total += batch.Count;
            }
            return total;
        }

        /// <summary>"6 $item_wood, 2 $item_coal", for the message and the hover line.</summary>
        private static string Describe()
        {
            Text.Length = 0;
            foreach (Batch batch in Plan)
            {
                if (Text.Length > 0)
                {
                    Text.Append(", ");
                }
                Text.Append(batch.Count).Append(' ').Append(batch.Name);
            }
            return Text.ToString();
        }

        // ---- AddAll: the interactions --------------------------------------------------------

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private static class FireUse
        {
            private static bool Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt)
            {
                return !Wants(user, hold, alt) || !Target.Of(__instance).Use(user);
            }
        }

        [HarmonyPatch(typeof(Switch), nameof(Switch.Interact))]
        private static class SwitchUse
        {
            private static bool Prefix(Switch __instance, Humanoid character, bool hold, bool alt)
            {
                return !Wants(character, hold, alt) || !Target.Of(__instance).Use(character);
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
        private static class StationUse
        {
            private static bool Prefix(CookingStation __instance, Humanoid user, bool hold, bool alt)
            {
                return !Wants(user, hold, alt) || !Target.Of(__instance).Use(user);
            }
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.Interact))]
        private static class BallistaUse
        {
            private static bool Prefix(Turret __instance, Humanoid character, bool hold, bool alt)
            {
                return !Wants(character, hold, alt) || !Target.Of(__instance).Use(character);
            }
        }

        // ---- AddAll: the hover lines ---------------------------------------------------------

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private static class FireHover
        {
            private static void Postfix(Fireplace __instance, ref string __result)
            {
                if (Hovering && !string.IsNullOrEmpty(__result))
                {
                    __result += Target.Of(__instance).HoverLine();
                }
            }
        }

        [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
        private static class SwitchHover
        {
            private static void Postfix(Switch __instance, ref string __result)
            {
                if (Hovering && !string.IsNullOrEmpty(__result))
                {
                    __result += Target.Of(__instance).HoverLine();
                }
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
        private static class StationHover
        {
            private static void Postfix(CookingStation __instance, ref string __result)
            {
                if (Hovering && !string.IsNullOrEmpty(__result))
                {
                    __result += Target.Of(__instance).HoverLine();
                }
            }
        }

        /// <summary>Only on a ballista that shoots enemies and that the ward lets the player load.</summary>
        [HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))]
        private static class BallistaHover
        {
            private static void Postfix(Turret __instance, ref string __result)
            {
                if (Hovering && !string.IsNullOrEmpty(__result) && __instance.m_targetEnemies
                    && PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false))
                {
                    __result += Target.Of(__instance).HoverLine();
                }
            }
        }
    }
}
