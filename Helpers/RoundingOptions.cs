using System;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Erlaubte Rundungseinheiten je Projekt für die SAP-Verrechnung ("pro angefangene Zeiteinheit").
    /// 0 = Aus, sonst wird jede Buchung auf die nächste volle Einheit aufgerundet.
    /// </summary>
    internal static class RoundingOptions
    {
        /// <summary>Erlaubte Werte in Minuten (0 = Aus).</summary>
        public static readonly int[] Allowed = { 0, 5, 10, 15, 60 };

        /// <summary>Liefert den Wert, wenn erlaubt, sonst 0 (Aus).</summary>
        public static int Normalize(int minutes)
        {
            return Array.IndexOf(Allowed, minutes) >= 0 ? minutes : 0;
        }

        /// <summary>Anzeigetext, z. B. "Aus", "15 Minuten", "60 Minuten".</summary>
        public static string Label(int minutes)
        {
            minutes = Normalize(minutes);
            return minutes == 0 ? "Aus" : minutes + " Minuten";
        }

        /// <summary>Rundet eine Dauer auf das nächste Vielfache der Einheit auf (Einheit 0 = unverändert).</summary>
        public static int RoundUp(int lengthMin, int roundingMinutes)
        {
            roundingMinutes = Normalize(roundingMinutes);
            if (roundingMinutes <= 0 || lengthMin <= 0) return lengthMin;
            int rem = lengthMin % roundingMinutes;
            return rem == 0 ? lengthMin : lengthMin + (roundingMinutes - rem);
        }
    }
}
