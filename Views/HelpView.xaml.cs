using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Views
{
    /// <summary>
    /// Interaction logic for HelpView.xaml
    /// </summary>
    public partial class HelpView : UserControl
    {
        public HelpView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Starts each tab at the top instead of keeping the other tab's scroll position.</summary>
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldViewModel)
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is INotifyPropertyChanged newViewModel)
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HelpViewModel.IsChangelogTab))
                Scroller.ScrollToTop();
        }
    }
}
