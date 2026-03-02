using CommunityToolkit.Mvvm.Input;
using WpfCompanyApp.Services;

namespace WpfCompanyApp.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly AppDataService _data;

        public INavigationService NavigationService => _navigationService;

        public MainViewModel(INavigationService navigationService, AppDataService data)
        {
            _navigationService = navigationService;
            _data = data;

            // Mặc định vào Home
            SetActivePage(home: true, manual: false, settings: false);
            _navigationService.NavigateTo<HomeViewModel>();
        }

        private void SetActivePage(bool home, bool manual, bool settings)
        {
            _data.HomeActive = home;
            _data.ManualActive = manual;
            _data.SettingsActive = settings;
        }

        [RelayCommand]
        private void GoHome()
        {
            SetActivePage(home: true, manual: false, settings: false);
            _navigationService.NavigateTo<HomeViewModel>();
        }

        [RelayCommand]
        private void GoSettings()
        {
            SetActivePage(home: false, manual: false, settings: true);
            _navigationService.NavigateTo<SettingsViewModel>();
        }

        [RelayCommand]
        private void GoManual()
        {
            SetActivePage(home: false, manual: true, settings: false);
            _navigationService.NavigateTo<ManualViewModel>();
        }
    }
}





