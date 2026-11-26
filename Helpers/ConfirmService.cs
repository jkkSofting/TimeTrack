using System;

namespace Zeitmanagement.Helpers
{
    /// <summary>Rückfrage vor einer Löschaktion. Bewusst ohne WPF-Typen, damit das Testprojekt es linken kann.</summary>
    internal interface IConfirmService
    {
        /// <summary>true = Ja. Der Standardknopf ist immer „Nein“.</summary>
        bool Confirm(string text, string title);
    }

    /// <summary>Texte für die Bestätigungsdialoge (rein, testbar).</summary>
    internal static class ConfirmTexts
    {
        public const string MoveProjectTitle = "Projekt in den Papierkorb";
        public const string MoveEntryTitle = "Buchung in den Papierkorb";
        public const string PurgeTitle = "Endgültig löschen";
        public const string EmptyTrashTitle = "Papierkorb leeren";

        public static string MoveProjectText(string name, int bookingCount)
        {
            string bookings = bookingCount == 0
                ? "Das Projekt hat keine Buchungen."
                : bookingCount == 1
                    ? "Die 1 Buchung des Projekts verschwindet mit ihm aus allen Ansichten."
                    : $"Die {bookingCount} Buchungen des Projekts verschwinden mit ihm aus allen Ansichten.";
            return $"Projekt „{name}“ in den Papierkorb verschieben?\n\n{bookings} Du kannst alles im Papierkorb wiederherstellen.";
        }

        public static string MoveEntryText(string start, string end, string projekt) =>
            $"Buchung {start}–{end} ({projekt}) in den Papierkorb verschieben?";

        public static string PurgeProjectText(string name, int bookingCount)
        {
            string bookings = bookingCount == 1 ? "1 Buchung" : $"{bookingCount} Buchungen";
            return $"Projekt „{name}“ mit {bookings} ENDGÜLTIG löschen?\n\nDas kann nicht rückgängig gemacht werden.";
        }

        public static string PurgeEntryText(string start, string end, string projekt) =>
            $"Buchung {start}–{end} ({projekt}) ENDGÜLTIG löschen?\n\nDas kann nicht rückgängig gemacht werden.";

        public static string EmptyTrashText(int projects, int entries) =>
            $"Papierkorb leeren? {projects} Projekt(e) und {entries} einzelne Buchung(en) werden ENDGÜLTIG gelöscht.\n\nDas kann nicht rückgängig gemacht werden.";
    }
}
