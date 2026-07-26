using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfCompanyApp.Models;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp.Views
{
    /// <summary>
    /// Interaction logic for HomeView.xaml
    /// </summary>
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }

        private void JobsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell?.Column?.Header == null || cell.DataContext is not JobModelHome job)
                return;

            string columnName = cell.Column.Header.ToString();
            if (columnName != "H1" && columnName != "H2" && columnName != "H3" && columnName != "R")
                return;

            var confirm = MessageBox.Show(
                $"Bạn có muốn thay đổi giá trị ô {columnName} của Job \"{job.JobName}\" không?",
                "Xác nhận thay đổi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            double currentValue = GetJobValue(job, columnName);
            string input = ShowValueInputDialog(columnName, currentValue);
            if (string.IsNullOrWhiteSpace(input))
                return;

            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out double newValue) &&
                !double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out newValue))
            {
                MessageBox.Show("Giá trị nhập không hợp lệ. Vui lòng nhập số.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (DataContext is HomeViewModel viewModel)
                    viewModel.UpdateJobHomeValue(job, columnName, newValue);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể cập nhật giá trị: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static double GetJobValue(JobModelHome job, string columnName)
        {
            return columnName switch
            {
                "H1" => job.H1,
                "H2" => job.H2,
                "H3" => job.H3,
                "R" => job.R,
                _ => 0
            };
        }

        private string ShowValueInputDialog(string columnName, double currentValue)
        {
            var inputBox = new TextBox
            {
                Text = currentValue.ToString(CultureInfo.CurrentCulture),
                Margin = new Thickness(12, 0, 12, 12),
                FontSize = 16,
                MinWidth = 260
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 12)
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = $"Nhập giá trị mới cho {columnName}:",
                Margin = new Thickness(12),
                FontSize = 16
            });
            panel.Children.Add(inputBox);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = "Nhập giá trị",
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            okButton.Click += (_, _) => dialog.DialogResult = true;
            inputBox.SelectAll();
            inputBox.Focus();

            return dialog.ShowDialog() == true ? inputBox.Text : string.Empty;
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }
    }
}
