using System;
using System.Windows;

namespace TimeTrack.Plugins
{
    /// <summary>
    /// Oberfläche von TimeTrack aus Sicht eines Plugins. Nur vom UI-Thread aufrufen.
    /// </summary>
    /// <remarks>
    /// Ansichten eines Plugins erscheinen im Design von TimeTrack, wenn sie die gemeinsamen Ressourcen benutzen:
    /// Farben über <c>{DynamicResource Theme.Text}</c>, <c>Theme.Card</c>, <c>Theme.Accent</c> usw., Stile über
    /// <c>{StaticResource Card}</c>, <c>Page</c>, <c>PageHeader</c>, <c>Text.PageTitle</c>, <c>Text.Subtitle</c>,
    /// <c>Text.Section</c>, <c>Text.Body</c>, <c>Text.Muted</c>, <c>Button.Primary</c>, <c>Button.Ghost</c>,
    /// <c>Switch</c>. Eigene Ressourcen gehören in die Ressourcen der Ansicht, nie in <c>Application.Resources</c>.
    /// </remarks>
    public interface IPluginUi
    {
        /// <summary>
        /// Fügt der Navigation eine Seite hinzu (Gruppe „Plugins“). Nur während <see cref="TimeTrackPlugin.Initialize"/>.
        /// </summary>
        void RegisterPage(PluginPage page);

        /// <summary>
        /// Fügt den Einstellungen unter dem Eintrag des Plugins einen Bereich hinzu. Nur während
        /// <see cref="TimeTrackPlugin.Initialize"/>. Der Bereich speichert selbst; „Speichern“ der Einstellungsseite
        /// betrifft ihn nicht.
        /// </summary>
        void RegisterSettingsSection(PluginSettingsSection section);

        /// <summary>Zeigt eine Seite dieses Plugins (Schlüssel wie bei <see cref="PluginPage.Key"/>).</summary>
        void Navigate(string pageKey);

        /// <summary>Öffnet die Einstellungsseite von TimeTrack.</summary>
        void OpenSettings();

        /// <summary>Zeigt einen Hinweis am Tray-Symbol. Ein Klick darauf ruft <paramref name="onClick"/> auf.</summary>
        void ShowNotification(string title, string message, Action onClick = null);
    }

    /// <summary>Eine Seite eines Plugins in der Navigation.</summary>
    public sealed class PluginPage
    {
        /// <param name="key">Innerhalb des Plugins eindeutiger Schlüssel, z. B. <c>"sync"</c>.</param>
        /// <param name="title">Text in der Navigation.</param>
        /// <param name="createView">
        /// Erzeugt die Ansicht beim ersten Öffnen. TimeTrack behält sie danach und zeigt beim nächsten Öffnen dieselbe
        /// Instanz; das Plugin setzt ihren <c>DataContext</c> selbst.
        /// </param>
        public PluginPage(string key, string title, Func<FrameworkElement> createView)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Schlüssel fehlt.", nameof(key));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Titel fehlt.", nameof(title));
            Key = key;
            Title = title;
            CreateView = createView ?? throw new ArgumentNullException(nameof(createView));
        }

        public string Key { get; }

        public string Title { get; }

        /// <summary>Icon-Glyph aus „Segoe Fluent Icons“ / „Segoe MDL2 Assets“, z. B. <c>""</c>. Optional.</summary>
        public string Glyph { get; set; }

        public Func<FrameworkElement> CreateView { get; }

        /// <summary>Wird bei jedem Öffnen der Seite aufgerufen (wie das Neuladen der eingebauten Seiten). Optional.</summary>
        public Action Refresh { get; set; }
    }

    /// <summary>Ein Bereich eines Plugins auf der Einstellungsseite.</summary>
    public sealed class PluginSettingsSection
    {
        /// <param name="title">Überschrift des Bereichs.</param>
        /// <param name="createView">Erzeugt die Ansicht beim ersten Anzeigen; danach wird dieselbe Instanz wiederverwendet.</param>
        public PluginSettingsSection(string title, Func<FrameworkElement> createView)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Titel fehlt.", nameof(title));
            Title = title;
            CreateView = createView ?? throw new ArgumentNullException(nameof(createView));
        }

        public string Title { get; }

        public Func<FrameworkElement> CreateView { get; }
    }
}
