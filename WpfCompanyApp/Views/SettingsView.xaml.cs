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
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
            vmMainViewConfigControl.PreviewMouseDown +=
                VmMainViewConfigControl_PreviewMouseDown;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
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
