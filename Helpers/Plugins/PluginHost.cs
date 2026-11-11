// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using TimeTrack.Plugins;

namespace Zeitmanagement.Helpers.Plugins
{
    /// <summary>
    /// Der <see cref="IPluginHost"/> eines Plugins. Daten und Ablage laufen immer auf dem UI-Thread, weil die
    /// SQLite-Verbindung nur dort benutzt wird; ein Aufruf aus einem Hintergrund-Thread wartet darauf.
    /// </summary>
    internal sealed class PluginHost : IPluginHost, IPluginUi
    {
        private readonly PluginManager _manager;
        private readonly PluginInfo _info;
        private readonly Dispatcher _dispatcher;
        private readonly List<PluginPage> _pages = new List<PluginPage>();
        private readonly List<PluginSettingsSection> _settingsSections = new List<PluginSettingsSection>();
        private bool _initializing = true;

        public PluginHost(PluginManager manager, PluginInfo info, TimeTracker db, Dispatcher dispatcher)
        {
            _manager = manager;
            _info = info;
            _dispatcher = dispatcher;
            Data = new DispatchingData(db, dispatcher);
            Storage = new DispatchingStorage(new PluginStorage(db, info.Id), dispatcher);
        }

        public int HostApiVersion => PluginLoader.HostApiVersion;

        public string HostVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public string PluginDirectory => _info.Directory;

        public ITimeTrackData Data { get; }

        public IPluginStorage Storage { get; }

        public IPluginUi Ui => this;

        public IReadOnlyList<PluginPage> Pages => _pages;

        public IReadOnlyList<PluginSettingsSection> SettingsSections => _settingsSections;

        /// <summary>Nach <see cref="TimeTrackPlugin.Initialize"/>: weitere Registrierungen werden abgelehnt.</summary>
        public void EndInitialization()
        {
            _initializing = false;
        }

        public void RegisterPage(PluginPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            CheckRegistration();
            if (_pages.Any(p => string.Equals(p.Key, page.Key, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Eine Seite mit dem Schlüssel '{page.Key}' ist bereits registriert.", nameof(page));
            _pages.Add(page);
        }

        public void RegisterSettingsSection(PluginSettingsSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            CheckRegistration();
            _settingsSections.Add(section);
        }

        public void Navigate(string pageKey)
        {
            Run(() => _manager.Navigate(_info, pageKey));
        }

        public void OpenSettings()
        {
            Run(() => _manager.OpenSettings());
        }

        public void ShowNotification(string title, string message, Action onClick = null)
        {
            Run(() => _manager.Notify(title, message, onClick));
        }

        private void CheckRegistration()
        {
            if (!_initializing)
                throw new InvalidOperationException("Seiten und Einstellungsbereiche lassen sich nur in Initialize registrieren.");
            if (!_dispatcher.CheckAccess())
                throw new InvalidOperationException("Nur vom UI-Thread aufrufen.");
        }

        private void Run(Action action)
        {
            if (_dispatcher.CheckAccess())
                action();
            else
                _dispatcher.BeginInvoke(action);
        }

        private static T OnUiThread<T>(Dispatcher dispatcher, Func<T> func)
        {
            return dispatcher.CheckAccess() ? func() : dispatcher.Invoke(func);
        }

        private static void OnUiThread(Dispatcher dispatcher, Action action)
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private sealed class DispatchingData : ITimeTrackData
        {
            private readonly TimeTracker _db;
            private readonly Dispatcher _dispatcher;

            public DispatchingData(TimeTracker db, Dispatcher dispatcher)
            {
                _db = db;
                _dispatcher = dispatcher;
            }

            public IReadOnlyList<PluginProject> GetProjects()
            {
                return OnUiThread(_dispatcher, () => _db.GetPluginProjects());
            }

            public IReadOnlyList<PluginTimeEntry> GetTimeEntries(DateTime fromInclusive, DateTime toInclusive)
            {
                return OnUiThread(_dispatcher, () => _db.GetPluginTimeEntries(fromInclusive.Date, toInclusive.Date));
            }
        }

        private sealed class DispatchingStorage : IPluginStorage
        {
            private readonly PluginStorage _inner;
            private readonly Dispatcher _dispatcher;

            public DispatchingStorage(PluginStorage inner, Dispatcher dispatcher)
            {
                _inner = inner;
                _dispatcher = dispatcher;
            }

            public string Get(string collection, string key) => OnUiThread(_dispatcher, () => _inner.Get(collection, key));

            public void Set(string collection, string key, string value) => OnUiThread(_dispatcher, () => _inner.Set(collection, key, value));

            public bool Delete(string collection, string key) => OnUiThread(_dispatcher, () => _inner.Delete(collection, key));

            public IReadOnlyDictionary<string, string> List(string collection) => OnUiThread(_dispatcher, () => _inner.List(collection));

            public void SetSecret(string key, string value) => OnUiThread(_dispatcher, () => _inner.SetSecret(key, value));

            public string GetSecret(string key) => OnUiThread(_dispatcher, () => _inner.GetSecret(key));
        }
    }
}
