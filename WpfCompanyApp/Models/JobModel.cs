using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WpfCompanyApp.Models
{
    public partial class JobModelHome : ObservableObject
    {
        public int Id { get; set; }
        public string JobName { get; set; }

        public double H1 { get; set; }
        public double H2 { get; set; }
        public double H3 { get; set; }
        public double H4 { get; set; }
        public double H5 { get; set; }
        public double H6 { get; set; }
        public double R { get; set; }
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
