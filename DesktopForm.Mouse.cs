using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace DesktopHtmlHost
{
    // Low-level mouse hook forwarding clicks inside the power-plan panel to Chromium.
    public partial class DesktopForm
    {
        private static void StartMouseHook()
        {
            if (mouseHookThread != null && mouseHookThread.IsAlive)
            {
                if (Volatile.Read(ref mouseHookStopRequested) == 0) return;
                // A stop is in flight (pause then quick resume): let that pump exit first.
                int threadId = Volatile.Read(ref mouseHookThreadId);
                if (threadId != 0) PostThreadMessage(unchecked((uint)threadId), 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
                if (!mouseHookThread.Join(1000)) return;
            }
            Volatile.Write(ref mouseHookStopRequested, 0);
            pairedRenderWindow = IntPtr.Zero;
            mouseProc = HookCallback;
            mouseHookThread = new Thread(RunMouseHook);
            mouseHookThread.IsBackground = true;
            mouseHookThread.Name = "NexusWpp mouse input";
            mouseHookThread.Start();
        }

        private static void RunMouseHook()
        {
            try
            {
                // Create the queue before publishing the ID, so shutdown can always
                // wake GetMessage, including a shutdown racing with startup.
                NativeMessage message;
                PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
                Volatile.Write(ref mouseHookThreadId, unchecked((int)GetCurrentThreadId()));
                if (Volatile.Read(ref mouseHookStopRequested) != 0) return;

                hookId = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
                if (hookId == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                // Windows delivers WH_MOUSE_LL callbacks to the installing thread.
                // Keep this message pump separate from rendering and fullscreen scans.
                while (Volatile.Read(ref mouseHookStopRequested) == 0)
                {
                    int result = GetMessage(out message, IntPtr.Zero, 0, 0);
                    if (result == 0) break;
                    if (result < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref mouseHookError, ex);
            }
            finally
            {
                IntPtr installedHook = Interlocked.Exchange(ref hookId, IntPtr.Zero);
                if (installedHook != IntPtr.Zero) UnhookWindowsHookEx(installedHook);
                Volatile.Write(ref mouseHookThreadId, 0);
            }
        }

        private static void StopMouseHook()
        {
            Volatile.Write(ref mouseRouting, null);
            Volatile.Write(ref mouseHookStopRequested, 1);
            int threadId = Volatile.Read(ref mouseHookThreadId);
            if (threadId != 0) PostThreadMessage(unchecked((uint)threadId), 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Motion, wheel and other buttons pass through without allocations or UI work.
            if (nCode < 0 || (wParam != (IntPtr)WM_LBUTTONDOWN && wParam != (IntPtr)WM_LBUTTONUP))
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            try
            {
                if (RouteLeftButton((uint)wParam.ToInt32(), lParam))
                {
                    return (IntPtr)1; // Suppress desktop selection only after successful forwarding.
                }
            }
            catch (Exception ex)
            {
                // File I/O here would stall mouse input for the whole desktop.
                Interlocked.Exchange(ref mouseHookError, ex);
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // Render window that received the last forwarded button-down; hook thread only.
        // The matching button-up follows the same route wherever it is released, so
        // neither Chromium nor Explorer is left in the middle of a press or a drag.
        private static IntPtr pairedRenderWindow;

        private static bool RouteLeftButton(uint message, IntPtr lParam)
        {
            if (message == WM_LBUTTONUP)
            {
                IntPtr paired = pairedRenderWindow;
                pairedRenderWindow = IntPtr.Zero;
                if (paired == IntPtr.Zero) return false;
                MSLLHOOKSTRUCT up = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                return IsWindow(paired) && ForwardMouseClick(paired, up.pt.x, up.pt.y, message);
            }

            pairedRenderWindow = IntPtr.Zero;
            MouseRoutingSnapshot routing = Volatile.Read(ref mouseRouting);
            if (routing == null) return false;
            MSLLHOOKSTRUCT down = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            if (!routing.ScreenBounds.Contains(down.pt.x, down.pt.y) || !IsWindow(routing.RenderWindow)) return false;
            if (!ShouldForwardHit(WindowFromPoint(down.pt), routing.HostWindow, routing.RenderWindow)) return false;
            if (!ForwardMouseClick(routing.RenderWindow, down.pt.x, down.pt.y, message)) return false;
            pairedRenderWindow = routing.RenderWindow;
            return true;
        }

        // Only the wallpaper itself or the desktop layers above it. Any other window,
        // including our own settings window, file dialog or tray menu, keeps its click.
        private static bool ShouldForwardHit(IntPtr hit, IntPtr hostWindow, IntPtr renderWindow)
        {
            if (hit == IntPtr.Zero) return false;
            if (hit == renderWindow || hit == hostWindow) return true;
            if (hostWindow != IntPtr.Zero && IsChild(hostWindow, hit)) return true;
            string cls = GetWindowClassName(hit);
            return cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView" || cls == "SysListView32";
        }

        // Panel rectangle reported by the page, limited to the wallpaper client area.
        internal static System.Drawing.Rectangle ClipPanelBounds(System.Drawing.Rectangle panel, System.Drawing.Rectangle client)
        {
            System.Drawing.Rectangle clipped = System.Drawing.Rectangle.Intersect(panel, client);
            return clipped.Width > 0 && clipped.Height > 0 ? clipped : System.Drawing.Rectangle.Empty;
        }

        private static string GetWindowClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            System.Text.StringBuilder className = new System.Text.StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            return className.ToString();
        }

        private void FindRenderWindow()
        {
            try
            {
                renderWindow = IntPtr.Zero;
                EnumChildWindows(this.Handle, FindRenderWindowCallback, IntPtr.Zero);
            }
            catch { }
            PublishMouseRouting();
        }

        private void PublishMouseRouting()
        {
            if (isClosing || !IsHandleCreated || renderWindow == IntPtr.Zero || remotePanelBounds.IsEmpty)
            {
                Volatile.Write(ref mouseRouting, null);
                return;
            }
            System.Drawing.Rectangle panel = ClipPanelBounds(remotePanelBounds, ClientRectangle);
            if (panel.IsEmpty)
            {
                Volatile.Write(ref mouseRouting, null);
                return;
            }
            // Client to screen through the real window: correct below WorkerW at any origin.
            System.Drawing.Point origin = PointToScreen(panel.Location);
            Volatile.Write(ref mouseRouting, new MouseRoutingSnapshot(
                new System.Drawing.Rectangle(origin, panel.Size), Handle, renderWindow));
        }

        private static bool FindRenderWindowCallback(IntPtr hwnd, IntPtr lParam)
        {
            System.Text.StringBuilder className = new System.Text.StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() == "Chrome_RenderWidgetHostHWND")
            {
                renderWindow = hwnd;
                return false;
            }
            return true;
        }

        private static bool ForwardMouseClick(IntPtr renderWin, int x, int y, uint msg)
        {
            POINT pt = new POINT { x = x, y = y };
            if (!ScreenToClient(renderWin, ref pt)) return false;
            IntPtr lParam = (IntPtr)((pt.y << 16) | (pt.x & 0xFFFF));
            IntPtr wParam = (IntPtr)(msg == WM_LBUTTONDOWN ? 1 : 0);
            return PostMessage(renderWin, msg, wParam, lParam);
        }

        private double GetDpiScale()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppliedDPI");
                        if (val != null)
                        {
                            int dpi = Convert.ToInt32(val);
                            return dpi / 96.0;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var g = this.CreateGraphics())
                {
                    return g.DpiX / 96.0;
                }
            }
            catch
            {
                return 1.0;
            }
        }
    }
}
