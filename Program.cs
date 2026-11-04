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
    static partial class Program
    {
        private static Mutex appMutex;
        private const string AppDataFolderName = "NexusWpp";
        internal const string ShowSettingsEventName = @"Local\NexusWppShowSettings";
        internal static bool QuitRequestedByUser;

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool SystemParametersInfo(int action, int param, char* value, int winIni);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int RegisterApplicationRestart(string commandLineArgs, int flags);

        [LibraryImport("kernel32.dll")]
        private static unsafe partial int GetCurrentPackageFullName(ref int packageFullNameLength, char* packageFullName);

        [LibraryImport("kernel32.dll")]
        private static unsafe partial int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, char* packageFamilyName);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AllowSetForegroundWindow(int processId);

        private const int ASFW_ANY = -1;
        // Startup already relaunches the app after a reboot (Run key or Store startup task).
        private const int RESTART_NO_REBOOT = 8;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                bool createdNew;
                appMutex = new Mutex(true, @"Local\NexusWppDesktopHost", out createdNew);
                if (!createdNew)
                {
                    // Launching NexusWpp again (Start menu, Store "Open") shows the running instance's settings.
                    EventWaitHandle showSettings;
                    if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out showSettings))
                    {
                        // This launch owns the foreground right: let the running instance bring Settings to front.
                        try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
                        using (showSettings) showSettings.Set();
                    }
                    return;
                }

                // Read before anything creates the data folder: a profile without WebView2 data is a first launch.
                FirstLaunch = !File.Exists(WelcomeFlagPath) && !Directory.Exists(GetWebViewUserDataFolder());
                if (!FirstLaunch) MarkWelcomeShown();
                LogDebug("NexusWpp " + NexusWpp.ReleaseVersion.Value + (IsPackaged ? " (Store)" : " (EXE)") + " starting on Windows " + RealWindowsVersion() + " (" + Application.ExecutablePath + ").");

                // Per-monitor v2 DPI awareness (NexusWpp.csproj), visual styles and GDI+ text rendering,
                // before any window exists: every coordinate is a physical pixel on every monitor.
                ApplicationConfiguration.Initialize();

                RegisterForUpdateRestart(args);
                CleanupStaleWebView2Processes();
                System.Threading.Tasks.Task.Run(() => PriorityStartup.ReconcileAsync());

                // No WinForms "Continue" dialog on the desktop: unexpected errors reach the crash handler below.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    Exception error = e.ExceptionObject as Exception;
                    if (error != null) WriteCrashLog(error);
                };

                string defaultHtml = GetDefaultHtmlPath();
                string htmlPath = args.Length > 0 ? args[0] : defaultHtml;

                using (var context = new DesktopHostContext(() => new DesktopForm(htmlPath)))
                {
                    Application.Run(context);
                }
                if (QuitRequestedByUser) RestoreDesktopWallpaper();
            }
            catch (Exception ex)
            {
                // A real crash: keep a trace, then let Windows Error Reporting record it (and the
                // registered restart apply) instead of exiting as if nothing happened.
                WriteCrashLog(ex);
                Environment.FailFast("NexusWpp crashed: " + ex.Message, ex);
            }
        }

        // .NET 5 and later report the real version, with or without a compatibility manifest.
        internal static string RealWindowsVersion()
        {
            return Environment.OSVersion.Version.ToString(3);
        }

        internal static bool FirstLaunch { get; private set; }

        private static string WelcomeFlagPath
        {
            get { return Path.Combine(GetAppDataFolder(), "welcome-shown"); }
        }

        internal static void MarkWelcomeShown()
        {
            try
            {
                Directory.CreateDirectory(GetAppDataFolder());
                File.WriteAllText(WelcomeFlagPath, NexusWpp.ReleaseVersion.Value);
            }
            catch (Exception ex)
            {
                LogDebug("Welcome flag write failed: " + ex.Message);
            }
            FirstLaunch = false;
        }

        // Store (MSIX) build: the process runs with a package identity.
        internal static unsafe bool IsPackaged
        {
            get
            {
                try
                {
                    int length = 0;
                    const int APPMODEL_ERROR_NO_PACKAGE = 15700;
                    return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
                }
                catch (EntryPointNotFoundException) { return false; }
            }
        }

        // Folder holding webview_debug.log as Explorer sees it. A packaged process writes its
        // AppData into the package's LocalCache, which Explorer does not redirect.
        internal static unsafe string GetLogsFolderForShell()
        {
            string folder = GetAppDataFolder();
            try
            {
                int length = 0;
                if (GetCurrentPackageFamilyName(ref length, null) == 122 /* ERROR_INSUFFICIENT_BUFFER */ && length > 0)
                {
                    string family = null;
                    fixed (char* name = new char[length])
                    {
                        if (GetCurrentPackageFamilyName(ref length, name) == 0) family = new string(name);
                    }
                    if (family != null)
                    {
                        string redirected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Packages", family, "LocalCache", "Local", AppDataFolderName);
                        if (Directory.Exists(redirected)) return redirected;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("Package folder lookup failed: " + ex.Message);
            }
            return folder;
        }

        private static void RegisterForUpdateRestart(string[] args)
        {
            try
            {
                string restartArgs = BuildRestartCommandLine(args);
                int result = RegisterApplicationRestart(restartArgs, RESTART_NO_REBOOT);
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

        // After quitting, Explorer may keep the last frame of the injected window: repaint the
        // user's own wallpaper with its current path, without writing the setting.
        private static unsafe void RestoreDesktopWallpaper()
        {
            try
            {
                const int SPI_GETDESKWALLPAPER = 0x0073, SPI_SETDESKWALLPAPER = 0x0014;
                // The path read back is null-terminated in the same buffer.
                char* current = stackalloc char[1024];
                if (SystemParametersInfo(SPI_GETDESKWALLPAPER, 1024, current, 0))
                {
                    SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, current, 0);
                }
            }
            catch (Exception ex)
            {
                LogDebug("Wallpaper repaint after quit failed: " + ex.Message);
            }
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

        // icon.ico ships next to the exe with every size, so each place picks its own entry: the round medallion
        // under 44 px, the medallion on its dark square above. ExtractAssociatedIcon only gives the large size, which Windows takes from the
        // 48 px medallion at 125 % and shrinks into the title bar and the notification area.
        internal static System.Drawing.Icon LoadAppIcon(System.Drawing.Size size)
        {
            try { return new System.Drawing.Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"), size); }
            catch (Exception ex) { LogDebug("icon.ico unavailable, using the exe icon: " + ex.Message); }
            try { return System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return System.Drawing.SystemIcons.Application; }
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
                File.WriteAllText(Path.Combine(dir, "crash.txt"), "[" + DateTime.Now.ToString("s", CultureInfo.InvariantCulture) + "] NexusWpp " +
                    NexusWpp.ReleaseVersion.Value + (IsPackaged ? " (Store)" : " (EXE)") + "\r\n" + ex);
            }
            catch { }
        }

        private static readonly object logLock = new object();
        internal const long MaxLogBytes = 1024 * 1024;
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

                    // Rotation: at most 1 MB current log plus one previous file.
                    if (File.Exists(filePath))
                    {
                        FileInfo fi = new FileInfo(filePath);
                        if (fi.Length > MaxLogBytes)
                        {
                            string backupPath = filePath + ".old";
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

        // Tests that compile the host sources log to their own folder, never the user's log.
        internal static string LogPathOverride = null;

        internal static void LogDebug(string message)
        {
            string line = string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), message);
            AppendLog(LogPathOverride ?? Path.Combine(GetAppDataFolder(), "webview_debug.log"), line);
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
}
