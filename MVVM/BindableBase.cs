using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Zeitmanagement.MVVM
{
    /// <summary>
    /// Represents the ViewModelBase.
    /// </summary>
    public abstract class BindableBase : INotifyPropertyChanged
    {
        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string property = null)
        {
            if (Object.Equals(storage, value)) return false;

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
            return true;
        }

        /// <summary>
        /// Raises <see cref="PropertyChanged"/> for a property that has no backing field of its
        /// own (e.g. one computed from another property), so its binding refreshes too.
        /// </summary>
        protected void RaisePropertyChanged([CallerMemberName] string property = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }


    public class BindingProxy : Freezable
    {
        #region Overrides of Freezable

        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        #endregion

        public object Data
        {
            get { return GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Data.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy)); // , new UIPropertyMetadata(null)
    }
}
