using Emgu.CV;
using Emgu.CV.Structure;
using PickAndPlace.Models;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;

namespace PickAndPlace.Utils
{
    public class FileNameConverterMain : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path)
            {
                var parentFolder = IO.GetParentFolderFromFilePath(path);
                var fileName = IO.GetFileName(path);
                return $"{parentFolder}/{fileName}"; // Trả về tên file từ đường dẫn
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value; // Không cần sử dụng ở đây, vì chỉ hiển thị tên file
        }
    }
    public class FileNameOnlyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path)
            {
                var parentFolder = IO.GetParentFolderFromFilePath(path);
                var fileName = IO.GetFileName(path);
                return $"{fileName}"; // Trả về tên file
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value; // Không cần sử dụng ở đây, vì chỉ hiển thị tên file
        }
    }
    public class BitmapSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Image<Bgr, byte> image)
            {
                var bitmapSource = Converter.BitmapToBitmapSource(image.Bitmap);
                return bitmapSource; // Trả về tên file
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value; // Không cần sử dụng ở đây, vì chỉ hiển thị tên file
        }
    }

    public class LabelColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool type)
            {
                if (type == true)
                {
                    return "Black";
                }
            }
            return "DarkGray";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value; // Không cần sử dụng ở đây, vì chỉ hiển thị tên file
        }
    }
    public class MyNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;   // Có ảnh -> true

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class StatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == 0) return "DarkGreen";
                else if (status == 1) return "Red";
                else if (status == 2) return "Black";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class MainStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.Ok)) return "CONNECTED";
                else if (status == (int)(StatusState.Ng)) return "DISCONNECTED";
                else if (status == (int)(StatusState.Stopped)) return "STOPPED";
                return "UNKNOWN";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class MainStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.Ok)) return "DarkGreen";
                else if (status == (int)(StatusState.Ng))  return "#F94449";
                else if (status == (int)(StatusState.Inspecting)) return "#E6B400";
                else if (status == (int)(StatusState.Stopped)) return "#C30010";
                return "Gray";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class TextColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.Ok)) return "White";
                else if (status == (int)(StatusState.Ng)) return "White";
                else if (status == (int)(StatusState.Inspecting)) return "White";
                return "White";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    public class MainResCamStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.Ok)) return "OK";
                else if (status == (int)(StatusState.Ng)) return "FAILED";
                else if (status == (int)(StatusState.Inspecting)) return "...";
                else if (status == (int)(StatusState.Stopped)) return "X";
                return "N/A";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class MainInspectionStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.Ok)) return "PASSED";
                else if (status == (int)(StatusState.Ng)) return "FAILED";
                else if (status == (int)(StatusState.Inspecting)) return "INSPECTING";
                else if (status == (int)(StatusState.Stopped)) return "STOPPED";
                return "N/A";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class TupleToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Tuple<double, double> tuple)
            {
                return $"({tuple.Item1:F2}, {tuple.Item2:F2})";
            }

            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
