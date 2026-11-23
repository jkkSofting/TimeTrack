using Zeitmanagement.Helpers;

namespace Zeitmanagement.ViewModel
{
    /// <summary>Eintrag für die Rundungs-Auswahl (Minuten + Anzeigetext).</summary>
    internal sealed class RoundingChoice
    {
        public RoundingChoice(int minutes)
        {
            Minutes = minutes;
            Label = RoundingOptions.Label(minutes);
        }

        public int Minutes { get; }
        public string Label { get; }

        /// <summary>Fallback für Stellen, die nur den Text anzeigen (z. B. das Auswahlfeld einer ComboBox ohne ItemTemplate).</summary>
        public override string ToString() => Label;
    }
}
