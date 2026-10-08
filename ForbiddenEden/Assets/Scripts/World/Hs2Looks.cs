using UnityEngine;

namespace ForbiddenEden.World
{
    /// <summary>Ready-made HS2 outfits (HS2 list ids) for the player's starting look and for NPCs.</summary>
    public static class Hs2Looks
    {
        // Natural school-life hair styles (HS2 so_hair_b / so_hair_f ids).
        private static readonly int[] FemaleBack = { 0, 1, 2, 4, 5, 7, 9, 12, 13, 14, 19, 20, 27, 52, 54, 63 };
        private static readonly int[] FemaleFront = { 1, 2, 3, 4, 5, 6, 7, 10, 13, 14, 15, 16, 53, 56, 57 };
        private static readonly int[] MaleBack = { 1, 9, 14, 55 };
        private static readonly int[] MaleFront = { 2, 3, 10, 15, 16, 54, 56 };

        private static readonly Color[] HairPalette =
        {
            new Color(0.10f, 0.09f, 0.09f), new Color(0.20f, 0.13f, 0.10f), new Color(0.33f, 0.20f, 0.13f),
            new Color(0.38f, 0.16f, 0.12f), new Color(0.36f, 0.31f, 0.28f), new Color(0.62f, 0.45f, 0.27f),
        };

        private static readonly Color White = new Color(0.95f, 0.95f, 0.97f);
        private static readonly Color Navy = new Color(0.13f, 0.17f, 0.33f);
        private static readonly Color Scarf = new Color(0.72f, 0.12f, 0.16f);
        private static readonly Color Indoor = new Color(0.22f, 0.36f, 0.72f);

        /// <summary>The academy's summer uniform: sailor top and pleated skirt, or shirt and trousers.</summary>
        public static Hs2Look Uniform(bool female)
        {
            var look = new Hs2Look { Female = female };
            if (female)
            {
                look.HairB = 1;
                look.HairF = 2;
                look.Top = 32;   // セーラー服
                look.Bot = 4;    // プリーツスカート
                look.Socks = 2;  // ハイソックス
                look.Shoes = 12; // 上履き
                look.SetColors("top", new[] { White, Navy, Scarf, White });
                look.SetColors("bot", new[] { Navy, Navy, Scarf, Color.white });
                look.SetColors("socks", new[] { new Color(0.12f, 0.13f, 0.20f), new Color(0.12f, 0.13f, 0.20f), Color.white, Color.white });
            }
            else
            {
                look.HairB = 14;
                look.HairF = 2;
                look.Top = 2;    // Yシャツ(A)
                look.Bot = 4;    // スーツパンツ
                look.Shoes = 10; // 上履き
                look.SetColors("top", new[] { White, new Color(0.90f, 0.91f, 0.93f), Navy, new Color(0.80f, 0.81f, 0.84f) });
                look.SetColors("bot", new[] { new Color(0.17f, 0.19f, 0.27f), new Color(0.12f, 0.13f, 0.18f), new Color(0.12f, 0.13f, 0.18f), Color.white });
            }
            look.SetColors("shoes", new[] { new Color(0.96f, 0.96f, 0.96f), Indoor, Indoor, Color.white });
            return look;
        }

        /// <summary>Gym clothes for the arena and other sports scenes.</summary>
        public static Hs2Look Sport(bool female)
        {
            Hs2Look look = Uniform(female);
            look.Colors.Clear();
            if (female)
            {
                look.Top = 221;  // ジャージトップス
                look.Bot = 212;  // ジャージパンツ
                look.Socks = 4;  // スニーカーソックス
                look.Shoes = 7;  // スニーカー
            }
            else
            {
                look.Top = 1;    // Tシャツ
                look.Bot = 2;    // ハーフパンツ
                look.Shoes = 5;  // ランニングシューズ
            }
            return look;
        }

        /// <summary>A stable look for an NPC: hair from the id, clothes for the scene.</summary>
        public static Hs2Look ForNpc(string npcId, bool female, SetKind scene)
        {
            int h = 17;
            foreach (char c in npcId ?? string.Empty)
                h = unchecked(h * 31 + c);
            var rng = new System.Random(h);
            Hs2Look look = scene == SetKind.Arena ? Sport(female) : Uniform(female);
            if (scene == SetKind.Pool && !female)
            {
                look.Top = -1;
                look.Bot = 202;  // 海パン
                look.Shoes = 2;  // ビーチサンダル
                look.Colors.Clear();
            }
            look.HairB = female ? FemaleBack[rng.Next(FemaleBack.Length)] : MaleBack[rng.Next(MaleBack.Length)];
            look.HairF = female ? FemaleFront[rng.Next(FemaleFront.Length)] : MaleFront[rng.Next(MaleFront.Length)];
            look.HairColor = HairPalette[rng.Next(HairPalette.Length)];
            look.EyeColor = Color.HSVToRGB((float)rng.NextDouble(), 0.6f, 0.45f);
            // Some students wear knee socks or loafers instead.
            if (female && rng.Next(3) == 0 && scene != SetKind.Arena)
                look.Socks = 3;
            if (rng.Next(3) == 0 && scene != SetKind.Arena && scene != SetKind.Pool)
            {
                look.Shoes = 3; // ローファー
                look.SetColors("shoes", null);
            }
            return look;
        }
    }
}
