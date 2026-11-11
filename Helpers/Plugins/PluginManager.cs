// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using TimeTrack.Plugins;

namespace Zeitmanagement.Helpers.Plugins
{
    /// <summary>Eine Plugin-Seite, wie sie in der Navigation erscheint.</summary>
    internal sealed class PluginPageEntry
    {
        public PluginPageEntry(PluginInfo plugin, PluginPage page)
        {
            Plugin = plugin;
            Page = page;
            NavigationKey = PluginManager.NavigationKey(plugin.Id, page.Key);
        }

        public PluginInfo Plugin { get; }
        public PluginPage Page { get; }

        /// <summary>Schlüssel für <c>MainViewModel.SelectViewCommand</c>: <c>plugin:&lt;id&gt;:&lt;seite&gt;</c>, klein geschrieben.</summary>
        public string NavigationKey { get; }
    }

    /// <summary>
    /// Verwaltet die Plugins einer Sitzung: welche aktiviert sind (in <c>plugin_records</c> unter
    /// <see cref="PluginStorage.HostId"/>), Laden beim Start, ihre Seiten und Einstellungsbereiche, Beenden.
    /// </summary>
    internal sealed class PluginManager
    {
        private const string EnabledCollection = "enabled";

        private readonly TimeTracker _db;
        private readonly PluginLoader _loader;
        private readonly Dispatcher _dispatcher;
        private readonly Dictionary<string, bool> _enabledAtStart = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private List<PluginInfo> _plugins = new List<PluginInfo>();
        private readonly List<PluginPageEntry> _pages = new List<PluginPageEntry>();

        public PluginManager(TimeTracker db, string pluginsRoot = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _loader = new PluginLoader(pluginsRoot ?? PluginLoader.DefaultPluginsRoot);
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        public string PluginsRoot => _loader.PluginsRoot;

        public IReadOnlyList<PluginInfo> Plugins => _plugins;

        public IReadOnlyList<PluginPageEntry> Pages => _pages;

        /// <summary>Navigiert zu einem Schlüssel wie bei <c>SelectViewCommand</c>; setzt das Hauptfenster.</summary>
        public Action<string> NavigateHandler { get; set; }

        /// <summary>Hinweis am Tray-Symbol (Titel, Text, Klick); verbindet App.xaml.cs mit dem Tray.</summary>
        public event Action<string, string, Action> NotificationRequested;

        /// <summary>Ob Aktivieren oder Deaktivieren in dieser Sitzung einen Neustart braucht.</summary>
        public bool RestartRequired => _plugins.Any(p => p.IsEnabled != WasEnabledAtStart(p));

        public static string NavigationKey(string pluginId, string pageKey)
        {
            return ("plugin:" + pluginId + ":" + pageKey).ToLowerInvariant();
        }

        /// <summary>Lädt alle aktivierten Plugins. Wirft nie; Fehler stehen am jeweiligen <see cref="PluginInfo"/>.</summary>
        public void LoadAll()
        {
            try
            {
                _plugins = _loader.LoadAll(IsEnabledStored, (info, plugin) => new PluginHost(this, info, _db, _dispatcher));
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Plugins konnten nicht geladen werden: {0}", ex);
                _plugins = new List<PluginInfo>();
            }

            foreach (var info in _plugins)
            {
                _enabledAtStart[info.FolderName] = info.IsEnabled;
                if (info.State == PluginState.Loaded && info.Host is PluginHost host)
                {
                    host.EndInitialization();
                    _pages.AddRange(host.Pages.Select(page => new PluginPageEntry(info, page)));
                }
            }
        }

        public IReadOnlyList<PluginSettingsSection> SettingsSectionsOf(PluginInfo info)
        {
            return info.State == PluginState.Loaded && info.Host is PluginHost host
                ? host.SettingsSections
                : (IReadOnlyList<PluginSettingsSection>)new PluginSettingsSection[0];
        }

        /// <summary>Merkt sich die Wahl; sie wirkt beim nächsten Start.</summary>
        public void SetEnabled(PluginInfo info, bool enabled)
        {
            if (enabled)
                _db.SetPluginRecord(PluginStorage.HostId, EnabledCollection, info.FolderName, "true");
            else
                _db.DeletePluginRecord(PluginStorage.HostId, EnabledCollection, info.FolderName);
            info.IsEnabled = enabled;
        }

        public void ShutdownAll()
        {
            PluginLoader.ShutdownAll(_plugins);
        }

        /// <summary>Öffnet den Plugin-Ordner im Explorer und legt ihn vorher an, falls nötig.</summary>
        public void OpenPluginsFolder()
        {
            Directory.CreateDirectory(PluginsRoot);
            Process.Start("explorer.exe", "\"" + PluginsRoot + "\"");
        }

        internal void Navigate(PluginInfo info, string pageKey)
        {
            var entry = _pages.FirstOrDefault(p => p.Plugin == info && string.Equals(p.Page.Key, pageKey, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                NavigateHandler?.Invoke(entry.NavigationKey);
        }

        internal void OpenSettings()
        {
            NavigateHandler?.Invoke("settings");
        }

        internal void Notify(string title, string message, Action onClick)
        {
            NotificationRequested?.Invoke(title ?? "TimeTrack", message ?? string.Empty, onClick);
        }

        private bool IsEnabledStored(string folderName)
        {
            return _db.GetPluginRecord(PluginStorage.HostId, EnabledCollection, folderName) == "true";
        }

        private bool WasEnabledAtStart(PluginInfo info)
        {
            return _enabledAtStart.TryGetValue(info.FolderName, out var enabled) && enabled;
        }
    }
}
