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
        }

        private void OpenQuickSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel mainViewModel)
                FloatingQuickSelectWindow.ShowOrActivate(mainViewModel.QuickSelectViewModel, this);
        }
    }
}
