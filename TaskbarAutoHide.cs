using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyTitle("Taskbar Auto-Hide")]
[assembly: AssemblyProduct("Taskbar Auto-Hide")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Achintya Giridhar Kini (https://achintya.design/)")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    const uint ABM_SETSTATE    = 0x0A;
    const uint ABS_AUTOHIDE    = 0x01;
    const uint ABS_ALWAYSONTOP = 0x02;

    static void SetTaskbarState(IntPtr taskbar, uint state)
    {
        var data = new APPBARDATA();
        data.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));
        data.hWnd = taskbar;
        data.lParam = (IntPtr)state;
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    static void Main()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;

        SetTaskbarState(taskbar, ABS_ALWAYSONTOP);
        Thread.Sleep(500);
        SetTaskbarState(taskbar, ABS_AUTOHIDE | ABS_ALWAYSONTOP);
    }
}
