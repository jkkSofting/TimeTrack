using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TimeTrack.Plugins;

namespace TimeTrack.Plugin.Sample
{
    /// <summary>
    /// Kleinstes sinnvolles Plugin: eine Seite, die die Buchungen der letzten sieben Tage zählt, und ein
    /// Einstellungsbereich, der einen Text in der Ablage des Plugins speichert.
    /// </summary>
    public sealed class SamplePlugin : TimeTrackPlugin
    {
        private IPluginHost _host;
        private TextBlock _summary;

        public override string Id => "sample";

        public override string Name => "Beispiel-Plugin";

        public override string Description => "Zeigt, wie ein Plugin eine Seite und Einstellungen hinzufügt.";

        public override void Initialize(IPluginHost host)
        {
            _host = host;
            host.Ui.RegisterPage(new PluginPage("main", "Beispiel", CreatePage)
            {
                Glyph = "",
                Refresh = UpdateSummary
            });
            host.Ui.RegisterSettingsSection(new PluginSettingsSection("Gruß", CreateSettings));
        }

        private FrameworkElement CreatePage()
        {
            var page = new StackPanel { Margin = new Thickness(28) };
            var title = new TextBlock { Text = "Beispiel-Plugin" };
            title.SetResourceReference(FrameworkElement.StyleProperty, "Text.PageTitle");
            _summary = new TextBlock { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            _summary.SetResourceReference(FrameworkElement.StyleProperty, "Text.Body");
            page.Children.Add(title);
            page.Children.Add(_summary);
            UpdateSummary();
            return page;
        }

        private void UpdateSummary()
        {
            if (_summary == null)
                return;

            var entries = _host.Data.GetTimeEntries(DateTime.Today.AddDays(-6), DateTime.Today);
            var hours = entries.Sum(e => e.Duration.TotalHours);
            var greeting = _host.Storage.Get("settings", "greeting") ?? "Hallo";
            _summary.Text = $"{greeting}! In den letzten sieben Tagen gibt es {entries.Count} Buchungen mit zusammen {hours:0.##} Stunden.";
        }

        private FrameworkElement CreateSettings()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new TextBox { Width = 200, Text = _host.Storage.Get("settings", "greeting") ?? "Hallo" };
            var save = new Button { Content = "Speichern", Margin = new Thickness(8, 0, 0, 0) };
            save.Click += (s, e) => _host.Storage.Set("settings", "greeting", box.Text);
            panel.Children.Add(box);
            panel.Children.Add(save);
            return panel;
        }
    }
}
