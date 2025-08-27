using System;
using System.Windows.Input;

namespace Zeitmanagement.MVVM
{
    public class DelegateCommand : ICommand
    {
        private readonly Action<object> _executeHandler;
        private readonly Predicate<object> _canExecuteHandler;

        public DelegateCommand(Action<object> execute) : this(execute, null)
        { }

        public DelegateCommand(Action<object> execute, Predicate<object> canExecute)
        {
            _executeHandler = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecuteHandler = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void Execute(object parameter)
        {
            _executeHandler(parameter);
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecuteHandler == null) return true;
            return _canExecuteHandler(parameter);
        }

        //public void RaiseCanExecuteChanged()
        //{
        //    CanExecuteChanged?.Invoke(this, new EventArgs());
        //}
    }




    public class DelegateCommand<T> : ICommand
    {
        private readonly Action<T> _executeHandler;
        private readonly Predicate<T> _canExecuteHandler;

        public DelegateCommand(Action<T> execute) : this(execute, null)
        { }

        public DelegateCommand(Action<T> execute, Predicate<T> canExecute)
        {
            _executeHandler = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecuteHandler = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void Execute(object parameter)
        {
            _executeHandler((T)parameter);
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecuteHandler == null) return true;
            return _canExecuteHandler((T)parameter);
        }
    }

}
