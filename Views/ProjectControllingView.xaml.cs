using System.Windows;
using System.Windows.Controls;

namespace Zeitmanagement.Views
{
    public partial class ProjectControllingView : UserControl
    {
        /// <summary>Ab dieser Breite steht der Detailbereich neben der Übersicht, sonst darunter.</summary>
        private const double SideBySideMinWidth = 1500;

        private const double DetailSideWidth = 600;
        private const double DetailBelowHeight = 440;

        private bool? _sideBySide;

        public ProjectControllingView()
        {
            InitializeComponent();
            SizeChanged += (s, e) => ApplyLayout();
            Loaded += (s, e) => ApplyLayout();
        }

        /// <summary>Detailbereich neben der Übersicht, wenn der Platz reicht; sonst in voller Breite darunter.</summary>
        private void ApplyLayout()
        {
            var sideBySide = ActualWidth >= SideBySideMinWidth;
            if (_sideBySide == sideBySide) return;
            _sideBySide = sideBySide;

            if (sideBySide)
            {
                DetailColumn.Width = new GridLength(DetailSideWidth);
                DetailRow.Height = new GridLength(0);
                Grid.SetColumn(DetailCard, 1);
                Grid.SetRow(DetailCard, 0);
                OverviewCard.Margin = new Thickness(0, 0, 12, 0);
                DetailCard.Margin = new Thickness(0);
            }
            else
            {
                DetailColumn.Width = new GridLength(0);
                DetailRow.Height = new GridLength(DetailBelowHeight);
                Grid.SetColumn(DetailCard, 0);
                Grid.SetRow(DetailCard, 1);
                OverviewCard.Margin = new Thickness(0, 0, 0, 12);
                DetailCard.Margin = new Thickness(0);
            }
        }
    }
}
