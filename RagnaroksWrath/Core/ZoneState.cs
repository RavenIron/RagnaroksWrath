using System;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The drifting state of a single zone.
    ///
    /// SPARSE BY CONSTRUCTION. A world has tens of thousands of zones and almost all of them
    /// are untouched. Only zones whose values have actually moved off default are stored, which
    /// is what keeps the save file proportional to what players have *done* rather than to the
    /// size of the map.
    ///
    /// All values are 0..1 normalised. Zero is "nothing has happened here" for every field, so
    /// a default-constructed ZoneState is the pristine state and <see cref="IsDefault"/> is the
    /// test for "does this need saving at all".
    /// </summary>
    [Serializable]
    public struct ZoneState
    {
        /// <summary>Soil quality. Drives FarmingSystem yield. Depleted by farming, restored by rest.</summary>
        public float Fertility;

        /// <summary>Corruption/blight level. Feeds PlagueSystem and EcologySystem.</summary>
        public float Corruption;

        /// <summary>Burn damage. Raised by FireSystem, decays slowly. Suppresses fertility and growth.</summary>
        public float Scorch;

        /// <summary>Accumulated cold. Raised in winter and by weather, drives HealthSystem effects.</summary>
        public float Frost;

        /// <summary>Active plague intensity. Separate from Corruption: plague spreads and can be cured.</summary>
        public float Plague;

        /// <summary>
        /// True when nothing has happened here. Such zones are never written to disk — this is
        /// the whole sparseness guarantee, in one method.
        /// </summary>
        public bool IsDefault =>
            Fertility == 0f && Corruption == 0f && Scorch == 0f && Frost == 0f && Plague == 0f;

        /// <summary>
        /// Clamp every field to 0..1.
        ///
        /// Called on READ from disk as well as on write. Config files and save files get
        /// hand-edited, and a value validated only on write has already taken a production
        /// service down for six hours in this codebase's history.
        /// </summary>
        public void Clamp()
        {
            Fertility  = Clamp01(Fertility);
            Corruption = Clamp01(Corruption);
            Scorch     = Clamp01(Scorch);
            Frost      = Clamp01(Frost);
            Plague     = Clamp01(Plague);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;      // NaN propagates through every later multiply
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        public override string ToString() =>
            $"fert={Fertility:F2} corr={Corruption:F2} scorch={Scorch:F2} frost={Frost:F2} plague={Plague:F2}";
    }
}
