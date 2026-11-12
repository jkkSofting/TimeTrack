// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TimeTrack.Plugins;
using Xunit;
using Zeitmanagement.Helpers.Plugins;

namespace Zeitmanagement.Tests
{
    /// <summary>
    /// Finden und Laden von Plugins. Die Test-Plugins werden hier mit CodeDom übersetzt (C# 5) und bekommen eindeutige
    /// Namen, weil eine einmal geladene Assembly im Testprozess bleibt.
    /// </summary>
    public class PluginLoaderTests : IDisposable
    {
        private readonly string _root;

        public PluginLoaderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "TimeTrackTests", "plugins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            // Geladene DLLs bleiben gesperrt, bis der Testprozess endet; Aufräumen ist nur ein Versuch.
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private const string PluginTemplate = @"
using System;
using TimeTrack.Plugins;
public class TestPlugin : TimeTrackPlugin
{
    public override string Id { get { return ""__ID__""; } }
    public override string Name { get { return ""Testplugin __ID__""; } }
    public override int MinimumHostApiVersion { get { return __MIN__; } }
    public override void Initialize(IPluginHost host) { __INIT__ }
    public static int ShutdownCalls;
    public override void Shutdown() { ShutdownCalls++; }
}";

        private string Plugin(string id, int minimumApi = 1, string initialize = "", string folder = null)
        {
            var source = PluginTemplate.Replace("__ID__", id).Replace("__MIN__", minimumApi.ToString()).Replace("__INIT__", initialize);
            return Compile(folder ?? UniqueFolder(), source);
        }

        private static string UniqueFolder() => TestPluginCompiler.UniqueFolder();

        private string Compile(string folder, string source) => TestPluginCompiler.Compile(_root, folder, source);

        private static IPluginHost FakeHost(PluginInfo info, TimeTrackPlugin plugin)
        {
            return new FakePluginHost();
        }

        private List<PluginInfo> LoadAll(Func<string, bool> isEnabled, int hostApi = PluginLoader.HostApiVersion)
        {
            return new PluginLoader(_root, hostApi).LoadAll(isEnabled, FakeHost);
        }

        [Fact]
        public void A_missing_or_empty_plugin_folder_yields_nothing()
        {
            Assert.Empty(new PluginLoader(Path.Combine(_root, "gibt-es-nicht")).LoadAll(_ => true, FakeHost));
            Assert.Empty(LoadAll(_ => true));
        }

        [Fact]
        public void An_enabled_plugin_is_loaded_and_initialized()
        {
            var folder = Plugin("alpha", initialize: "host.Storage.Set(\"c\", \"k\", \"init\");");
            var host = new FakePluginHost();

            var plugins = new PluginLoader(_root).LoadAll(name => name == folder, (i, p) => host);

            var info = Assert.Single(plugins);
            Assert.Equal(PluginState.Loaded, info.State);
            Assert.Null(info.Error);
            Assert.Equal("alpha", info.Id);
            Assert.Equal("Testplugin alpha", info.DisplayName);
            Assert.Same(host, info.Host);
            Assert.Equal("init", host.Values["c/k"]);
        }

        [Fact]
        public void A_disabled_plugin_is_found_but_not_loaded()
        {
            var folder = Plugin("beta", initialize: "throw new InvalidOperationException(\"darf nicht laufen\");");

            var info = Assert.Single(LoadAll(_ => false));

            Assert.Equal(PluginState.Disabled, info.State);
            Assert.False(info.IsEnabled);
            Assert.Equal(folder, info.FolderName);
            Assert.Null(info.Instance);
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == folder);
        }

        [Fact]
        public void A_foreign_dll_without_plugin_class_fails_with_a_message()
        {
            var folder = Compile(UniqueFolder(), "public class NurEineKlasse { }");

            var info = Assert.Single(LoadAll(_ => true));

            Assert.Equal(PluginState.Failed, info.State);
            Assert.Contains("keine öffentliche Klasse", info.Error);
            Assert.Equal(folder, info.FolderName);
        }

        [Fact]
        public void A_folder_without_its_dll_fails_with_a_message()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Leer"));
            File.WriteAllText(Path.Combine(_root, "Leer", "liesmich.txt"), "x");

            var info = Assert.Single(LoadAll(_ => true));

            Assert.Equal(PluginState.Failed, info.State);
            Assert.Contains("Leer.dll", info.Error);
        }

        [Fact]
        public void A_file_that_is_no_assembly_fails_with_a_message()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Kaputt"));
            File.WriteAllText(Path.Combine(_root, "Kaputt", "Kaputt.dll"), "keine DLL");

            var info = Assert.Single(LoadAll(_ => true));

            Assert.Equal(PluginState.Failed, info.State);
            Assert.Contains("keine gültige", info.Error);
        }

        [Fact]
        public void A_plugin_needing_a_newer_host_is_not_initialized()
        {
            Plugin("gamma", minimumApi: PluginLoader.HostApiVersion + 1, initialize: "throw new InvalidOperationException(\"darf nicht laufen\");");

            var info = Assert.Single(LoadAll(_ => true));

            Assert.Equal(PluginState.Failed, info.State);
            Assert.Contains("neuere TimeTrack-Version", info.Error);
            Assert.Null(info.Instance);
        }

        [Fact]
        public void An_exception_in_initialize_only_disables_that_plugin()
        {
            Plugin("delta", initialize: "throw new InvalidOperationException(\"Kaputt beim Start\");");
            Plugin("epsilon");

            var plugins = LoadAll(_ => true);

            var failed = Assert.Single(plugins, p => p.State == PluginState.Failed);
            Assert.Contains("Kaputt beim Start", failed.Error);
            Assert.Null(failed.Id);
            var loaded = Assert.Single(plugins, p => p.State == PluginState.Loaded);
            Assert.Equal("epsilon", loaded.Id);
        }

        [Fact]
        public void Duplicate_and_invalid_ids_are_rejected()
        {
            Plugin("zeta", folder: "A" + UniqueFolder());
            Plugin("zeta", folder: "B" + UniqueFolder());
            Plugin("Ungültig Groß", folder: "C" + UniqueFolder());

            var plugins = LoadAll(_ => true);

            Assert.Equal(PluginState.Loaded, plugins[0].State);
            Assert.Equal(PluginState.Failed, plugins[1].State);
            Assert.Contains("bereits geladen", plugins[1].Error);
            Assert.Equal(PluginState.Failed, plugins[2].State);
            Assert.Contains("Ungültige Plugin-ID", plugins[2].Error);
        }

        [Fact]
        public void Shutdown_is_called_for_loaded_plugins_and_errors_are_ignored()
        {
            Plugin("eta");
            var plugins = LoadAll(_ => true);
            var type = plugins[0].Instance.GetType();
            var calls = type.GetField("ShutdownCalls");

            PluginLoader.ShutdownAll(plugins);

            Assert.Equal(1, (int)calls.GetValue(null));
        }

        private sealed class FakePluginHost : IPluginHost, IPluginStorage
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();

            public int HostApiVersion => PluginLoader.HostApiVersion;
            public string HostVersion => "test";
            public string PluginDirectory => null;
            public ITimeTrackData Data => null;
            public IPluginStorage Storage => this;
            public IPluginUi Ui => null;

            public string Get(string collection, string key) => Values.TryGetValue(collection + "/" + key, out var v) ? v : null;
            public void Set(string collection, string key, string value) => Values[collection + "/" + key] = value;
            public bool Delete(string collection, string key) => Values.Remove(collection + "/" + key);
            public IReadOnlyDictionary<string, string> List(string collection) => new Dictionary<string, string>();
            public void SetSecret(string key, string value) { }
            public string GetSecret(string key) => null;
        }
    }
}
