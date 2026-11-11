using System;

namespace TimeTrack.Plugins
{
    /// <summary>
    /// Basisklasse jedes TimeTrack-Plugins. TimeTrack sucht im Plugin-Ordner nach nicht abstrakten, öffentlichen
    /// Ableitungen mit parameterlosem Konstruktor, erzeugt je Plugin genau eine Instanz und ruft
    /// <see cref="Initialize"/> beim Start sowie <see cref="Shutdown"/> beim Beenden auf.
    /// </summary>
    /// <remarks>
    /// Eine Basisklasse statt eines Interfaces, damit spätere Erweiterungen (neue virtuelle Mitglieder mit
    /// Standardverhalten) bestehende Plugins nicht brechen. Der Konstruktor soll nichts tun; alle Arbeit gehört in
    /// <see cref="Initialize"/>, dort sind Fehler einem Plugin zugeordnet und werden in den Einstellungen angezeigt.
    /// </remarks>
    public abstract class TimeTrackPlugin
    {
        /// <summary>
        /// Stabile, eindeutige Kennung, z. B. <c>"map"</c>. Unter ihr legt TimeTrack die Daten des Plugins ab; sie darf
        /// sich also nie ändern. Erlaubt sind Kleinbuchstaben, Ziffern, Punkt, Binde- und Unterstrich.
        /// </summary>
        public abstract string Id { get; }

        /// <summary>Anzeigename in Navigation und Einstellungen.</summary>
        public abstract string Name { get; }

        /// <summary>Kurze Beschreibung für die Einstellungen.</summary>
        public virtual string Description => string.Empty;

        /// <summary>Version des Plugins. Standard: die Assembly-Version.</summary>
        public virtual Version Version => GetType().Assembly.GetName().Version;

        /// <summary>
        /// Kleinste <see cref="IPluginHost.HostApiVersion"/>, mit der das Plugin funktioniert. Ist TimeTrack älter,
        /// wird das Plugin nicht geladen und die Einstellungen nennen den Grund.
        /// </summary>
        public virtual int MinimumHostApiVersion => 1;

        /// <summary>
        /// Wird einmal beim Start auf dem UI-Thread aufgerufen. Hier registriert das Plugin Seiten und
        /// Einstellungsbereiche. Eine Ausnahme deaktiviert nur dieses Plugin, TimeTrack startet trotzdem.
        /// </summary>
        public abstract void Initialize(IPluginHost host);

        /// <summary>Wird beim Beenden von TimeTrack auf dem UI-Thread aufgerufen. Ausnahmen werden ignoriert.</summary>
        public virtual void Shutdown()
        {
        }
    }
}
