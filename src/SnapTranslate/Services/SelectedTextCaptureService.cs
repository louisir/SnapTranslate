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
    private const int ClipboardSettleWindowMs = 240;
    private const int DirectSelectionProbeCount = 4;
    private const int DirectSelectionProbeDelayMs = 80;
    private const int MaxSelectionTextLength = 4000;
    private const int MaxScintillaSelectionBytes = (MaxSelectionTextLength * 4) + 8;
    private const int ScintillaContextBytes = 192;
    private const int MaxScintillaParentDepth = 5;
    private const int ScintillaClassNameCapacity = 128;
    private const int MaxAutomationParentDepth = 6;
    private const string ClipboardProbePrefix = "SNAPTRANSLATE_CLIPBOARD_PROBE_";

    public async Task<string?> TryCaptureSelectedTextAsync(
        CancellationToken cancellationToken,
        bool allowClipboardFallback = true,
        bool requireDirectTextForClipboardFallback = false)
    {
        string? directText = await TryCaptureDirectSelectedTextAsync(cancellationToken);
        if (!allowClipboardFallback)
        {
            return directText;
        }

        if (requireDirectTextForClipboardFallback && string.IsNullOrWhiteSpace(directText))
        {
            SelectionCaptureDiagnostics.Write(
                $"selected final=<null> direct={SelectionCaptureDiagnostics.Text(directText)} clipboard=<skipped> requireDirect=true");
            return null;
        }

        string? clipboardText = await TryCaptureClipboardSelectedTextAsync(cancellationToken);
        string? finalText = IsBetterSelectionText(clipboardText, directText) ? clipboardText : directText;
        SelectionCaptureDiagnostics.Write(
            $"selected final={SelectionCaptureDiagnostics.Text(finalText)} direct={SelectionCaptureDiagnostics.Text(directText)} clipboard={SelectionCaptureDiagnostics.Text(clipboardText)} allowClipboard={allowClipboardFallback} requireDirect={requireDirectTextForClipboardFallback}");
        return finalText;
    }

    private static async Task<string?> TryCaptureClipboardSelectedTextAsync(CancellationToken cancellationToken)
    {
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
            string? candidateText = await Task.Run(TryCaptureDirectSelectedText, cancellationToken);
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
        string? bestText = null;

        string? scintillaText = TryCaptureScintillaSelectedText();
        SelectionCaptureDiagnostics.Write($"direct.scintilla {SelectionCaptureDiagnostics.Text(scintillaText)}");
        if (IsBetterSelectionText(scintillaText, bestText))
        {
            bestText = scintillaText;
        }

        string? automationText = TryCaptureAutomationSelectedText();
        SelectionCaptureDiagnostics.Write($"direct.uia {SelectionCaptureDiagnostics.Text(automationText)}");
        if (IsBetterSelectionText(automationText, bestText))
        {
            bestText = automationText;
        }

        return bestText;
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
        DateTimeOffset stableUntil = DateTimeOffset.MinValue;
        string? bestText = null;

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
                if (IsBetterSelectionText(normalizedText, bestText))
                {
                    bestText = normalizedText;
                    stableUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(ClipboardSettleWindowMs);
                }
            }

            if (!string.IsNullOrWhiteSpace(bestText) && DateTimeOffset.UtcNow >= stableUntil)
            {
                return bestText;
            }

            await Task.Delay(ClipboardPollDelayMs, cancellationToken);
        }

        return bestText;
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
        string? bestText = null;

        try
        {
            if (!GetCursorPos(out NativePoint cursorPosition))
            {
                return TryCaptureForegroundScintillaSelectedText();
            }

            IntPtr window = WindowFromPoint(cursorPosition);
            SelectionCaptureDiagnostics.Write(
                $"scintilla.cursor {cursorPosition.X},{cursorPosition.Y} hwnd=0x{window.ToInt64():X} class={GetWindowClassName(window)}");
            for (int depth = 0; window != IntPtr.Zero && depth < MaxScintillaParentDepth; depth++)
            {
                if (IsScintillaWindow(window))
                {
                    string? text = TryReadScintillaSelectedText(window);
                    SelectionCaptureDiagnostics.Write(
                        $"scintilla.point depth={depth} hwnd=0x{window.ToInt64():X} selected={SelectionCaptureDiagnostics.Text(text)}");
                    if (IsBetterSelectionText(text, bestText))
                    {
                        bestText = text;
                    }

                    string? tokenText = TryReadScintillaTokenAtPoint(window, cursorPosition);
                    SelectionCaptureDiagnostics.Write(
                        $"scintilla.point depth={depth} hwnd=0x{window.ToInt64():X} token={SelectionCaptureDiagnostics.Text(tokenText)}");
                    if (IsBetterSelectionText(tokenText, bestText))
                    {
                        bestText = tokenText;
                    }
                }

                window = GetParent(window);
            }

            string? foregroundText = TryCaptureForegroundScintillaSelectedText();
            SelectionCaptureDiagnostics.Write($"scintilla.foreground {SelectionCaptureDiagnostics.Text(foregroundText)}");
            if (IsBetterSelectionText(foregroundText, bestText))
            {
                bestText = foregroundText;
            }
        }
        catch
        {
        }

        return bestText;
    }

    private static string? TryCaptureForegroundScintillaSelectedText()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return null;
        }

        string? bestText = null;
        _ = EnumChildWindows(
            foregroundWindow,
            (window, _) =>
            {
                if (IsScintillaWindow(window))
                {
                    string? text = TryReadScintillaSelectedText(window);
                    if (IsBetterSelectionText(text, bestText))
                    {
                        bestText = text;
                    }
                }

                return true;
            },
            IntPtr.Zero);

        return bestText;
    }

    private static bool IsScintillaWindow(IntPtr window)
    {
        string className = GetWindowClassName(window);
        return className.StartsWith("Scintilla", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowClassName(IntPtr window)
    {
        StringBuilder className = new(ScintillaClassNameCapacity);
        int length = GetClassName(window, className, className.Capacity);
        return length > 0 ? className.ToString() : string.Empty;
    }

    private static string? TryReadScintillaSelectedText(IntPtr scintillaWindow)
    {
        string? bestText = TryReadScintillaExpandedSelectionToken(scintillaWindow);
        SelectionCaptureDiagnostics.Write(
            $"scintilla.read hwnd=0x{scintillaWindow.ToInt64():X} expanded={SelectionCaptureDiagnostics.Text(bestText)}");

        long selectedByteLength = SendMessage(scintillaWindow, SciGetSelText, IntPtr.Zero, IntPtr.Zero).ToInt64();
        SelectionCaptureDiagnostics.Write(
            $"scintilla.read hwnd=0x{scintillaWindow.ToInt64():X} selectedByteLength={selectedByteLength}");
        if (selectedByteLength <= 0 || selectedByteLength > MaxScintillaSelectionBytes)
        {
            return bestText;
        }

        _ = GetWindowThreadProcessId(scintillaWindow, out uint processId);
        if (processId == 0)
        {
            return bestText;
        }

        IntPtr process = OpenProcess(
            ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryLimitedInformation,
            false,
            processId);
        if (process == IntPtr.Zero)
        {
            return bestText;
        }

        IntPtr remoteBuffer = IntPtr.Zero;
        try
        {
            int bufferSize = GetScintillaTextBufferSize(selectedByteLength);
            remoteBuffer = VirtualAllocEx(
                process,
                IntPtr.Zero,
                (UIntPtr)bufferSize,
                MemCommit | MemReserve,
                PageReadWrite);
            if (remoteBuffer == IntPtr.Zero)
            {
                return bestText;
            }

            _ = SendMessage(scintillaWindow, SciGetSelText, IntPtr.Zero, remoteBuffer);

            byte[] buffer = new byte[bufferSize];
            if (!ReadProcessMemory(process, remoteBuffer, buffer, buffer.Length, out IntPtr bytesRead) ||
                bytesRead == IntPtr.Zero)
            {
                return bestText;
            }

            int readLength = Math.Min(buffer.Length, checked((int)bytesRead.ToInt64()));
            if (readLength <= 0)
            {
                return bestText;
            }

            int codePage = SendMessage(scintillaWindow, SciGetCodePage, IntPtr.Zero, IntPtr.Zero).ToInt32();
            string normalizedText = TextSanitizer.NormalizeForTranslation(DecodeScintillaText(buffer, readLength, codePage));
            SelectionCaptureDiagnostics.Write(
                $"scintilla.read hwnd=0x{scintillaWindow.ToInt64():X} raw={SelectionCaptureDiagnostics.Text(normalizedText)} readLength={readLength} codePage={codePage}");
            if (IsBetterSelectionText(normalizedText, bestText))
            {
                bestText = normalizedText;
            }

            return bestText;
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

    private static string? TryReadScintillaExpandedSelectionToken(IntPtr scintillaWindow)
    {
        long selectionStart = SendMessage(scintillaWindow, SciGetSelectionStart, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long selectionEnd = SendMessage(scintillaWindow, SciGetSelectionEnd, IntPtr.Zero, IntPtr.Zero).ToInt64();
        if (selectionStart < 0 || selectionEnd < 0 || selectionStart == selectionEnd)
        {
            return null;
        }

        long startPosition = Math.Min(selectionStart, selectionEnd);
        long endPosition = Math.Max(selectionStart, selectionEnd);
        string? unicodeContextText = TryReadScintillaUtf16WordAroundRange(scintillaWindow, startPosition, endPosition);
        if (!string.IsNullOrWhiteSpace(unicodeContextText))
        {
            SelectionCaptureDiagnostics.Write(
                $"scintilla.expand.utf16 hwnd=0x{scintillaWindow.ToInt64():X} text={SelectionCaptureDiagnostics.Text(unicodeContextText)}");
            return unicodeContextText;
        }

        long wordStart = SendMessage(scintillaWindow, SciWordStartPosition, new IntPtr(startPosition), new IntPtr(1)).ToInt64();
        long wordEndSeed = Math.Max(startPosition, endPosition - 1);
        long wordEnd = SendMessage(scintillaWindow, SciWordEndPosition, new IntPtr(wordEndSeed), new IntPtr(1)).ToInt64();
        SelectionCaptureDiagnostics.Write(
            $"scintilla.expand hwnd=0x{scintillaWindow.ToInt64():X} selectionStart={selectionStart} selectionEnd={selectionEnd} wordStart={wordStart} wordEnd={wordEnd}");
        if (wordStart < 0 || wordEnd <= wordStart || wordEnd - wordStart > MaxScintillaSelectionBytes)
        {
            return null;
        }

        string normalizedText = TextSanitizer.NormalizeForTranslation(TryReadScintillaTextRange(scintillaWindow, wordStart, wordEnd) ?? string.Empty);
        return TextSanitizer.IsUsefulForTranslation(normalizedText) ? normalizedText : null;
    }

    private static string? TryReadScintillaTokenAtPoint(IntPtr scintillaWindow, NativePoint screenPoint)
    {
        NativePoint clientPoint = screenPoint;
        if (!ScreenToClient(scintillaWindow, ref clientPoint))
        {
            return null;
        }

        long position = SendMessage(
            scintillaWindow,
            SciPositionFromPointClose,
            new IntPtr(clientPoint.X),
            new IntPtr(clientPoint.Y)).ToInt64();
        if (position < 0)
        {
            return null;
        }

        long start = SendMessage(scintillaWindow, SciWordStartPosition, new IntPtr(position), new IntPtr(1)).ToInt64();
        long end = SendMessage(scintillaWindow, SciWordEndPosition, new IntPtr(position), new IntPtr(1)).ToInt64();
        if (start < 0 || end <= start || end - start > MaxScintillaSelectionBytes)
        {
            return null;
        }

        string normalizedText = TextSanitizer.NormalizeForTranslation(TryReadScintillaTextRange(scintillaWindow, start, end) ?? string.Empty);
        return TextSanitizer.IsUsefulForTranslation(normalizedText) ? normalizedText : null;
    }

    private static string? TryReadScintillaUtf16WordAroundRange(IntPtr scintillaWindow, long start, long end)
    {
        string? bestText = null;
        for (int parity = 0; parity <= 1; parity++)
        {
            long contextStart = Math.Max(0, start - ScintillaContextBytes);
            if (contextStart % 2 != parity)
            {
                contextStart++;
            }

            if (contextStart > start)
            {
                contextStart = Math.Max(0, start - (start % 2 == parity ? 0 : 1));
            }

            long contextEnd = end + ScintillaContextBytes;
            if (contextEnd % 2 != parity)
            {
                contextEnd++;
            }

            byte[]? bytes = TryReadScintillaBytesByCharAt(scintillaWindow, contextStart, contextEnd, stopAtNull: false);
            if (bytes is null || !LooksLikeUtf16Le(bytes, bytes.Length))
            {
                continue;
            }

            int evenLength = bytes.Length - (bytes.Length % 2);
            if (evenLength <= 0)
            {
                continue;
            }

            string text = Encoding.Unicode.GetString(bytes, 0, evenLength);
            int selectedStart = (int)Math.Clamp((start - contextStart) / 2, 0, Math.Max(0, text.Length - 1));
            int selectedEnd = (int)Math.Clamp((end - contextStart + 1) / 2, selectedStart + 1, text.Length);
            string? candidate = SelectWordAroundDecodedRange(text, selectedStart, selectedEnd);
            if (IsBetterSelectionText(candidate, bestText))
            {
                bestText = candidate;
            }
        }

        return bestText;
    }

    private static string? SelectWordAroundDecodedRange(string text, int selectedStart, int selectedEnd)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int start = Math.Clamp(selectedStart, 0, text.Length);
        int end = Math.Clamp(selectedEnd, start, text.Length);
        while (start > 0 && IsScintillaWordCharacter(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && IsScintillaWordCharacter(text[end]))
        {
            end++;
        }

        if (end <= start)
        {
            return null;
        }

        string normalizedText = TextSanitizer.NormalizeForTranslation(text[start..end]);
        return TextSanitizer.IsUsefulForTranslation(normalizedText) ? normalizedText : null;
    }

    private static bool IsScintillaWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private static string? TryReadScintillaTextRange(IntPtr scintillaWindow, long start, long end)
    {
        string? charAtText = TryReadScintillaTextRangeByCharAt(scintillaWindow, start, end);
        if (!string.IsNullOrEmpty(charAtText))
        {
            return charAtText;
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

        IntPtr remoteText = IntPtr.Zero;
        IntPtr remoteRange = IntPtr.Zero;
        try
        {
            int textBufferSize = GetScintillaTextBufferSize(end - start);
            remoteText = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)textBufferSize, MemCommit | MemReserve, PageReadWrite);
            if (remoteText == IntPtr.Zero)
            {
                return null;
            }

            int rangeSize = Marshal.SizeOf<SciTextRangeFull>();
            remoteRange = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)rangeSize, MemCommit | MemReserve, PageReadWrite);
            if (remoteRange == IntPtr.Zero)
            {
                return null;
            }

            byte[] rangeBuffer = StructureToBytes(new SciTextRangeFull
            {
                CpMin = start,
                CpMax = end,
                Text = remoteText
            });
            if (!WriteProcessMemory(process, remoteRange, rangeBuffer, rangeBuffer.Length, out _))
            {
                return null;
            }

            _ = SendMessage(scintillaWindow, SciGetTextRangeFull, IntPtr.Zero, remoteRange);

            byte[] textBuffer = new byte[textBufferSize];
            if (!ReadProcessMemory(process, remoteText, textBuffer, textBuffer.Length, out IntPtr bytesRead) ||
                bytesRead == IntPtr.Zero)
            {
                return null;
            }

            int codePage = SendMessage(scintillaWindow, SciGetCodePage, IntPtr.Zero, IntPtr.Zero).ToInt32();
            int readLength = Math.Min(textBuffer.Length, checked((int)bytesRead.ToInt64()));
            return DecodeScintillaText(textBuffer, readLength, codePage);
        }
        finally
        {
            if (remoteRange != IntPtr.Zero)
            {
                _ = VirtualFreeEx(process, remoteRange, UIntPtr.Zero, MemRelease);
            }

            if (remoteText != IntPtr.Zero)
            {
                _ = VirtualFreeEx(process, remoteText, UIntPtr.Zero, MemRelease);
            }

            _ = CloseHandle(process);
        }
    }

    private static string? TryReadScintillaTextRangeByCharAt(IntPtr scintillaWindow, long start, long end)
    {
        byte[]? buffer = TryReadScintillaBytesByCharAt(scintillaWindow, start, end, stopAtNull: false);
        if (buffer is null || buffer.Length == 0)
        {
            return null;
        }

        int codePage = SendMessage(scintillaWindow, SciGetCodePage, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return DecodeScintillaText(buffer, buffer.Length, codePage);
    }

    private static byte[]? TryReadScintillaBytesByCharAt(IntPtr scintillaWindow, long start, long end, bool stopAtNull)
    {
        if (start < 0 || end <= start || end - start > MaxScintillaSelectionBytes)
        {
            return null;
        }

        int byteLength = checked((int)(end - start));
        byte[] buffer = new byte[byteLength];
        int actualLength = 0;
        for (int offset = 0; offset < byteLength; offset++)
        {
            int value = SendMessage(scintillaWindow, SciGetCharAt, new IntPtr(start + offset), IntPtr.Zero).ToInt32();
            if (stopAtNull && value == 0)
            {
                break;
            }

            buffer[actualLength++] = (byte)(value & 0xFF);
        }

        if (actualLength <= 0)
        {
            return null;
        }

        if (actualLength == buffer.Length)
        {
            return buffer;
        }

        byte[] resized = new byte[actualLength];
        Array.Copy(buffer, resized, actualLength);
        return resized;
    }

    private static byte[] StructureToBytes<T>(T value)
        where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            Marshal.Copy(pointer, buffer, 0, size);
            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static int GetScintillaTextBufferSize(long characterCount)
    {
        long bufferSize = checked((characterCount * 4) + 8);
        return checked((int)Math.Min(bufferSize, MaxScintillaSelectionBytes + 8L));
    }

    private static string DecodeScintillaText(byte[] buffer, int length, int codePage)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        string bestText = string.Empty;

        int singleByteLength = Array.IndexOf(buffer, (byte)0, 0, length);
        if (singleByteLength < 0)
        {
            singleByteLength = length;
        }

        if (singleByteLength > 0 &&
            TryDecodeWithWindowsCodePage(buffer, singleByteLength, codePage > 0 ? (uint)codePage : CodePageAnsi, out string decodedText))
        {
            bestText = decodedText;
        }

        int utf16LeLength = GetUtf16LeTextByteLength(buffer, length, codePage);
        if (utf16LeLength > 0)
        {
            string unicodeText = Encoding.Unicode.GetString(buffer, 0, utf16LeLength);
            if (IsBetterSelectionText(unicodeText, bestText))
            {
                bestText = unicodeText;
            }
        }

        if (!string.IsNullOrEmpty(bestText))
        {
            return bestText;
        }

        return Encoding.UTF8.GetString(buffer, 0, singleByteLength);
    }

    private static int GetUtf16LeTextByteLength(byte[] buffer, int length, int codePage)
    {
        bool likelyUtf16Le = codePage == CodePageUtf16Le || LooksLikeUtf16Le(buffer, length);
        if (!likelyUtf16Le)
        {
            return 0;
        }

        int evenLength = length - (length % 2);
        for (int index = 1; index + 1 < evenLength; index++)
        {
            if (buffer[index] == 0 && buffer[index + 1] == 0)
            {
                return index % 2 == 1 ? index + 1 : index;
            }
        }

        return evenLength;
    }

    private static bool LooksLikeUtf16Le(byte[] buffer, int length)
    {
        int sampleLength = Math.Min(length, 64);
        int pairCount = sampleLength / 2;
        if (pairCount < 2)
        {
            return false;
        }

        int oddZeroCount = 0;
        for (int index = 1; index < sampleLength; index += 2)
        {
            if (buffer[index] == 0)
            {
                oddZeroCount++;
            }
        }

        return oddZeroCount >= Math.Max(2, pairCount / 2);
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
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildWindowProc lpEnumFunc, IntPtr lParam);

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
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int nSize,
        out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int MultiByteToWideChar(
        uint codePage,
        uint dwFlags,
        byte[] lpMultiByteStr,
        int cbMultiByte,
        [Out] char[]? lpWideCharStr,
        int cchWideChar);

    private const uint SciGetSelText = 2161;
    private const uint SciGetCharAt = 2007;
    private const uint SciPositionFromPointClose = 2023;
    private const uint SciGetTextRangeFull = 2039;
    private const uint SciGetCodePage = 2137;
    private const uint SciGetSelectionStart = 2143;
    private const uint SciGetSelectionEnd = 2145;
    private const uint SciWordStartPosition = 2266;
    private const uint SciWordEndPosition = 2267;
    private const uint CodePageAnsi = 0;
    private const int CodePageUtf16Le = 1200;
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

    private delegate bool EnumChildWindowProc(IntPtr window, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct SciTextRangeFull
    {
        public long CpMin;
        public long CpMax;
        public IntPtr Text;
    }

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
