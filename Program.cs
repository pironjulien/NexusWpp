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
        internal const string ShowSettingsEventName = @"Local\NexusWppShowSettings";
        internal static bool QuitRequestedByUser;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int winIni);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegisterApplicationRestart(string commandLineArgs, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder packageFullName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, StringBuilder packageFamilyName);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

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
                        // This launch owns the foreground: let the running instance bring its window forward.
                        const int ASFW_ANY = -1;
                        try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
                        using (showSettings) showSettings.Set();
                    }
                    return;
                }

                // Read before anything creates the data folder: a profile without WebView2 data is a first launch.
                FirstLaunch = !File.Exists(WelcomeFlagPath) && !Directory.Exists(GetWebViewUserDataFolder());
                if (!FirstLaunch) MarkWelcomeShown();
                LogDebug("NexusWpp " + NexusWpp.ReleaseVersion.Value + (IsPackaged ? " (Store)" : " (EXE)") + " starting on Windows " + Environment.OSVersion.Version + ".");

                // Force Process DPI Awareness to prevent Windows from virtualizing coordinates
                SetProcessDPIAware();

                RegisterForUpdateRestart(args);
                CleanupStaleWebView2Processes();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // No WinForms "Continue" dialog on the desktop: unexpected errors reach the crash handler below.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    Exception error = e.ExceptionObject as Exception;
                    if (error != null) WriteCrashLog(error);
                };

                string defaultHtml = GetDefaultHtmlPath();
                string htmlPath = args.Length > 0 ? args[0] : defaultHtml;

                Application.Run(new DesktopForm(htmlPath));
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
        internal static bool IsPackaged
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
        internal static string GetLogsFolderForShell()
        {
            string folder = GetAppDataFolder();
            try
            {
                int length = 0;
                if (GetCurrentPackageFamilyName(ref length, null) == 122 /* ERROR_INSUFFICIENT_BUFFER */ && length > 0)
                {
                    var family = new StringBuilder(length);
                    if (GetCurrentPackageFamilyName(ref length, family) == 0)
                    {
                        string redirected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Packages", family.ToString(), "LocalCache", "Local", AppDataFolderName);
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

        // After quitting, Explorer may keep the last frame of the injected window: repaint the
        // user's own wallpaper with its current path, without writing the setting.
        private static void RestoreDesktopWallpaper()
        {
            try
            {
                const int SPI_GETDESKWALLPAPER = 0x0073, SPI_SETDESKWALLPAPER = 0x0014;
                var current = new StringBuilder(1024);
                if (SystemParametersInfo(SPI_GETDESKWALLPAPER, current.Capacity, current, 0))
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
}
