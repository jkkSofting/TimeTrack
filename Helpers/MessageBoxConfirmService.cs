using System.Windows;

namespace Zeitmanagement.Helpers
{
    /// <summary>Zeigt die Rückfrage als WPF-MessageBox. Standardknopf ist „Nein“.</summary>
    internal sealed class MessageBoxConfirmService : IConfirmService
    {
        public bool Confirm(string text, string title) =>
            MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
