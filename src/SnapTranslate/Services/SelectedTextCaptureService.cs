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
    private const int CopySettleDelayMs = 220;
    private const string ClipboardProbePrefix = "SNAPTRANSLATE_CLIPBOARD_PROBE_";

    public async Task<string?> TryCaptureSelectedTextAsync(CancellationToken cancellationToken)
    {
        WpfDataObject? originalData = TryGetClipboardDataObject();
        string probeText = ClipboardProbePrefix + Guid.NewGuid().ToString("N");
        bool probeSet = TrySetClipboardText(probeText);
        uint beforeSequence = GetClipboardSequenceNumber();

        try
        {
            Forms.SendKeys.SendWait("^c");
            await Task.Delay(CopySettleDelayMs, cancellationToken);

            uint afterSequence = GetClipboardSequenceNumber();
            if (!probeSet && afterSequence == beforeSequence)
            {
                return null;
            }

            string text = TryGetClipboardText();
            if (probeSet && string.Equals(text, probeText, StringComparison.Ordinal))
            {
                return null;
            }

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

    private static bool TrySetClipboardText(string text)
    {
        for (int attempt = 0; attempt < ClipboardSetRetryCount; attempt++)
        {
            try
            {
                WpfClipboard.SetText(text, WpfTextDataFormat.UnicodeText);
                return true;
            }
            catch
            {
                Thread.Sleep(30);
            }
        }

        return false;
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
