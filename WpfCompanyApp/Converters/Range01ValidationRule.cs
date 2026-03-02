using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace WpfCompanyApp.Converters
{
    public class Range01ValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = value as string ?? string.Empty;

            // Cho phép cả . và ,  => chuẩn hóa về .
            text = text.Replace(',', '.');

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
            {
                if (v < 0 || v > 1)
                    return new ValidationResult(false, "Giá trị phải từ 0 đến 1");
                return ValidationResult.ValidResult;
            }

            return new ValidationResult(false, "Giá trị không hợp lệ");
        }
    }


}
