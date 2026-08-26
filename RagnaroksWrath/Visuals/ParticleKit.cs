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
            Shader shader = null;
            foreach (string name in CandidateShaders)
            {
                shader = Shader.Find(name);
                if (shader != null)
                {
                    RagnaroksWrath.Log.LogInfo($"{owner}: using shader '{name}'.");
                    break;
                }
            }
            if (shader == null) throw new InvalidOperationException("no particle shader available");

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
    }
}
