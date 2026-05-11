using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.IDataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace SnapTranslate.Services;

public sealed class SelectedTextCaptureService
{
    private const int ClipboardSetRetryCount = 3;

    public async Task<string?> TryCaptureSelectedTextAsync(CancellationToken cancellationToken)
    {
        WpfDataObject? originalData = TryGetClipboardDataObject();
        uint beforeSequence = GetClipboardSequenceNumber();

        try
        {
            Forms.SendKeys.SendWait("^c");
            await Task.Delay(140, cancellationToken);

            uint afterSequence = GetClipboardSequenceNumber();
            if (afterSequence == beforeSequence)
            {
                return null;
            }

            string text = TryGetClipboardText();
            text = TextSanitizer.NormalizeForTranslation(text);
            return TextSanitizer.IsUsefulForTranslation(text) ? text : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            RestoreClipboard(originalData);
        }
    }

    private static WpfDataObject? TryGetClipboardDataObject()
    {
        try
        {
            return WpfClipboard.GetDataObject();
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetClipboardText()
    {
        try
        {
            return WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText)
                ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void RestoreClipboard(WpfDataObject? originalData)
    {
        for (int attempt = 0; attempt < ClipboardSetRetryCount; attempt++)
        {
            try
            {
                if (originalData is null)
                {
                    WpfClipboard.Clear();
                }
                else
                {
                    WpfClipboard.SetDataObject(originalData, true);
                }

                return;
            }
            catch
            {
                Thread.Sleep(30);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
