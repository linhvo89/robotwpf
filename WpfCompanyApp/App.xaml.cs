using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VM.Core;
using WpfCompanyApp.Logging;
using WpfCompanyApp.Services;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp
{
    public partial class App : Application
    {
        private IHost _host;
        private static Assembly? _visionMasterXmlUiAssembly;
        private Mutex? _singleInstanceMutex;
        private bool _startupCompleted;
        private const string SingleInstanceMutexName = @"Local\WpfCompanyApp.HMRobotV3.SingleInstance";
        // ⭐ Cho phép chỗ khác lấy ServiceProvider
        public static IServiceProvider Services { get; private set; }
        public App()
        {
            LoadVisionMasterXmlUiIntoSingleContext();
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
                    services.AddSingleton<ModbusRtuToolSensorService>();
                    services.AddSingleton<AppBackgroundService>();
                    services.AddSingleton<FileLogger>();

                    // MainWindow
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            // gán cho static Services
            Services = _host.Services;
        }

        private static void LoadVisionMasterXmlUiIntoSingleContext()
        {
            // GlobalCameraDlg is created dynamically by VisionMaster. If Apps.XmlUI
            // is first loaded from the application output and later loaded again
            // from the VisionMaster SDK directory, CLR treats its identical types
            // as different types and the camera window fails with InvalidCastException.
            // Preload the SDK copy before any VisionMaster WPF control is created so
            // all subsequent binds reuse one Assembly instance/load context.
            string xmlUiPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "VisionMaster4.3.0",
                "Development",
                "V4.x",
                "ComControls",
                "Assembly",
                "Apps.XmlUI.dll");

            if (File.Exists(xmlUiPath))
                _visionMasterXmlUiAssembly = Assembly.LoadFrom(xmlUiPath);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show(
                    "Chương trình đang chạy. Không thể mở thêm lần nữa.",
                    "Thông báo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            // ⭐⭐ BẮT BUỘC PHẢI START BACKGROUND SERVICE ⭐⭐
            var bgService = _host.Services.GetRequiredService<AppBackgroundService>();
            bgService.Start(1);
            _startupCompleted = true;
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                if (!_startupCompleted)
                    return;

                // AppBackgroundService được khởi chạy thủ công nên Host.StopAsync()
                // không tự dừng nó. Phải hủy loop và đóng robot/PLC trước.
                var backgroundService = _host.Services.GetService<AppBackgroundService>();
                if (backgroundService != null)
                {
                    Task stopTask = backgroundService.StopAsync();
                    Task completedTask = await Task.WhenAny(stopTask, Task.Delay(5000));
                    if (completedTask != stopTask)
                        Log.Warning("Background service did not stop within 5 seconds.");
                    else
                        await stopTask;
                }

                // Đóng Solution VisionMaster rõ ràng khi người vận hành đóng ứng dụng.
                // OnExit chạy trên UI thread nên có thể gọi trực tiếp SDK VisionMaster.
                if (VmSolution.Instance.SolutionPath != null)
                {
                    VmSolution.Instance.CloseSolution();
                    Log.Information("VisionMaster solution closed during application exit.");
                }

                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while shutting down application.");
            }
            finally
            {
                _host.Dispose();
                _singleInstanceMutex?.Dispose();
                Log.CloseAndFlush();
                base.OnExit(e);
            }
        }
    }
}
