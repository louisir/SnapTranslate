using System.Drawing;

namespace SnapTranslate.Runtime;

public sealed record BrowserSelectionReceivedEventArgs(string Text, Point ScreenPoint, Rectangle? SelectionBounds);
