using System;
using UnityEngine;

namespace RavenIron.RagnaroksWrath.Visuals
{
    /// <summary>
    /// The shared scaffolding every procedural emitter builds on, extracted from PlagueFog
    /// when FrostBreath became the second customer. The candidate-shader chain is the part
    /// that must never fork: it encodes the 0.7.0 lesson (Valheim strips Unity's standard
    /// particle shaders — two candidates, both absent, no fog anywhere), and a fix applied
    /// to one emitter's private copy but not another's would resurrect exactly that bug.
    /// </summary>
    internal static class ParticleKit
    {
        /// <summary>
        /// Shaders Valheim's build might actually contain, in soft-haze-first order.
        /// "Particles/Standard Unlit" is CONFIRMED stripped from Valheim's build, kept first
        /// only for future Unity versions; "Sprites/Default" is the first that ships.
        /// </summary>
        private static readonly string[] CandidateShaders =
        {
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Sprites/Default",
            "UI/Default",
            "Particles/Standard Surface",
            "Legacy Shaders/Particles/Additive",
        };

        /// <summary>
        /// A soft radial-falloff blob material, generated rather than shipped. Every
        /// candidate shader is tried before giving up (Shader.Find returns null on stripped
        /// builds), and the chosen one is logged under <paramref name="owner"/>'s name,
        /// because two clients disagreeing about a visual is otherwise undiagnosable.
        /// Throws when no candidate resolves — the caller's build path owns the latch.
        /// </summary>
        public static Material BuildMaterial(string owner)
        {
            Shader shader = FindShader(owner);

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            float half = (size - 1) / 2f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));   // squared: soft edge, dense heart
            }
            tex.Apply();

            return new Material(shader) { mainTexture = tex };
        }

        /// <summary>The one place the candidate chain lives — see the class comment.</summary>
        private static Shader FindShader(string owner)
        {
            foreach (string name in CandidateShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader != null)
                {
                    RagnaroksWrath.Log.LogInfo($"{owner}: using shader '{name}'.");
                    return shader;
                }
            }
            throw new InvalidOperationException("no particle shader available");
        }

        /// <summary>
        /// A 2x2 sheet of rune glyphs — fehu, algiz, gebo, thurisaz — drawn as raster
        /// strokes, generated rather than shipped (the no-assets rule is absolute; the
        /// nordic design is CODE). White on transparent so emitters tint by startColor.
        /// Use with a TextureSheetAnimation module picking one random tile per particle.
        /// </summary>
        public static Material BuildRuneMaterial(string owner)
        {
            Shader shader = FindShader(owner);

            const int tile = 64;
            const int size = tile * 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, clear);

            // Strokes in per-tile 0..1 coordinates, y up. Straight segments only — the
            // Elder Futhark's staves are all a knife can cut.
            // fehu (bottom-left tile): stave + two rising twigs.
            DrawStroke(tex, 0, 0, tile, 0.38f, 0.10f, 0.38f, 0.90f);
            DrawStroke(tex, 0, 0, tile, 0.38f, 0.86f, 0.72f, 0.98f);
            DrawStroke(tex, 0, 0, tile, 0.38f, 0.62f, 0.72f, 0.74f);
            // algiz (bottom-right tile): stave + two raised arms.
            DrawStroke(tex, 1, 0, tile, 0.50f, 0.10f, 0.50f, 0.90f);
            DrawStroke(tex, 1, 0, tile, 0.50f, 0.62f, 0.24f, 0.92f);
            DrawStroke(tex, 1, 0, tile, 0.50f, 0.62f, 0.76f, 0.92f);
            // gebo (top-left tile): the crossed gift.
            DrawStroke(tex, 0, 1, tile, 0.18f, 0.14f, 0.82f, 0.86f);
            DrawStroke(tex, 0, 1, tile, 0.82f, 0.14f, 0.18f, 0.86f);
            // thurisaz (top-right tile): stave + the thorn.
            DrawStroke(tex, 1, 1, tile, 0.36f, 0.08f, 0.36f, 0.92f);
            DrawStroke(tex, 1, 1, tile, 0.36f, 0.70f, 0.72f, 0.50f);
            DrawStroke(tex, 1, 1, tile, 0.72f, 0.50f, 0.36f, 0.30f);

            tex.Apply();
            return new Material(shader) { mainTexture = tex };
        }

        /// <summary>Rasterize one thick stroke into a tile: per-pixel distance to the
        /// segment, soft-edged over the last third of the width.</summary>
        private static void DrawStroke(Texture2D tex, int tileX, int tileY, int tile,
                                       float x0, float y0, float x1, float y1)
        {
            const float halfWidth = 0.055f;   // stroke half-width in tile units

            var a = new Vector2(x0, y0);
            var b = new Vector2(x1, y1);
            Vector2 ab = b - a;
            float abLen2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);

            for (int py = 0; py < tile; py++)
            for (int px = 0; px < tile; px++)
            {
                var p = new Vector2((px + 0.5f) / tile, (py + 0.5f) / tile);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLen2);
                float d = Vector2.Distance(p, a + ab * t);
                if (d >= halfWidth) continue;

                float alpha = Mathf.Clamp01((halfWidth - d) / (halfWidth * 0.35f));
                int x = tileX * tile + px;
                int y = tileY * tile + py;
                Color existing = tex.GetPixel(x, y);
                if (alpha > existing.a)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
    }
}
