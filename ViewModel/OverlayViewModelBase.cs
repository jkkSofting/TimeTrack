using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// Basis aller Bearbeiten-Dialoge, die als Overlay über dem Hauptfenster laufen. Die Hülle (Titel,
    /// Speichern/Abbrechen/optional Löschen) zeichnet das MainWindow; der Inhalt kommt aus einem impliziten
    /// DataTemplate des abgeleiteten Typs (Views/Templates.xaml).
    /// </summary>
    internal abstract class OverlayViewModelBase : BindableBase
    {
        private string _title;
        private string _saveText = "Speichern";
        private string _cancelText = "Abbrechen";
        private string _dangerText = "Löschen";
        private DelegateCommand _dangerCommand;

        protected OverlayViewModelBase()
            : this(null)
        {
        }

        protected OverlayViewModelBase(string title)
        {
            _title = title;
            SaveCommand = new DelegateCommand(_ => OnSave(), _ => CanSave());
            CancelCommand = new DelegateCommand(_ => OnCancel());
        }

        /// <summary>Überschrift der Karte.</summary>
        public virtual string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>Beschriftung des Speichern-Buttons.</summary>
        public string SaveText
        {
            get => _saveText;
            set => SetProperty(ref _saveText, value);
        }

        /// <summary>Beschriftung des Abbrechen-Buttons.</summary>
        public string CancelText
        {
            get => _cancelText;
            set => SetProperty(ref _cancelText, value);
        }

        /// <summary>Beschriftung der optionalen dritten Aktion (z. B. „Löschen“).</summary>
        public string DangerText
        {
            get => _dangerText;
            set => SetProperty(ref _dangerText, value);
        }

        /// <summary>Ruft <see cref="OnSave"/> auf, sofern <see cref="CanSave"/> es erlaubt. Schließt NICHT von selbst.</summary>
        public DelegateCommand SaveCommand { get; }

        /// <summary>Ruft <see cref="OnCancel"/> auf (Standard: schließen).</summary>
        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// Optionale dritte Aktion (z. B. „Löschen“), im Shell links neben den Buttons. Null (Standard):
        /// der Button ist ausgeblendet.
        /// </summary>
        public DelegateCommand DangerCommand
        {
            get => _dangerCommand;
            set => SetProperty(ref _dangerCommand, value);
        }

        /// <summary>Ob Speichern gerade möglich ist (Standard: ja). Nach Änderungen <see cref="RaiseCanSaveChanged"/> rufen.</summary>
        protected virtual bool CanSave() => true;

        /// <summary>Speichert. Das Overlay bleibt offen, bis die Unterklasse nach Erfolg selbst <see cref="Close"/> ruft.</summary>
        protected abstract void OnSave();

        /// <summary>Abbrechen; Standard: das Overlay schließen.</summary>
        protected virtual void OnCancel()
        {
            Close();
        }

        /// <summary>Aktualisiert den Zustand des Speichern-Buttons (aktiv/inaktiv).</summary>
        protected void RaiseCanSaveChanged()
        {
            SaveCommand.RaiseCanExecuteChanged();
        }

        /// <summary>Schließt dieses Overlay (wirkt nicht, wenn inzwischen ein anderes angezeigt wird).</summary>
        public void Close()
        {
            OverlayService.Instance.Close(this);
        }
    }
}
