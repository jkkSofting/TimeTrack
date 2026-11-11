using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Datei-Dialoge über Windows Forms: Sie zeigen die gewohnte Explorer-Ansicht (anders als die WPF-Dialoge) und hängen am
    /// Hauptfenster.
    /// </summary>
    internal static class FileDialogHelper
    {
        /// <summary>
        /// Fragt nach dem Speicherort. Gibt den gewählten Pfad zurück oder <c>null</c> bei Abbruch. Eine vorhandene Datei wird erst
        /// nach Rückfrage überschrieben.
        /// </summary>
        /// <param name="filter">z. B. <c>"PDF-Dokument (*.pdf)|*.pdf"</c></param>
        public static string ChooseSavePath(string title, string filter, string defaultExtension, string suggestedFileName, string initialFolder)
        {
            using (var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = title,
                Filter = filter,
                DefaultExt = defaultExtension,
                AddExtension = true,
                FileName = suggestedFileName,
                OverwritePrompt = true,
                CheckPathExists = true,
                RestoreDirectory = true
            })
            {
                if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                {
                    dialog.InitialDirectory = initialFolder;
                }

                var owner = new WindowOwner(Application.Current == null ? null : Application.Current.MainWindow);
                return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
            }
        }

        /// <summary>
        /// Fragt nach einer vorhandenen Bilddatei (Logo). Gibt den Pfad zurück oder <c>null</c> bei Abbruch.
        /// </summary>
        public static string ChooseImagePath(string title, string initialFolder)
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = title,
                Filter = "Bilder (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            })
            {
                if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                {
                    dialog.InitialDirectory = initialFolder;
                }

                var owner = new WindowOwner(Application.Current == null ? null : Application.Current.MainWindow);
                return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
            }
        }

        /// <summary>Macht ein WPF-Fenster zum Besitzer eines Windows-Forms-Dialogs.</summary>
        private sealed class WindowOwner : System.Windows.Forms.IWin32Window
        {
            public WindowOwner(Window window)
            {
                Handle = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
            }

            public IntPtr Handle { get; }
        }
    }
}
