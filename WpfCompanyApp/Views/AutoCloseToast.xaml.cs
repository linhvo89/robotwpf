using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfCompanyApp.Views
{
    /// <summary>
    /// Interaction logic for AutoCloseToast.xaml
    /// </summary>
    public partial class AutoCloseToast : Window
    {
        private readonly int _durationMs;

        public AutoCloseToast(string message, string title, bool isSuccess, int durationMs)
        {
            InitializeComponent();

            _durationMs = durationMs;

            TitleText.Text = title;
            MessageText.Text = message;

            // Màu + icon theo trạng thái
            if (isSuccess)
            {
                RootBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2ECC71"); // xanh
                IconText.Text = "✔";
            }
            else
            {
                RootBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFE74C3C"); // đỏ
                IconText.Text = "✖";
            }

            Loaded += AutoCloseToast_Loaded;
        }

        private async void AutoCloseToast_Loaded(object sender, RoutedEventArgs e)
        {
            MoveToBottomRight();

            // Fade in
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

            // Đợi
            await Task.Delay(Math.Max(0, _durationMs - 220));

            // Fade out
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)));
            await Task.Delay(230);

            Close();
        }

        private void MoveToBottomRight()
        {
            var workArea = SystemParameters.WorkArea; // vùng màn hình không tính taskbar
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
        }

        // ✅ Hàm gọi nhanh
        public static void ShowSuccess(string message, int durationMs = 1000, string title = "Thông báo")
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var t = new AutoCloseToast(message, title, isSuccess: true, durationMs: durationMs);
                t.Show();
            });
        }

        public static void ShowError(string message, int durationMs = 1000, string title = "Thông báo")
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var t = new AutoCloseToast(message, title, isSuccess: false, durationMs: durationMs);
                t.Show();
            });
        }
    }
}
