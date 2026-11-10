// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Zuordnung zwischen dem Kostenträger eines lokalen Projekts und dem PSP-Element der SAP-Datei.
    ///
    /// Der lokale Kostenträger muss die PSP-Element-ID <b>enthalten</b>, muss ihr aber nicht gleichen
    /// (z. B. "1500410A24RD001.01 Entwicklung" enthält "1500410A24RD001.01"). Damit die PSP-Hierarchie
    /// nicht verschwimmt, zählt die ID nur als ganzes Token: Gefolgt von ".01" ist sie nur der Anfang
    /// einer längeren ID ("…001" steckt also nicht in "…001.01"), und eine ID mitten in einer längeren
    /// Zeichenfolge zählt nicht. Es gibt keine Unschärfe, keine Teilzeichenfolgen ohne Tokengrenze
    /// und keine Abkürzung auf eine feste Länge.
    /// </summary>
    public static class CarrierKeys
    {
        /// <summary>
        /// Vergleichsform: Leerraum (auch geschütztes Leerzeichen) an den Rändern entfernt,
        /// Unicode-Normalform C, Großschreibung unabhängig von der Kultur. Innere Zeichen bleiben unverändert.
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var trimmed = text.Trim();
            if (trimmed.Length == 0) return string.Empty;
            return trimmed.Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }

        public static string NormalizeOrNull(string text)
        {
            var n = Normalize(text);
            return n.Length == 0 ? null : n;
        }

        /// <summary>
        /// Prüft, ob <paramref name="normalizedText"/> die ID <paramref name="normalizedKey"/> als eigenständiges Token enthält.
        /// Beide Werte müssen bereits mit <see cref="Normalize"/> normalisiert sein.
        /// </summary>
        public static bool TextContainsKey(string normalizedText, string normalizedKey)
        {
            if (string.IsNullOrEmpty(normalizedText) || string.IsNullOrEmpty(normalizedKey)) return false;
            if (normalizedKey.Length > normalizedText.Length) return false;

            var from = 0;
            while (from <= normalizedText.Length - normalizedKey.Length)
            {
                var index = normalizedText.IndexOf(normalizedKey, from, StringComparison.Ordinal);
                if (index < 0) return false;

                if (StartsAtTokenBoundary(normalizedText, index) &&
                    EndsAtTokenBoundary(normalizedText, index + normalizedKey.Length))
                {
                    return true;
                }

                from = index + 1;
            }

            return false;
        }

        private static bool IsIdChar(char c)
        {
            return char.IsLetterOrDigit(c);
        }

        // Davor darf kein Zeichen stehen, das die ID nach vorn verlängern würde ("X1500…", "ABC.1500…").
        private static bool StartsAtTokenBoundary(string text, int index)
        {
            if (index == 0) return true;
            if (IsIdChar(text[index - 1])) return false;
            if (text[index - 1] == '.' && index >= 2 && IsIdChar(text[index - 2])) return false;
            return true;
        }

        // Danach darf nichts folgen, das die ID verlängert: weder Buchstabe/Ziffer noch ".<Buchstabe/Ziffer>"
        // (das wäre ein Kind-PSP-Element). Ein Punkt am Satzende oder vor Leerraum ist unschädlich.
        private static bool EndsAtTokenBoundary(string text, int endIndex)
        {
            if (endIndex >= text.Length) return true;
            if (IsIdChar(text[endIndex])) return false;
            if (text[endIndex] == '.' && endIndex + 1 < text.Length && IsIdChar(text[endIndex + 1])) return false;
            return true;
        }
    }

    /// <summary>
    /// Die Kostenträger aller lokalen Projekte (unabhängig von Intern/Extern, Quick Switch, Suche oder Buchungen).
    /// Nur PSP-Elemente, die von mindestens einem davon enthalten werden, dürfen importiert werden.
    /// </summary>
    public sealed class CarrierAllowlist
    {
        private readonly Dictionary<string, List<string>> _spellings = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly List<string> _carriers;

        public CarrierAllowlist(IEnumerable<string> localCarriers)
        {
            foreach (var raw in localCarriers ?? Enumerable.Empty<string>())
            {
                var key = CarrierKeys.Normalize(raw);
                if (key.Length == 0) continue;

                List<string> spellings;
                if (!_spellings.TryGetValue(key, out spellings))
                {
                    spellings = new List<string>();
                    _spellings[key] = spellings;
                }

                var text = raw.Trim();
                if (!spellings.Contains(text, StringComparer.Ordinal)) spellings.Add(text);
            }

            _carriers = _spellings.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Hash = Hashing.Sha256Hex(string.Join("\n", _carriers));
        }

        /// <summary>Normalisierte, eindeutige lokale Kostenträger, sortiert.</summary>
        public IReadOnlyList<string> NormalizedCarriers => _carriers;

        /// <summary>Deterministischer Hash der normalisierten Kostenträger (für die Import-Identität).</summary>
        public string Hash { get; }

        /// <summary>Ob mindestens ein lokaler Kostenträger die (normalisierte) PSP-Element-ID enthält.</summary>
        public bool IsEligible(string normalizedPspKey)
        {
            if (string.IsNullOrEmpty(normalizedPspKey)) return false;
            for (var i = 0; i < _carriers.Count; i++)
            {
                if (CarrierKeys.TextContainsKey(_carriers[i], normalizedPspKey)) return true;
            }
            return false;
        }

        /// <summary>Die Schreibweisen, in denen ein normalisierter Kostenträger lokal vorkommt.</summary>
        public IReadOnlyList<string> SpellingsOf(string normalizedCarrier)
        {
            List<string> spellings;
            return _spellings.TryGetValue(normalizedCarrier, out spellings) ? spellings : new List<string>();
        }

        /// <summary>Kostenträger, die sich erst durch die Normalisierung gleichen, aber verschieden geschrieben sind.</summary>
        public IEnumerable<KeyValuePair<string, List<string>>> AmbiguousSpellings()
        {
            return _spellings.Where(kv => kv.Value.Count > 1);
        }
    }

    public static class CarrierLinking
    {
        /// <summary>
        /// Lokale Projekte, deren Kostenträger das (normalisierte) PSP-Element enthält. Ein PSP-Element kann zu
        /// mehreren Projekten gehören; jedes Projekt erscheint höchstens einmal.
        /// </summary>
        public static List<LocalProjectInfo> LinkedProjects(IEnumerable<LocalProjectInfo> projects, string normalizedPspKey)
        {
            var result = new List<LocalProjectInfo>();
            var seen = new HashSet<int>();
            foreach (var project in projects ?? Enumerable.Empty<LocalProjectInfo>())
            {
                if (!seen.Add(project.Id)) continue;
                if (CarrierKeys.TextContainsKey(CarrierKeys.Normalize(project.Carrier), normalizedPspKey))
                    result.Add(project);
            }
            return result;
        }
    }

    internal static class Hashing
    {
        public static string Sha256Hex(string text)
        {
            return Sha256Hex(Encoding.UTF8.GetBytes(text ?? string.Empty));
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
