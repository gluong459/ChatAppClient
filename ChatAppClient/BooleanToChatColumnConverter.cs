using System;
using System.Globalization;
using System.Windows.Data;

namespace ChatAppClient
{
    // Kế thừa IValueConverter
    public class BooleanToChatColumnConverter : IValueConverter
    {
        // Hàm này dịch dữ liệu từ C# (Source) mang lên giao diện XAML (Target)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMe)
            {
                // Nếu IsMe == true (Là tôi) -> trả về số 1 (Nghĩa là Cột 1 - Bên phải)
                // Nếu IsMe == false (Là người kia) -> trả về số 0 (Nghĩa là Cột 0 - Bên trái)
                return isMe ? 1 : 0;
            }
            return 0; // Mặc định trả về 0 nếu có lỗi
        }

        // Hàm này dịch ngược từ giao diện về C# (ít dùng, thường ném lỗi NotImplemented)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}