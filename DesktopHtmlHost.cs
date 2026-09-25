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
        private const string AppDataFolderName = "NexusWpp";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegisterApplicationRestart(string commandLineArgs, int flags);

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

                RegisterForUpdateRestart(args);
                CleanupStaleWebView2Processes();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string defaultHtml = GetDefaultHtmlPath();
                string htmlPath = args.Length > 0 ? args[0] : defaultHtml;

                Application.Run(new DesktopForm(htmlPath));
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex);
            }
        }

        private static void RegisterForUpdateRestart(string[] args)
        {
            try
            {
                string restartArgs = BuildRestartCommandLine(args);
                int result = RegisterApplicationRestart(restartArgs, 0);
                if (result != 0)
                {
                    LogDebug("Application restart registration returned 0x" + result.ToString("X8", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                LogDebug("Application restart registration failed: " + ex.Message);
            }
        }

        private static string BuildRestartCommandLine(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            return string.Join(" ", args.Select(QuoteCommandLineArgument).ToArray());
        }

        private static string QuoteCommandLineArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            StringBuilder escaped = new StringBuilder();
            escaped.Append('"');

            int backslashCount = 0;
            foreach (char current in value)
            {
                if (current == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (current == '"')
                {
                    escaped.Append('\\', (backslashCount * 2) + 1);
                    escaped.Append('"');
                    backslashCount = 0;
                    continue;
                }

                escaped.Append('\\', backslashCount);
                escaped.Append(current);
                backslashCount = 0;
            }

            escaped.Append('\\', backslashCount * 2);
            escaped.Append('"');
            return escaped.ToString();
        }

        internal static string GetAppDataFolder()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                localAppData = AppDomain.CurrentDomain.BaseDirectory;
            }
            return Path.Combine(localAppData, AppDataFolderName);
        }

        internal static string GetWebViewUserDataFolder()
        {
            return Path.Combine(GetAppDataFolder(), "WebView2");
        }

        private static string GetDefaultHtmlPath()
        {
            string packagedHtml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            if (File.Exists(packagedHtml))
            {
                return packagedHtml;
            }

            return @"C:\nexuswpp\index.html";
        }

        private static void WriteCrashLog(Exception ex)
        {
            try
            {
                string dir = GetAppDataFolder();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "crash.txt"), ex.ToString());
            }
            catch { }
        }

        private static readonly object logLock = new object();
        private static void AppendLog(string filePath, string content)
        {
            try
            {
                lock (logLock)
                {
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

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
            AppendLog(Path.Combine(GetAppDataFolder(), "webview_debug.log"), line);
        }

        private static void CleanupStaleWebView2Processes()
        {
            try
            {
                string profileFolder = Program.GetWebViewUserDataFolder();

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
        private bool webViewInitializing = false;
        private bool webViewRecoveryPending = false;
        private CoreWebView2Environment webViewEnvironment;
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
        private bool sessionInactive = false;
        private int telemetryGeneration = 0;
        private string fullscreenReason = "";
        private bool isClosing = false;

        // --- Static Hook and Bounds State ---
        private static DesktopForm activeInstance;
        private static IntPtr hookId = IntPtr.Zero;
        private static LowLevelMouseProc mouseProc;
        private static Thread mouseHookThread;
        private static int mouseHookThreadId;
        private static int mouseHookStopRequested;
        private static Exception mouseHookError;
        private static System.Drawing.Rectangle remotePanelBounds = System.Drawing.Rectangle.Empty;
        private static IntPtr renderWindow = IntPtr.Zero;
        private static MouseRoutingSnapshot mouseRouting;
        private static readonly uint currentProcessId = (uint)Process.GetCurrentProcess().Id;

        // Published by the UI thread as one immutable snapshot. The mouse thread must
        // never invoke a WinForms control or wait for WebView2/the display driver.
        private sealed class MouseRoutingSnapshot
        {
            internal readonly System.Drawing.Rectangle ScreenBounds;
            internal readonly IntPtr HostWindow;
            internal readonly IntPtr RenderWindow;

            internal MouseRoutingSnapshot(System.Drawing.Rectangle bounds, IntPtr host, IntPtr render)
            {
                ScreenBounds = bounds;
                HostWindow = host;
                RenderWindow = render;
            }
        }

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

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

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
        private static extern bool IsWindow(IntPtr hWnd);

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

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT point;
            public uint privateData;
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
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            // Create and configure the WebView2 UI component
            CreateWebViewControl();

            // Warm WebView2 immediately while Explorer is still preparing the desktop layer.
            InitializeWebViewAsync();

            // Initialize and start the non-blocking GUI timer to search for WorkerW.
            searchTimer = new System.Windows.Forms.Timer();
            searchTimer.Interval = 100;
            searchTimer.Tick += SearchTimer_Tick;
            searchTimer.Start();
        }

        private void CreateWebViewControl()
        {
            ResilientWebView2 newWebView = new ResilientWebView2();
            newWebView.Dock = DockStyle.Fill;
            newWebView.Disposed += WebView_Disposed;
            webView = newWebView;
            this.Controls.Add(webView);
        }

        private void WebView_Disposed(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed || !ReferenceEquals(sender, webView))
            {
                return;
            }

            QueueWebViewRecovery("unexpected WebView2 disposal");
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
            PublishMouseRouting();
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            TryBeginInvoke((MethodInvoker)delegate {
                UpdateBoundsToVirtualScreen();
                MoveWindow(this.Handle, this.Left, this.Top, this.Width, this.Height, true);
                QueueDesktopReattach("display settings changed");
            });
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                QueueDesktopReattach("system resumed from sleep");
            }
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock ||
                e.Reason == SessionSwitchReason.RemoteDisconnect ||
                e.Reason == SessionSwitchReason.ConsoleDisconnect)
            {
                TryBeginInvoke((MethodInvoker)delegate {
                    sessionInactive = true;
                    FullscreenTimer_Tick(null, null);
                });
            }
            if (e.Reason == SessionSwitchReason.SessionUnlock ||
                e.Reason == SessionSwitchReason.SessionLogon ||
                e.Reason == SessionSwitchReason.RemoteConnect ||
                e.Reason == SessionSwitchReason.ConsoleConnect)
            {
                TryBeginInvoke((MethodInvoker)delegate {
                    sessionInactive = false;
                    FullscreenTimer_Tick(null, null);
                });
                QueueDesktopReattach("session became active: " + e.Reason);
            }
        }

        private void QueueDesktopReattach(string reason)
        {
            if (isClosing || IsDisposed) return;

            TryBeginInvoke((MethodInvoker)delegate
            {
                if (isClosing || IsDisposed) return;
                RestartDesktopSearch(reason);
            });
        }

        private void RestartDesktopSearch(string reason)
        {
            Program.LogDebug("Restarting desktop attachment after " + reason + ".");

            desktopAttached = false;
            attachedToTemporaryParent = false;
            currentDesktopParent = IntPtr.Zero;
            retryCount = 0;
            startupTime = DateTime.Now;

            UpdateBoundsToVirtualScreen();
            if (this.Handle != IntPtr.Zero)
            {
                MoveWindow(this.Handle, this.Left, this.Top, this.Width, this.Height, true);
                ShowWindow(this.Handle, SW_SHOW);
            }

            if (searchTimer != null)
            {
                searchTimer.Stop();
                searchTimer.Start();
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
            StopMouseHook();

            if (webView != null)
            {
                try
                {
                    ResilientWebView2 resilientWebView = webView as ResilientWebView2;
                    if (resilientWebView != null)
                    {
                        resilientWebView.BeginShutdown();
                    }

                    webView.Disposed -= WebView_Disposed;
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
                    options.AdditionalBrowserArguments = "--disable-features=EdgeSidebar,EdgeTranslate";
                    webViewEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                }

                if (isClosing || IsDisposed || webView == null || webView.IsDisposed) return;

                await webView.EnsureCoreWebView2Async(webViewEnvironment);
                if (isClosing || IsDisposed || webView == null || webView.IsDisposed) return;

                webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;

                // Configure virtual host mapping to serve files from local directory without HTTP server
                string directory = Path.GetDirectoryName(htmlPath);
                if (string.IsNullOrEmpty(directory)) directory = AppDomain.CurrentDomain.BaseDirectory;
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
                webViewRecoveryPending = false;

                if (!telemetryReady || telemetryCollector == null)
                {
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
                else if (!runtimeSuspended)
                {
                    TelemetryTimer_Tick(null, null);
                }
            }
            catch (Exception ex)
            {
                webViewRecoveryPending = false;
                if (isClosing || IsDisposed) return;

                Program.LogDebug("WebView2 Runtime failed to initialize: " + ex.ToString());
                BeginCleanShutdown();
            }
            finally
            {
                webViewInitializing = false;
            }
        }

        private void CoreWebView2_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            string kind = e != null ? e.ProcessFailedKind.ToString() : "unknown failure";
            Program.LogDebug("WebView2 process failed: " + kind + ".");
            QueueWebViewRecovery("process failure (" + kind + ")");
        }

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
                fullscreenTimer.Interval = 500;
                fullscreenTimer.Tick += FullscreenTimer_Tick;
            }

            fullscreenTimer.Start();
            if (runtimeSuspended) ApplyWebViewRuntimeState();
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
            telemetryGeneration++;

            if (telemetryTimer != null)
            {
                if (suspended) telemetryTimer.Stop();
                else telemetryTimer.Start();
            }

            ApplyWebViewRuntimeState();
            Program.LogDebug(suspended ? "Runtime suspended: " + fullscreenReason + "." : "Runtime resumed: desktop visible.");
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

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            if (runtimeSuspended) return;
            if (!telemetryReady || telemetryCollector == null || telemetryCollectPending) return;
            telemetryCollectPending = true;
            int generation = telemetryGeneration;

            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                {
                    string statsJson = "";
                    try
                    {
                        if (isClosing || IsDisposed || runtimeSuspended)
                        {
                            telemetryCollectPending = false;
                            return;
                        }
                        statsJson = telemetryCollector.CollectStats();
                        if (!TryBeginInvoke((MethodInvoker)delegate
                        {
                            try
                            {
                                if (!runtimeSuspended && generation == telemetryGeneration)
                                    PostWebMessageAsJsonSafe(statsJson, "Telemetry post");
                            }
                            catch (Exception ex)
                            {
                                Program.LogDebug("Telemetry post error: " + ex.Message + " | JSON: " + statsJson);
                            }
                            finally
                            {
                                telemetryCollectPending = false;
                                if (!runtimeSuspended && generation != telemetryGeneration)
                                    TelemetryTimer_Tick(null, null);
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
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
                StopMouseHook();
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
                    ResilientWebView2 resilientWebView = webView as ResilientWebView2;
                    if (resilientWebView != null)
                    {
                        resilientWebView.BeginShutdown();
                    }

                    webView.Disposed -= WebView_Disposed;
                    try
                    {
                        webView.Dispose();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Program.LogDebug("Suppressed WebView2 disposal after shutdown: " + ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        if (!ResilientWebView2.IsDisposedLifecycleException(ex))
                        {
                            throw;
                        }

                        Program.LogDebug("Suppressed WebView2 disposal after shutdown: " + ex.Message);
                    }
                    finally
                    {
                        webView = null;
                    }
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
            // Startup messages are replayed by the page's runtime-state handshake.
            // A controller still being created does not need concurrent recovery.
            if (webViewInitializing) return false;
            if (isClosing || IsDisposed || webView == null || webView.IsDisposed)
            {
                if (!isClosing && !IsDisposed)
                {
                    QueueWebViewRecovery("CoreWebView2 unavailable");
                }

                return false;
            }

            try
            {
                coreWebView = webView.CoreWebView2;
                if (coreWebView == null)
                {
                    QueueWebViewRecovery("CoreWebView2 missing");
                    return false;
                }

                return coreWebView != null;
            }
            catch (ObjectDisposedException)
            {
                QueueWebViewRecovery("disposed CoreWebView2 access");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                if (ResilientWebView2.IsDisposedLifecycleException(ex))
                {
                    QueueWebViewRecovery("disposed CoreWebView2 lifecycle");
                    return false;
                }

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
            private bool shutdownStarted = false;

            internal void BeginShutdown()
            {
                shutdownStarted = true;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                try
                {
                    base.OnHandleCreated(e);
                }
                catch (ObjectDisposedException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("handle creation", ex);
                }
                catch (InvalidOperationException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("handle creation", ex);
                }
            }

            protected override void WndProc(ref Message m)
            {
                try
                {
                    base.WndProc(ref m);
                }
                catch (ObjectDisposedException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("window message " + m.Msg.ToString(CultureInfo.InvariantCulture), ex);
                }
                catch (InvalidOperationException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("window message " + m.Msg.ToString(CultureInfo.InvariantCulture), ex);
                }
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                try
                {
                    base.OnSizeChanged(e);
                }
                catch (ObjectDisposedException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("resize", ex);
                }
                catch (InvalidOperationException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("resize", ex);
                }
            }

            private bool CanSuppressLifecycleException(Exception ex)
            {
                if (ex is ObjectDisposedException)
                {
                    return shutdownStarted || IsDisposed || Disposing;
                }

                return IsDisposedLifecycleException(ex);
            }

            internal static bool IsDisposedLifecycleException(Exception ex)
            {
                InvalidOperationException invalid = ex as InvalidOperationException;
                if (invalid == null || string.IsNullOrEmpty(invalid.Message))
                {
                    return false;
                }

                return invalid.Message.IndexOf(
                    "CoreWebView2 members cannot be accessed after the WebView2 control is disposed",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static void LogSuppressedLifecycleException(string context, Exception ex)
            {
                Program.LogDebug("Suppressed WebView2 " + context + " after disposal: " + ex.Message);
            }
        }

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

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus([In, Out] SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }

    [StructLayout(LayoutKind.Sequential)]
    public class SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    public static class WlanApi
    {
        [DllImport("wlanapi.dll")]
        public static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        public static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        public static extern int WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll")]
        public static extern void WlanFreeMemory(IntPtr pMemory);
    }

}
