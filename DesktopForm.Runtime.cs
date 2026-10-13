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
    // Runtime services: pause/resume when the desktop is covered, window watcher and WebView2 recovery.
    public partial class DesktopForm
    {
        private void StartRuntimeServices()
        {
            StartMouseHook();

            if (telemetryTimer == null)
            {
                telemetryTimer = new System.Windows.Forms.Timer();
                telemetryTimer.Interval = 1000;
                telemetryTimer.Tick += TelemetryTimer_Tick;
            }

            if (!runtimeSuspended)
            {
                telemetryTimer.Start();
            }

            if (fullscreenTimer == null)
            {
                fullscreenTimer = new System.Windows.Forms.Timer();
                fullscreenTimer.Interval = 250;
                fullscreenTimer.Tick += FullscreenTimer_Tick;
            }

            fullscreenTimer.Start();
            if (runtimeSuspended) { StartWindowWatcher(); ApplyWebViewRuntimeState(); }
        }

        // While covered, a top-level window change re-checks the desktop within ~60 ms
        // rather than waiting for the 250 ms poll.
        private void StartWindowWatcher()
        {
            if (windowWatcher != null || isClosing) return;
            if (windowChangeTimer == null)
            {
                windowChangeTimer = new System.Windows.Forms.Timer { Interval = 60 };
                windowChangeTimer.Tick += delegate
                {
                    windowChangeTimer.Stop();
                    FullscreenTimer_Tick(null, null);
                };
            }
            windowWatcher = new WindowChangeWatcher(delegate
            {
                if (windowChangeTimer != null && !windowChangeTimer.Enabled && !isClosing) windowChangeTimer.Start();
            });
        }

        private void StopWindowWatcher()
        {
            if (windowWatcher != null) { windowWatcher.Dispose(); windowWatcher = null; }
            if (windowChangeTimer != null) windowChangeTimer.Stop();
        }

        private void DisposeWindowWatcher()
        {
            StopWindowWatcher();
            if (windowChangeTimer != null) { windowChangeTimer.Dispose(); windowChangeTimer = null; }
        }

        private void QueueWebViewRecovery(string reason)
        {
            if (isClosing || IsDisposed || webViewRecoveryPending)
            {
                return;
            }

            webViewRecoveryPending = true;
            Program.LogDebug("Scheduling WebView2 recovery after " + reason + ".");

            if (!TryBeginInvoke((MethodInvoker)delegate
            {
                RecoverWebView(reason);
            }))
            {
                webViewRecoveryPending = false;
            }
        }

        private void RecoverWebView(string reason)
        {
            if (isClosing || IsDisposed)
            {
                webViewRecoveryPending = false;
                return;
            }

            Program.LogDebug("Recovering WebView2 after " + reason + ".");
            telemetryCollectPending = false;
            renderWindow = IntPtr.Zero;
            Volatile.Write(ref mouseRouting, null);

            if (telemetryTimer != null) telemetryTimer.Stop();

            WebView2 oldWebView = webView;
            webView = null;
            webViewInitialized = false;

            if (oldWebView != null)
            {
                try
                {
                    oldWebView.Disposed -= WebView_Disposed;
                    Controls.Remove(oldWebView);
                }
                catch (Exception ex)
                {
                    if (!ResilientWebView2.IsDisposedLifecycleException(ex))
                    {
                        Program.LogDebug("WebView2 recovery detach skipped: " + ex.Message);
                    }
                }

                try
                {
                    ResilientWebView2 resilientWebView = oldWebView as ResilientWebView2;
                    if (resilientWebView != null)
                    {
                        resilientWebView.BeginShutdown();
                    }

                    if (!oldWebView.IsDisposed)
                    {
                        oldWebView.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    if (!ResilientWebView2.IsDisposedLifecycleException(ex))
                    {
                        Program.LogDebug("WebView2 recovery disposal skipped: " + ex.Message);
                    }
                }
            }

            CreateWebViewControl();
            InitializeWebViewAsync();
            RestartDesktopSearch("WebView2 recovery");
        }

        private void FullscreenTimer_Tick(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            try
            {
                Exception hookError = Interlocked.Exchange(ref mouseHookError, null);
                if (hookError != null) Program.LogDebug("Mouse hook error: " + hookError);
                if (renderWindow == IntPtr.Zero || !IsWindow(renderWindow)) FindRenderWindow();
                else PublishMouseRouting();
                string reason;
                bool fullscreen = DesktopVisibility.ShouldSuspend(this.Handle, out reason);
                if (sessionInactive) { fullscreen = true; reason = "session inactive"; }
                fullscreenReason = reason;
                // Elapsed time, not call count: page requests and session events also run this check.
                if (!fullscreen) coveredPending = false;
                else if (!coveredPending) { coveredPending = true; coveredSince = Environment.TickCount; }
                bool confirmed = sessionInactive || (fullscreen &&
                    (runtimeSuspended || unchecked(Environment.TickCount - coveredSince) >= SuspendConfirmMs));
                SetRuntimeSuspended(confirmed);
            }
            catch (Exception ex)
            {
                Program.LogDebug("Fullscreen detection error: " + ex.Message);
            }
        }

        private void SetRuntimeSuspended(bool suspended)
        {
            if (runtimeSuspended == suspended) return;
            runtimeSuspended = suspended;
            telemetryGeneration++;

            if (telemetryTimer != null)
            {
                if (suspended) telemetryTimer.Stop();
                else telemetryTimer.Start();
            }
            if (suspended) StartWindowWatcher();
            else StopWindowWatcher();

            ApplyWebViewRuntimeState();
            Program.LogDebug(suspended ? "Runtime suspended: " + fullscreenReason + "." : "Runtime resumed: " + fullscreenReason + ".");
        }

        private void ApplyWebViewRuntimeState()
        {
            CoreWebView2 core;
            if (!TryGetCoreWebView2(out core)) return;
            WebView2 currentView = webView;
            try
            {
                // Keep the real DOM and compositor surface attached and visible.
                // Hiding/suspending WebView2 discards the desktop surface and makes
                // its return depend on a later visibility poll and renderer wake-up.
                core.PostWebMessageAsJson(runtimeSuspended
                    ? "{\"control\":\"SUSPEND\"}"
                    : "{\"control\":\"RESUME\"}");
                if (!runtimeSuspended) TelemetryTimer_Tick(null, null);
            }
            catch (Exception ex)
            {
                if (!isClosing && !IsDisposed && currentView == webView && !currentView.IsDisposed)
                    Program.LogDebug("WebView runtime state error: " + ex.Message);
            }
        }
    }
}
