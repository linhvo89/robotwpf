using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfCompanyApp.Views
{
    public sealed class VietnameseConfirmationDialog : Window
    {
        private VietnameseConfirmationDialog(string title, string message, bool confirmation = true)
        {
            Title = title;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(26, 34, 49));

            var root = new StackPanel { Margin = new Thickness(24) };
            root.Children.Add(new TextBlock
            {
                Text = "⚠  CẢNH BÁO",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16)
            });
            root.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24,
                Margin = new Thickness(0, 0, 0, 24)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var noButton = CreateButton(confirmation ? "KHÔNG" : "ĐÓNG", Color.FromRgb(85, 85, 85));
            noButton.IsDefault = true;
            noButton.IsCancel = true;
            noButton.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(noButton);
            if (confirmation)
            {
                var yesButton = CreateButton("CÓ", Color.FromRgb(211, 47, 47));
                yesButton.Click += (_, _) => DialogResult = true;
                buttons.Children.Add(yesButton);
            }
            root.Children.Add(buttons);
            Content = root;
        }

        private static Button CreateButton(string content, Color background)
        {
            return new Button
            {
                Content = content,
                Width = 100,
                Height = 38,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(background),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
        }

        public static bool Confirm(string title, string message)
        {
            var dialog = new VietnameseConfirmationDialog(title, message)
            {
                Owner = Application.Current?.MainWindow
            };
            return dialog.ShowDialog() == true;
        }

        public static void ShowWarning(string title, string message)
        {
            var dialog = new VietnameseConfirmationDialog(title, message, confirmation: false)
            {
                Owner = Application.Current?.MainWindow
            };
            dialog.ShowDialog();
        }
    }
}
