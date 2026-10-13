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
            if (mouseHookThread != null && mouseHookThread.IsAlive) return;
            Volatile.Write(ref mouseHookStopRequested, 0);
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
                MouseRoutingSnapshot routing = Volatile.Read(ref mouseRouting);
                if (routing != null)
                {
                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (routing.ScreenBounds.Contains(hookStruct.pt.x, hookStruct.pt.y) &&
                        IsWindow(routing.RenderWindow) &&
                        ShouldForwardDesktopClick(hookStruct.pt, routing) &&
                        ForwardMouseClick(routing.RenderWindow, hookStruct.pt.x, hookStruct.pt.y, (uint)wParam.ToInt32()))
                    {
                        return (IntPtr)1; // Suppress desktop selection only after successful forwarding.
                    }
                }
            }
            catch (Exception ex)
            {
                // File I/O here would stall mouse input for the whole desktop.
                Interlocked.Exchange(ref mouseHookError, ex);
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private static bool ShouldForwardDesktopClick(POINT screenPt, MouseRoutingSnapshot routing)
        {
            IntPtr hit = WindowFromPoint(screenPt);
            if (hit == IntPtr.Zero) return false;
            if (hit == routing.HostWindow || hit == routing.RenderWindow) return true;

            IntPtr current = hit;
            for (int i = 0; i < 8 && current != IntPtr.Zero; i++)
            {
                if (current == routing.HostWindow || current == routing.RenderWindow) return true;

                string cls = GetWindowClassName(current);
                if (cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView" || cls == "SysListView32")
                {
                    return true;
                }

                uint pid;
                GetWindowThreadProcessId(current, out pid);
                if (pid == currentProcessId)
                {
                    return true;
                }

                current = GetParent(current);
            }

            return false;
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
            System.Drawing.Point origin = PointToScreen(remotePanelBounds.Location);
            Volatile.Write(ref mouseRouting, new MouseRoutingSnapshot(
                new System.Drawing.Rectangle(origin, remotePanelBounds.Size), Handle, renderWindow));
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
