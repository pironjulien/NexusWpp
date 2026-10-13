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
    // WebView2 creation, page wiring and host messages.
    public partial class DesktopForm
    {
        private async void InitializeWebViewAsync()
        {
            if (webViewInitialized) return;
            webViewInitialized = true;
            webViewInitializing = true;

            try
            {
                string userDataFolder = Program.GetWebViewUserDataFolder();
                Directory.CreateDirectory(userDataFolder);

                if (webView == null || webView.IsDisposed)
                {
                    CreateWebViewControl();
                }

                if (webViewEnvironment == null)
                {
                    var options = new CoreWebView2EnvironmentOptions();
                    // CalculateNativeWinOcclusion: Chromium would otherwise mark the wallpaper hidden whenever windows
                    // cover the whole desktop (two snapped halves) and drop its frame, flashing black when one closes.
                    // NexusWpp pauses its own work for covered desktops; the composed scene must stay.
                    options.AdditionalBrowserArguments = "--disable-features=EdgeSidebar,EdgeTranslate,CalculateNativeWinOcclusion";
                    webViewEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                }

                if (isClosing || IsDisposed || webView == null || webView.IsDisposed) return;

                await webView.EnsureCoreWebView2Async(webViewEnvironment);
                if (isClosing || IsDisposed || webView == null || webView.IsDisposed) return;

                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
                HardenWebView(webView.CoreWebView2);

                // Configure virtual host mapping to serve files from local directory without HTTP server
                string directory = Path.GetDirectoryName(htmlPath);
                if (string.IsNullOrEmpty(directory)) directory = AppDomain.CurrentDomain.BaseDirectory;
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "nexuswpp.local",
                    directory,
                    CoreWebView2HostResourceAccessKind.DenyCors
                );

                webView.CoreWebView2.WebMessageReceived += (s, ev) =>
                {
                    try
                    {
                        if (isClosing || IsDisposed) return;

                        if (ev.Source != "http://nexuswpp.local/index.html") return;

                        string msg = ev.TryGetWebMessageAsString();
                        if (msg != null && !msg.StartsWith("BOUNDS:"))
                        {
                            Program.LogDebug(msg);
                        }
                        
                        if (msg != null)
                        {
                            if (msg.StartsWith("BOUNDS:"))
                            {
                                string[] parts = msg.Substring(7).Split(',');
                                if (parts.Length >= 4)
                                {
                                    double scale = activeInstance.GetDpiScale();
                                    double webViewScale;
                                    if (parts.Length >= 5 &&
                                        double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out webViewScale) &&
                                        webViewScale > 0.0)
                                    {
                                        scale = webViewScale;
                                    }
                                    int left = (int)Math.Round(double.Parse(parts[0], CultureInfo.InvariantCulture) * scale);
                                    int top = (int)Math.Round(double.Parse(parts[1], CultureInfo.InvariantCulture) * scale);
                                    int right = (int)Math.Round(double.Parse(parts[2], CultureInfo.InvariantCulture) * scale);
                                    int bottom = (int)Math.Round(double.Parse(parts[3], CultureInfo.InvariantCulture) * scale);
                                    remotePanelBounds = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
                                    
                                    FindRenderWindow();
                                }
                            }
                            else if (msg.StartsWith("SET_POWER:"))
                            {
                                string guidStr = msg.Substring(10);
                                SetPowerPlanFromUi(guidStr);
                            }
                            else if (msg == "REQUEST_RUNTIME_STATE")
                            {
                                // The page installs its listener before requesting state.
                                // Also covers navigation/recovery while the desktop is covered.
                                FullscreenTimer_Tick(null, null);
                                ApplyWebViewRuntimeState();
                                PostSettings();
                                PostLayout();
                            }
                            else if (msg == "REQUEST_TELEMETRY")
                            {
                                TelemetryTimer_Tick(null, null);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.LogDebug(string.Format("WebMessageReceived Error: {0}", ex.ToString()));
                    }
                };

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    (function() {
                        window.onerror = function(message, source, lineno, colno, error) {
                            try { window.chrome.webview.postMessage('ERROR: ' + message + ' at ' + source + ':' + lineno); } catch(e) {}
                        };
                        const origErr = console.error;
                        console.error = function(...args) {
                            try {
                                origErr.apply(console, args);
                                window.chrome.webview.postMessage('CONSOLE_ERROR: ' + args.join(' '));
                            } catch(e) {}
                        };
                    })();
                ");

                // Load via virtual host
                webView.CoreWebView2.NavigationStarting += (sender, navigation) => {
                    if (navigation.Uri != "http://nexuswpp.local/index.html") navigation.Cancel = true;
                };
                webView.CoreWebView2.NewWindowRequested += (sender, request) => { request.Handled = true; };
                webView.Source = new Uri("http://nexuswpp.local/index.html");

                StartRuntimeServices();
                webViewStartedOnce = true;

                if (!telemetryReady || telemetryCollector == null)
                {
                    System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                    {
                        try
                        {
                            var collector = new TelemetryCollector();
                            collector.Initialize();
                            // Warm-up sample (discarded): first WMI queries and counter baselines are slow,
                            // so the first visible frame gets fresh values even after a covered start.
                            try { collector.CollectStats(); }
                            catch (Exception ex) { Program.LogDebug("Telemetry warm-up error: " + ex.Message); }
                            TryBeginInvoke((MethodInvoker)delegate
                            {
                                telemetryCollector = collector;
                                telemetryReady = true;
                                if (!runtimeSuspended)
                                {
                                    TelemetryTimer_Tick(null, null);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Program.LogDebug("Telemetry initialization error: " + ex.Message);
                        }
                    });
                }
                else if (!runtimeSuspended)
                {
                    TelemetryTimer_Tick(null, null);
                }
            }
            catch (Exception ex)
            {
                if (isClosing || IsDisposed) return;

                Program.LogDebug("WebView2 Runtime failed to initialize: " + ex.ToString());
                if (webViewStartedOnce)
                {
                    // A recovery (browser process restart) can fail transiently: retry with back-off.
                    webViewEnvironment = null;
                    webViewRecoveryPending = false;
                    QueueWebViewRecovery("failed WebView2 re-initialization");
                    recoveryRetryQueued = webViewRecoveryPending;
                    return;
                }
                if (IsWebView2RuntimeMissing(ex)) ShowWebView2RuntimeMissing();
                BeginCleanShutdown();
                // Without WebView2 the form would cover the desktop in grey and hold the
                // single-instance mutex, blocking any relaunch: exit instead.
                try { BeginInvoke((MethodInvoker)Close); } catch (InvalidOperationException) { Application.Exit(); }
            }
            finally
            {
                webViewInitializing = false;
                // A retry queued from the catch above owns the flag again.
                if (!recoveryRetryQueued) webViewRecoveryPending = false;
                recoveryRetryQueued = false;
            }
        }

        private bool webViewStartedOnce;
        private bool recoveryRetryQueued;

        internal enum ProcessFailureAction { LogOnly, Reload, RecreateEnvironment }

        // Only a lost browser process needs a new environment. GPU/utility helpers are
        // restarted by WebView2 itself: recreating the control there would discard the
        // scene and the 60 s histories for nothing.
        internal static ProcessFailureAction ClassifyProcessFailure(CoreWebView2ProcessFailedKind kind)
        {
            switch (kind)
            {
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    return ProcessFailureAction.RecreateEnvironment;
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                    return ProcessFailureAction.Reload;
                default:
                    return ProcessFailureAction.LogOnly;
            }
        }

        // The page is a local, read-only dashboard: no browser UI, no downloads, no permissions,
        // no external schemes, no sub-frames and no form or password memory.
        private static void HardenWebView(CoreWebView2 core)
        {
            CoreWebView2Settings settings = core.Settings;
            TrySetting(() => settings.AreDefaultContextMenusEnabled = false);
#if DEBUG
            TrySetting(() => settings.AreDevToolsEnabled = true);
#else
            TrySetting(() => settings.AreDevToolsEnabled = false);
#endif
            TrySetting(() => settings.AreBrowserAcceleratorKeysEnabled = false);
            TrySetting(() => settings.IsStatusBarEnabled = false);
            TrySetting(() => settings.IsZoomControlEnabled = false);
            TrySetting(() => settings.IsPinchZoomEnabled = false);
            TrySetting(() => settings.IsSwipeNavigationEnabled = false);
            TrySetting(() => settings.IsGeneralAutofillEnabled = false);
            TrySetting(() => settings.IsPasswordAutosaveEnabled = false);
            TrySetting(() => settings.AreHostObjectsAllowed = false);

            core.PermissionRequested += (sender, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
            core.DownloadStarting += (sender, e) => { e.Cancel = true; e.Handled = true; };
            core.FrameNavigationStarting += (sender, e) => { e.Cancel = true; };
            try { core.LaunchingExternalUriScheme += (sender, e) => { e.Cancel = true; }; }
            catch (Exception ex) { Program.LogDebug("External scheme guard unavailable: " + ex.Message); }
        }

        // Older runtimes lack some settings interfaces: skip those instead of failing startup.
        private static void TrySetting(Action apply)
        {
            try { apply(); }
            catch (Exception ex) { Program.LogDebug("WebView2 setting unavailable: " + ex.Message); }
        }

        private static bool IsWebView2RuntimeMissing(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (current is WebView2RuntimeNotFoundException) return true;
            }
            return false;
        }

        private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

        // Without the runtime, nothing can be drawn: say so instead of exiting silently.
        private static void ShowWebView2RuntimeMissing()
        {
            bool french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
            string text = french
                ? "NexusWpp a besoin de Microsoft Edge WebView2 Runtime, qui n'est pas installé ou ne démarre pas.\r\n\r\n" +
                  "Ce composant Microsoft est gratuit (version « Evergreen »). Ouvrir la page de téléchargement ?\r\n\r\n" + WebView2DownloadUrl
                : "NexusWpp needs the Microsoft Edge WebView2 Runtime, which is missing or cannot start.\r\n\r\n" +
                  "This Microsoft component is free (\"Evergreen\" version). Open the download page?\r\n\r\n" + WebView2DownloadUrl;
            try
            {
                if (MessageBox.Show(text, "NexusWpp", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(WebView2DownloadUrl) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("WebView2 runtime message failed: " + ex.Message);
            }
        }

        private void CoreWebView2_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (isClosing || IsDisposed || e == null) return;
            CoreWebView2ProcessFailedKind kind = e.ProcessFailedKind;
            ProcessFailureAction action = ClassifyProcessFailure(kind);
            Program.LogDebug("WebView2 process failed: " + kind + " (" + action + ").");

            if (action == ProcessFailureAction.Reload)
            {
                try
                {
                    CoreWebView2 core = sender as CoreWebView2;
                    if (core == null) { TryGetCoreWebView2(out core); }
                    if (core != null) { core.Reload(); return; }
                }
                catch (Exception ex)
                {
                    Program.LogDebug("WebView2 reload after renderer failure failed: " + ex.Message);
                }
                QueueWebViewRecovery("renderer failure (" + kind + ")");
            }
            else if (action == ProcessFailureAction.RecreateEnvironment)
            {
                webViewEnvironment = null;
                QueueWebViewRecovery("process failure (" + kind + ")");
            }
        }
    }
}
