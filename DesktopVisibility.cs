using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopHtmlHost
{
    // Subtract opaque windows from each monitor's usable desktop. Overlapping
    // windows must not double-count coverage or hide an uncovered second screen.
    internal sealed class DesktopCoverage
    {
        private List<Rectangle> remaining;
        private readonly bool hasScreens;

        internal DesktopCoverage(IEnumerable<Rectangle> screens)
        {
            remaining = screens.Where(r => r.Width > 0 && r.Height > 0).ToList();
            hasScreens = remaining.Count > 0;
        }

        internal bool IsCovered { get { return hasScreens && remaining.Count == 0; } }

        internal void Cover(Rectangle window)
        {
            var next = new List<Rectangle>();
            foreach (Rectangle area in remaining)
            {
                Rectangle overlap = Rectangle.Intersect(area, window);
                if (overlap.Width <= 0 || overlap.Height <= 0) { next.Add(area); continue; }
                if (overlap.Top > area.Top) next.Add(Rectangle.FromLTRB(area.Left, area.Top, area.Right, overlap.Top));
                if (overlap.Bottom < area.Bottom) next.Add(Rectangle.FromLTRB(area.Left, overlap.Bottom, area.Right, area.Bottom));
                if (overlap.Left > area.Left) next.Add(Rectangle.FromLTRB(area.Left, overlap.Top, overlap.Left, overlap.Bottom));
                if (overlap.Right < area.Right) next.Add(Rectangle.FromLTRB(overlap.Right, overlap.Top, area.Right, overlap.Bottom));
            }
            remaining = next;
        }
    }

    internal static class DesktopVisibility
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint color, out byte alpha, out uint flags);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetFrameBounds(IntPtr hwnd, int attribute, out Rect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr hwnd, int attribute, out int value, int size);
        private static readonly uint OwnProcessId = (uint)Process.GetCurrentProcess().Id;

        internal static bool ShouldSuspend(IntPtr host, out string reason)
        {
            Screen[] screens = Screen.AllScreens;
            var coverage = new DesktopCoverage(screens.Select(s => s.WorkingArea));
            string foundReason = null;
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (hwnd == host || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId == OwnProcessId) return true;
                var className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                string cls = className.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" || cls == "Button") return true;
                int cloaked;
                if (GetCloaked(hwnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                long exStyle = IntPtr.Size == 8 ? GetWindowLong64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);
                if ((exStyle & 0x20) != 0) return true; // WS_EX_TRANSPARENT
                if ((exStyle & 0x80000) != 0) // Per-pixel/partly transparent windows do not occlude the desktop.
                {
                    uint color, flags;
                    byte alpha;
                    if (!GetLayeredWindowAttributes(hwnd, out color, out alpha, out flags) ||
                        (flags & 1) != 0 || (flags & 2) == 0 || alpha != 255) return true;
                }
                Rect bounds;
                if (GetFrameBounds(hwnd, 9, out bounds, Marshal.SizeOf(typeof(Rect))) != 0 &&
                    !GetWindowRect(hwnd, out bounds)) return true;
                Rectangle window = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                if (window.Width <= 0 || window.Height <= 0) return true;

                // Shell/input surfaces can span the screen without opaque pixels.
                // Keep Explorer file windows eligible; desktop windows are excluded by class.
                try
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        string name = process.ProcessName.ToLowerInvariant();
                        if (name == "textinputhost" || name == "tabtip" ||
                            name == "shellexperiencehost" || name == "searchhost" ||
                            name == "startmenuexperiencehost") return true;
                    }
                }
                catch (ArgumentException) { return true; }
                catch (InvalidOperationException) { return true; }
                catch (System.ComponentModel.Win32Exception) { return true; }
                // A fullscreen window on one monitor must not pause a visible second monitor.
                coverage.Cover(window);
                if (coverage.IsCovered)
                {
                    foundReason = "desktop covered on all monitors";
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            reason = foundReason ?? "desktop visible";
            return foundReason != null;
        }
    }
}
