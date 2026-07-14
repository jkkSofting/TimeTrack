using System.Windows;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// A small, always-on-top companion window that mirrors the Quick Select state and lets
    /// the user switch the active project via a combo box without opening the main window.
    /// </summary>
    public partial class FloatingQuickSelectWindow : Window
    {
        public FloatingQuickSelectWindow()
        {
            InitializeComponent();
        }
    }
}
