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

            if (!ShowChangeConfirmationDialog(columnName, job.JobName))
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

            bool isHeightColumn = columnName == "H1" || columnName == "H2" || columnName == "H3";
            if (isHeightColumn &&
                (newValue < JobModelHome.MinHeight || newValue > JobModelHome.MaxHeight))
            {
                MessageBox.Show(
                    $"{columnName} chỉ được nhập từ {JobModelHome.MinHeight} đến {JobModelHome.MaxHeight} mm.",
                    "Giá trị vượt giới hạn",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

        private bool ShowChangeConfirmationDialog(string columnName, string jobName)
        {
            var dialog = new Window
            {
                Title = "Xác nhận thay đổi",
                Width = 600,
                Height = 300,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(31, 38, 50))
            };

            var panel = new Grid { Margin = new Thickness(24) };
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var message = new TextBlock
            {
                Text = $"Bạn có muốn thay đổi giá trị {columnName}\ncủa Job \"{jobName}\" không?",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var buttons = new Grid { Margin = new Thickness(50, 15, 50, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition());

            var acceptButton = new Button
            {
                Content = "ĐỒNG Ý",
                Height = 64,
                Margin = new Thickness(6),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(30, 145, 83)),
                BorderThickness = new Thickness(0),
                IsDefault = true
            };
            var cancelButton = new Button
            {
                Content = "HỦY",
                Height = 64,
                Margin = new Thickness(6),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(100, 109, 122)),
                BorderThickness = new Thickness(0),
                IsCancel = true
            };

            acceptButton.Click += (_, _) => dialog.DialogResult = true;
            Grid.SetColumn(cancelButton, 1);
            buttons.Children.Add(acceptButton);
            buttons.Children.Add(cancelButton);
            Grid.SetRow(buttons, 1);
            panel.Children.Add(message);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true;
        }

        private string ShowValueInputDialog(string columnName, double currentValue)
        {
            var inputBox = new TextBox
            {
                Text = currentValue.ToString(CultureInfo.CurrentCulture),
                Height = 64,
                Margin = new Thickness(18, 8, 18, 16),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };

            var keypad = new Grid { Margin = new Thickness(14, 0, 14, 14) };
            for (int row = 0; row < 5; row++)
                keypad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int column = 0; column < 3; column++)
                keypad.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dialog = new Window
            {
                Title = $"Nhập giá trị {columnName}",
                Width = 500,
                Height = Math.Min(720, SystemParameters.WorkArea.Height - 20),
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(31, 38, 50))
            };

            bool replaceOnNextInput = true;
            string decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            Button CreateKey(string text, Brush background, Brush foreground)
            {
                return new Button
                {
                    Content = text,
                    Height = 70,
                    Margin = new Thickness(5),
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Background = background,
                    Foreground = foreground,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
            }

            void AddKey(string text, int row, int column, Action action, Brush? background = null, int columnSpan = 1)
            {
                var button = CreateKey(
                    text,
                    background ?? new SolidColorBrush(Color.FromRgb(58, 70, 89)),
                    Brushes.White);
                button.Click += (_, _) => action();
                Grid.SetRow(button, row);
                Grid.SetColumn(button, column);
                Grid.SetColumnSpan(button, columnSpan);
                keypad.Children.Add(button);
            }

            void Append(string value)
            {
                if (replaceOnNextInput)
                {
                    inputBox.Text = value;
                    replaceOnNextInput = false;
                }
                else
                {
                    inputBox.Text += value;
                }
            }

            AddKey("7", 0, 0, () => Append("7"));
            AddKey("8", 0, 1, () => Append("8"));
            AddKey("9", 0, 2, () => Append("9"));
            AddKey("4", 1, 0, () => Append("4"));
            AddKey("5", 1, 1, () => Append("5"));
            AddKey("6", 1, 2, () => Append("6"));
            AddKey("1", 2, 0, () => Append("1"));
            AddKey("2", 2, 1, () => Append("2"));
            AddKey("3", 2, 2, () => Append("3"));
            AddKey("−", 3, 0, () =>
            {
                replaceOnNextInput = false;
                inputBox.Text = inputBox.Text.StartsWith("-")
                    ? inputBox.Text.Substring(1)
                    : "-" + inputBox.Text;
            });
            AddKey("0", 3, 1, () => Append("0"));
            AddKey(decimalSeparator, 3, 2, () =>
            {
                if (replaceOnNextInput)
                {
                    inputBox.Text = "0" + decimalSeparator;
                    replaceOnNextInput = false;
                }
                else if (!inputBox.Text.Contains(decimalSeparator))
                {
                    inputBox.Text += decimalSeparator;
                }
            });
            AddKey("10", 4, 0, () => Append("10"));
            AddKey("⌫ XÓA", 4, 1, () =>
            {
                replaceOnNextInput = false;
                if (inputBox.Text.Length > 0)
                    inputBox.Text = inputBox.Text.Substring(0, inputBox.Text.Length - 1);
            }, new SolidColorBrush(Color.FromRgb(198, 120, 35)));
            AddKey("XÓA HẾT", 4, 2, () =>
            {
                inputBox.Text = string.Empty;
                replaceOnNextInput = false;
            }, new SolidColorBrush(Color.FromRgb(173, 55, 55)));

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = $"NHẬP GIÁ TRỊ MỚI CHO {columnName}",
                Margin = new Thickness(18, 18, 18, 4),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(inputBox);
            panel.Children.Add(keypad);

            var actionButtons = new Grid { Margin = new Thickness(14, 0, 14, 22) };
            actionButtons.ColumnDefinitions.Add(new ColumnDefinition());
            actionButtons.ColumnDefinitions.Add(new ColumnDefinition());
            var enterButton = CreateKey(
                "ENTER",
                new SolidColorBrush(Color.FromRgb(30, 145, 83)),
                Brushes.White);
            var cancelButton = CreateKey(
                "CANCEL",
                new SolidColorBrush(Color.FromRgb(100, 109, 122)),
                Brushes.White);
            enterButton.Height = 76;
            cancelButton.Height = 76;
            enterButton.IsDefault = true;
            cancelButton.IsCancel = true;
            enterButton.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(inputBox.Text) && inputBox.Text != "-")
                    dialog.DialogResult = true;
            };
            Grid.SetColumn(cancelButton, 1);
            actionButtons.Children.Add(enterButton);
            actionButtons.Children.Add(cancelButton);
            panel.Children.Add(actionButtons);

            dialog.Content = panel;

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
