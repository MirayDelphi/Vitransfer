using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Vitransfer.App;

public sealed class IconImageConverter : IValueConverter {
    private static BitmapSource? Load(string? path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try {
            using var stream = File.OpenRead(path);
            var image = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad).Frames[0];
            if (image.PixelWidth > 2048 || image.PixelHeight > 2048) return null;
            int stride = checked((image.PixelWidth * image.Format.BitsPerPixel + 7) / 8);
            image.CopyPixels(new byte[checked(stride * image.PixelHeight)], stride, 0);
            image.Freeze();
            return image;
        } catch { return null; }
    }
    public static bool IsReadable(string path) => Load(path) != null;
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => Load(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
