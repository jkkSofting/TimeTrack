using System;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Verwaltet das modale In-App-Overlay: höchstens ein Bearbeitungsformular liegt über dem
    /// Hauptfenster. Das MainWindow bindet <see cref="Current"/> und <see cref="IsOpen"/>.
    /// Bewusst ohne WPF-Abhängigkeit (nur <see cref="BindableBase"/>), damit es testbar bleibt.
    /// </summary>
    internal sealed class OverlayService : BindableBase
    {
        public static OverlayService Instance { get; } = new OverlayService();

        private OverlayViewModelBase _current;

        /// <summary>Das aktuell angezeigte Overlay oder null, wenn keines offen ist.</summary>
        public OverlayViewModelBase Current => _current;

        /// <summary>True, solange ein Overlay offen ist (das Hauptfenster ist dann gesperrt).</summary>
        public bool IsOpen => _current != null;

        /// <summary>Wird ausgelöst, nachdem ein Overlay geschlossen wurde (nicht beim Ersetzen durch ein anderes).</summary>
        public event EventHandler Closed;

        /// <summary>Zeigt das Overlay; ein bereits offenes wird ohne Rückfrage ersetzt.</summary>
        public void Show(OverlayViewModelBase vm)
        {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            SetCurrent(vm);
        }

        /// <summary>
        /// Schließt das Overlay, aber nur wenn <paramref name="vm"/> das aktuelle ist (oder null: das aktuelle,
        /// egal welches). Ein veraltetes Overlay kann so kein neueres schließen.
        /// </summary>
        public void Close(OverlayViewModelBase vm = null)
        {
            if (_current == null)
                return;
            if (vm != null && !ReferenceEquals(vm, _current))
                return;

            SetCurrent(null);
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void SetCurrent(OverlayViewModelBase value)
        {
            if (ReferenceEquals(_current, value))
                return;

            var wasOpen = IsOpen;
            _current = value;
            RaisePropertyChanged(nameof(Current));
            if (wasOpen != IsOpen)
                RaisePropertyChanged(nameof(IsOpen));
        }
    }
}
