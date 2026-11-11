// Target: .NET Framework + C# 7.3
using TimeTrack.Plugins;

namespace Zeitmanagement.Helpers.Plugins
{
    internal enum PluginState
    {
        /// <summary>Gefunden, aber nicht aktiviert; es wurde kein Code des Plugins ausgeführt.</summary>
        Disabled,

        /// <summary>Geladen und initialisiert.</summary>
        Loaded,

        /// <summary>Aktiviert, aber Laden oder Initialisieren ist fehlgeschlagen (siehe <see cref="PluginInfo.Error"/>).</summary>
        Failed
    }

    /// <summary>Ein Plugin im Plugin-Ordner und sein Zustand in dieser Sitzung.</summary>
    internal sealed class PluginInfo
    {
        /// <summary>Name des Unterordners. Darunter merkt sich TimeTrack, ob das Plugin aktiviert ist.</summary>
        public string FolderName { get; set; }

        public string Directory { get; set; }

        /// <summary>Die Plugin-Assembly: <c>&lt;Ordnername&gt;.dll</c> im Ordner.</summary>
        public string AssemblyPath { get; set; }

        /// <summary>Vor dem Laden aus den Dateieigenschaften der DLL, danach <see cref="TimeTrackPlugin.Name"/>.</summary>
        public string DisplayName { get; set; }

        public string VersionText { get; set; }

        public string Description { get; set; }

        /// <summary>Ob das Plugin beim Start geladen werden soll (gespeicherte Wahl).</summary>
        public bool IsEnabled { get; set; }

        public PluginState State { get; set; }

        /// <summary>Verständliche Fehlermeldung bei <see cref="PluginState.Failed"/>.</summary>
        public string Error { get; set; }

        /// <summary><see cref="TimeTrackPlugin.Id"/>, sobald das Plugin geladen ist.</summary>
        public string Id { get; set; }

        public TimeTrackPlugin Instance { get; set; }

        /// <summary>Der Host, den das Plugin in <see cref="TimeTrackPlugin.Initialize"/> bekommen hat.</summary>
        public IPluginHost Host { get; set; }
    }
}
