using System.Collections.Generic;

namespace TimeTrack.Plugins
{
    /// <summary>
    /// Schlüssel-Wert-Ablage eines Plugins in der TimeTrack-Datenbank. Sie ist dadurch in jeder Datensicherung
    /// enthalten. Jedes Plugin sieht nur seine eigenen Einträge.
    /// </summary>
    /// <remarks>
    /// Werte sind Texte, in der Regel JSON. Sammlung und Schlüssel sind frei wählbar (nicht leer, höchstens 200 Zeichen);
    /// Sammlungen, die mit <c>$</c> beginnen, sind TimeTrack vorbehalten. Darf von jedem Thread aufgerufen werden, siehe
    /// <see cref="ITimeTrackData"/>.
    /// </remarks>
    public interface IPluginStorage
    {
        /// <summary>Der gespeicherte Wert oder <c>null</c>.</summary>
        string Get(string collection, string key);

        /// <summary>Legt den Wert an oder überschreibt ihn. <c>null</c> löscht den Eintrag.</summary>
        void Set(string collection, string key, string value);

        /// <summary>Löscht den Eintrag. <c>true</c>, wenn es ihn gab.</summary>
        bool Delete(string collection, string key);

        /// <summary>Alle Einträge einer Sammlung, Schlüssel → Wert.</summary>
        IReadOnlyDictionary<string, string> List(string collection);

        /// <summary>
        /// Speichert ein Geheimnis (z. B. ein Zugriffstoken) mit Windows-DPAPI für den angemeldeten Benutzer verschlüsselt.
        /// Nach einer Wiederherstellung auf einem anderen Rechner oder unter einem anderen Benutzer ist es nicht mehr lesbar
        /// und muss neu eingegeben werden. <c>null</c> oder leer löscht es.
        /// </summary>
        void SetSecret(string key, string value);

        /// <summary>Das entschlüsselte Geheimnis, oder <c>null</c>, wenn keins gespeichert ist oder es nicht lesbar ist.</summary>
        string GetSecret(string key);
    }
}
