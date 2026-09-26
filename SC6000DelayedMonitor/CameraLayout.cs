using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SC6000DelayedMonitor
{
    internal sealed class CameraLayout
    {
        public int Rows { get; private set; }
        public int Columns { get; private set; }
        public CameraLayout(int count)
        {
            if (count < 1 || count > 4) throw new ArgumentOutOfRangeException("count");
            Rows = count <= 2 ? 1 : 2;
            Columns = count == 1 ? 1 : 2;
        }
        public Point Cell(int zeroBasedSlot) { return new Point(zeroBasedSlot % Columns, zeroBasedSlot / Columns); }
    }

    // Use native rectangles rather than cached WinForms Bounds across process boundaries.
    // Both executables use the same DPI manifest, including before VM/WPF initialization.
    internal static class EmbeddedWindow
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        private delegate bool EnumProc(IntPtr hwnd, IntPtr arg);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr arg);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Rect rect, uint points);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        internal static void Fit(IntPtr child, IntPtr parent, bool asynchronous)
        {
            if (!IsWindow(child) || GetParent(child) != parent) return;
            Rect area, actual;
            if (!GetClientRect(parent, out area) || !GetWindowRect(child, out actual)) return;
            MapWindowPoints(IntPtr.Zero, parent, ref actual, 2);
            int width = Math.Max(0, area.Right - area.Left), height = Math.Max(0, area.Bottom - area.Top);
            if (actual.Left == 0 && actual.Top == 0 && actual.Right == width && actual.Bottom == height) return;
            // No activation/Z-order changes. A busy SDK thread must not freeze the main UI.
            SetWindowPos(child, IntPtr.Zero, 0, 0, width, height, 0x0004 | 0x0010 | (asynchronous ? 0x4000u : 0));
        }

        internal static void FitChildren(Panel panel, int processId)
        {
            if (!panel.IsHandleCreated || panel.IsDisposed) return;
            EnumChildWindows(panel.Handle, delegate(IntPtr child, IntPtr arg)
            {
                uint pid;
                GetWindowThreadProcessId(child, out pid);
                if (pid == processId && GetParent(child) == panel.Handle) Fit(child, panel.Handle, true);
                return true;
            }, IntPtr.Zero);
        }
    }
}
