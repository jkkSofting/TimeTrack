using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Zeitmanagement.MVVM
{
    public class WindowClosingBehavior
    {
        public static ICommand GetClosing(DependencyObject depObj)
        {
            return (ICommand)depObj.GetValue(ClosingProperty);
        }
        public static void SetClosing(DependencyObject depObj, ICommand value)
        {
            depObj.SetValue(ClosingProperty, value);
        }

        public static readonly DependencyProperty ClosingProperty = DependencyProperty.RegisterAttached(
            "Closing", typeof(ICommand), typeof(WindowClosingBehavior),
            new UIPropertyMetadata(new PropertyChangedCallback(OnClosingChanged)));

        private static void OnClosingChanged(DependencyObject depObj, DependencyPropertyChangedEventArgs e)
        {
            if (depObj is Window window)
            {
                if (e.NewValue != null)
                {
                    window.Closing += Window_Closing;
                }
                else
                {
                    window.Closing -= Window_Closing;
                }
            }
        }

        private static void Window_Closing(object sender, CancelEventArgs e)
        {
            ICommand closing = GetClosing(sender as Window);
            if (closing != null)
            {
                if (closing.CanExecute(sender))
                {
                    closing.Execute(sender);
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
