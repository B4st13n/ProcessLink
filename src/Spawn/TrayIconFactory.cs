using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using FontStyle = System.Drawing.FontStyle;
using StringFormat = System.Drawing.StringFormat;

namespace Spawn;

/// <summary>Generates the Spawn icon at runtime (blue disc with a white "S") — no asset files needed.</summary>
public static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using (var brush = new SolidBrush(Color.FromArgb(0x2D, 0x6C, 0xDF)))
                graphics.FillEllipse(brush, new Rectangle(1, 1, size - 2, size - 2));
            using (var font = new Font("Segoe UI", 14f, FontStyle.Bold))
            using (var white = new SolidBrush(Color.White))
            {
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                graphics.DrawString("S", font, white, new RectangleF(0, 0, size, size), format);
            }
        }

        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        var png = pngStream.ToArray();

        // Wrap the PNG in an ICO container (ICONDIR + one ICONDIRENTRY + payload).
        // A PNG payload inside an .ico is supported since Windows Vista.
        using var icoStream = new MemoryStream();
        using (var writer = new BinaryWriter(icoStream, System.Text.Encoding.Default, leaveOpen: true))
        {
            writer.Write((short)0);      // reserved
            writer.Write((short)1);      // type: icon
            writer.Write((short)1);      // count
            writer.Write((byte)size);    // width
            writer.Write((byte)size);    // height
            writer.Write((byte)0);       // palette colors
            writer.Write((byte)0);       // reserved
            writer.Write((short)1);      // color planes
            writer.Write((short)32);     // bits per pixel
            writer.Write(png.Length);    // payload size
            writer.Write(22);            // payload offset (6 + 16)
            writer.Write(png);
        }
        icoStream.Position = 0;
        return new Icon(icoStream);
    }

    public static ImageSource CreateImageSource()
    {
        using var icon = CreateIcon();
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
