using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Category = OdinsMissingPatch.UniversalPins.Category;
using PinData = Minimap.PinData;

namespace OdinsMissingPatch
{
    /// <summary>
    /// Hands an auto pin to every player online the moment it is made, with no table in between:
    /// a routed RPC to everybody. The server forwards it to each client whether or not the server
    /// has the mod, and a client without the mod ignores the unknown method, so nothing breaks
    /// for anyone. A player who comes in later asks everyone for their universal pins once, and
    /// every modded client answers with its list, so a map is caught up without a walk to a
    /// table. What is sent is the pin's category, name and position; the receiver picks its own
    /// icon and applies its own rules (its category switches, PinSpacing, the pins it dismissed),
    /// exactly as if it had found the place itself.
    /// <para>
    /// The tables are still there for what this cannot do: a player without the mod, and a
    /// player who was offline when the pin was made and never asked - the request goes out once
    /// per session, so the pins made after it are the live broadcasts.
    /// </para>
    /// </summary>
    internal static class PinBroadcast
    {
        private const string PinsRpc = "OdinsMissingPatch_Pins";
        private const string RequestRpc = "OdinsMissingPatch_PinsRequest";

        /// <summary>The package layout: version, announce, count, then category, name, position per pin.</summary>
        private const int Version = 1;

        private static ZRoutedRpc registeredOn;
        private static ZRoutedRpc requestedOn;

        private static bool Sharing => AutoPins.Instance.Sharing;

        /// <summary>Tells every other player about a pin this client just made.</summary>
        internal static void Send(Category category, Vector3 pos, string name)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || !Sharing)
            {
                return;
            }
            ZPackage package = new ZPackage();
            package.Write(Version);
            package.Write(true);
            package.Write(1);
            WritePin(package, category, name, pos);
            rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, PinsRpc, package);
        }

        /// <summary>
        /// Asks every other player for their universal pins, once per session. Called from the
        /// sweep, so a player who switches sharing on mid-game asks then.
        /// </summary>
        internal static void RequestOnce()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || rpc == requestedOn || !Sharing || Player.m_localPlayer == null)
            {
                return;
            }
            requestedOn = rpc;
            rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RequestRpc);
        }

        private static void WritePin(ZPackage package, Category category, string name, Vector3 pos)
        {
            package.Write((int)category);
            package.Write(name ?? "");
            package.Write(pos);
        }

        /// <summary>Everybody includes the sender, so its own broadcast comes back to it.</summary>
        private static bool FromSelf(long sender)
        {
            return ZRoutedRpc.instance == null || sender == ZRoutedRpc.instance.m_id;
        }

        private static void RPC_Pins(long sender, ZPackage package)
        {
            if (FromSelf(sender) || !Sharing || Player.m_localPlayer == null || Minimap.instance == null
                || package == null || package.ReadInt() != Version)
            {
                return;
            }
            bool announce = package.ReadBool();
            int count = package.ReadInt();
            for (int i = 0; i < count; i++)
            {
                int category = package.ReadInt();
                string name = package.ReadString();
                Vector3 pos = package.ReadVector3();
                if (category >= 0 && category < UniversalPins.CategoryCount)
                {
                    AutoPins.Instance.Receive((Category)category, pos, name, announce);
                }
            }
        }

        /// <summary>A player asked for the pins: every saved universal pin of this map goes back to them, quietly.</summary>
        private static void RPC_Request(long sender)
        {
            if (FromSelf(sender) || !Sharing || Player.m_localPlayer == null || Minimap.instance == null)
            {
                return;
            }
            var pins = new List<PinData>();
            foreach (PinData pin in Minimap.instance.m_pins)
            {
                if (pin.m_save && UniversalPins.TryGetCategory(pin, out _))
                {
                    pins.Add(pin);
                }
            }
            if (pins.Count == 0)
            {
                return;
            }
            ZPackage package = new ZPackage();
            package.Write(Version);
            package.Write(false);
            package.Write(pins.Count);
            foreach (PinData pin in pins)
            {
                UniversalPins.TryGetCategory(pin, out Category category);
                WritePin(package, category, pin.m_name, pin.m_pos);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, PinsRpc, package);
        }

        /// <summary>ZNet builds a fresh ZRoutedRpc per session, so re-register once per game.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        [Serves(typeof(AutoPins), Optional = true)]
        [LoadHook]
        private static class Register
        {
            private static void Postfix()
            {
                ZRoutedRpc rpc = ZRoutedRpc.instance;
                if (rpc == null || rpc == registeredOn)
                {
                    return;
                }
                registeredOn = rpc;
                rpc.Register<ZPackage>(PinsRpc, RPC_Pins);
                rpc.Register(RequestRpc, RPC_Request);
            }
        }
    }
}
