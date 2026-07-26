using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WpfCompanyApp.Models
{
    public partial class JobModelHome : ObservableObject
    {
        public int Id { get; set; }
        public string JobName { get; set; }

        private double _h1;
        public double H1
        {
            get => _h1;
            set => SetProperty(ref _h1, value);
        }

        private double _h2;
        public double H2
        {
            get => _h2;
            set => SetProperty(ref _h2, value);
        }

        private double _h3;
        public double H3
        {
            get => _h3;
            set => SetProperty(ref _h3, value);
        }

        public double H4 { get; set; }
        public double H5 { get; set; }
        public double H6 { get; set; }

        private double _r;
        public double R
        {
            get => _r;
            set => SetProperty(ref _r, value);
        }

        [ObservableProperty]
        private bool isActiveJob;

        public DateTime DatetimeJob { get; set; }
    
    }


    public class JobModelSetting
    {
        public int Id { get; set; }
        public string JobName { get; set; }
        public DateTime DatetimeJob { get; set; }

    }
}
