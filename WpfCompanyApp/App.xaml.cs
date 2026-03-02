using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO;
using System.Windows;
using WpfCompanyApp.Logging;
using WpfCompanyApp.Services;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp
{
    public partial class App : Application
    {
        private IHost _host;
        // ⭐ Cho phép chỗ khác lấy ServiceProvider
        public static IServiceProvider Services { get; private set; }
        public App()
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
            _host = Host.CreateDefaultBuilder()
                .UseSerilog((context, config) =>
                {
                    config.WriteTo.Console()
                          .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day);
                })
                .ConfigureServices((context, services) =>
                {
                    // Services
                    services.AddSingleton<INavigationService, NavigationService>();
                    // Đường dẫn config
                    string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "HMRobotV3.ini");
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!); // tạo folder nếu chưa tồn tại
                    // INIFile dùng chung, truyền đường dẫn
                    services.AddSingleton(new INIFile(configPath));
                    // ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<HomeViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<ManualViewModel>();
                    services.AddSingleton<AppDataService>();
                    services.AddSingleton<AppBackgroundService>();
                    services.AddSingleton<FileLogger>();

                    // MainWindow
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            // gán cho static Services
            Services = _host.Services;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            // ⭐⭐ BẮT BUỘC PHẢI START BACKGROUND SERVICE ⭐⭐
            var bgService = _host.Services.GetRequiredService<AppBackgroundService>();
            bgService.Start(50);
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await _host.StopAsync();
            _host.Dispose();
            base.OnExit(e);
        }
    }
}
