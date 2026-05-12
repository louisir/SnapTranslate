using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SnapTranslate.Runtime;

internal sealed class GlobalMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int HcAction = 0;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;

    private HookProc? _hookProc;
    private IntPtr _hookId;

    public event EventHandler<GlobalMouseEventArgs>? MouseAction;

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        _hookProc = HookCallback;
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        IntPtr moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);

        _hookId = SetWindowsHookEx(WhMouseLl, _hookProc, moduleHandle, 0);
        if (_hookId == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _hookProc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HcAction)
        {
            GlobalMouseAction? action = wParam.ToInt32() switch
            {
                WmLButtonDown => GlobalMouseAction.LeftButtonDown,
                WmLButtonUp => GlobalMouseAction.LeftButtonUp,
                _ => null
            };

            if (action is not null)
            {
                MsllHookStruct hookInfo = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                MouseAction?.Invoke(this, new GlobalMouseEventArgs(action.Value, new Point(hookInfo.Point.X, hookInfo.Point.Y)));
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MsllHookStruct
    {
        public readonly NativePoint Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }
}

internal enum GlobalMouseAction
{
    LeftButtonDown,
    LeftButtonUp
}

internal sealed record GlobalMouseEventArgs(GlobalMouseAction Action, Point Position);
