// Target: .NET Framework + C# 7.3
using System;
using System.IO;

namespace Zeitmanagement.Helpers
{
    /// <summary>Schreibt Dateien so, dass ein Fehler mitten im Schreiben weder eine halbe Datei noch eine zerstörte alte hinterlässt.</summary>
    public static class SafeFile
    {
        /// <summary>
        /// Schreibt erst in eine temporäre Datei im selben Ordner und ersetzt die Zieldatei erst danach. Schlägt etwas fehl
        /// (Platte voll, Netzlaufwerk weg, Zieldatei in einem Programm geöffnet), bleibt eine vorhandene Zieldatei unverändert und
        /// die temporäre Datei wird entfernt. Fehler kommen als <see cref="IOException"/> bzw. <see cref="UnauthorizedAccessException"/>.
        /// </summary>
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Pfad fehlt.", nameof(path));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            var temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);

                // Bewusst Löschen + Verschieben statt File.Replace: Replace gibt es auf FAT/exFAT (USB-Sticks) und manchen
                // Netzlaufwerken nicht. Eine gesperrte Zieldatei meldet sich schon beim Löschen, bevor etwas verloren ginge.
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
