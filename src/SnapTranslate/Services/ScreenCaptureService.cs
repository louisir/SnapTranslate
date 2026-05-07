using System;
using System.Drawing;
using Forms = System.Windows.Forms;
using SnapTranslate.Configuration;

namespace SnapTranslate.Services;

public sealed class ScreenCaptureService
{
    private readonly AppOptions _options;

    public ScreenCaptureService(AppOptions options)
    {
        _options = options;
    }

    public CaptureRegion CaptureAround(Point center)
    {
        Forms.Screen screen = Forms.Screen.FromPoint(center);
        Rectangle bounds = screen.Bounds;
        Size size = new(Math.Min(_options.CaptureSize.Width, bounds.Width), Math.Min(_options.CaptureSize.Height, bounds.Height));
        int left = Clamp(center.X - size.Width / 2, bounds.Left, bounds.Right - size.Width);
        int top = Clamp(center.Y - size.Height / 2, bounds.Top, bounds.Bottom - size.Height);
        Rectangle captureRect = new(left, top, size.Width, size.Height);
        Bitmap bitmap = new(captureRect.Width, captureRect.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(captureRect.Location, Point.Empty, captureRect.Size);
        return new CaptureRegion(bitmap, captureRect.Location);
    }

    private static int Clamp(int value, int min, int max)
    {
        return max < min ? min : value < min ? min : value > max ? max : value;
    }
}
