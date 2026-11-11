using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Zeitmanagement.Helpers.Plugins;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Kapselt eine Plugin-Seite als <see cref="BaseViewModel"/>, damit sie wie jede eingebaute Seite rechts angezeigt
    /// und bei jedem Öffnen neu geladen wird. Die Ansicht des Plugins entsteht beim ersten Öffnen und bleibt danach
    /// dieselbe Instanz.
    /// </summary>
    internal sealed class PluginPageViewModel : BaseViewModel
    {
        private readonly PluginPageEntry _entry;
        private FrameworkElement _view;

        public PluginPageViewModel(PluginPageEntry entry)
        {
            _entry = entry;
        }

        public string Title => _entry.Page.Title;

        public FrameworkElement View => _view ?? (_view = PluginViewFactory.Create(_entry.Page.CreateView, _entry.Plugin.DisplayName));

        public override void Refresh()
        {
            try
            {
                _entry.Page.Refresh?.Invoke();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Plugin {0}: Fehler beim Aktualisieren der Seite {1}: {2}", _entry.Plugin.Id, _entry.Page.Key, ex);
            }
        }
    }

    /// <summary>Ein Eintrag der Plugin-Gruppe in der Navigation.</summary>
    internal sealed class PluginNavigationItem : BindableBase
    {
        /// <summary>„Puzzle“ aus Segoe MDL2 Assets, wenn das Plugin kein eigenes Icon nennt.</summary>
        public const string DefaultGlyph = "";

        public PluginNavigationItem(PluginPageEntry entry)
        {
            Key = entry.NavigationKey;
            Title = entry.Page.Title;
            Glyph = string.IsNullOrEmpty(entry.Page.Glyph) ? DefaultGlyph : entry.Page.Glyph;
        }

        public string Key { get; }
        public string Title { get; }
        public string Glyph { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    /// <summary>Erzeugt die Ansicht eines Plugins; scheitert das, steht an ihrer Stelle die Fehlermeldung.</summary>
    internal static class PluginViewFactory
    {
        public static FrameworkElement Create(Func<FrameworkElement> createView, string pluginName)
        {
            try
            {
                var view = createView();
                if (view != null)
                    return view;
                return ErrorView(pluginName, "Das Plugin hat keine Ansicht geliefert.");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Plugin {0}: Ansicht konnte nicht erzeugt werden: {1}", pluginName, ex);
                return ErrorView(pluginName, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static FrameworkElement ErrorView(string pluginName, string message)
        {
            var text = new TextBlock
            {
                Margin = new Thickness(28),
                TextWrapping = TextWrapping.Wrap,
                Text = $"Die Ansicht von „{pluginName}“ konnte nicht angezeigt werden.\n\n{message}"
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Danger");
            return text;
        }
    }
}
