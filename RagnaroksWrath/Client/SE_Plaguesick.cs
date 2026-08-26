using System;
using UnityEngine;

namespace RavenIron.RagnaroksWrath.Client
{
    /// <summary>
    /// The sickness icon, with something written under it.
    ///
    /// Vanilla only writes text under an icon when the effect has a `m_ttl` to count down,
    /// and ours deliberately has none — the sickness ends when the WORLD says so, not when a
    /// timer expires. So this subclass overrides `GetIconText` instead (virtual, and `Hud`
    /// calls it unconditionally then shows the TimeText object for any non-empty string —
    /// both decompile-verified, because "the icon has no ttl so it can have no text" is
    /// exactly the plausible-looking assumption this codebase has been bitten by before).
    ///
    /// What it says depends on which way the sickness is going, because a countdown while
    /// you are still breathing it in would be a lie:
    ///
    ///  - GETTING WORSE (standing on plagued ground): the severity, as a percentage.
    ///  - RECOVERING (off it): a real countdown to clean, formatted by vanilla's own
    ///    `GetTimeString` so it reads identically to every other timed effect in the game.
    ///
    /// The countdown is an ESTIMATE computed from this client's config. The server owns the
    /// real arithmetic, and a client whose config disagrees will show a number that drifts
    /// from the truth — acceptable for a cosmetic readout, and the reason nothing in this
    /// file feeds back into gameplay.
    /// </summary>
    public class SE_Plaguesick : SE_Stats
    {
        /// <summary>Current exposure, 0..1. Rendered as a percentage while worsening.</summary>
        public float Severity;

        /// <summary>Estimated seconds until clean, or 0 while exposure is still climbing.</summary>
        public float SecondsToClean;

        public override string GetIconText()
        {
            // Called every frame by the HUD: pure formatting off cached fields, and it must
            // never throw — a status bar that dies takes the whole HUD's icon row with it.
            try
            {
                if (SecondsToClean > 0f) return GetTimeString(SecondsToClean);

                int percent = Mathf.Clamp(Mathf.RoundToInt(Severity * 100f), 1, 100);
                return percent + "%";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
