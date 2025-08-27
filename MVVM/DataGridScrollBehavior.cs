using System.Windows;
using System.Windows.Controls;

namespace Zeitmanagement.MVVM
{
    internal class DataGridScrollBehavior
    {
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.RegisterAttached("AutoScroll", typeof(bool), typeof(DataGridScrollBehavior), new PropertyMetadata(false, OnAutoScrollPropertyChanged));

        public static bool GetAutoScroll(DataGrid dataGrid)
        {
            return (bool)dataGrid.GetValue(AutoScrollProperty);
        }

        public static void SetAutoScroll(DataGrid dataGrid, bool value)
        {
            dataGrid.SetValue(AutoScrollProperty, value);
        }

        private static void OnAutoScrollPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid dataGrid && (bool)e.NewValue)
            {
                dataGrid.SelectionChanged += (sender, args) =>
                {
                    if (sender is DataGrid grid)
                    {
                        if (grid.SelectedItem != null)
                        {
                            grid.ScrollIntoView(grid.SelectedItem);
                        }
                    }
                };
            }
        }
    }
}
