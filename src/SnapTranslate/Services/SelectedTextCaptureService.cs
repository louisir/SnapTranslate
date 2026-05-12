using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
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
    private const int ClipboardPollDelayMs = 60;
    private const int ClipboardWaitTimeoutMs = 1200;
    private const int DirectSelectionProbeCount = 4;
    private const int DirectSelectionProbeDelayMs = 80;
    private const int MaxSelectionTextLength = 4000;
    private const int MaxScintillaSelectionBytes = (MaxSelectionTextLength * 4) + 8;
    private const int MaxScintillaParentDepth = 5;
    private const int ScintillaClassNameCapacity = 128;
    private const int MaxAutomationParentDepth = 6;
    private const string ClipboardProbePrefix = "SNAPTRANSLATE_CLIPBOARD_PROBE_";

    public async Task<string?> TryCaptureSelectedTextAsync(CancellationToken cancellationToken)
    {
        string? directText = await TryCaptureDirectSelectedTextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
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

    private static async Task<string?> TryCaptureDirectSelectedTextAsync(CancellationToken cancellationToken)
    {
        string? bestText = null;

        for (int attempt = 0; attempt < DirectSelectionProbeCount; attempt++)
        {
            string? candidateText = TryCaptureDirectSelectedText();
            if (IsBetterSelectionText(candidateText, bestText))
            {
                bestText = candidateText;
            }

            if (attempt < DirectSelectionProbeCount - 1)
            {
                await Task.Delay(DirectSelectionProbeDelayMs, cancellationToken);
            }
        }

        return bestText;
    }

    private static string? TryCaptureDirectSelectedText()
    {
        string? scintillaText = TryCaptureScintillaSelectedText();
        if (!string.IsNullOrWhiteSpace(scintillaText))
        {
            return scintillaText;
        }

        string? automationText = TryCaptureAutomationSelectedText();
        return string.IsNullOrWhiteSpace(automationText) ? null : automationText;
    }

    private static bool IsBetterSelectionText(string? candidateText, string? currentText)
    {
        if (string.IsNullOrWhiteSpace(candidateText))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentText))
        {
            return true;
        }

        return candidateText.Length > currentText.Length;
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

    private static string? TryCaptureScintillaSelectedText()
    {
        try
        {
            if (!GetCursorPos(out NativePoint cursorPosition))
            {
                return null;
            }

            IntPtr window = WindowFromPoint(cursorPosition);
            for (int depth = 0; window != IntPtr.Zero && depth < MaxScintillaParentDepth; depth++)
            {
                if (IsScintillaWindow(window))
                {
                    return TryReadScintillaSelectedText(window);
                }

                window = GetParent(window);
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsScintillaWindow(IntPtr window)
    {
        StringBuilder className = new(ScintillaClassNameCapacity);
        int length = GetClassName(window, className, className.Capacity);
        return length > 0 &&
            className.ToString().StartsWith("Scintilla", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadScintillaSelectedText(IntPtr scintillaWindow)
    {
        long selectedByteLength = SendMessage(scintillaWindow, SciGetSelText, IntPtr.Zero, IntPtr.Zero).ToInt64();
        if (selectedByteLength <= 0 || selectedByteLength > MaxScintillaSelectionBytes)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(scintillaWindow, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        IntPtr process = OpenProcess(
            ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryLimitedInformation,
            false,
            processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        IntPtr remoteBuffer = IntPtr.Zero;
        try
        {
            int bufferSize = checked((int)selectedByteLength + 2);
            remoteBuffer = VirtualAllocEx(
                process,
                IntPtr.Zero,
                (UIntPtr)bufferSize,
                MemCommit | MemReserve,
                PageReadWrite);
            if (remoteBuffer == IntPtr.Zero)
            {
                return null;
            }

            _ = SendMessage(scintillaWindow, SciGetSelText, IntPtr.Zero, remoteBuffer);

            byte[] buffer = new byte[bufferSize];
            if (!ReadProcessMemory(process, remoteBuffer, buffer, buffer.Length, out IntPtr bytesRead) ||
                bytesRead == IntPtr.Zero)
            {
                return null;
            }

            int readLength = Math.Min(buffer.Length, checked((int)bytesRead.ToInt64()));
            int textLength = Array.IndexOf(buffer, (byte)0, 0, readLength);
            if (textLength < 0)
            {
                textLength = readLength;
            }

            if (textLength <= 0)
            {
                return null;
            }

            int codePage = SendMessage(scintillaWindow, SciGetCodePage, IntPtr.Zero, IntPtr.Zero).ToInt32();
            string normalizedText = TextSanitizer.NormalizeForTranslation(DecodeScintillaText(buffer, textLength, codePage));
            return TextSanitizer.IsUsefulForTranslation(normalizedText) ? normalizedText : null;
        }
        finally
        {
            if (remoteBuffer != IntPtr.Zero)
            {
                _ = VirtualFreeEx(process, remoteBuffer, UIntPtr.Zero, MemRelease);
            }

            _ = CloseHandle(process);
        }
    }

    private static string DecodeScintillaText(byte[] buffer, int length, int codePage)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        if (TryDecodeWithWindowsCodePage(buffer, length, codePage > 0 ? (uint)codePage : CodePageAnsi, out string decodedText))
        {
            return decodedText;
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static bool TryDecodeWithWindowsCodePage(byte[] buffer, int length, uint codePage, out string decodedText)
    {
        decodedText = string.Empty;

        try
        {
            int charCount = MultiByteToWideChar(codePage, 0, buffer, length, null, 0);
            if (charCount <= 0)
            {
                return false;
            }

            char[] chars = new char[charCount];
            int written = MultiByteToWideChar(codePage, 0, buffer, length, chars, chars.Length);
            if (written <= 0)
            {
                return false;
            }

            decodedText = new string(chars, 0, written);
            return true;
        }
        catch
        {
            return false;
        }
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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int nSize,
        out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int MultiByteToWideChar(
        uint codePage,
        uint dwFlags,
        byte[] lpMultiByteStr,
        int cbMultiByte,
        [Out] char[]? lpWideCharStr,
        int cchWideChar);

    private const uint SciGetSelText = 2161;
    private const uint SciGetCodePage = 2137;
    private const uint CodePageAnsi = 0;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyC = 0x43;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

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
