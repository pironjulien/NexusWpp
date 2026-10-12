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

    // Wakes the visibility check as soon as a top-level window changes, instead of
    // waiting for the next poll. Callbacks run on the creating (UI) thread.
    internal sealed class WindowChangeWatcher : IDisposable
    {
        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

        private const uint ForegroundEvent = 0x0003, MinimizeStart = 0x0016, MinimizeEnd = 0x0017;
        private const uint ObjectDestroy = 0x8001, ObjectLocationChange = 0x800B;
        private const uint OutOfContext = 0, SkipOwnProcess = 2;
        private const int WindowObject = 0;
        private readonly WinEventProc callback;
        private readonly Action onChange;
        private readonly Func<bool> shouldWatch;
        private readonly List<IntPtr> hooks = new List<IntPtr>();

        // shouldWatch keeps drag-time location events free while the wallpaper runs.
        internal WindowChangeWatcher(Action onChange, Func<bool> shouldWatch)
        {
            this.onChange = onChange;
            this.shouldWatch = shouldWatch;
            callback = OnEvent;
            Add(ForegroundEvent, ForegroundEvent);
            Add(MinimizeStart, MinimizeEnd);
            Add(ObjectDestroy, ObjectLocationChange);
        }

        private void Add(uint min, uint max)
        {
            IntPtr hook = SetWinEventHook(min, max, IntPtr.Zero, callback, 0, 0, OutOfContext | SkipOwnProcess);
            if (hook != IntPtr.Zero) hooks.Add(hook);
        }

        private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != WindowObject || hwnd == IntPtr.Zero || !shouldWatch()) return;
            onChange();
        }

        public void Dispose()
        {
            foreach (IntPtr hook in hooks) UnhookWinEvent(hook);
            hooks.Clear();
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
        private static readonly Dictionary<uint, string> ProcessNames = new Dictionary<uint, string>();
        private static readonly HashSet<string> NonOpaqueShellProcesses = new HashSet<string>
        {
            "textinputhost", "tabtip", "shellexperiencehost", "searchhost", "startmenuexperiencehost"
        };

        // Process.GetProcessById on every poll is costly; pids are reused rarely
        // and the cache is bounded, so a stale name only delays one decision.
        private static string GetProcessName(uint processId)
        {
            string name;
            if (ProcessNames.TryGetValue(processId, out name)) return name;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    name = process.ProcessName.ToLowerInvariant();
            }
            catch (ArgumentException) { name = null; }
            catch (InvalidOperationException) { name = null; }
            catch (System.ComponentModel.Win32Exception) { name = null; }
            if (name == null) return null;
            if (ProcessNames.Count > 512) ProcessNames.Clear();
            ProcessNames[processId] = name;
            return name;
        }

        internal static bool ShouldSuspend(IntPtr host, out string reason)
        {
            Screen[] screens = Screen.AllScreens;
            var coverage = new DesktopCoverage(screens.Select(s => s.WorkingArea));
            string foundReason = null;
            var coverers = new List<string>();
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
                string name = GetProcessName(processId);
                if (name == null || NonOpaqueShellProcesses.Contains(name)) return true;
                // A fullscreen window on one monitor must not pause a visible second monitor.
                coverage.Cover(window);
                coverers.Add(name + "/" + cls + " " + window.Width + "x" + window.Height + "@" + window.Left + "," + window.Top);
                if (coverage.IsCovered)
                {
                    foundReason = "desktop covered on all monitors by " + string.Join("; ", coverers.ToArray());
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            reason = foundReason ?? "desktop visible";
            return foundReason != null;
        }
    }
}
