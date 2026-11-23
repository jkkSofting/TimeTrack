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

        /// <summary>Globale Standardrundung, solange in den Einstellungen nichts anderes gewählt wurde.</summary>
        public const int DefaultGlobalMinutes = 15;

        /// <summary>
        /// Effektive Rundung eines Projekts: die globale Standardrundung, wenn das Projekt sie erbt,
        /// sonst der eigene Wert (0 = für dieses Projekt aus). Das Ergebnis ist immer ein erlaubter Wert.
        /// </summary>
        public static int Resolve(bool useGlobal, int ownMinutes, int globalMinutes)
        {
            return useGlobal ? Normalize(globalMinutes) : Normalize(ownMinutes);
        }

        /// <summary>Anzeigetext für die Auswahl am Projekt, z. B. "Standard (15 Minuten)", "Standard (Aus)", "Aus", "10 Minuten".</summary>
        public static string LabelFor(bool useGlobal, int ownMinutes, int globalMinutes)
        {
            return useGlobal ? "Standard (" + Label(globalMinutes) + ")" : Label(ownMinutes);
        }

        /// <summary>Rundet eine Dauer auf das nächste Vielfache der Einheit auf (Einheit 0 = unverändert).</summary>
        public static int RoundUp(int lengthMin, int roundingMinutes)
        {
            roundingMinutes = Normalize(roundingMinutes);
            if (roundingMinutes <= 0 || lengthMin <= 0) return lengthMin;
            int rem = lengthMin % roundingMinutes;
            return rem == 0 ? lengthMin : lengthMin + (roundingMinutes - rem);
        }

        /// <summary>Minuten, um die eine Dauer durch die Aufrundung länger wird (0, wenn nichts aufgerundet wird).</summary>
        public static int AddedMinutes(int lengthMin, int roundingMinutes)
        {
            return RoundUp(lengthMin, roundingMinutes) - lengthMin;
        }
    }
}
