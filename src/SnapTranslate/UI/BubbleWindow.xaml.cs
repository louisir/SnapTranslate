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
        SourceText.Text = sourceText;
        TranslatedText.Text = translatedText;
        SetStatus(statusMessage);
        ShowNear(cursorPosition);
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

    private void ShowNear(DrawingPoint cursorPosition)
    {
        double left = cursorPosition.X + 16;
        double top = cursorPosition.Y + 18;
        Forms.Screen screen = Forms.Screen.FromPoint(cursorPosition);
        Rectangle workingArea = screen.WorkingArea;
        if (left > workingArea.Right - Width - 12)
        {
            left = cursorPosition.X - Width - 16;
        }

        Left = left < workingArea.Left ? workingArea.Left + 8 : left;
        Top = top < workingArea.Top ? workingArea.Top + 8 : top;
        Show();
    }
}
