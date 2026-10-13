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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AllowSetForegroundWindow(int processId);

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
                        AllowSetForegroundWindow(ASFW_ANY);
                        using (showSettings) showSettings.Set();
                    }
                    return;
                }

                // Force Process DPI Awareness to prevent Windows from virtualizing coordinates
                SetProcessDPIAware();
                LogDebug("NexusWpp " + NexusWpp.ReleaseVersion.Value + " starting (" + Application.ExecutablePath + ").");

                RegisterForUpdateRestart(args);
                CleanupStaleWebView2Processes();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

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
                WriteCrashLog(ex);
            }
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
