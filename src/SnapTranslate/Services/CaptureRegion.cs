using System;
using System.Drawing;

namespace SnapTranslate.Services;

public sealed class CaptureRegion : IDisposable
{
    public CaptureRegion(Bitmap bitmap, Point origin)
    {
        Bitmap = bitmap;
        Origin = origin;
    }

    public Bitmap Bitmap { get; }
    public Point Origin { get; }
    public void Dispose() => Bitmap.Dispose();
}
