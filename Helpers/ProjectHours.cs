using System;
using System.Collections.Generic;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Stundenberechnung für die Projektseite. Reine Funktionen ohne Datenbank, damit sie testbar sind.
    /// </summary>
    internal static class ProjectHours
    {
        /// <summary>Dauer einer Buchung in ganzen Minuten (kaufmännisch gerundet).</summary>
        public static int ToMinutes(TimeSpan duration)
        {
            return (int)Math.Round(duration.TotalMinutes);
        }

        /// <summary>
        /// Summe der Stunden, wenn jede Buchung einzeln auf die Rundung aufgerundet wird (wie in der SAP-Verrechnung).
        /// Rundung 0 = Aus, dann entspricht das Ergebnis der reinen Summe.
        /// </summary>
        public static double RoundedHours(IEnumerable<int> entryMinutes, int roundingMinutes)
        {
            int total = 0;
            if (entryMinutes == null)
                return 0;

            foreach (var minutes in entryMinutes)
                total += RoundingOptions.RoundUp(minutes, roundingMinutes);

            return total / 60.0;
        }
    }
}
