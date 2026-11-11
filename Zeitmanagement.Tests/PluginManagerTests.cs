// Target: .NET Framework + C# 7.3
using System;
using System.IO;
using System.Linq;
using TimeTrack.Plugins;
using Xunit;
using Zeitmanagement.Helpers.Plugins;

namespace Zeitmanagement.Tests
{
    /// <summary>
    /// Zusammenspiel von Manager, Host und Plugin: Aktivierung, Seiten, Einstellungsbereiche, Daten und Ablage über den
    /// Host. Die Ansichten selbst werden hier nicht erzeugt (dafür bräuchte es einen STA-Thread mit WPF-Anwendung).
    /// </summary>
    public class PluginManagerTests : IDisposable
    {
        private readonly string _root;
        private readonly TestDb _db = new TestDb();

        public PluginManagerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "TimeTrackTests", "plugins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            _db.Dispose();
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Ein Plugin mit zwei Seiten und einem Einstellungsbereich. Es merkt sich den Host statisch, damit die Tests
        /// danach über ihn auf Daten und Ablage zugreifen können.
        /// </summary>
        private const string PluginSource = @"
using System;
using System.Windows;
using TimeTrack.Plugins;
public class PagesPlugin : TimeTrackPlugin
{
    public static IPluginHost Host;
    public override string Id { get { return ""__ID__""; } }
    public override string Name { get { return ""Seiten-Plugin""; } }
    public override void Initialize(IPluginHost host)
    {
        Host = host;
        host.Ui.RegisterPage(new PluginPage(""Sync"", ""Übertragen"", delegate { return (FrameworkElement)null; }) { Glyph = """" });
        host.Ui.RegisterPage(new PluginPage(""log"", ""Protokoll"", delegate { return (FrameworkElement)null; }));
        host.Ui.RegisterSettingsSection(new PluginSettingsSection(""Verbindung"", delegate { return (FrameworkElement)null; }));
        host.Storage.Set(""state"", ""initialized"", ""ja"");
    }
}";

        private string Plugin(string id)
        {
            return TestPluginCompiler.Compile(_root, TestPluginCompiler.UniqueFolder(), PluginSource.Replace("__ID__", id));
        }

        private PluginManager Manager()
        {
            var manager = new PluginManager(_db.Db, _root);
            manager.LoadAll();
            return manager;
        }

        private static IPluginHost HostOf(PluginInfo info)
        {
            return (IPluginHost)info.Instance.GetType().GetField("Host").GetValue(null);
        }

        [Fact]
        public void A_new_plugin_stays_disabled_until_enabled_and_restarted()
        {
            Plugin("pages-a");

            var first = Manager();
            var info = Assert.Single(first.Plugins);
            Assert.Equal(PluginState.Disabled, info.State);
            Assert.Empty(first.Pages);
            Assert.False(first.RestartRequired);

            first.SetEnabled(info, true);
            Assert.True(first.RestartRequired);
            Assert.Equal(1, _db.Scalar("SELECT COUNT(*) FROM plugin_records WHERE plugin_id = '$host' AND collection = 'enabled'"));

            var second = Manager();
            Assert.Equal(PluginState.Loaded, second.Plugins[0].State);
            Assert.False(second.RestartRequired);

            second.SetEnabled(second.Plugins[0], false);
            Assert.True(second.RestartRequired);
            Assert.Equal(PluginState.Disabled, Manager().Plugins[0].State);
        }

        [Fact]
        public void Pages_and_settings_sections_of_a_loaded_plugin_are_published()
        {
            var folder = Plugin("pages-b");
            _db.Db.SetPluginRecord(PluginStorage.HostId, "enabled", folder, "true");

            var manager = Manager();

            Assert.Equal(new[] { "plugin:pages-b:sync", "plugin:pages-b:log" }, manager.Pages.Select(p => p.NavigationKey).ToArray());
            Assert.Equal("Übertragen", manager.Pages[0].Page.Title);
            Assert.Equal("Verbindung", Assert.Single(manager.SettingsSectionsOf(manager.Plugins[0])).Title);
        }

        [Fact]
        public void Pages_can_only_be_registered_during_initialize()
        {
            var folder = Plugin("pages-c");
            _db.Db.SetPluginRecord(PluginStorage.HostId, "enabled", folder, "true");
            var host = HostOf(Manager().Plugins[0]);

            Assert.Throws<InvalidOperationException>(() =>
                host.Ui.RegisterPage(new PluginPage("spaet", "Zu spät", () => null)));
        }

        [Fact]
        public void The_host_gives_read_access_and_a_storage_scoped_to_the_plugin()
        {
            var folder = Plugin("pages-d");
            _db.Db.SetPluginRecord(PluginStorage.HostId, "enabled", folder, "true");
            _db.Db.AddOrEnsureProject("Alpha", "Kunde", "1500410A24RD001.01");
            _db.Db.AddTimeEntry(new DateTime(2026, 11, 10), "08:00", "10:00", "Alpha", "ABC-1");

            var manager = Manager();
            var host = HostOf(manager.Plugins[0]);

            Assert.Equal(PluginLoader.HostApiVersion, host.HostApiVersion);
            Assert.Equal("Alpha", Assert.Single(host.Data.GetProjects()).Name);
            var entry = Assert.Single(host.Data.GetTimeEntries(new DateTime(2026, 11, 10, 15, 0, 0), new DateTime(2026, 11, 10)));
            Assert.Equal(2.0, entry.Duration.TotalHours);
            Assert.Equal("ja", host.Storage.Get("state", "initialized"));
            Assert.Equal("ja", _db.Db.GetPluginRecord("pages-d", "state", "initialized"));
        }

        [Fact]
        public void Rescan_finds_a_plugin_added_after_the_start_without_loading_it()
        {
            var manager = Manager();
            Assert.Empty(manager.Plugins);

            var folder = Plugin("later");
            var result = manager.Rescan();

            Assert.Equal(1, result.Added);
            var info = Assert.Single(manager.Plugins);
            Assert.Equal(folder, info.FolderName);
            Assert.Equal(PluginState.Disabled, info.State);
            Assert.Null(info.Instance);
            Assert.False(manager.RestartRequired);

            manager.SetEnabled(info, true);
            Assert.True(manager.RestartRequired);

            Assert.Equal(0, manager.Rescan().Added);
            Assert.Same(info, Assert.Single(manager.Plugins));
        }

        [Fact]
        public void Rescan_notices_a_replaced_dll_of_a_running_plugin()
        {
            var folder = Plugin("updated");
            _db.Db.SetPluginRecord(PluginStorage.HostId, "enabled", folder, "true");
            var manager = Manager();
            var info = manager.Plugins.Single();
            Assert.Equal(PluginState.Loaded, info.State);

            Assert.Equal(0, manager.Rescan().Added);
            Assert.False(info.UpdateOnDisk);
            Assert.False(manager.RestartRequired);

            // Eine geladene DLL ist gesperrt, lässt sich aber umbenennen; daneben kommt die neue Fassung.
            var old = info.AssemblyPath + ".old";
            File.Move(info.AssemblyPath, old);
            File.Copy(old, info.AssemblyPath);
            File.SetLastWriteTimeUtc(info.AssemblyPath, DateTime.UtcNow.AddMinutes(5));
            manager.Rescan();

            Assert.True(info.UpdateOnDisk);
            Assert.True(manager.RestartRequired);
            Assert.Equal(PluginState.Loaded, info.State);
        }

        [Fact]
        public void Rescan_drops_a_removed_plugin_that_was_not_loaded()
        {
            var folder = Plugin("removed");
            var manager = Manager();
            Assert.Single(manager.Plugins);

            Directory.Delete(Path.Combine(_root, folder), true);
            var result = manager.Rescan();

            Assert.Equal(1, result.Removed);
            Assert.Empty(manager.Plugins);
        }

        [Fact]
        public void Navigation_requests_of_a_plugin_reach_the_main_window()
        {
            var folder = Plugin("pages-e");
            _db.Db.SetPluginRecord(PluginStorage.HostId, "enabled", folder, "true");
            var manager = Manager();
            string navigatedTo = null;
            manager.NavigateHandler = key => navigatedTo = key;
            var host = HostOf(manager.Plugins[0]);

            host.Ui.Navigate("LOG");
            Assert.Equal("plugin:pages-e:log", navigatedTo);

            host.Ui.Navigate("gibt-es-nicht");
            Assert.Equal("plugin:pages-e:log", navigatedTo);

            host.Ui.OpenSettings();
            Assert.Equal("settings", navigatedTo);
        }
    }
}
