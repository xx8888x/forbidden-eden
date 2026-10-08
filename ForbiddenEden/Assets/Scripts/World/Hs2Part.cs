using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>A skinned mesh of an exported clothes part, bound to the wearer's skeleton by bone name.</summary>
    [Serializable]
    public sealed class Hs2PartMesh
    {
        public string Name;
        public Mesh Mesh;
        public Material[] Materials;
        public string[] Bones;
        public float[] BlendWeights;
    }

    /// <summary>
    /// Sits on every exported HS2 part prefab (Resources/HS2/Parts/…). Clothes carry their meshes as data and are
    /// re-bound to the wearer's body bones; hair and accessories are self-contained hierarchies parented to a
    /// named node (N_hair_Root, N_Head_top…). Spring chains (skirts, ribbons) are stored as disabled DynamicBone
    /// templates whose roots are body bones, named in <see cref="SpringRoots"/>.
    /// </summary>
    public sealed class Hs2Part : MonoBehaviour
    {
        public string Category;
        public int Id;
        public Hs2PartMesh[] Meshes = Array.Empty<Hs2PartMesh>();
        public DynamicBone[] Springs = Array.Empty<DynamicBone>();
        public string[] SpringRoots = Array.Empty<string>();
        /// <summary>Colours 1–4 (HS2 defaults); which of the first three the player may change.</summary>
        public Color[] DefaultColors = { Color.white, Color.white, Color.white, Color.white };
        public bool[] UseColor = { true, false, false };
        /// <summary>Node the hierarchy is parented to; empty for re-bound clothes.</summary>
        public string AttachTo = "";
        /// <summary>Shoes: the body's foot triangles are hidden so toes do not poke through.</summary>
        public bool HidesFeet;
        /// <summary>One-piece tops (dresses, swimsuits) leave the bottom slot empty.</summary>
        public bool FullBody;
    }

    /// <summary>One selectable item in the HS2 parts catalogue (Resources/HS2/Catalog.json).</summary>
    [Serializable]
    public sealed class Hs2CatalogItem
    {
        public string Slot;     // hair_b, hair_f, hair_s, hair_o, top, bot, socks, shoes, gloves, panst, acc_head, …
        public string Sex;      // f, m or "" for both
        public int Id;          // HS2 list id
        public string Name;     // display name (繁體中文)
        public string Prefab;   // Resources path
        public string Thumb;    // Resources path, may be empty
        public string Tag;      // uniform, casual, sport, formal, swim, … for NPC outfit picks
    }

    [Serializable]
    public sealed class Hs2Catalog
    {
        public List<Hs2CatalogItem> Items = new List<Hs2CatalogItem>();

        private static Hs2Catalog _loaded;

        /// <summary>The exported catalogue, or an empty one when nothing was exported.</summary>
        public static Hs2Catalog Load()
        {
            if (_loaded != null)
                return _loaded;
            var text = Resources.Load<TextAsset>("HS2/Catalog");
            _loaded = text != null ? JsonUtility.FromJson<Hs2Catalog>(text.text) : new Hs2Catalog();
            return _loaded;
        }

        public List<Hs2CatalogItem> For(string slot, bool female)
        {
            string sex = female ? "f" : "m";
            return Items.FindAll(i => i.Slot == slot && (i.Sex == "" || i.Sex == sex));
        }

        public Hs2CatalogItem Find(string slot, int id, bool female)
        {
            string sex = female ? "f" : "m";
            return Items.Find(i => i.Slot == slot && i.Id == id && (i.Sex == "" || i.Sex == sex));
        }
    }
}
