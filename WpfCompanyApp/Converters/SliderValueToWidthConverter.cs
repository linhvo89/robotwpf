using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace WpfCompanyApp.Converters
{
    public class SliderValueToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 || values[0] == null || values[1] == null || values[2] == null)
                return 0;
            double value = System.Convert.ToDouble(values[0]);
            double totalWidth = System.Convert.ToDouble(values[1]);
            double max = System.Convert.ToDouble(values[2]);
            if (max <= 0) return 0;
            return totalWidth * (value / max);
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    }
