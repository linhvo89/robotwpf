using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp.Services
{
    public interface INavigationService : INotifyPropertyChanged
    {
        ViewModelBase CurrentViewModel { get; }
        void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
    }

    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase _currentViewModel;

        public event PropertyChangedEventHandler PropertyChanged;

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            private set
            {
                if (_currentViewModel != value)
                {
                    _currentViewModel = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentViewModel)));
                }
            }
        }

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
            CurrentViewModel = viewModel;
        }
    }

}
