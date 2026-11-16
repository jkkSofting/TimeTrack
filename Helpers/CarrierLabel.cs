// Target: .NET Framework + C# 7.3
using System.Text.RegularExpressions;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Kostenträger heißen oft "Bezeichnung (KTR-ID)". Für das Buchen im SAP wird nur die Bezeichnung
    /// gebraucht; die ID steht dort in einem eigenen Feld.
    /// </summary>
    internal static class CarrierLabel
    {
        // Letzte Klammer am Ende, deren Inhalt wie eine ID aussieht: ohne Leerzeichen, mit mindestens
        // einer Ziffer. So bleibt "Multiflasher VIII (?)" oder "Server (alt)" unangetastet.
        private static readonly Regex TrailingId = new Regex(
            @"^(?<name>.*?)\s*\(\s*(?=[^\s()]*\d)(?<id>[A-Za-z0-9][A-Za-z0-9._\-/]*)\s*\)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Entfernt eine angehängte "(KTR-ID)"; ohne ID oder bei leerer Bezeichnung bleibt der Text, wie er ist.</summary>
        public static string StripId(string carrier)
        {
            if (string.IsNullOrWhiteSpace(carrier)) return carrier ?? string.Empty;

            var text = carrier.Trim();
            var m = TrailingId.Match(text);
            if (!m.Success) return text;

            var name = m.Groups["name"].Value.Trim();
            return name.Length == 0 ? text : name;
        }

        /// <summary>Die angehängte KTR-ID ohne Klammern, oder ein leerer Text, wenn es keine gibt.</summary>
        public static string ExtractId(string carrier)
        {
            if (string.IsNullOrWhiteSpace(carrier)) return string.Empty;

            var m = TrailingId.Match(carrier.Trim());
            if (!m.Success || m.Groups["name"].Value.Trim().Length == 0) return string.Empty;
            return m.Groups["id"].Value;
        }
    }
}
