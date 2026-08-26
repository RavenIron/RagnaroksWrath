namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// Phase E, the nemesis: pure rules for marking the creature that kills a player.
    /// The mark lives in the creature's OWN ZDO as custom keys, because ZDOIDs regenerate
    /// at world load and only the ZDO itself travels with the save — see
    /// docs/reference/CREATURE-PERSISTENCE-AND-NEMESIS-FACTS.md. Pure so the harness can
    /// pin both the level arithmetic and the rich-text suffix (a malformed TMP tag does
    /// not error — it prints itself on every marked creature's plate).
    /// </summary>
    public static class NemesisMark
    {
        // ZDO custom key NAMES; hashed with GetStableHashCode at the patch layer, where
        // the game's string extensions exist.
        public const string KeyVictim = "rw_nemesis";        // long — victim playerID
        public const string KeyKills  = "rw_nemesis_kills";  // int  — player kills by this creature
        public const string KeyName   = "rw_nemesis_name";   // string — latest victim's display name

        /// <summary>
        /// Level after one more player kill: one step up, capped. A cap configured below
        /// the level the creature already holds never demotes it — SetMaxHealth clamps
        /// current health when a level drops, and a mark must never weaken its bearer.
        /// </summary>
        public static int NextLevel(int currentLevel, int maxLevel)
        {
            if (currentLevel < 1) currentLevel = 1;
            if (maxLevel < 1) maxLevel = 1;

            int next = currentLevel + 1;
            if (next > maxLevel) next = maxLevel;
            return next < currentLevel ? currentLevel : next;
        }

        /// <summary>
        /// The line under a marked creature's name: muted, blood-tinted, quieter than the
        /// name itself (TitleFormat's shape). Empty when the mark is incomplete — a
        /// decoration must never invent the half of the story the data cannot back.
        /// </summary>
        public static string Suffix(string victimName, int kills)
        {
            if (string.IsNullOrWhiteSpace(victimName) || kills <= 0) return "";

            string story = kills == 1
                ? "slayer of " + victimName.Trim()
                : "slayer of " + victimName.Trim() + " x" + kills;

            return "\n<size=70%><color=#b45050>" + story + "</color></size>";
        }
    }
}
