using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsPaths
{
    /// <summary>
    /// The docks' furniture: copies of the game's pieces (the harbour blueprints' furniture, <see cref="Blueprints"/>)
    /// that give nothing back. A piece nobody placed drops a third of what it costs when it
    /// breaks (<c>Piece.DropResources</c>, at least one of each), and the hammer takes down any
    /// piece outside a ward - so a dragon bed on a Meadows dock would be iron nails and a
    /// blackmetal chest blackmetal, long before the player could make either. A relic is the same
    /// piece under a name of its own (<see cref="Prefix"/>), with none of its resources recovered
    /// and the hammer's removal refused; it still breaks when hit, and is gone.
    ///
    /// Like the harbour stone, a relic is a prefab of the mod's own, registered after
    /// <c>ZoneSystem.Start</c> on the server and on every client with the mod: a client without it
    /// sees the dock but not its furniture.
    /// </summary>
    internal static class Relics
    {
        public const string Prefix = "OdinsPaths_Relic_";

        private static readonly Dictionary<string, GameObject> relics = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> failed = new HashSet<string>();
        private static GameObject holder;

        /// <summary>Every blueprint's furniture - its deco list and its deco pieces - and the spots' defaults into this scene's <c>ZNetScene</c>, copied the first time.</summary>
        public static void Register()
        {
            if (ZNetScene.instance == null)
            {
                return;
            }
            foreach (string name in Builder.DefaultDeco)
            {
                Get(name);
            }
            foreach (Blueprint blueprint in Blueprints.All)
            {
                foreach (string name in blueprint.deco ?? new string[0])
                {
                    Get(name);
                }
                foreach (BlueprintPiece piece in blueprint.pieces)
                {
                    if (piece.Role == Role.Deco)
                    {
                        Get(piece.prefab);
                    }
                }
            }
        }

        /// <summary>The relic of a piece, or null if the game has no such piece.</summary>
        public static GameObject Get(string name)
        {
            GameObject relic = Make(name);
            if (relic != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.m_namedPrefabs[relic.name.GetStableHashCode()] = relic;
            }
            return relic;
        }

        /// <summary>
        /// The piece copied under an inactive holder that outlives the scene (so the copy's
        /// <c>Awake</c> runs only when the game instantiates it), renamed, its resources kept for
        /// the hover text but none recovered, and not removable with the hammer.
        /// </summary>
        private static GameObject Make(string name)
        {
            if (relics.TryGetValue(name, out GameObject known))
            {
                return known;
            }
            if (failed.Contains(name) || ZNetScene.instance == null)
            {
                return null;
            }
            GameObject source = ZNetScene.instance.GetPrefab(name);
            if (source == null || source.GetComponent<ZNetView>() == null)
            {
                failed.Add(name);
                Debug.LogWarning("[OdinsPaths] Harbours: no piece " + name + " to make a relic of - left out.");
                return null;
            }
            if (holder == null)
            {
                holder = new GameObject("OdinsPaths_Relics");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            GameObject relic = Object.Instantiate(source, holder.transform);
            relic.name = Prefix + name;
            relic.transform.localPosition = Vector3.zero;
            relic.transform.localRotation = Quaternion.identity;
            Piece piece = relic.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_canBeRemoved = false;
                foreach (Piece.Requirement requirement in piece.m_resources ?? new Piece.Requirement[0])
                {
                    requirement.m_recover = false;
                }
            }
            relics[name] = relic;
            return relic;
        }
    }

    public partial class OdinsPathsPlugin
    {
        /// <summary>The docks' furniture joins the scene's prefabs, on the server and on every client with the mod.</summary>
        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        public static class RegisterRelics
        {
            private static void Postfix()
            {
                Relics.Register();
            }
        }
    }
}
