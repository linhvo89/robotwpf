using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace WpfCompanyApp.Converters
{
    public class EnableButtonStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEnabled = (bool)value;

            // 🔁 Đảo lại logic
            // Nếu đang ENABLE thì nút hiển thị màu vàng (vì sẽ bấm để disable)
            // Nếu đang DISABLE thì nút hiển thị màu xanh (vì sẽ bấm để enable)
            string styleKey = isEnabled ? "ModernENABLE_Green" : "ModernDISABLE_Yellow"; 

            return (Style)Application.Current.FindResource(styleKey);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

    }
}
