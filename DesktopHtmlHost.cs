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
    static class Program
    {
        private static Mutex appMutex;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                bool createdNew;
                appMutex = new Mutex(true, @"Local\NexusWppDesktopHost", out createdNew);
                if (!createdNew)
                {
                    return;
                }

                // Force Process DPI Awareness to prevent Windows from virtualizing coordinates
                SetProcessDPIAware();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Set up local HTML file path. Prefer the executable folder so packaged builds work too.
                string defaultHtml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
                if (!File.Exists(defaultHtml))
                {
                    defaultHtml = @"C:\nexuswpp\index.html";
                }
                string htmlPath = args.Length > 0 ? args[0] : defaultHtml;

                Application.Run(new DesktopForm(htmlPath));
            }
            catch (Exception ex)
            {
                try
                {
                    string dir = @"C:\nexuswpp";
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(Path.Combine(dir, "crash.txt"), ex.ToString());
                }
                catch { }
            }
        }

        private static readonly object logLock = new object();
        private static void AppendLog(string filePath, string content)
        {
            try
            {
                lock (logLock)
                {
                    // Basic log rotation (5MB limit)
                    if (File.Exists(filePath))
                    {
                        FileInfo fi = new FileInfo(filePath);
                        if (fi.Length > 5 * 1024 * 1024)
                        {
                            string backupPath = filePath + ".bak";
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                            File.Move(filePath, backupPath);
                        }
                    }
                    File.AppendAllText(filePath, content);
                }
            }
            catch { }
        }

        internal static void LogDebug(string message)
        {
            string line = string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), message);
            AppendLog(@"C:\nexuswpp\webview_debug.log", line);
        }

        private static void CleanupStaleWebView2Processes()
        {
            try
            {
                string profileFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"nexuswpp\EBWebView"
                );

                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        string commandLine = Convert.ToString(process["CommandLine"] ?? "");
                        if (commandLine.IndexOf(profileFolder, StringComparison.OrdinalIgnoreCase) < 0 &&
                            commandLine.IndexOf("--webview-exe-name=nexuswpp.exe", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        try
                        {
                            process.InvokeMethod("Terminate", null);
                            Program.LogDebug("Cleaned stale Nexus WebView2 process pid=" + process["ProcessId"]);
                        }
                        catch (Exception ex)
                        {
                            Program.LogDebug("Stale WebView2 cleanup failed: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("Stale WebView2 cleanup error: " + ex.Message);
            }
        }
    }

    public class DesktopForm : Form
    {
        private WebView2 webView;
        private string htmlPath;
        private System.Windows.Forms.Timer searchTimer;
        private int retryCount = 0;
        private const int maxRetries = 300;
        private const int progmanFallbackRetries = 8;
        private bool webViewInitialized = false;
        private bool desktopAttached = false;
        private bool attachedToTemporaryParent = false;
        private IntPtr currentDesktopParent = IntPtr.Zero;
        private DateTime startupTime = DateTime.Now;

        // --- Telemetry state ---
        private System.Windows.Forms.Timer telemetryTimer;
        private TelemetryCollector telemetryCollector;
        private bool telemetryReady = false;
        private bool telemetryCollectPending = false;
        private System.Windows.Forms.Timer fullscreenTimer;
        private bool runtimeSuspended = false;
        private string fullscreenReason = "";
        private bool isClosing = false;

        // --- Static Hook and Bounds State ---
        private static DesktopForm activeInstance;
        private static IntPtr hookId = IntPtr.Zero;
        private static LowLevelMouseProc mouseProc;
        private static System.Drawing.Rectangle remotePanelBounds = System.Drawing.Rectangle.Empty;
        private static IntPtr renderWindow = IntPtr.Zero;

        // --- Win32 P/Invoke API Definitions ---

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, 
            uint Msg, 
            IntPtr wParam, 
            IntPtr lParam, 
            uint fuFlags, 
            uint uTimeout, 
            out IntPtr lpdwResult
        );

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        // Win32 Constants
        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int SW_SHOW = 5;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                return cp;
            }
        }

        public DesktopForm(string htmlPath)
        {
            activeInstance = this;
            this.htmlPath = htmlPath;

            // Configure Form to act as a stealth, borderless wallpaper container
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;

            // Start off-screen to avoid visual flashes before parenting
            this.Location = new System.Drawing.Point(-32000, -32000);
            this.Size = new System.Drawing.Size(100, 100);

            // Force handle creation
            IntPtr forceHandle = this.Handle;

            // Listen for system resolution or display setting changes
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            // Create and configure the WebView2 UI component
            webView = new ResilientWebView2();
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);

            // Warm WebView2 immediately while Explorer is still preparing the desktop layer.
            InitializeWebViewAsync();

            // Initialize and start the non-blocking GUI timer to search for WorkerW.
            searchTimer = new System.Windows.Forms.Timer();
            searchTimer.Interval = 100;
            searchTimer.Tick += SearchTimer_Tick;
            searchTimer.Start();
        }

        private void SearchTimer_Tick(object sender, EventArgs e)
        {
            if (desktopAttached && !attachedToTemporaryParent)
            {
                searchTimer.Stop();
                return;
            }

            IntPtr workerw = IntPtr.Zero;

            // 1. Signal Progman to split the desktop
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr result;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 100, out result);
            }

            // 2. Discover the target WorkerW window designed to hold the background
            EnumWindows((hwnd, lParam) =>
            {
                IntPtr shellDll = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellDll != IntPtr.Zero)
                {
                    workerw = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (workerw == IntPtr.Zero)
                    {
                        workerw = FindWindowEx(hwnd, IntPtr.Zero, "WorkerW", null);
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (workerw != IntPtr.Zero)
            {
                AttachToDesktopParent(workerw, false);
                searchTimer.Stop();
            }
            else
            {
                if (!desktopAttached && progman != IntPtr.Zero && retryCount >= progmanFallbackRetries)
                {
                    AttachToDesktopParent(progman, true);
                }

                retryCount++;
                if (retryCount >= maxRetries)
                {
                    searchTimer.Stop();
                    if (!desktopAttached)
                    {
                        Application.Exit();
                    }
                }
            }
        }

        private void AttachToDesktopParent(IntPtr parent, bool temporary)
        {
            if (parent == IntPtr.Zero || (desktopAttached && currentDesktopParent == parent))
            {
                return;
            }

            bool firstAttach = !desktopAttached;

            // Inject our WinForms application handle into the desktop wallpaper layer.
            SetParent(this.Handle, parent);

            int style = GetWindowLong(this.Handle, GWL_STYLE);
            style |= WS_CHILD;
            style &= ~WS_POPUP;
            SetWindowLong(this.Handle, GWL_STYLE, style);
            SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

            UpdateBoundsToVirtualScreen();
            MoveWindow(this.Handle, this.Left, this.Top, this.Width, this.Height, true);
            ShowWindow(this.Handle, SW_SHOW);

            desktopAttached = true;
            attachedToTemporaryParent = temporary;
            currentDesktopParent = parent;

            double elapsed = (DateTime.Now - startupTime).TotalSeconds;
            if (temporary)
            {
                Program.LogDebug(string.Format(CultureInfo.InvariantCulture, "Wallpaper attached after {0:F2}s (Progman fallback while WorkerW is not ready).", elapsed));
            }
            else if (firstAttach)
            {
                Program.LogDebug(string.Format(CultureInfo.InvariantCulture, "Wallpaper attached after {0:F2}s.", elapsed));
            }
            else
            {
                Program.LogDebug(string.Format(CultureInfo.InvariantCulture, "Wallpaper reparented to WorkerW after {0:F2}s.", elapsed));
            }
        }

        private void UpdateBoundsToVirtualScreen()
        {
            this.Left = SystemInformation.VirtualScreen.Left;
            this.Top = SystemInformation.VirtualScreen.Top;
            this.Width = SystemInformation.VirtualScreen.Width;
            this.Height = SystemInformation.VirtualScreen.Height;
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            UpdateBoundsToVirtualScreen();
            if (this.Handle != IntPtr.Zero)
            {
                MoveWindow(this.Handle, this.Left, this.Top, this.Width, this.Height, true);
            }
        }

        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            Program.LogDebug("Windows session ending: closing NexusWpp cleanly.");
            BeginCleanShutdown();
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Close();
                });
            }
            catch
            {
                Close();
            }
        }

        private void BeginCleanShutdown()
        {
            isClosing = true;
            telemetryCollectPending = false;
            runtimeSuspended = true;

            if (searchTimer != null) searchTimer.Stop();
            if (telemetryTimer != null) telemetryTimer.Stop();
            if (fullscreenTimer != null) fullscreenTimer.Stop();
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }

            if (webView != null)
            {
                try
                {
                    webView.Dock = DockStyle.None;
                    webView.Visible = false;
                    Controls.Remove(webView);
                }
                catch (Exception ex)
                {
                    Program.LogDebug("WebView detach during shutdown failed: " + ex.Message);
                }
            }
        }

        private async void InitializeWebViewAsync()
        {
            if (webViewInitialized) return;
            webViewInitialized = true;

            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "nexuswpp"
                );

                var options = new CoreWebView2EnvironmentOptions();
                options.AdditionalBrowserArguments = "--disable-features=EdgeSidebar,EdgeTranslate --disable-gpu-driver-bug-workarounds --ignore-gpu-blocklist";

                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                if (isClosing || IsDisposed || webView == null || webView.IsDisposed) return;

                await webView.EnsureCoreWebView2Async(environment);
                if (isClosing || IsDisposed || webView == null || webView.IsDisposed) return;

                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

                // Configure virtual host mapping to serve files from local directory without HTTP server
                string directory = Path.GetDirectoryName(htmlPath);
                if (string.IsNullOrEmpty(directory)) directory = @"C:\nexuswpp";
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "nexuswpp.local",
                    directory,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                webView.CoreWebView2.WebMessageReceived += (s, ev) =>
                {
                    try
                    {
                        if (isClosing || IsDisposed) return;

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
                                if (parts.Length == 4)
                                {
                                    double scale = activeInstance.GetDpiScale();
                                    int left = (int)Math.Round(int.Parse(parts[0], CultureInfo.InvariantCulture) * scale);
                                    int top = (int)Math.Round(int.Parse(parts[1], CultureInfo.InvariantCulture) * scale);
                                    int right = (int)Math.Round(int.Parse(parts[2], CultureInfo.InvariantCulture) * scale);
                                    int bottom = (int)Math.Round(int.Parse(parts[3], CultureInfo.InvariantCulture) * scale);
                                    remotePanelBounds = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
                                    
                                    FindRenderWindow();
                                }
                            }
                            else if (msg.StartsWith("SET_POWER:"))
                            {
                                string guidStr = msg.Substring(10);
                                SetPowerPlanFromUi(guidStr);
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
                webView.Source = new Uri("http://nexuswpp.local/index.html");

                // Install low-level mouse hook
                mouseProc = HookCallback;
                hookId = SetHook(mouseProc);

                telemetryTimer = new System.Windows.Forms.Timer();
                telemetryTimer.Interval = 1000;
                telemetryTimer.Tick += TelemetryTimer_Tick;
                telemetryTimer.Start();

                fullscreenTimer = new System.Windows.Forms.Timer();
                fullscreenTimer.Interval = 500;
                fullscreenTimer.Tick += FullscreenTimer_Tick;
                fullscreenTimer.Start();

                System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        var collector = new TelemetryCollector();
                        collector.Initialize();
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
            catch (Exception ex)
            {
                if (isClosing || IsDisposed) return;

                MessageBox.Show(
                    string.Format("WebView2 Runtime failed to initialize.\n\nDetails:\n{0}", ex.Message),
                    "Host Initialization Failure", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error
                );
            }
        }

        private void FullscreenTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                string reason;
                bool fullscreen = IsAnyFullscreenWindow(out reason);
                fullscreenReason = reason;
                SetRuntimeSuspended(fullscreen);
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

            if (telemetryTimer != null)
            {
                if (suspended) telemetryTimer.Stop();
                else telemetryTimer.Start();
            }

            try
            {
                PostWebMessageAsJsonSafe(suspended ? "{\"control\":\"SUSPEND\"}" : "{\"control\":\"RESUME\"}", "Runtime suspend post");
            }
            catch (Exception ex)
            {
                Program.LogDebug("Runtime suspend post error: " + ex.Message);
            }

            if (!suspended)
            {
                TelemetryTimer_Tick(null, null);
            }

            Program.LogDebug(suspended ? "Runtime suspended: fullscreen foreground detected (" + fullscreenReason + ")." : "Runtime resumed: fullscreen foreground cleared.");
        }

        private void SetPowerPlanFromUi(string guidStr)
        {
            bool success = false;
            string activeGuid = "";
            string error = "";

            try
            {
                if (telemetryCollector == null)
                {
                    error = "telemetry collector not ready";
                }
                else
                {
                    success = telemetryCollector.SetActivePowerPlan(guidStr, out activeGuid, out error);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            Program.LogDebug(string.Format(CultureInfo.InvariantCulture,
                "Power plan request: requested={0}, active={1}, success={2}, error={3}",
                guidStr,
                activeGuid,
                success,
                error));

            try
            {
                string payload = string.Format(CultureInfo.InvariantCulture,
                    "{{\"control\":\"POWER_RESULT\",\"requestedGuid\":{0},\"activeGuid\":{1},\"success\":{2},\"error\":{3}}}",
                    JsonString(guidStr),
                    JsonString(activeGuid),
                    success ? "true" : "false",
                    JsonString(error));
                PostWebMessageAsJsonSafe(payload, "Power result post");
            }
            catch (Exception ex)
            {
                Program.LogDebug("Power result post error: " + ex.Message);
            }

            TelemetryTimer_Tick(null, null);
        }

        private static string JsonString(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append(@"\r"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\t': sb.Append(@"\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private bool IsAnyFullscreenWindow(out string reason)
        {
            bool found = false;
            string foundReason = "";
            EnumWindows((hwnd, lParam) =>
            {
                if (found) return false;
                string candidateReason;
                if (IsFullscreenCandidate(hwnd, out candidateReason))
                {
                    found = true;
                    foundReason = candidateReason;
                }
                return !found;
            }, IntPtr.Zero);

            reason = foundReason;
            return found;
        }

        private bool IsFullscreenCandidate(IntPtr hwnd, out string reason)
        {
            reason = "";
            if (hwnd == IntPtr.Zero || hwnd == this.Handle || IsIconic(hwnd) || !IsWindowVisible(hwnd)) return false;

            System.Text.StringBuilder className = new System.Text.StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Button") return false;

            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) return false;
            string processName = "";
            try
            {
                Process p = Process.GetProcessById((int)pid);
                processName = p.ProcessName.ToLowerInvariant();
                if (processName == "msedgewebview2" ||
                    processName == "explorer" ||
                    processName == "textinputhost" ||
                    processName == "shellexperiencehost" ||
                    processName == "searchhost" ||
                    processName == "startmenuexperiencehost")
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return false;

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;

            MONITORINFO info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;

            int tolerance = 3;
            bool fullscreen = rect.Left <= info.rcMonitor.Left + tolerance &&
                              rect.Top <= info.rcMonitor.Top + tolerance &&
                              rect.Right >= info.rcMonitor.Right - tolerance &&
                              rect.Bottom >= info.rcMonitor.Bottom - tolerance;
            if (fullscreen)
            {
                reason = string.Format(CultureInfo.InvariantCulture, "{0}/{1} hwnd=0x{2:x}", processName, cls, hwnd.ToInt64());
            }
            return fullscreen;
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            if (runtimeSuspended) return;
            if (!telemetryReady || telemetryCollector == null || telemetryCollectPending) return;
            telemetryCollectPending = true;

            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                {
                    string statsJson = "";
                    try
                    {
                        if (isClosing || IsDisposed)
                        {
                            telemetryCollectPending = false;
                            return;
                        }
                        statsJson = telemetryCollector.CollectStats();
                        if (!TryBeginInvoke((MethodInvoker)delegate
                        {
                            try
                            {
                                PostWebMessageAsJsonSafe(statsJson, "Telemetry post");
                            }
                            catch (Exception ex)
                            {
                                Program.LogDebug("Telemetry post error: " + ex.Message + " | JSON: " + statsJson);
                            }
                            finally
                            {
                                telemetryCollectPending = false;
                            }
                        }))
                        {
                            telemetryCollectPending = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.LogDebug("Telemetry collect error: " + ex.Message + " | JSON: " + statsJson);
                        try
                        {
                            if (!TryBeginInvoke((MethodInvoker)delegate
                            {
                                telemetryCollectPending = false;
                            }))
                            {
                                telemetryCollectPending = false;
                            }
                        }
                        catch
                        {
                            telemetryCollectPending = false;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                telemetryCollectPending = false;
                Program.LogDebug("TelemetryTimer_Tick error: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            BeginCleanShutdown();
            try
            {
                Nvml.nvmlShutdown();
            }
            catch {}
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
                if (hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hookId);
                    hookId = IntPtr.Zero;
                }
                if (searchTimer != null)
                {
                    searchTimer.Dispose();
                }
                if (telemetryTimer != null)
                {
                    telemetryTimer.Dispose();
                }
                if (fullscreenTimer != null)
                {
                    fullscreenTimer.Dispose();
                }
                if (webView != null)
                {
                    webView.Dispose();
                    webView = null;
                }
            }
            base.Dispose(disposing);
        }

        private bool TryBeginInvoke(MethodInvoker action)
        {
            if (isClosing || IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private bool TryGetCoreWebView2(out CoreWebView2 coreWebView)
        {
            coreWebView = null;
            if (isClosing || IsDisposed || webView == null || webView.IsDisposed)
            {
                return false;
            }

            try
            {
                coreWebView = webView.CoreWebView2;
                return coreWebView != null;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException ex)
            {
                if (!isClosing)
                {
                    Program.LogDebug("WebView unavailable: " + ex.Message);
                }
                return false;
            }
        }

        private void PostWebMessageAsJsonSafe(string payload, string context)
        {
            CoreWebView2 coreWebView;
            if (!TryGetCoreWebView2(out coreWebView))
            {
                return;
            }

            try
            {
                coreWebView.PostWebMessageAsJson(payload);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException ex)
            {
                if (!isClosing)
                {
                    Program.LogDebug(context + " skipped: " + ex.Message);
                }
            }
        }

        private sealed class ResilientWebView2 : WebView2
        {
            protected override void OnSizeChanged(EventArgs e)
            {
                try
                {
                    base.OnSizeChanged(e);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException ex)
                {
                    Program.LogDebug("Suppressed WebView2 resize after disposal: " + ex.Message);
                }
            }
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    IntPtr hMod = GetModuleHandle(curModule.ModuleName);
                    IntPtr result = SetWindowsHookEx(WH_MOUSE_LL, proc, hMod, 0);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug(string.Format("SetHook Exception: {0}", ex.ToString()));
                return IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_LBUTTONUP))
                {
                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                    if (activeInstance != null && !remotePanelBounds.IsEmpty)
                    {
                        System.Drawing.Point screenPt = new System.Drawing.Point(hookStruct.pt.x, hookStruct.pt.y);
                        System.Drawing.Point clientPt = activeInstance.PointToClient(screenPt);

                        if (remotePanelBounds.Contains(clientPt))
                        {
                            if (!activeInstance.ShouldForwardDesktopClick(screenPt))
                            {
                                return CallNextHookEx(hookId, nCode, wParam, lParam);
                            }

                            if (renderWindow == IntPtr.Zero)
                            {
                                activeInstance.FindRenderWindow();
                            }

                            if (renderWindow != IntPtr.Zero)
                            {
                                uint msg = (uint)wParam.ToInt32();
                                ForwardMouseClick(renderWindow, hookStruct.pt.x, hookStruct.pt.y, msg);
                                return (IntPtr)1; // Swallow click to prevent desktop listview selection box/focus loss
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug(string.Format("HookCallback Exception: {0}", ex.ToString()));
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private bool ShouldForwardDesktopClick(System.Drawing.Point screenPt)
        {
            IntPtr hit = WindowFromPoint(new POINT { x = screenPt.X, y = screenPt.Y });
            if (hit == IntPtr.Zero) return false;
            if (hit == this.Handle || hit == renderWindow) return true;

            IntPtr current = hit;
            for (int i = 0; i < 8 && current != IntPtr.Zero; i++)
            {
                if (current == this.Handle || current == renderWindow) return true;

                string cls = GetWindowClassName(current);
                if (cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView" || cls == "SysListView32")
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
                EnumChildWindows(this.Handle, FindRenderWindowCallback, IntPtr.Zero);
            }
            catch { }
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

        private static void ForwardMouseClick(IntPtr renderWin, int x, int y, uint msg)
        {
            POINT pt = new POINT { x = x, y = y };
            ScreenToClient(renderWin, ref pt);
            IntPtr lParam = (IntPtr)((pt.y << 16) | (pt.x & 0xFFFF));
            IntPtr wParam = (IntPtr)(msg == WM_LBUTTONDOWN ? 1 : 0);
            PostMessage(renderWin, msg, wParam, lParam);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    public static class Win32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(
            out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime
        );

        [DllImport("powrprof.dll", EntryPoint = "PowerGetActiveScheme", SetLastError = true)]
        public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme", SetLastError = true)]
        public static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid ActiveSchemeGuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);
    }

    public static class Nvml
    {
        private const string NvmlDll = "nvml.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlUtilization_t
        {
            public uint gpu;
            public uint memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_t
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        public enum nvmlTemperatureSensors_t
        {
            NVML_TEMPERATURE_GPU = 0
        }

        public enum nvmlClockType_t
        {
            NVML_CLOCK_GRAPHICS = 0,
            NVML_CLOCK_SM = 1,
            NVML_CLOCK_MEM = 2,
            NVML_CLOCK_VIDEO = 3
        }

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlInit_v2")]
        public static extern int nvmlInit();

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetTemperature")]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, nvmlTemperatureSensors_t sensorType, out uint temp);

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetClockInfo")]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, nvmlClockType_t clockType, out uint clock);

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetName")]
        public static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlShutdown")]
        public static extern int nvmlShutdown();
    }

    public class TelemetryCollector
    {
        // Static / Boot cached specs
        public string MotherboardInfo = "";
        public string CpuInfo = "";
        public string CpuL2Cache = "";
        public string CpuL3Cache = "";
        public int CpuCores = 0;
        public int CpuLogical = 0;
        public string CpuBaseSpeed = "0.00";
        public int TotalRamGb = 0;
        public int RamSpeedMts = 0;
        public string NetworkName = "";
        public string IgpuInfo = "";
        public string IgpuDriver = "";
        public string IgpuDriverDate = "";
        public string DgpuDriver = "";
        public string DgpuDriverDate = "";
        public string GpuName = "";
        public string NpuName = "";
        public string NpuDriver = "";
        public string NpuDriverDate = "";
        public bool NpuDetected = false;
        public string DiskName = "";

        private string igpuLuid = "";
        private string dgpuLuid = "";
        private string npuLuid = "";
        private readonly List<string> directxAdapterLuids = new List<string>();
        private double cpuBaseSpeedVal = 3.80;

        // Active power plans cached list
        public class PowerPlanInfo
        {
            public string Guid;
            public string Name;
            public bool Active;
        }
        public List<PowerPlanInfo> PowerPlans = new List<PowerPlanInfo>();

        // Historical rates
        private ulong prevIdleTime = 0;
        private ulong prevSystemTime = 0;
        private long prevRxBytes = 0;
        private long prevTxBytes = 0;
        private DateTime prevNetTime = DateTime.MinValue;
        private int pingTime = -1;
        private DateTime lastPingTime = DateTime.MinValue;
        private bool isPingPending = false;

        // Process lists and cached top processes
        private string cachedTopProcessesJson = "[]";
        private int topProcessCounter = 0;
        private bool topProcessUpdatePending = false;

        // Network rates cache
        private long cachedNetRxRate = 0;
        private long cachedNetTxRate = 0;
        private string cachedActiveIp = "127.0.0.1";
        private string cachedActiveIpv6 = "fe80::1";
        private string cachedActiveNetName = "Ethernet";
        private string cachedNetType = "Ethernet";
        private long cachedNetLinkSpeedMbps = 1000;

        // Slow perf counters cached separately so the UI can receive 2Hz payloads
        // without running every WMI query twice per second.
        private DateTime lastMemoryPerfTime = DateTime.MinValue;
        private long cachedRamCachedBytes = 0;
        private long cachedRamPoolPagedBytes = 0;
        private long cachedRamPoolNonPagedBytes = 0;
        private long cachedRamActivityVal = 0;
        private DateTime lastSystemPerfTime = DateTime.MinValue;
        private int cachedThreadsCount = 1200;
        private int cachedProcessesCount = 180;
        private DateTime lastGpuPerfTime = DateTime.MinValue;
        private int cachedIgpuUtil = 0;
        private long cachedIgpuMemBytes = 0;
        private int cachedNpuUtil = 0;
        private long cachedNpuMemBytes = 0;
        private int cachedDgpuUtil = 0;
        public void Initialize()
        {
            // Read GPUs AdapterLuid and names from Registry
            try
            {
                using (var rootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX"))
                {
                    if (rootKey != null)
                    {
                        foreach (var subkeyName in rootKey.GetSubKeyNames())
                        {
                            using (var subkey = rootKey.OpenSubKey(subkeyName))
                            {
                                if (subkey != null)
                                {
                                    object descVal = subkey.GetValue("Description");
                                    string desc = descVal != null ? descVal.ToString() : "";
                                    object luidValObj = subkey.GetValue("AdapterLuid");
                                    if (luidValObj != null && !string.IsNullOrEmpty(desc))
                                    {
                                        long luidVal = Convert.ToInt64(luidValObj);
                                        uint low = (uint)(luidVal & 0xFFFFFFFF);
                                        uint high = (uint)((luidVal >> 32) & 0xFFFFFFFF);
                                        string formattedLuid = string.Format("0x{0:x8}_0x{1:x8}", high, low);
                                        if (!directxAdapterLuids.Contains(formattedLuid))
                                        {
                                            directxAdapterLuids.Add(formattedLuid);
                                        }
                                        
                                        if (desc.ToLower().Contains("nvidia"))
                                        {
                                            dgpuLuid = formattedLuid;
                                        }
                                        else if (desc.ToLower().Contains("intel") || desc.ToLower().Contains("uhd") || desc.ToLower().Contains("arc"))
                                        {
                                            igpuLuid = formattedLuid;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("DirectX Registry read error: " + ex.Message);
            }

            // Query CPU, Motherboard, RAM, GPU from WMI
            try
            {
                // Motherboard
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object mfgVal = obj["Manufacturer"];
                        object prodVal = obj["Product"];
                        string mfg = mfgVal != null ? mfgVal.ToString() : "";
                        string prod = prodVal != null ? prodVal.ToString() : "";
                        mfg = mfg.Replace(" Co., Ltd.", "").Replace("ASUSTeK COMPUTER INC.", "ASUS");
                        MotherboardInfo = string.Format("{0} ({1})", mfg, prod);
                        break;
                    }
                }

                // CPU
                using (var searcher = new ManagementObjectSearcher("SELECT Name, L2CacheSize, L3CacheSize, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object cpuNameVal = obj["Name"];
                        if (cpuNameVal != null) CpuInfo = cpuNameVal.ToString().Trim();
                        uint l2 = (uint)(obj["L2CacheSize"] ?? 0);
                        uint l3 = (uint)(obj["L3CacheSize"] ?? 0);
                        CpuL2Cache = string.Format("{0:F1} Mo", l2 / 1024.0);
                        CpuL3Cache = string.Format("{0:F1} Mo", l3 / 1024.0);
                        CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? CpuCores);
                        CpuLogical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? CpuLogical);
                        uint maxClock = (uint)(obj["MaxClockSpeed"] ?? 0);
                        CpuBaseSpeed = (maxClock / 1000.0).ToString("F2", CultureInfo.InvariantCulture);
                        break;
                    }
                }
                double.TryParse(CpuBaseSpeed, NumberStyles.Any, CultureInfo.InvariantCulture, out cpuBaseSpeedVal);

                // RAM
                using (var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory"))
                {
                    ulong totalCapacity = 0;
                    uint speed = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        totalCapacity += (ulong)(obj["Capacity"] ?? 0);
                        speed = (uint)(obj["Speed"] ?? speed);
                    }
                    if (totalCapacity > 0)
                    {
                        TotalRamGb = (int)Math.Round(totalCapacity / (1024.0 * 1024.0 * 1024.0));
                    }
                    if (speed > 0)
                    {
                        RamSpeedMts = (int)speed;
                    }
                }

                // GPUs
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object nameVal = obj["Name"];
                        object driverVerVal = obj["DriverVersion"];
                        object dateRawVal = obj["DriverDate"];
                        string name = nameVal != null ? nameVal.ToString() : "";
                        string driverVer = driverVerVal != null ? driverVerVal.ToString() : "";
                        string dateRaw = dateRawVal != null ? dateRawVal.ToString() : "";
                        string driverDate = "";
                        if (dateRaw.Length >= 8)
                        {
                            try
                            {
                                int y = int.Parse(dateRaw.Substring(0, 4));
                                int m = int.Parse(dateRaw.Substring(4, 2));
                                int d = int.Parse(dateRaw.Substring(6, 2));
                                driverDate = string.Format("{0:D2}/{1:D2}/{2:D4}", d, m, y);
                            }
                            catch {}
                        }

                        if (name.ToLower().Contains("nvidia"))
                        {
                            GpuName = name;
                            DgpuDriver = driverVer;
                            DgpuDriverDate = driverDate;
                        }
                        else if (name.ToLower().Contains("intel") || name.ToLower().Contains("arc") || name.ToLower().Contains("uhd"))
                        {
                            IgpuInfo = name;
                            IgpuDriver = driverVer;
                            IgpuDriverDate = driverDate;
                        }
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceName LIKE '%AI Boost%' OR DeviceName LIKE '%Neural%' OR DeviceName LIKE '%VPU%' OR DeviceName LIKE '%Inference%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object nameVal = obj["DeviceName"];
                        object driverVerVal = obj["DriverVersion"];
                        object dateRawVal = obj["DriverDate"];
                        string name = nameVal != null ? nameVal.ToString() : "";
                        if (!IsLikelyNpuDeviceName(name))
                        {
                            continue;
                        }

                        NpuName = name;
                        NpuDetected = true;
                        NpuDriver = driverVerVal != null ? driverVerVal.ToString() : "";

                        string dateRaw = dateRawVal != null ? dateRawVal.ToString() : "";
                        if (dateRaw.Length >= 8)
                        {
                            try
                            {
                                int y = int.Parse(dateRaw.Substring(0, 4));
                                int m = int.Parse(dateRaw.Substring(4, 2));
                                int d = int.Parse(dateRaw.Substring(6, 2));
                                NpuDriverDate = string.Format("{0:D2}/{1:D2}/{2:D4}", d, m, y);
                            }
                            catch {}
                        }
                        break;
                    }
                }

                try
                {
                    string partitionDeviceId = "";
                    using (var searcher = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='C:'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object deviceId = obj["DeviceID"];
                            if (deviceId != null)
                            {
                                partitionDeviceId = deviceId.ToString().Replace("\\", "\\\\").Replace("'", "\\'");
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(partitionDeviceId))
                    {
                        string query = string.Format(CultureInfo.InvariantCulture, "ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{0}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition", partitionDeviceId);
                        using (var searcher = new ManagementObjectSearcher(query))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                object model = obj["Model"];
                                if (model != null)
                                {
                                    DiskName = model.ToString().Trim();
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(DiskName))
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                object model = obj["Model"];
                                if (model != null)
                                {
                                    DiskName = model.ToString().Trim();
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug("Disk name detection error: " + ex.Message);
                }

                if (!NpuDetected)
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%AI Boost%' OR Name LIKE '%Neural%' OR Name LIKE '%VPU%' OR Name LIKE '%Inference%'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object nameVal = obj["Name"];
                            string name = nameVal != null ? nameVal.ToString() : "";
                            if (IsLikelyNpuDeviceName(name))
                            {
                                NpuName = name;
                                NpuDetected = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("Telemetry init error: " + ex.Message);
            }

            // Initialize NVML
            try
            {
                int nvmlRes = Nvml.nvmlInit();
                if (nvmlRes == 0)
                {
                    Program.LogDebug("NVML initialized successfully!");
                }
                else
                {
                    Program.LogDebug("NVML initialization failed: " + nvmlRes);
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("NVML init exception: " + ex.Message);
            }

            // Initialize power plans
            UpdatePowerPlansCache();
        }

        private static bool IsLikelyNpuDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("ai boost") ||
                   lower.Contains("neural") ||
                   lower.Contains("inference") ||
                   lower.Contains(" vpu") ||
                   lower.Contains("(vpu") ||
                   lower.Equals("npu") ||
                   lower.StartsWith("npu ") ||
                   lower.Contains(" npu ") ||
                   lower.Contains("(npu");
        }

        private string DiscoverNpuLuid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%engtype_compute%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"] ?? "");
                        string luid = ExtractLuidFromCounterName(name);
                        if (string.IsNullOrEmpty(luid))
                        {
                            continue;
                        }
                        if (luid.Equals(igpuLuid, StringComparison.OrdinalIgnoreCase) ||
                            luid.Equals(dgpuLuid, StringComparison.OrdinalIgnoreCase) ||
                            directxAdapterLuids.Contains(luid))
                        {
                            continue;
                        }
                        return luid;
                    }
                }
            }
            catch {}

            return "";
        }

        private static string ExtractLuidFromCounterName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return "";
            }
            start += 5;
            int end = name.IndexOf("_phys_", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
            {
                return "";
            }
            return name.Substring(start, end - start).ToLowerInvariant();
        }

        public void UpdatePowerPlansCache()
        {
            try
            {
                var newPlans = new List<PowerPlanInfo>();
                ProcessStartInfo startInfo = new ProcessStartInfo("powercfg", "/list")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(startInfo))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"GUID.*:\s*([a-f0-9\-]+)\s*\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string guid = match.Groups[1].Value.Trim();
                            string name = match.Groups[2].Value.Trim();
                            bool active = line.Contains("*");
                            newPlans.Add(new PowerPlanInfo { Guid = guid, Name = name, Active = active });
                        }
                    }
                }
                if (newPlans.Count > 0)
                {
                    PowerPlans = newPlans;
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("UpdatePowerPlansCache error: " + ex.Message);
            }
        }

        public string GetActivePowerPlanGuid()
        {
            try
            {
                IntPtr activeGuidPtr;
                uint res = Win32.PowerGetActiveScheme(IntPtr.Zero, out activeGuidPtr);
                if (res == 0 && activeGuidPtr != IntPtr.Zero)
                {
                    Guid activeGuid = (Guid)Marshal.PtrToStructure(activeGuidPtr, typeof(Guid));
                    Win32.LocalFree(activeGuidPtr);
                    return activeGuid.ToString();
                }
            }
            catch {}

            return "";
        }

        public bool SetActivePowerPlan(string guidStr, out string activeGuid, out string error)
        {
            activeGuid = "";
            error = "";

            Guid requestedGuid;
            if (!Guid.TryParse(guidStr, out requestedGuid))
            {
                error = "invalid GUID";
                activeGuid = GetActivePowerPlanGuid();
                return false;
            }

            if (PowerPlans.Count > 0 && !PowerPlans.Any(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase)))
            {
                UpdatePowerPlansCache();
                if (!PowerPlans.Any(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "GUID not found in powercfg /list";
                    activeGuid = GetActivePowerPlanGuid();
                    return false;
                }
            }

            uint result = Win32.PowerSetActiveScheme(IntPtr.Zero, ref requestedGuid);
            if (result != 0)
            {
                error = "PowerSetActiveScheme=" + result.ToString(CultureInfo.InvariantCulture);
            }

            Thread.Sleep(120);
            activeGuid = GetActivePowerPlanGuid();
            bool success = activeGuid.Equals(guidStr, StringComparison.OrdinalIgnoreCase);
            if (success)
            {
                foreach (var plan in PowerPlans)
                {
                    plan.Active = plan.Guid.Equals(activeGuid, StringComparison.OrdinalIgnoreCase);
                }
            }
            else if (string.IsNullOrEmpty(error))
            {
                error = "active scheme did not match request";
            }

            return success;
        }

        private void UpdateNetworkStats()
        {
            try
            {
                long currentRx = 0;
                long currentTx = 0;
                string activeIp = "127.0.0.1";
                string activeIpv6 = "fe80::1";
                string activeNetName = "Ethernet";
                string netType = "Ethernet";
                long linkSpeedMbps = cachedNetLinkSpeedMbps;

                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        var ipProps = ni.GetIPProperties();
                        var stats = ni.GetIPv4Statistics();
                        currentRx += stats.BytesReceived;
                        currentTx += stats.BytesSent;
                        
                        foreach (var addr in ipProps.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                activeIp = addr.Address.ToString();
                                activeNetName = ni.Description;
                                netType = (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) ? "Wi-Fi" : "Ethernet";
                                if (ni.Speed > 0)
                                {
                                    linkSpeedMbps = Math.Max(1, ni.Speed / 1000000L);
                                }
                            }
                            else if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                            {
                                activeIpv6 = addr.Address.ToString();
                            }
                        }
                    }
                }

                if (prevNetTime != DateTime.MinValue)
                {
                    double secElapsed = (DateTime.Now - prevNetTime).TotalSeconds;
                    if (secElapsed > 0.1)
                    {
                        cachedNetRxRate = (long)Math.Max(0, (currentRx - prevRxBytes) / 1024.0 / secElapsed);
                        cachedNetTxRate = (long)Math.Max(0, (currentTx - prevTxBytes) / 1024.0 / secElapsed);
                    }
                }
                prevRxBytes = currentRx;
                prevTxBytes = currentTx;
                prevNetTime = DateTime.Now;

                cachedActiveIp = activeIp;
                cachedActiveIpv6 = activeIpv6;
                cachedActiveNetName = activeNetName;
                cachedNetType = netType;
                cachedNetLinkSpeedMbps = linkSpeedMbps;
            }
            catch {}
        }

        private void UpdatePing()
        {
            if (isPingPending) return;
            if (lastPingTime != DateTime.MinValue && (DateTime.Now - lastPingTime).TotalSeconds < 5.0) return;

            lastPingTime = DateTime.Now;
            isPingPending = true;

            try
            {
                Ping pingSender = new Ping();
                pingSender.PingCompleted += (s, e) =>
                {
                    isPingPending = false;
                    if (e.Reply != null && e.Reply.Status == IPStatus.Success)
                    {
                        pingTime = (int)e.Reply.RoundtripTime;
                    }
                    else
                    {
                        pingTime = -1;
                    }
                    try { ((Ping)s).Dispose(); } catch {}
                };
                pingSender.SendAsync("1.1.1.1", 1000, null);
            }
            catch
            {
                isPingPending = false;
            }
        }

        private void UpdateTopProcesses()
        {
            topProcessCounter++;
            if (topProcessCounter < 4) return; // run once every 4 seconds
            topProcessCounter = 0;
            if (topProcessUpdatePending) return;
            topProcessUpdatePending = true;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    var processes = new List<Tuple<string, int, long>>();
                    int cpuCount = Environment.ProcessorCount;
                    if (cpuCount <= 0) cpuCount = 1;

                    using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, PercentProcessorTime, WorkingSetPrivate FROM Win32_PerfFormattedData_PerfProc_Process WHERE Name <> '_Total' AND Name <> 'Idle'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object nameVal = obj["Name"];
                            string name = nameVal != null ? nameVal.ToString() : "";
                            int hashIndex = name.IndexOf('#');
                            if (hashIndex != -1) name = name.Substring(0, hashIndex);

                            int pct = Convert.ToInt32(obj["PercentProcessorTime"] ?? 0);
                            int cpuPercent = (int)Math.Min(99, Math.Round((double)pct / cpuCount));
                            long ramBytes = Convert.ToInt64(obj["WorkingSetPrivate"] ?? 0L);
                            long ramMb = ramBytes / (1024 * 1024);

                            processes.Add(new Tuple<string, int, long>(name, cpuPercent, ramMb));
                        }
                    }

                    var topList = processes.OrderByDescending(p => p.Item2).Take(4).ToList();

                    StringBuilder sb = new StringBuilder();
                    sb.Append("[");
                    for (int i = 0; i < topList.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{{\"Name\":{0},\"PercentProcessorTime\":{1},\"WorkingSetPrivate\":{2}}}", 
                            JsonString(topList[i].Item1), topList[i].Item2 * cpuCount, topList[i].Item3 * (long)1024 * 1024);
                    }
                    sb.Append("]");
                    
                    lock (this)
                    {
                        cachedTopProcessesJson = sb.ToString();
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug("UpdateTopProcesses error: " + ex.Message);
                }
                finally
                {
                    topProcessUpdatePending = false;
                }
            });
        }

        private static string JsonString(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append(@"\b"); break;
                    case '\f': sb.Append(@"\f"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\r': sb.Append(@"\r"); break;
                    case '\t': sb.Append(@"\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public string CollectStats()
        {
            // Gather CPU Load
            int cpuLoad = 0;
            System.Runtime.InteropServices.ComTypes.FILETIME idleTime, kernelTime, userTime;
            if (Win32.GetSystemTimes(out idleTime, out kernelTime, out userTime))
            {
                ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
                ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
                ulong user = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;
                ulong system = kernel + user;

                if (prevSystemTime > 0)
                {
                    ulong systemDiff = system - prevSystemTime;
                    ulong idleDiff = idle - prevIdleTime;
                    if (systemDiff > 0)
                    {
                        double load = 1.0 - (double)idleDiff / systemDiff;
                        cpuLoad = (int)Math.Max(0, Math.Min(100, Math.Round(load * 100)));
                    }
                }
                prevIdleTime = idle;
                prevSystemTime = system;
            }

            double sinOffset = Math.Sin(DateTime.Now.Ticks / 120000000.0);
            int cpuTemp = (int)Math.Round(38.0 + (cpuLoad * 0.35) + sinOffset * 1.5);

            // RAM usage
            int ramLoad = 0;
            ulong ramTotalPhys = 0;
            ulong ramFreePhys = 0;
            ulong ramTotalPage = 0;
            ulong ramAvailPage = 0;
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (Win32.GlobalMemoryStatusEx(memStatus))
            {
                ramLoad = (int)memStatus.dwMemoryLoad;
                ramTotalPhys = memStatus.ullTotalPhys;
                ramFreePhys = memStatus.ullAvailPhys;
                ramTotalPage = memStatus.ullTotalPageFile;
                ramAvailPage = memStatus.ullAvailPageFile;
            }

            // Disk Space
            double diskFreeGb = 0.0;
            double diskTotalGb = 0.0;
            int diskStoragePercent = 0;
            try
            {
                var drive = new DriveInfo("C");
                diskFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                diskTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                diskStoragePercent = (int)Math.Round((1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100);
            }
            catch {}

            // Network stats
            UpdateNetworkStats();

            // Query fast WMI metrics
            long commitUsedBytes = (long)(ramTotalPage - ramAvailPage);
            long commitLimitBytes = (long)ramTotalPage;
            long ramCachedBytes = cachedRamCachedBytes;
            long ramPoolPagedBytes = cachedRamPoolPagedBytes;
            long ramPoolNonPagedBytes = cachedRamPoolNonPagedBytes;
            long ramActivityVal = cachedRamActivityVal;
            if (lastMemoryPerfTime == DateTime.MinValue || (DateTime.Now - lastMemoryPerfTime).TotalSeconds >= 2.0)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT CacheBytes, StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes, StandbyCacheCoreBytes, PoolPagedBytes, PoolNonpagedBytes, PageFaultsPersec FROM Win32_PerfFormattedData_PerfOS_Memory"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            ulong cache = (ulong)(obj["CacheBytes"] ?? 0);
                            ulong standbyNormal = (ulong)(obj["StandbyCacheNormalPriorityBytes"] ?? 0);
                            ulong standbyReserve = (ulong)(obj["StandbyCacheReserveBytes"] ?? 0);
                            ulong standbyCore = (ulong)(obj["StandbyCacheCoreBytes"] ?? 0);
                            ramCachedBytes = (long)(cache + standbyNormal + standbyReserve + standbyCore);
                            ramPoolPagedBytes = (long)((ulong)(obj["PoolPagedBytes"] ?? 0));
                            ramPoolNonPagedBytes = (long)((ulong)(obj["PoolNonpagedBytes"] ?? 0));
                            ramActivityVal = (long)((uint)(obj["PageFaultsPersec"] ?? 0));
                            cachedRamCachedBytes = ramCachedBytes;
                            cachedRamPoolPagedBytes = ramPoolPagedBytes;
                            cachedRamPoolNonPagedBytes = ramPoolNonPagedBytes;
                            cachedRamActivityVal = ramActivityVal;
                            lastMemoryPerfTime = DateTime.Now;
                            break;
                        }
                    }
                }
                catch {}
            }

            int threadsCount = cachedThreadsCount;
            int processesCount = cachedProcessesCount;
            if (lastSystemPerfTime == DateTime.MinValue || (DateTime.Now - lastSystemPerfTime).TotalSeconds >= 2.0)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Threads, Processes FROM Win32_PerfFormattedData_PerfOS_System"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            threadsCount = Convert.ToInt32(obj["Threads"] ?? 0);
                            processesCount = Convert.ToInt32(obj["Processes"] ?? 0);
                            cachedThreadsCount = threadsCount;
                            cachedProcessesCount = processesCount;
                            lastSystemPerfTime = DateTime.Now;
                            break;
                        }
                    }
                }
                catch {}
            }

            long diskReadSec = 0;
            long diskWriteSec = 0;
            long diskActiveTime = 0;
            double diskResponse = 0.0;
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT DiskReadBytesPersec, DiskWriteBytesPersec, PercentDiskTime, AvgDiskSecPerTransfer FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name = 'C:'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        diskReadSec = (long)((ulong)(obj["DiskReadBytesPersec"] ?? 0));
                        diskWriteSec = (long)((ulong)(obj["DiskWriteBytesPersec"] ?? 0));
                        diskActiveTime = (long)((uint)(obj["PercentDiskTime"] ?? 0));
                        diskResponse = Convert.ToDouble(obj["AvgDiskSecPerTransfer"] ?? 0.0);
                        break;
                    }
                }
            }
            catch {}

            double diskReadMbNum = diskReadSec / (1024.0 * 1024.0);
            double diskWriteMbNum = diskWriteSec / (1024.0 * 1024.0);
            double diskThroughputMb = diskReadMbNum + diskWriteMbNum;
            int diskThroughputActivity = diskThroughputMb < 0.05
                ? 0
                : (int)Math.Ceiling(Math.Min(100.0, Math.Sqrt(diskThroughputMb / 2500.0) * 100.0));
            diskActiveTime = Math.Max(0, Math.Min(100, Math.Max(diskActiveTime, diskThroughputActivity)));

            // GPU stats
            int igpuUtil = cachedIgpuUtil;
            long igpuMemBytes = cachedIgpuMemBytes;
            int npuUtil = cachedNpuUtil;
            long npuMemBytes = cachedNpuMemBytes;
            int dgpuUtil = cachedDgpuUtil;
            if (lastGpuPerfTime == DateTime.MinValue || (DateTime.Now - lastGpuPerfTime).TotalSeconds >= 2.0)
            {
                int nextIgpuUtil = 0;
                long nextIgpuMemBytes = igpuMemBytes;
                int nextNpuUtil = 0;
                long nextNpuMemBytes = npuMemBytes;
                int nextDgpuUtil = 0;
                try
                {
                    if (NpuDetected && string.IsNullOrEmpty(npuLuid))
                    {
                        npuLuid = DiscoverNpuLuid();
                    }

                    if (!string.IsNullOrEmpty(igpuLuid))
                    {
                        string filter = string.Format("Name LIKE '%{0}%engtype_3d%'", igpuLuid);
                        using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE " + filter))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                nextIgpuUtil += Convert.ToInt32(obj["UtilizationPercentage"] ?? 0);
                            }
                        }
                    
                        string memFilter = string.Format("Name LIKE '%{0}%'", igpuLuid);
                        using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory WHERE " + memFilter))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                nextIgpuMemBytes = Convert.ToInt64(obj["SharedUsage"] ?? 0L);
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(dgpuLuid))
                    {
                        string filter = string.Format("Name LIKE '%{0}%engtype_3d%'", dgpuLuid);
                        using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE " + filter))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                nextDgpuUtil += Convert.ToInt32(obj["UtilizationPercentage"] ?? 0);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(npuLuid))
                    {
                        string filter = string.Format("Name LIKE '%{0}%engtype_compute%'", npuLuid);
                        using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE " + filter))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                nextNpuUtil += Convert.ToInt32(obj["UtilizationPercentage"] ?? 0);
                            }
                        }

                        string memFilter = string.Format("Name LIKE '%{0}%'", npuLuid);
                        using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory WHERE " + memFilter))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                nextNpuMemBytes = Convert.ToInt64(obj["SharedUsage"] ?? 0L);
                                break;
                            }
                        }
                    }

                    cachedIgpuUtil = nextIgpuUtil;
                    cachedIgpuMemBytes = nextIgpuMemBytes;
                    cachedNpuUtil = nextNpuUtil;
                    cachedNpuMemBytes = nextNpuMemBytes;
                    cachedDgpuUtil = nextDgpuUtil;
                    igpuUtil = cachedIgpuUtil;
                    igpuMemBytes = cachedIgpuMemBytes;
                    npuUtil = cachedNpuUtil;
                    npuMemBytes = cachedNpuMemBytes;
                    dgpuUtil = cachedDgpuUtil;
                    lastGpuPerfTime = DateTime.Now;
                }
                catch {}
            }

            // nvidia GPU stats via NVML
            int gpuUtil = dgpuUtil;
            int gpuTemp = 35;
            int gpuCoreClock = 210;
            int gpuMemClock = 405;
            ulong vramTotal = 16376 * 1024 * 1024L;
            ulong vramUsed = 0;
            bool nvmlSuccess = false;

            try
            {
                IntPtr dev;
                if (Nvml.nvmlDeviceGetHandleByIndex(0, out dev) == 0)
                {
                    Nvml.nvmlUtilization_t util;
                    if (Nvml.nvmlDeviceGetUtilizationRates(dev, out util) == 0)
                    {
                        gpuUtil = (int)util.gpu;
                    }
                    
                    uint temp;
                    if (Nvml.nvmlDeviceGetTemperature(dev, Nvml.nvmlTemperatureSensors_t.NVML_TEMPERATURE_GPU, out temp) == 0)
                    {
                        gpuTemp = (int)temp;
                    }
                    
                    uint coreClock;
                    if (Nvml.nvmlDeviceGetClockInfo(dev, Nvml.nvmlClockType_t.NVML_CLOCK_GRAPHICS, out coreClock) == 0)
                    {
                        gpuCoreClock = (int)coreClock;
                    }
                    
                    uint memClock;
                    if (Nvml.nvmlDeviceGetClockInfo(dev, Nvml.nvmlClockType_t.NVML_CLOCK_MEM, out memClock) == 0)
                    {
                        gpuMemClock = (int)memClock;
                    }
                    
                    Nvml.nvmlMemory_t mem;
                    if (Nvml.nvmlDeviceGetMemoryInfo(dev, out mem) == 0)
                    {
                        vramTotal = mem.total;
                        vramUsed = mem.used;
                    }
                    
                    nvmlSuccess = true;
                }
            }
            catch {}

            if (!nvmlSuccess)
            {
                vramTotal = 16376 * 1024 * 1024L;
                vramUsed = (ulong)(gpuUtil / 100.0 * vramTotal);
            }

            UpdatePing();
            UpdateTopProcesses();

            // Fan speeds
            int fan1 = cpuTemp > 45 ? (int)Math.Round(1000 + (cpuLoad * 8.0) + (cpuTemp - 45) * 20.0) : (int)Math.Round(800 + cpuLoad * 4.0);
            int fan2 = (gpuUtil > 5 || gpuTemp > 45) ? (int)Math.Round(900 + (gpuUtil * 6.0) + (gpuTemp - 40) * 15.0) : 0;

            string activePlanGuidStr = GetActivePowerPlanGuid();

            foreach (var plan in PowerPlans)
            {
                plan.Active = plan.Guid.Equals(activePlanGuidStr, StringComparison.OrdinalIgnoreCase);
            }

            // Generate JSON payload
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            
            // CPU
            string cpuFreqGhzStr = (cpuBaseSpeedVal * (1.0 + cpuLoad * 0.002)).ToString("F2", CultureInfo.InvariantCulture);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"cpu\":{{\"utilization\":{0},\"temp\":{1},\"name\":{2},\"freqGhz\":\"{3}\",\"baseSpeedGhz\":\"{4}\",\"cores\":{5},\"logical\":{6},\"threads\":{7},\"handles\":0,\"l2Cache\":{8},\"l3Cache\":{9}}},",
                cpuLoad, cpuTemp, JsonString(CpuInfo), cpuFreqGhzStr, CpuBaseSpeed, CpuCores, CpuLogical, threadsCount, JsonString(CpuL2Cache), JsonString(CpuL3Cache));

            // igpu
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"igpu\":{{\"utilization\":{0},\"usedMb\":{1},\"totalMb\":{2},\"totalGb\":{3},\"name\":{4},\"driver\":{5},\"driverDate\":{6}}},",
                igpuUtil, igpuMemBytes / (1024 * 1024), ramTotalPhys / 1024 / 1024 / 2, ramTotalPhys / 1024 / 1024 / 1024 / 2, JsonString(IgpuInfo), JsonString(IgpuDriver), JsonString(IgpuDriverDate));

            // npu
            long npuTotalMb = (long)(ramTotalPhys / 1024 / 1024 / 2);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"npu\":{{\"detected\":{0},\"utilization\":{1},\"usedMb\":{2},\"totalMb\":{3},\"totalGb\":{4},\"name\":{5}}},",
                NpuDetected ? "true" : "false", npuUtil, npuMemBytes / (1024 * 1024), npuTotalMb, npuTotalMb / 1024, JsonString(NpuName));

            // vram
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"vram\":{{\"utilization\":{0},\"usedMb\":{1},\"totalMb\":{2},\"totalGb\":{3}}},",
                (int)Math.Round((double)vramUsed / vramTotal * 100), vramUsed / (1024 * 1024), vramTotal / (1024 * 1024), vramTotal / 1024 / 1024 / 1024);

            // gpu
            int tops = GpuName.Contains("4070") ? 321 : (GpuName.Contains("4080") ? 486 : 321);
            int tflops = GpuName.Contains("4070") ? 39 : (GpuName.Contains("4080") ? 52 : 39);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"gpu\":{{\"utilization\":{0},\"temp\":{1},\"coreClock\":{2},\"memoryClock\":{3},\"name\":{4},\"tops\":{5},\"tflops\":{6},\"driver\":{7},\"driverDate\":{8}}},",
                gpuUtil, gpuTemp, gpuCoreClock, gpuMemClock, JsonString(GpuName), tops, tflops, JsonString(DgpuDriver), JsonString(DgpuDriverDate));

            // ram
            string commitUsedGbStr = (commitUsedBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
            string commitLimitGbStr = (commitLimitBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
            string cachedGbStr = (ramCachedBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"ram\":{{\"utilization\":{0},\"totalGb\":{1},\"commitUsedGb\":\"{2}\",\"commitLimitGb\":\"{3}\",\"cachedGb\":\"{4}\",\"poolPagedMb\":{5},\"poolNonPagedMb\":{6},\"speedMts\":{7},\"activity\":{8}}},",
                ramLoad, TotalRamGb, commitUsedGbStr, commitLimitGbStr, cachedGbStr, ramPoolPagedBytes / (1024 * 1024), ramPoolNonPagedBytes / (1024 * 1024), RamSpeedMts, ramActivityVal);

            // disk
            string diskFreeGbStr = diskFreeGb.ToString("F1", CultureInfo.InvariantCulture);
            string diskReadMbStr = diskReadMbNum.ToString("F1", CultureInfo.InvariantCulture);
            string diskWriteMbStr = diskWriteMbNum.ToString("F1", CultureInfo.InvariantCulture);
            string diskResponseMsStr = (diskResponse * 1000.0).ToString("F1", CultureInfo.InvariantCulture);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"disk\":{{\"freeGb\":{0},\"utilization\":{1},\"storagePercent\":{2},\"totalGb\":{3},\"readMb\":\"{4}\",\"writeMb\":\"{5}\",\"responseTimeMs\":{6},\"name\":{7}}},",
                diskFreeGbStr, diskActiveTime, diskStoragePercent, (int)diskTotalGb, diskReadMbStr, diskWriteMbStr, diskResponseMsStr, JsonString(DiskName));

            // fans
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"fans\":{{\"fan1\":{0},\"fan2\":{1}}},", fan1, fan2);

            // network
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"network\":{{\"lan\":{0},\"wifi\":{1},\"ip\":{2},\"ipv6\":{3},\"name\":{4},\"type\":{5},\"linkSpeedMbps\":{6}}},",
                cachedNetTxRate, cachedNetRxRate, JsonString(cachedActiveIp), JsonString(cachedActiveIpv6), JsonString(cachedActiveNetName), JsonString(cachedNetType), cachedNetLinkSpeedMbps);

            // global/uptime/ping
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"uptime\":{0},\"ping\":{1},\"totalProcesses\":{2},\"motherboard\":{3},",
                (int)Math.Round((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds), pingTime, processesCount, JsonString(MotherboardInfo));

            // topProcesses
            string topProcessesStr;
            lock (this)
            {
                topProcessesStr = cachedTopProcessesJson;
            }
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "\"topProcesses\":{0},", topProcessesStr);

            // powerPlans
            sb.Append("\"powerPlans\":[");
            for (int i = 0; i < PowerPlans.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.AppendFormat(CultureInfo.InvariantCulture, "{{\"guid\":{0},\"name\":{1},\"active\":{2}}}",
                    JsonString(PowerPlans[i].Guid), JsonString(PowerPlans[i].Name), PowerPlans[i].Active ? "true" : "false");
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }
    }
}
