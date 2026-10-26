using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyTitle("Taskbar Auto-Hide")]
[assembly: AssemblyProduct("Taskbar Auto-Hide")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Achintya Giridhar Kini (https://achintya.design/)")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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

    const uint ABM_GETSTATE    = 0x04;
    const uint ABM_SETSTATE    = 0x0A;
    const uint ABS_AUTOHIDE    = 0x01;
    const uint ABS_ALWAYSONTOP = 0x02;

    static APPBARDATA NewData(IntPtr taskbar)
    {
        var data = new APPBARDATA();
        data.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));
        data.hWnd = taskbar;
        return data;
    }

    static void SetTaskbarState(IntPtr taskbar, uint state)
    {
        APPBARDATA data = NewData(taskbar);
        data.lParam = (IntPtr)state;
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    static bool IsAutoHideOn(IntPtr taskbar)
    {
        APPBARDATA data = NewData(taskbar);
        return ((uint)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    // Returns 0 if auto-hide ended up on, 1 if it didn't. The exit code shows in
    // Task Scheduler's history as the task's last run result.
    static int Main()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // Look the taskbar up each time, since Explorer may restart after waking.
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
            {
                Thread.Sleep(2000);
                continue;
            }

            SetTaskbarState(taskbar, ABS_ALWAYSONTOP);
            Thread.Sleep(500);
            SetTaskbarState(taskbar, ABS_AUTOHIDE | ABS_ALWAYSONTOP);
            Thread.Sleep(500);

            if (IsAutoHideOn(taskbar)) return 0;
        }
        return 1;
    }
}