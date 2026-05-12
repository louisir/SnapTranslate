using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Forms = System.Windows.Forms;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.IDataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;
using WpfPoint = System.Windows.Point;

namespace SnapTranslate.Services;

public sealed class SelectedTextCaptureService
{
    private const int ClipboardSetRetryCount = 3;
    private const int PreCopyDelayMs = 220;
    private const int ClipboardPollDelayMs = 60;
    private const int ClipboardWaitTimeoutMs = 1200;
    private const int MaxSelectionTextLength = 4000;
    private const int MaxAutomationParentDepth = 6;
    private const string ClipboardProbePrefix = "SNAPTRANSLATE_CLIPBOARD_PROBE_";

    public async Task<string?> TryCaptureSelectedTextAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(PreCopyDelayMs, cancellationToken);

        string? automationText = TryCaptureAutomationSelectedText();
        if (!string.IsNullOrWhiteSpace(automationText))
        {
            return automationText;
        }

        WpfDataObject? originalData = TryGetClipboardDataObject();
        string probeText = ClipboardProbePrefix + Guid.NewGuid().ToString("N");
        bool probeSet = TrySetClipboardText(probeText);
        uint beforeSequence = GetClipboardSequenceNumber();

        try
        {
            SendCopyShortcut();
            return await WaitForCopiedTextAsync(probeSet, probeText, beforeSequence, cancellationToken);
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

    private static async Task<string?> WaitForCopiedTextAsync(
        bool probeSet,
        string probeText,
        uint beforeSequence,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(ClipboardWaitTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint afterSequence = GetClipboardSequenceNumber();
            string text = TryGetClipboardText();
            if (probeSet && string.Equals(text, probeText, StringComparison.Ordinal))
            {
                await Task.Delay(ClipboardPollDelayMs, cancellationToken);
                continue;
            }

            string normalizedText = TextSanitizer.NormalizeForTranslation(text);
            if (TextSanitizer.IsUsefulForTranslation(normalizedText) &&
                (probeSet || afterSequence != beforeSequence))
            {
                return normalizedText;
            }

            await Task.Delay(ClipboardPollDelayMs, cancellationToken);
        }

        return null;
    }

    private static string? TryCaptureAutomationSelectedText()
    {
        try
        {
            System.Drawing.Point cursorPosition = Forms.Cursor.Position;
            AutomationElement? element = AutomationElement.FromPoint(new WpfPoint(cursorPosition.X, cursorPosition.Y));
            if (element is null)
            {
                return null;
            }

            foreach (AutomationElement candidate in EnumerateSelfAndParents(element))
            {
                if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject) ||
                    patternObject is not TextPattern textPattern)
                {
                    continue;
                }

                TextPatternRange[] ranges = textPattern.GetSelection();
                foreach (TextPatternRange range in ranges)
                {
                    string text = TextSanitizer.NormalizeForTranslation(range.GetText(MaxSelectionTextLength));
                    if (TextSanitizer.IsUsefulForTranslation(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static IEnumerable<AutomationElement> EnumerateSelfAndParents(AutomationElement element)
    {
        AutomationElement? current = element;
        for (int depth = 0; current is not null && depth < MaxAutomationParentDepth; depth++)
        {
            yield return current;

            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch
            {
                yield break;
            }
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

    private static void SendCopyShortcut()
    {
        Input[] inputs =
        {
            Input.KeyDown(VirtualKeyControl),
            Input.KeyDown(VirtualKeyC),
            Input.KeyUp(VirtualKeyC),
            Input.KeyUp(VirtualKeyControl)
        };

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyC = 0x43;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;

        public static Input KeyDown(ushort virtualKey)
        {
            return CreateKeyboardInput(virtualKey, 0);
        }

        public static Input KeyUp(ushort virtualKey)
        {
            return CreateKeyboardInput(virtualKey, KeyEventKeyUp);
        }

        private static Input CreateKeyboardInput(ushort virtualKey, uint flags)
        {
            return new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    KeyboardInput = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = flags
                    }
                }
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
