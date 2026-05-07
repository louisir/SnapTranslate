using System.Drawing;

namespace SnapTranslate.Services;

public sealed record OcrTextLine(string Text, Rectangle ScreenBounds, double Confidence);
