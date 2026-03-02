using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace WpfCompanyApp.Converters
{
    public class OnOffTextConverter : IValueConverter
    {
        public string OnText { get; set; } = "ON";
        public string OffText { get; set; } = "OFF";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? OnText : OffText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
