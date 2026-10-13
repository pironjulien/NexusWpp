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
            // While paused the hook stays uninstalled; resume installs it.
            if (!runtimeSuspended) StartMouseHook();

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

        // Recoveries closer than this apart count as one failing streak.
        private const int RecoveryStreakWindowMs = 60000;
        private int recoveryStreak;
        private int lastRecoveryTick;
        private System.Windows.Forms.Timer recoveryDelayTimer;
        private string delayedRecoveryReason;

        // First recovery is immediate; a streak backs off 1 s, 2 s, 4 s ... up to 60 s.
        internal static int RecoveryDelayMs(int streak)
        {
            if (streak <= 0) return 0;
            return streak >= 7 ? 60000 : Math.Min(60000, 1000 << (streak - 1));
        }

        private void QueueWebViewRecovery(string reason)
        {
            if (isClosing || IsDisposed || webViewRecoveryPending)
            {
                return;
            }

            webViewRecoveryPending = true;
            if (recoveryStreak > 0 && unchecked(Environment.TickCount - lastRecoveryTick) > RecoveryStreakWindowMs) recoveryStreak = 0;
            int delay = RecoveryDelayMs(recoveryStreak);
            recoveryStreak++;
            lastRecoveryTick = Environment.TickCount;
            Program.LogDebug("Scheduling WebView2 recovery after " + reason +
                (delay > 0 ? " in " + delay.ToString(CultureInfo.InvariantCulture) + " ms." : "."));

            if (!TryBeginInvoke((MethodInvoker)delegate
            {
                if (delay == 0) { RecoverWebView(reason); return; }
                if (recoveryDelayTimer == null)
                {
                    recoveryDelayTimer = new System.Windows.Forms.Timer();
                    recoveryDelayTimer.Tick += delegate
                    {
                        recoveryDelayTimer.Stop();
                        RecoverWebView(delayedRecoveryReason);
                    };
                }
                delayedRecoveryReason = reason;
                recoveryDelayTimer.Interval = delay;
                recoveryDelayTimer.Start();
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

            try
            {
                RecreateWebView(reason);
            }
            catch (Exception ex)
            {
                Program.LogDebug("WebView2 recovery failed: " + ex);
                webViewRecoveryPending = false;
            }
        }

        private void RecreateWebView(string reason)
        {
            Program.LogDebug("Recovering WebView2 after " + reason + ".");
            // An in-flight collection clears its own pending flag when it completes.
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
                    ResilientWebView2 faulted = oldWebView as ResilientWebView2;
                    if (faulted != null) faulted.LifecycleFault -= WebView_LifecycleFault;
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
            // Never touch Handle without one: that would create a new top-level window.
            if (isClosing || IsDisposed || !IsHandleCreated) return;
            try
            {
                Exception hookError = Interlocked.Exchange(ref mouseHookError, null);
                if (hookError != null) Program.LogDebug("Mouse hook error: " + hookError);
                // Explorer restarted or replaced WorkerW without destroying our window.
                if (desktopAttached && (!IsWindow(currentDesktopParent) || GetParent(Handle) != currentDesktopParent))
                {
                    RestartDesktopSearch("desktop parent lost");
                }
                if (renderWindow == IntPtr.Zero || !IsWindow(renderWindow)) FindRenderWindow();
                else PublishMouseRouting();
                string reason;
                bool fullscreen = DesktopVisibility.ShouldSuspend(this.Handle, out reason);
                bool forced = sessionInactive || displayOff;
                if (sessionInactive) { fullscreen = true; reason = "session inactive"; }
                else if (displayOff) { fullscreen = true; reason = "display off"; }
                fullscreenReason = reason;
                // Elapsed time, not call count: page requests and session events also run this check.
                if (!fullscreen) coveredPending = false;
                else if (!coveredPending) { coveredPending = true; coveredSince = Environment.TickCount; }
                bool confirmed = forced || (fullscreen &&
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
            UpdateMouseHookForRuntime(suspended);

            ApplyWebViewRuntimeState();
            Program.LogDebug(suspended ? "Runtime suspended: " + fullscreenReason + "." : "Runtime resumed: " + fullscreenReason + ".");
        }

        // No global mouse hook while paused (covered, fullscreen game, locked, display off).
        // Reinstalling on resume also re-arms a hook Windows removed after a timeout.
        private static void UpdateMouseHookForRuntime(bool suspended)
        {
            if (suspended) StopMouseHook();
            else StartMouseHook();
        }

        private void ApplyWebViewRuntimeState()
        {
            // Keep the real DOM and compositor surface attached and visible.
            // Hiding/suspending WebView2 discards the desktop surface and makes
            // its return depend on a later visibility poll and renderer wake-up.
            if (!PostWebMessageAsJsonSafe(runtimeSuspended
                    ? "{\"control\":\"SUSPEND\"}"
                    : "{\"control\":\"RESUME\"}", "WebView runtime state"))
            {
                return;
            }
            if (!runtimeSuspended)
            {
                try { TelemetryTimer_Tick(null, null); }
                catch (Exception ex) { Program.LogDebug("Telemetry after resume error: " + ex.Message); }
            }
        }
    }
}
