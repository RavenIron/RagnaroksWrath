using System;
using System.Globalization;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The pure half of the `wrath` console: field-name parsing and value application for
    /// zone edits. Pure so the harness pins it — a console command that silently writes
    /// the wrong field is the store-edit dance's worst failure mode, automated.
    /// </summary>
    public static class WrathAdmin
    {
        public const string ZoneFields = "fert, corr, scorch, frost, plague";

        /// <summary>Apply one named field to a zone state. Values clamp to 0..1 by the
        /// same rule the store enforces on read; unknown fields and non-finite values
        /// refuse rather than guess.</summary>
        public static bool TrySetZoneField(ZoneState state, string field, float value,
                                           out ZoneState result)
        {
            result = state;
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            if (value < 0f) value = 0f;
            if (value > 1f) value = 1f;

            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "fert":
                case "fertility":  result.Fertility  = value; return true;
                case "corr":
                case "corruption": result.Corruption = value; return true;
                case "scorch":     result.Scorch     = value; return true;
                case "frost":      result.Frost      = value; return true;
                case "plague":     result.Plague     = value; return true;
                default: return false;
            }
        }

        /// <summary>Invariant-culture float parse — a console used on a comma-decimal
        /// locale must read "0.5" the way the store writes it.</summary>
        public static bool TryParseValue(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool TryParseInt(string text, out int value)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        public static bool TryParseLong(string text, out long value)
            => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
