using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;

namespace SnapTranslate.UI;

public partial class BubbleWindow : Window
{
    public BubbleWindow()
    {
        InitializeComponent();
    }

    public void ShowResult(DrawingPoint cursorPosition, string sourceText, string translatedText, string? statusMessage)
    {
        ShowResult(cursorPosition, null, sourceText, translatedText, statusMessage);
    }

    public void ShowResult(
        DrawingPoint cursorPosition,
        Rectangle? selectionBounds,
        string sourceText,
        string translatedText,
        string? statusMessage)
    {
        SourceText.Text = sourceText;
        TranslatedText.Text = translatedText;
        SetStatus(statusMessage);
        ShowNear(cursorPosition, selectionBounds);
    }

    public void ShowMessage(DrawingPoint cursorPosition, string title, string? details)
    {
        SourceText.Text = title;
        TranslatedText.Text = string.Empty;
        SetStatus(details);
        ShowNear(cursorPosition);
    }

    private void SetStatus(string? statusMessage)
    {
        StatusText.Text = statusMessage ?? string.Empty;
        StatusText.Visibility = string.IsNullOrWhiteSpace(statusMessage) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowNear(DrawingPoint cursorPosition, Rectangle? selectionBounds = null)
    {
        UpdateLayout();

        Forms.Screen screen = Forms.Screen.FromPoint(cursorPosition);
        Rectangle workingArea = screen.WorkingArea;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : 160;
        double left;
        double top;

        if (selectionBounds is { IsEmpty: false } bounds)
        {
            left = bounds.Left;
            top = bounds.Bottom + 18;
            if (top + height > workingArea.Bottom - 8)
            {
                top = bounds.Top - height - 18;
            }
        }
        else
        {
            left = cursorPosition.X + 16;
            top = cursorPosition.Y + 18;
            if (left > workingArea.Right - width - 12)
            {
                left = cursorPosition.X - width - 16;
            }
        }

        Left = Clamp(left, workingArea.Left + 8, workingArea.Right - width - 8);
        Top = Clamp(top, workingArea.Top + 8, workingArea.Bottom - height - 8);
        Show();
        Topmost = false;
        Topmost = true;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return value < min ? min : value > max ? max : value;
    }
}
