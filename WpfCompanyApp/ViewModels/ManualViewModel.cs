using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net;
using WpfCompanyApp.Services;

namespace WpfCompanyApp.ViewModels
{
    public partial class ManualViewModel : ViewModelBase
    {
        public AppDataService Data { get; }

        public ManualViewModel(AppDataService data)
        {
            Data = data;
        }
      

    }
}
