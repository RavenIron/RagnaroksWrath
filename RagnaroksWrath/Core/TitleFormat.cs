namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// How a title renders under a nameplate. Pure so the harness can pin it: the nameplate
    /// field is TextMeshPro, and a malformed rich-text tag doesn't error — it prints itself,
    /// putting literal angle brackets under every titled player's name.
    /// </summary>
    public static class TitleFormat
    {
        /// <summary>
        /// The string appended to a player's hover name. Newline first, so the title sits on
        /// its own line under the name; smaller and muted so the NAME stays the loudest thing
        /// on the plate.
        /// </summary>
        public static string Suffix(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";

            return "\n<size=70%><color=#c9b458>" + title.Trim() + "</color></size>";
        }
    }
}
