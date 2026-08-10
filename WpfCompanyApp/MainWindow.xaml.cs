using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfCompanyApp.Services;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const uint ScClose = 0xF060;
        private const uint MfByCommand = 0x00000000;


        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr GetSystemMenu(System.IntPtr hWnd, bool revert);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DeleteMenu(System.IntPtr hMenu, uint position, uint flags);

        // ===== THÔNG TIN PHIÊN BẢN - CHỈNH SỬA TẠI ĐÂY =====
        public const string ApplicationVersion = "2.1.0";
        public static readonly string ReleaseDateTime =
            System.IO.File.GetLastWriteTime(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
            .ToString("dd/MM/yyyy HH:mm:ss");

        private const string OperatingManualFileName = "Huong_dan_van_hanh_KBOT.pdf";

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            Title = $"KBOT v{ApplicationVersion}";
            DataContext = viewModel; // ✅ Inject từ DI
            SourceInitialized += DisableWindowCloseButton;
        }

        private void DisableWindowCloseButton(object? sender, System.EventArgs e)
        {
            System.IntPtr windowHandle =
                new System.Windows.Interop.WindowInteropHelper(this).Handle;
            System.IntPtr systemMenu = GetSystemMenu(windowHandle, false);
            if (systemMenu != System.IntPtr.Zero)
                DeleteMenu(systemMenu, ScClose, MfByCommand);
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu == null)
            {
                return;
            }

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement =
                System.Windows.Controls.Primitives.PlacementMode.Right;
            button.ContextMenu.IsOpen = true;
        }

        private void OpenOperatingManual_Click(object sender, RoutedEventArgs e)
        {
            string manualPath = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory,
                "Documents",
                OperatingManualFileName);

            if (!System.IO.File.Exists(manualPath))
            {
                MessageBox.Show(
                    $"Không tìm thấy tài liệu vận hành:\n{manualPath}",
                    "KBOT - Help",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(manualPath)
                    {
                        UseShellExecute = true
                    });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Không thể mở tài liệu vận hành.\n{ex.Message}",
                    "KBOT - Help",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowVersionInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                $"KBOT - Automatic Pick & Place System\n\n" +
                $"Phiên bản: {ApplicationVersion}\n" +
                $"Ngày phát hành: {ReleaseDateTime}\n\n" +
                "Nittan Vietnam",
                "Thông tin phiên bản KBOT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OpenCompanyWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("https://kbot.vn")
                    {
                        UseShellExecute = true
                    });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Không thể mở trang web công ty.\n{ex.Message}",
                    "KBOT - Help",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
