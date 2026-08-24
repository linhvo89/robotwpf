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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Serilog;

namespace WpfCompanyApp.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private const string ProtectedSettingsPassword = "111111";
        private TabItem? _lastAllowedTab;
        private bool _isRestoringTab;
        private bool _isPasswordPromptScheduled;

        public SettingsView()
        {
            InitializeComponent();
            tabRobot.SelectedItem = tabRobotSensor;
            _lastAllowedTab = tabRobotSensor;
            tabRobot.SelectionChanged += TabRobot_SelectionChanged;
            Loaded += SettingsView_Loaded;
            vmMainViewConfigControl.PreviewMouseDown +=
                VmMainViewConfigControl_PreviewMouseDown;
        }

        private void TabRobot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRestoringTab || e.Source != tabRobot)
                return;

            ScheduleSelectedTabValidation();
        }

        private void ScheduleSelectedTabValidation()
        {
            if (_isPasswordPromptScheduled)
                return;

            _isPasswordPromptScheduled = true;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _isPasswordPromptScheduled = false;
                    ValidateSelectedTabAccess();
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void ValidateSelectedTabAccess()
        {
            var selectedTab = tabRobot.SelectedItem as TabItem;
            bool requiresPassword = selectedTab == tabRobotPositionManagement ||
                                    selectedTab == tabRobotTrajectory;

            if (!requiresPassword)
            {
                _lastAllowedTab = selectedTab;
                return;
            }

            if (ShowPasswordDialog(selectedTab?.Header?.ToString() ?? "SETTING"))
            {
                _lastAllowedTab = selectedTab;
                return;
            }

            _isRestoringTab = true;
            tabRobot.SelectedItem = _lastAllowedTab ?? tabRobot.Items[0];
            _isRestoringTab = false;
        }

        private static bool ShowPasswordDialog(string pageName)
        {
            var passwordBox = new PasswordBox
            {
                FontSize = 18,
                Height = 36,
                Margin = new Thickness(0, 8, 0, 16)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 90,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 34,
                IsCancel = true
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var content = new StackPanel { Margin = new Thickness(22) };
            content.Children.Add(new TextBlock
            {
                Text = $"Nhập mật khẩu để vào trang {pageName}:",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(passwordBox);
            content.Children.Add(buttonPanel);

            var dialog = new Window
            {
                Title = "Yêu cầu mật khẩu",
                Content = content,
                Width = 540,
                MinHeight = 230,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Owner = Application.Current?.MainWindow
            };

            okButton.Click += (_, __) =>
            {
                if (passwordBox.Password == ProtectedSettingsPassword)
                {
                    dialog.DialogResult = true;
                    return;
                }

                MessageBox.Show(
                    dialog,
                    "Mật khẩu không đúng. Vui lòng thử lại.",
                    "Sai mật khẩu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                passwordBox.Clear();
                passwordBox.Focus();
            };

            dialog.ContentRendered += (_, __) => passwordBox.Focus();
            return dialog.ShowDialog() == true;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            _isRestoringTab = true;
            tabRobot.SelectedItem = tabRobotSensor;
            _lastAllowedTab = tabRobotSensor;
            _isRestoringTab = false;
            ScheduleSelectedTabValidation();

            try
            {
                vmMainViewConfigControl.BindMultiProcedure();
                vmMainViewConfigControl.SetParamTabEditable(true);
                vmMainViewConfigControl.UnlockWorkArea();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VISION] Cannot initialize configuration control: {ex}");
            }
        }

        private void VmMainViewConfigControl_PreviewMouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var button = FindVisualParent<Button>(source);
            var selectedModule = vmMainViewConfigControl.SelectedModule;

            Log.Information(
                "[VISION_CLICK] Source={Source}; ButtonName={ButtonName}; Content={Content}; " +
                "ToolTip={ToolTip}; DataContext={DataContext}; Command={Command}; SelectedModule={SelectedModule}",
                e.OriginalSource?.GetType().FullName ?? "<null>",
                button?.Name ?? "<none>",
                button?.Content?.ToString() ?? "<none>",
                button?.ToolTip?.ToString() ?? "<none>",
                button?.DataContext?.GetType().FullName ?? "<none>",
                button?.Command?.GetType().FullName ?? "<none>",
                selectedModule?.GetType().FullName ?? "<none>");

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    string windows = string.Join(
                        " | ",
                        Application.Current.Windows
                            .OfType<Window>()
                            .Select(w =>
                                $"{w.GetType().FullName};Title={w.Title};" +
                                $"Visible={w.IsVisible};Active={w.IsActive};State={w.WindowState}"));

                    Log.Information("[VISION_WINDOWS_AFTER_CLICK] {Windows}", windows);
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private static T? FindVisualParent<T>(DependencyObject? child)
            where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match)
                    return match;

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
        }
    }
}
