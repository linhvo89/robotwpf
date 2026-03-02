using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp.Models
{
    public class IoPoint : ViewModelBase
    {
        private bool _state;
        public string Address { get; set; }
        public string Description { get; set; }
        public bool State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(); }
        }
    }

}
