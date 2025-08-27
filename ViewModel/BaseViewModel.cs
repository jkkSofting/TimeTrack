using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    internal abstract class BaseViewModel:BindableBase
    {
        public abstract void Refresh();
    }
}
