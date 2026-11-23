using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Zeitmanagement.ViewModel;
using Zeitmanagement.Views;

namespace Zeitmanagement
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            OverlayService.Instance.PropertyChanged += Overlay_PropertyChanged;
        }

        /// <summary>Das Element, das vor dem Öffnen des Overlays den Tastaturfokus hatte (für die Rückgabe beim Schließen).</summary>
        private IInputElement _focusBeforeOverlay;

        /// <summary>Beim Öffnen wandert der Fokus ins Overlay, beim Schließen zurück ins Hauptfenster.</summary>
        private void Overlay_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(OverlayService.Current))
                return;

            if (OverlayService.Instance.IsOpen)
            {
                // Beim ersten Öffnen den bisherigen Fokus merken; beim Ersetzen eines Overlays bleibt er erhalten.
                if (_focusBeforeOverlay == null)
                    _focusBeforeOverlay = Keyboard.FocusedElement;

                // Erst nach dem Layout: Sichtbarkeit und DataTemplate des Inhalts müssen angewendet sein.
                Dispatcher.BeginInvoke(new Action(FocusOverlay), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                var previous = _focusBeforeOverlay;
                _focusBeforeOverlay = null;
                Dispatcher.BeginInvoke(new Action(() => RestoreFocus(previous)), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void FocusOverlay()
        {
            if (!OverlayService.Instance.IsOpen)
                return;

            OverlayCard.UpdateLayout();
            // Erstes fokussierbares Element im Inhalt (Eingabefeld), sonst der Karte selbst.
            if (!OverlayCard.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
            {
                OverlayCard.Focusable = true;
                OverlayCard.Focus();
            }
        }

        private void RestoreFocus(IInputElement previous)
        {
            if (OverlayService.Instance.IsOpen)
                return;

            // Das Overlay ist nun ausgeblendet; die Schicht darunter ist wieder aktiv.
            if (previous is UIElement element && element.IsVisible && element.IsEnabled && element.Focusable)
                element.Focus();
            else
                Focus();
        }

        /// <summary>Die Maus-Zusatztasten blättern im Navigationsverlauf zurück (XButton1) und vor (XButton2).</summary>
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.XButton1 && e.ChangedButton != MouseButton.XButton2)
                return;

            // Bei offenem Overlay gibt es keine Navigation; die Taste wird verschluckt.
            if (OverlayService.Instance.IsOpen)
            {
                e.Handled = true;
                return;
            }

            if (DataContext is MainViewModel mainViewModel)
            {
                var command = e.ChangedButton == MouseButton.XButton1
                    ? mainViewModel.GoBackCommand
                    : mainViewModel.GoForwardCommand;
                if (command.CanExecute(null))
                    command.Execute(null);
            }
            e.Handled = true;
        }

        private void OpenQuickSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel mainViewModel)
                FloatingQuickSelectWindow.ShowOrActivate(mainViewModel.QuickSelectViewModel, this);
        }
    }
}
