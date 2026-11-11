namespace TimeTrack.Plugins
{
    /// <summary>Alles, was TimeTrack einem Plugin zur Verfügung stellt. Eine Instanz je Plugin.</summary>
    public interface IPluginHost
    {
        /// <summary>
        /// Version dieser Schnittstelle. Steigt, wenn neue Mitglieder hinzukommen; bestehende bleiben unverändert.
        /// Version 1: Daten lesen, Ablage, Seiten, Einstellungsbereiche, Hinweise.
        /// </summary>
        int HostApiVersion { get; }

        /// <summary>Version von TimeTrack, z. B. <c>"2026.47.0"</c>.</summary>
        string HostVersion { get; }

        /// <summary>Ordner, aus dem das Plugin geladen wurde (für mitgelieferte Dateien).</summary>
        string PluginDirectory { get; }

        /// <summary>Lesender Zugriff auf Projekte und Buchungen.</summary>
        ITimeTrackData Data { get; }

        /// <summary>Ablage des Plugins in der TimeTrack-Datenbank, inklusive verschlüsselter Geheimnisse.</summary>
        IPluginStorage Storage { get; }

        /// <summary>Seiten, Einstellungsbereiche und Hinweise.</summary>
        IPluginUi Ui { get; }
    }
}
