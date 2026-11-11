// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using TimeTrack.Plugins;

namespace Zeitmanagement.Helpers.Plugins
{
    /// <summary>
    /// Findet und lädt Plugins aus dem Plugin-Ordner. Jedes Plugin liegt in einem eigenen Unterordner
    /// <c>Plugins\&lt;Name&gt;\</c> und besteht dort aus <c>&lt;Name&gt;.dll</c> plus ihren Abhängigkeiten.
    /// </summary>
    /// <remarks>
    /// <see cref="Discover"/> führt keinen Code eines Plugins aus: Name und Version eines noch nicht aktivierten Plugins
    /// stammen aus den Dateieigenschaften. Erst <see cref="Load"/> lädt die Assembly. Jeder Fehler bleibt bei seinem
    /// Plugin; die übrigen laden trotzdem. .NET Framework kann Assemblies nicht entladen, deshalb wirkt Aktivieren und
    /// Deaktivieren erst nach einem Neustart.
    /// </remarks>
    internal sealed class PluginLoader
    {
        /// <summary>Version der Plugin-Schnittstelle, die diese TimeTrack-Version anbietet (<see cref="IPluginHost.HostApiVersion"/>).</summary>
        public const int HostApiVersion = 1;

        private const string AbstractionsAssemblyName = "TimeTrack.Plugins.Abstractions";

        /// <summary>HRESULT, wenn .NET eine aus dem Internet heruntergeladene (blockierte) Assembly nicht lädt.</summary>
        private const int BlockedRemoteSourceHResult = unchecked((int)0x80131515);

        private static readonly object ResolveLock = new object();
        private static readonly List<string> ProbingDirectories = new List<string>();
        private static bool _resolveHooked;

        private readonly int _hostApiVersion;

        public PluginLoader(string pluginsRoot, int hostApiVersion = HostApiVersion)
        {
            PluginsRoot = pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot));
            _hostApiVersion = hostApiVersion;
        }

        /// <summary><c>C:\ProgramData\Kaltenmark-Engineering\TimeTracker\Plugins</c>: neben der Datenbank, übersteht Updates.</summary>
        public static string DefaultPluginsRoot =>
            Path.Combine(Path.GetDirectoryName(BackupHelper.DbFilePath), "Plugins");

        public string PluginsRoot { get; }

        /// <summary>Alle Unterordner des Plugin-Ordners, nach Name sortiert. Ein fehlender Ordner ergibt eine leere Liste.</summary>
        public List<PluginInfo> Discover(Func<string, bool> isEnabled)
        {
            var result = new List<PluginInfo>();
            if (!System.IO.Directory.Exists(PluginsRoot))
                return result;

            string[] folders;
            try
            {
                folders = System.IO.Directory.GetDirectories(PluginsRoot);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return result;
            }

            foreach (var folder in folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                var info = new PluginInfo
                {
                    FolderName = name,
                    Directory = folder,
                    AssemblyPath = Path.Combine(folder, name + ".dll"),
                    DisplayName = name,
                    IsEnabled = isEnabled != null && isEnabled(name),
                    State = PluginState.Disabled
                };
                ReadFileProperties(info);
                result.Add(info);
            }
            return result;
        }

        /// <summary>Lädt alle aktivierten Plugins aus <see cref="Discover"/>; liefert alle gefundenen.</summary>
        public List<PluginInfo> LoadAll(Func<string, bool> isEnabled, Func<PluginInfo, TimeTrackPlugin, IPluginHost> hostFactory)
        {
            var plugins = Discover(isEnabled);
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in plugins.Where(p => p.IsEnabled))
                Load(info, hostFactory, usedIds);
            return plugins;
        }

        /// <summary>
        /// Lädt ein Plugin, erzeugt es und ruft <see cref="TimeTrackPlugin.Initialize"/> auf. Wirft nie: Ein Fehler setzt
        /// <see cref="PluginInfo.State"/> auf <see cref="PluginState.Failed"/> mit verständlichem Text.
        /// </summary>
        public void Load(PluginInfo info, Func<PluginInfo, TimeTrackPlugin, IPluginHost> hostFactory, ISet<string> usedIds)
        {
            try
            {
                var error = TryLoad(info, hostFactory, usedIds);
                if (error != null)
                    Fail(info, error);
            }
            catch (Exception ex)
            {
                Fail(info, "Unerwarteter Fehler beim Laden: " + Describe(ex));
            }
        }

        /// <summary>Ruft <see cref="TimeTrackPlugin.Shutdown"/> aller geladenen Plugins auf und ignoriert deren Fehler.</summary>
        public static void ShutdownAll(IEnumerable<PluginInfo> plugins)
        {
            foreach (var info in plugins.Where(p => p.State == PluginState.Loaded && p.Instance != null))
            {
                try
                {
                    info.Instance.Shutdown();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Plugin {0}: Fehler beim Beenden: {1}", info.Id, ex);
                }
            }
        }

        private string TryLoad(PluginInfo info, Func<PluginInfo, TimeTrackPlugin, IPluginHost> hostFactory, ISet<string> usedIds)
        {
            if (!File.Exists(info.AssemblyPath))
                return $"Im Ordner „{info.FolderName}“ fehlt die Datei „{Path.GetFileName(info.AssemblyPath)}“. Ein Plugin-Ordner muss so heißen wie seine DLL.";

            EnsureAssemblyResolve(info.Directory);

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(info.AssemblyPath);
            }
            catch (Exception ex) when (IsBlockedDownload(ex))
            {
                return "Windows blockiert die Datei, weil sie aus dem Internet stammt. Rechtsklick auf die DLL(s) im Plugin-Ordner → Eigenschaften → „Zulassen“ anhaken, dann TimeTrack neu starten.";
            }
            catch (BadImageFormatException)
            {
                return $"„{Path.GetFileName(info.AssemblyPath)}“ ist keine gültige .NET-Framework-Assembly.";
            }
            catch (Exception ex) when (ex is FileLoadException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return "Die Plugin-DLL konnte nicht geladen werden: " + Describe(ex);
            }

            var types = GetLoadableTypes(assembly);
            var pluginTypes = types
                .Where(t => typeof(TimeTrackPlugin).IsAssignableFrom(t) && t.IsPublic && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .ToList();

            if (pluginTypes.Count == 0)
            {
                if (types.Any(t => DerivesFromForeignContract(t)))
                    return $"Das Plugin bringt eine eigene Kopie von „{AbstractionsAssemblyName}.dll“ mit, die nicht zu dieser TimeTrack-Version passt. Die Datei aus dem Plugin-Ordner löschen.";
                return "In der DLL gibt es keine öffentliche Klasse, die von TimeTrackPlugin erbt und einen parameterlosen Konstruktor hat.";
            }
            if (pluginTypes.Count > 1)
                return "Die DLL enthält mehrere Plugin-Klassen (" + string.Join(", ", pluginTypes.Select(t => t.Name)) + "). Erlaubt ist eine je DLL.";

            TimeTrackPlugin plugin;
            try
            {
                plugin = (TimeTrackPlugin)Activator.CreateInstance(pluginTypes[0]);
            }
            catch (TargetInvocationException ex)
            {
                return "Das Plugin konnte nicht erzeugt werden: " + Describe(ex.InnerException ?? ex);
            }

            string id;
            int minimumApi;
            try
            {
                id = plugin.Id;
                info.DisplayName = string.IsNullOrWhiteSpace(plugin.Name) ? info.DisplayName : plugin.Name;
                info.Description = plugin.Description;
                info.VersionText = plugin.Version?.ToString() ?? info.VersionText;
                minimumApi = plugin.MinimumHostApiVersion;
            }
            catch (Exception ex)
            {
                return "Das Plugin liefert seine Angaben nicht: " + Describe(ex);
            }

            if (!PluginStorage.IsValidPluginId(id))
                return $"Ungültige Plugin-ID „{id}“. Erlaubt sind Kleinbuchstaben, Ziffern, Punkt, Binde- und Unterstrich.";
            if (usedIds != null && usedIds.Contains(id))
                return $"Ein anderes Plugin mit der ID „{id}“ ist bereits geladen.";
            if (minimumApi > _hostApiVersion)
                return $"Das Plugin braucht eine neuere TimeTrack-Version (Plugin-Schnittstelle {minimumApi}, vorhanden {_hostApiVersion}). Bitte TimeTrack aktualisieren.";

            info.Id = id;
            var host = hostFactory(info, plugin);
            try
            {
                plugin.Initialize(host);
            }
            catch (Exception ex)
            {
                info.Id = null;
                return "Fehler beim Initialisieren: " + Describe(ex);
            }

            usedIds?.Add(id);
            info.Instance = plugin;
            info.Host = host;
            info.State = PluginState.Loaded;
            info.Error = null;
            return null;
        }

        private static void Fail(PluginInfo info, string error)
        {
            info.State = PluginState.Failed;
            info.Error = error;
            info.Instance = null;
            info.Host = null;
            Trace.TraceWarning("Plugin {0}: {1}", info.FolderName, error);
        }

        /// <summary>Name, Version und Beschreibung aus den Dateieigenschaften; liest nur Ressourcen, führt keinen Code aus.</summary>
        private static void ReadFileProperties(PluginInfo info)
        {
            if (!File.Exists(info.AssemblyPath))
                return;

            try
            {
                var version = FileVersionInfo.GetVersionInfo(info.AssemblyPath);
                var name = FirstNonEmpty(version.ProductName, version.FileDescription);
                if (name != null)
                    info.DisplayName = name;
                info.VersionText = FirstNonEmpty(version.ProductVersion, version.FileVersion);
                info.Description = FirstNonEmpty(version.Comments, version.FileDescription != name ? version.FileDescription : null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Die Angaben sind nur Anzeige; ohne sie bleibt der Ordnername.
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        }

        private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToList();
            }
        }

        /// <summary>Erbt von einer TimeTrackPlugin-Klasse, die nicht aus der Abstractions-Bibliothek dieser Anwendung stammt.</summary>
        private static bool DerivesFromForeignContract(Type type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
            {
                if (t.FullName == typeof(TimeTrackPlugin).FullName && t != typeof(TimeTrackPlugin))
                    return true;
            }
            return false;
        }

        private static bool IsBlockedDownload(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e.HResult == BlockedRemoteSourceHResult)
                    return true;
            }
            return false;
        }

        private static string Describe(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        /// <summary>
        /// Abhängigkeiten eines Plugins werden in seinem Ordner gesucht. Die Abstractions-Bibliothek kommt immer aus der
        /// Anwendung, damit Plugin und TimeTrack denselben Typ <see cref="TimeTrackPlugin"/> sehen.
        /// </summary>
        private static void EnsureAssemblyResolve(string pluginDirectory)
        {
            lock (ResolveLock)
            {
                if (!ProbingDirectories.Contains(pluginDirectory, StringComparer.OrdinalIgnoreCase))
                    ProbingDirectories.Add(pluginDirectory);

                if (_resolveHooked)
                    return;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                _resolveHooked = true;
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.Equals(name, AbstractionsAssemblyName, StringComparison.OrdinalIgnoreCase))
                return typeof(TimeTrackPlugin).Assembly;

            string[] directories;
            lock (ResolveLock)
                directories = ProbingDirectories.ToArray();

            foreach (var directory in directories)
            {
                var candidate = Path.Combine(directory, name + ".dll");
                if (File.Exists(candidate))
                {
                    try
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                    catch (Exception ex) when (ex is FileLoadException || ex is BadImageFormatException || ex is IOException)
                    {
                        return null;
                    }
                }
            }
            return null;
        }
    }
}
