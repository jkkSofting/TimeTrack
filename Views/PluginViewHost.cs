using System.Windows;
using System.Windows.Controls;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Zeigt eine Ansicht, die ein Plugin einmal erzeugt hat und die über mehrere Besuche derselben Seite erhalten
    /// bleibt. Ein WPF-Element kann nur einen Elternteil haben; die Seiten werden aber über DataTemplates bei jedem
    /// Besuch neu aufgebaut. Darum gibt der Host die Ansicht beim Entladen wieder frei und löst sie beim Anzeigen aus
    /// einem eventuell noch bestehenden alten Host.
    /// </summary>
    public sealed class PluginViewHost : Decorator
    {
        public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
            nameof(View), typeof(FrameworkElement), typeof(PluginViewHost), new PropertyMetadata(null, OnViewChanged));

        public PluginViewHost()
        {
            Loaded += (s, e) => Attach();
            Unloaded += (s, e) => Child = null;
        }

        public FrameworkElement View
        {
            get => (FrameworkElement)GetValue(ViewProperty);
            set => SetValue(ViewProperty, value);
        }

        private static void OnViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var host = (PluginViewHost)d;
            host.Child = null;
            host.Attach();
        }

        private void Attach()
        {
            var view = View;
            if (view == null || ReferenceEquals(Child, view))
                return;

            if (view.Parent is Decorator previous)
                previous.Child = null;
            Child = view;
        }
    }
}
