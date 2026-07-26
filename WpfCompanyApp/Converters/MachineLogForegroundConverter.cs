using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfCompanyApp.Converters
{
    public sealed class MachineLogForegroundConverter : IValueConverter
    {
        private static readonly Brush NormalBrush =
            new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));

        private static readonly Brush ConnectionErrorBrush =
            new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));

        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            string message = value as string ?? string.Empty;

            if (message.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("không thể kết nối", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("mất kết nối", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("lỗi kết nối", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("cannot connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("connection lost", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ConnectionErrorBrush;
            }

            return NormalBrush;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
