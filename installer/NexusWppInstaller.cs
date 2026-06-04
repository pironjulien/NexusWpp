using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexusWppInstaller
{
    internal static class Program
    {
        private const string InstallDir = @"C:\nexuswpp";
        private const string PayloadResourceName = "NexusWpp.Payload.zip";
        private const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        private const string WebView2ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string ProductName = "NexusWpp";
        private const string ProductVersion = "1.0.0";
        private const string Publisher = "JULIENPIRON.FR";
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NexusWpp";

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = HasArg(args, "/silent") || HasArg(args, "-silent") || HasArg(args, "/quiet") || HasArg(args, "-quiet");
            bool uninstall = HasArg(args, "/uninstall") || HasArg(args, "-uninstall");

            try
            {
                try { SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                if (!IsAdministrator())
                {
                    Show(silent, "NexusWpp installer must be run as administrator.");
                    return 1;
                }

                if (uninstall)
                {
                    Uninstall(silent);
                    return 0;
                }

                Log("Starting NexusWpp installation.");
                StopRunningWallpaper();
                Directory.CreateDirectory(InstallDir);

                if (!IsWebView2RuntimeInstalled())
                {
                    InstallWebView2Runtime();
                }

                ExtractPayload();
                GenerateScreenMatchedLoadingSnapshot();
                RegisterStartup();
                RegisterUninstallEntry();
                PersistInstallerForUninstall();
                StartWallpaper();

                Log("Installation completed.");
                Show(silent, "NexusWpp is installed and running.");
                return 0;
            }
            catch (Exception ex)
            {
                Log("Installation failed: " + ex);
                Show(false, "NexusWpp installation failed:\r\n\r\n" + ex.Message + "\r\n\r\nLog: " + Path.Combine(InstallDir, "install.log"));
                return 1;
            }
        }

        private static void Uninstall(bool silent)
        {
            Log("Starting NexusWpp uninstall.");
            StopRunningWallpaper();
            CleanupStartupEntries();
            RemoveUninstallEntry();

            string self = Assembly.GetExecutingAssembly().Location;
            string installFullPath = Path.GetFullPath(InstallDir);
            string selfFullPath = Path.GetFullPath(self);
            if (selfFullPath.StartsWith(installFullPath, StringComparison.OrdinalIgnoreCase))
            {
                ScheduleInstallDirectoryRemoval();
            }
            else if (Directory.Exists(InstallDir))
            {
                Directory.Delete(InstallDir, true);
            }

            Show(silent, "NexusWpp has been uninstalled.");
        }

        private static bool HasArg(string[] args, string value)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void StopRunningWallpaper()
        {
            KillProcessesByName("nexuswpp");
            KillProcessesByName("DesktopHtmlHost");
            KillOwnedWebView2Processes();
            Thread.Sleep(500);
        }

        private static void KillProcessesByName(string name)
        {
            Process[] processes = Process.GetProcessesByName(name);
            for (int i = 0; i < processes.Length; i++)
            {
                try
                {
                    processes[i].Kill();
                    processes[i].WaitForExit(3000);
                }
                catch { }
                finally
                {
                    processes[i].Dispose();
                }
            }
        }

        private static void KillOwnedWebView2Processes()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string commandLine = Convert.ToString(item["CommandLine"]);
                        if (commandLine == null) continue;
                        if (commandLine.IndexOf("nexuswpp", StringComparison.OrdinalIgnoreCase) < 0 &&
                            commandLine.IndexOf("EBWebView", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        int pid = Convert.ToInt32(item["ProcessId"]);
                        try
                        {
                            using (Process process = Process.GetProcessById(pid))
                            {
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsWebView2RuntimeInstalled()
        {
            string[] paths = new string[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid,
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid
            };

            for (int i = 0; i < paths.Length; i++)
            {
                if (HasValidRuntimeVersion(Registry.LocalMachine, paths[i])) return true;
                if (HasValidRuntimeVersion(Registry.CurrentUser, paths[i])) return true;
            }

            return false;
        }

        private static bool HasValidRuntimeVersion(RegistryKey root, string path)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path, false))
                {
                    if (key == null) return false;
                    string version = Convert.ToString(key.GetValue("pv"));
                    return !string.IsNullOrWhiteSpace(version) && version != "0.0.0.0";
                }
            }
            catch
            {
                return false;
            }
        }

        private static void InstallWebView2Runtime()
        {
            Log("WebView2 Runtime missing. Downloading Evergreen Bootstrapper.");
            string bootstrapper = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
            using (WebClient client = new WebClient())
            {
                client.DownloadFile(WebView2BootstrapperUrl, bootstrapper);
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = bootstrapper;
            psi.Arguments = "/silent /install";
            psi.UseShellExecute = false;
            using (Process process = Process.Start(psi))
            {
                process.WaitForExit();
                if (process.ExitCode != 0 && process.ExitCode != 1638)
                {
                    throw new InvalidOperationException("WebView2 Runtime installer failed with exit code " + process.ExitCode + ".");
                }
            }

            if (!IsWebView2RuntimeInstalled())
            {
                throw new InvalidOperationException("WebView2 Runtime was not detected after installation.");
            }
        }

        private static void ExtractPayload()
        {
            Log("Extracting payload to " + InstallDir + ".");

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream payload = assembly.GetManifestResourceStream(PayloadResourceName))
            {
                if (payload == null)
                {
                    throw new InvalidOperationException("Embedded payload not found: " + PayloadResourceName);
                }

                using (ZipArchive archive = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        ZipArchiveEntry entry = archive.Entries[i];
                        string targetPath = Path.GetFullPath(Path.Combine(InstallDir, entry.FullName));
                        if (!targetPath.StartsWith(Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Unsafe archive entry: " + entry.FullName);
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(targetPath);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        using (Stream input = entry.Open())
                        using (FileStream output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            input.CopyTo(output);
                        }
                    }
                }
            }
        }

        private static void GenerateScreenMatchedLoadingSnapshot()
        {
            try
            {
                string script = Path.Combine(InstallDir, "scripts", "generate_loading_snapshot.ps1");
                if (!File.Exists(script))
                {
                    Log("Snapshot generator not found. Keeping bundled loading image.");
                    return;
                }

                int width = SystemInformation.VirtualScreen.Width;
                int height = SystemInformation.VirtualScreen.Height;
                if (width <= 0 || height <= 0)
                {
                    Log("Invalid primary screen size. Keeping bundled loading image.");
                    return;
                }

                Log("Generating virtual-screen zero loading snapshot: " + width + "x" + height + ".");

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Width " + width.ToString() + " -Height " + height.ToString() + " -RenderScale 1 -Mode Zero -OutputPath .\\loading-zero-5120x1440.png";
                psi.WorkingDirectory = InstallDir;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (Process process = Process.Start(psi))
                {
                    if (!process.WaitForExit(45000))
                    {
                        try { process.Kill(); } catch { }
                        Log("Snapshot generation timed out. Keeping bundled loading image.");
                        return;
                    }

                    if (process.ExitCode != 0)
                    {
                        Log("Snapshot generation failed with exit code " + process.ExitCode + ". Keeping bundled loading image.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Snapshot generation skipped: " + ex.Message);
            }
        }

        private static void RegisterStartup()
        {
            string exe = Path.Combine(InstallDir, "nexuswpp.exe");
            string command = "\"" + exe + "\"";

            CleanupStartupEntries();

            using (RegistryKey run = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (run == null) throw new InvalidOperationException("Unable to open HKLM Run key.");
                run.SetValue("NexusWpp", command, RegistryValueKind.String);
            }

            try
            {
                using (RegistryKey serialize = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"))
                {
                    serialize.SetValue("StartupDelayInMSec", 0, RegistryValueKind.DWord);
                }
            }
            catch { }

            Log("Startup registered through HKLM Run only.");
        }

        private static void CleanupStartupEntries()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (run != null) run.DeleteValue("NexusWpp", false);
                }
            }
            catch (Exception ex)
            {
                Log("HKCU Run cleanup failed: " + ex.Message);
            }

            DeleteStartupShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            DeleteStartupShortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));

            try
            {
                using (RegistryKey run = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (run != null) run.DeleteValue("NexusWpp", false);
                }
            }
            catch (Exception ex)
            {
                Log("HKLM Run cleanup failed: " + ex.Message);
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "schtasks.exe";
                psi.Arguments = "/Delete /TN NexusWpp /F";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Log("Scheduled task cleanup failed: " + ex.Message);
            }
        }

        private static void DeleteStartupShortcut(string startupFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(startupFolder)) return;
                string shortcut = Path.Combine(startupFolder, "NexusWpp.lnk");
                if (File.Exists(shortcut)) File.Delete(shortcut);
            }
            catch (Exception ex)
            {
                Log("Startup shortcut cleanup failed: " + ex.Message);
            }
        }

        private static void RegisterUninstallEntry()
        {
            string installerPath = Path.Combine(InstallDir, "NexusWppSetup.exe");
            string uninstallString = "\"" + installerPath + "\" /uninstall";

            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath))
            {
                key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", ProductVersion, RegistryValueKind.String);
                key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                key.SetValue("InstallLocation", InstallDir, RegistryValueKind.String);
                key.SetValue("DisplayIcon", Path.Combine(InstallDir, "icon.ico"), RegistryValueKind.String);
                key.SetValue("UninstallString", uninstallString, RegistryValueKind.String);
                key.SetValue("QuietUninstallString", uninstallString + " /quiet", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", GetInstallSizeKb(), RegistryValueKind.DWord);
            }
        }

        private static void RemoveUninstallEntry()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, false);
            }
            catch (Exception ex)
            {
                Log("Uninstall registry cleanup failed: " + ex.Message);
            }
        }

        private static int GetInstallSizeKb()
        {
            try
            {
                long bytes = 0;
                if (Directory.Exists(InstallDir))
                {
                    string[] files = Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < files.Length; i++)
                    {
                        bytes += new FileInfo(files[i]).Length;
                    }
                }
                return (int)Math.Max(1, bytes / 1024);
            }
            catch
            {
                return 1;
            }
        }

        private static void PersistInstallerForUninstall()
        {
            string source = Assembly.GetExecutingAssembly().Location;
            string destination = Path.Combine(InstallDir, "NexusWppSetup.exe");
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, destination, true);
            }
        }

        private static void ScheduleInstallDirectoryRemoval()
        {
            string cmd = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + InstallDir + "\"";
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = cmd;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try { Process.Start(psi); } catch { }
        }

        private static void StartWallpaper()
        {
            string exe = Path.Combine(InstallDir, "nexuswpp.exe");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.WorkingDirectory = InstallDir;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                File.AppendAllText(Path.Combine(InstallDir, "install.log"), "[" + DateTime.Now.ToString("s") + "] " + message + Environment.NewLine);
            }
            catch
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "NexusWppInstall.log"), "[" + DateTime.Now.ToString("s") + "] " + message + Environment.NewLine);
                }
                catch { }
            }
        }

        private static void Show(bool silent, string message)
        {
            if (!silent)
            {
                MessageBox.Show(message, "NexusWpp Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
