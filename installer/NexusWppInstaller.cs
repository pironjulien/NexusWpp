using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("NexusWpp Installer")]
[assembly: AssemblyProduct("NexusWpp")]
[assembly: AssemblyCompany("JULIENPIRON.FR")]

namespace NexusWppInstaller
{
    internal static class Program
    {
        private const string InstallDir = @"C:\nexuswpp";
        private const string PayloadResourceName = "NexusWpp.Payload.zip";
        private const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        private const string WebView2ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string ProductName = "NexusWpp";
        internal const string ProductVersion = NexusWpp.ReleaseVersion.Value;
        private const string Publisher = "JULIENPIRON.FR";
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NexusWpp";

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = HasArg(args, "/silent") || HasArg(args, "-silent") || HasArg(args, "/quiet") || HasArg(args, "-quiet");
            bool uninstall = HasArg(args, "/uninstall") || HasArg(args, "-uninstall");

            try { SetProcessDPIAware(); } catch { }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            if (!IsAdministrator())
            {
                Show(silent, "NexusWpp installer must be run as administrator.");
                return 1;
            }

            if (!silent)
            {
                return RunWithProgressWindow(uninstall);
            }

            try
            {
                if (uninstall)
                {
                    Uninstall(new NullProgressReporter());
                    return 0;
                }

                bool launched = Install(new NullProgressReporter());
                Show(silent, ProductName + " " + ProductVersion + (launched ? " is installed and running." : " is installed and will start at the next sign-in."));
                return 0;
            }
            catch (Exception ex)
            {
                Log("Installation failed: " + ex);
                Show(false, "NexusWpp installation failed:\r\n\r\n" + ex.Message + "\r\n\r\nLog: " + LogPath);
                return 1;
            }
        }

        private static string CompletionMessage(bool launched)
        {
            return launched
                ? "NexusWpp " + ProductVersion + " est installe et lance."
                : "NexusWpp " + ProductVersion + " est installe. Il demarrera a la prochaine ouverture de session.";
        }

        private static int RunWithProgressWindow(bool uninstall)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (InstallerProgressForm form = new InstallerProgressForm(uninstall))
            {
                form.Shown += delegate
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            if (uninstall)
                            {
                                Uninstall(form);
                                form.Complete(true, "NexusWpp " + ProductVersion + " a ete desinstalle.", 0);
                            }
                            else
                            {
                                bool launched = Install(form);
                                form.Complete(true, CompletionMessage(launched), 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log((uninstall ? "Uninstall" : "Installation") + " failed: " + ex);
                            form.Complete(false, ex.Message + "\r\n\r\nLog: " + LogPath, 1);
                        }
                    });
                };

                Application.Run(form);
                return form.ExitCode;
            }
        }

        private static bool Install(IProgressReporter progress)
        {
            Log("Starting NexusWpp " + ProductVersion + " installation.");
            progress.Report(5, "Preparation de l'installation " + ProductVersion + "...");

            progress.Report(12, "Arret de l'ancienne version...");
            StopRunningWallpaper();
            PrepareInstallDir();

            progress.Report(24, "Verification du runtime WebView2...");
            if (!IsWebView2RuntimeInstalled())
            {
                progress.Report(30, "Installation du runtime WebView2 Microsoft...");
                InstallWebView2Runtime();
            }

            progress.Report(48, "Copie des fichiers NexusWpp...");
            ExtractPayload();
            VerifyInstallDirSecurity();

            progress.Report(64, "Configuration du demarrage Windows...");
            RegisterStartup();

            progress.Report(78, "Ajout au menu Demarrer...");
            RegisterStartMenuShortcut();

            progress.Report(86, "Enregistrement de la desinstallation...");
            RegisterUninstallEntry();
            PersistInstallerForUninstall();

            progress.Report(94, "Lancement du fond d'ecran...");
            bool launched = StartWallpaper();

            progress.Report(100, "Installation terminee.");
            Log("NexusWpp " + ProductVersion + " installation completed" + (launched ? "." : "; it starts at the next sign-in."));
            return launched;
        }

        private static void Uninstall(IProgressReporter progress)
        {
            installDirReady = IsTrustedInstallDir();
            Log("Starting NexusWpp " + ProductVersion + " uninstall.");
            progress.Report(10, "Arret de NexusWpp...");
            StopRunningWallpaper();
            // Explorer may keep the last frame of the wallpaper window: repaint the user's own wallpaper.
            RepaintUserWallpaper();
            progress.Report(25, "Suppression des donnees utilisateur...");
            RemoveInteractiveUserData();
            progress.Report(35, "Nettoyage du demarrage Windows...");
            CleanupStartupEntries();
            progress.Report(55, "Suppression du menu Demarrer...");
            RemoveStartMenuShortcut();
            progress.Report(70, "Suppression de l'entree Applications installees...");
            RemoveUninstallEntry();

            string self = Assembly.GetExecutingAssembly().Location;
            string installFullPath = Path.GetFullPath(InstallDir);
            string selfFullPath = Path.GetFullPath(self);
            progress.Report(84, "Suppression des fichiers...");
            if (selfFullPath.StartsWith(installFullPath, StringComparison.OrdinalIgnoreCase))
            {
                ScheduleInstallDirectoryRemoval();
            }
            else if (Directory.Exists(InstallDir))
            {
                installDirReady = false; // the log folder is going away
                if ((File.GetAttributes(InstallDir) & FileAttributes.ReparsePoint) != 0)
                {
                    // Never follow a link planted in place of the folder: remove the link only.
                    Directory.Delete(InstallDir, false);
                    Log("Install directory was a link: only the link was removed.");
                }
                else
                {
                    // Recursive delete removes inner links without following them.
                    Directory.Delete(InstallDir, true);
                }
            }

            progress.Report(100, "Desinstallation terminee.");
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

        private static readonly SecurityIdentifier AdministratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        // C:\ grants "Modify" to Authenticated Users and children inherit it. The folder
        // holds an exe run at every logon (HKLM Run) and the elevated uninstaller, so only
        // administrators and SYSTEM may write to it, and it is owned by Administrators.
        private static DirectorySecurity BuildInstallDirSecurity()
        {
            var security = new DirectorySecurity();
            security.SetOwner(AdministratorsSid);
            security.SetAccessRuleProtection(true, false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid,
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(SystemSid,
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            return security;
        }

        private static bool IsTrustedOwner(FileSystemSecurity security)
        {
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            // The administrator running this elevated installer may also own files it created
            // (depends on the "default owner" policy); a standard user cannot plant those.
            return owner != null && (owner.Equals(AdministratorsSid) || owner.Equals(SystemSid) || owner.Equals(WindowsIdentity.GetCurrent().User));
        }

        // True when every item under the install folder is a real file or folder owned by
        // Administrators or SYSTEM: nothing a standard user could have planted or redirected.
        private static bool IsTrustedInstallDir()
        {
            try
            {
                var root = new DirectoryInfo(InstallDir);
                if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (!IsTrustedOwner(root.GetAccessControl(AccessControlSections.Owner))) return false;
                var pending = new System.Collections.Generic.Stack<DirectoryInfo>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    foreach (FileSystemInfo item in pending.Pop().EnumerateFileSystemInfos())
                    {
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                        var directory = item as DirectoryInfo;
                        FileSystemSecurity security = directory != null
                            ? (FileSystemSecurity)directory.GetAccessControl(AccessControlSections.Owner)
                            : ((FileInfo)item).GetAccessControl(AccessControlSections.Owner);
                        if (!IsTrustedOwner(security)) return false;
                        if (directory != null) pending.Push(directory);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("Install directory check failed: " + ex.Message);
                return false;
            }
        }

        // Creates C:\nexuswpp with its restricted ACL from the start. A reparse point is refused;
        // a folder (or content) owned by someone else is removed first, so no planted file survives.
        private static void PrepareInstallDir()
        {
            var info = new DirectoryInfo(InstallDir);
            if (info.Exists || File.Exists(InstallDir))
            {
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(InstallDir + " is a link (reparse point). Remove it, then run the installer again.");
                }
                if (!info.Exists)
                {
                    throw new InvalidOperationException(InstallDir + " is a file. Remove it, then run the installer again.");
                }
                if (!IsTrustedInstallDir())
                {
                    string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                    if (self.StartsWith(Path.GetFullPath(InstallDir) + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(InstallDir + " is not owned by administrators. Run the installer from another folder.");
                    }
                    Log("Install directory has a foreign owner or a link inside: removing it before installation.");
                    Directory.Delete(InstallDir, true);
                }
            }

            if (!Directory.Exists(InstallDir))
            {
                Directory.CreateDirectory(InstallDir, BuildInstallDirSecurity());
            }
            else
            {
                Directory.SetAccessControl(InstallDir, BuildInstallDirSecurity());
            }

            installDirReady = true;
            Log("Install directory owned by Administrators, ACL restricted to administrators and SYSTEM.");
        }

        private static void VerifyInstallDirSecurity()
        {
            DirectorySecurity security = Directory.GetAccessControl(InstallDir);
            if (!IsTrustedInstallDir() || !security.AreAccessRulesProtected)
            {
                throw new InvalidOperationException("The security of " + InstallDir + " could not be verified after installation.");
            }
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes;
                if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & write) != 0 &&
                    !sid.Equals(AdministratorsSid) && !sid.Equals(SystemSid))
                {
                    throw new InvalidOperationException(InstallDir + " grants write access to " + sid.Value + ".");
                }
            }
            Log("Install directory owner and ACL verified.");
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
                        if (commandLine.IndexOf("--webview-exe-name=nexuswpp.exe", StringComparison.OrdinalIgnoreCase) < 0 &&
                            commandLine.IndexOf("\\nexuswpp\\EBWebView", StringComparison.OrdinalIgnoreCase) < 0)
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
            // Downloaded inside the administrators-only install folder (not %TEMP%), so no
            // standard user can swap the file between the signature check and its launch.
            string downloadDir = Path.Combine(InstallDir, "webview2-setup");
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, true);
            Directory.CreateDirectory(downloadDir);
            string bootstrapper = Path.Combine(downloadDir, "MicrosoftEdgeWebview2Setup.exe");
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(WebView2BootstrapperUrl, bootstrapper);
                }

                string signer;
                if (!IsSignedByMicrosoft(bootstrapper, out signer))
                {
                    throw new InvalidOperationException("The downloaded WebView2 installer is not validly signed by Microsoft (" + signer + "). It was not run.");
                }
                Log("WebView2 bootstrapper signature verified: " + signer);

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
            }
            finally
            {
                try { Directory.Delete(downloadDir, true); } catch (Exception ex) { Log("WebView2 download cleanup failed: " + ex.Message); }
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
                psi.FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
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

        private static void RegisterStartMenuShortcut()
        {
            string shortcutPath = GetCommonStartMenuShortcutPath();
            string shortcutDir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(shortcutDir)) Directory.CreateDirectory(shortcutDir);

            CreateShellShortcut(
                shortcutPath,
                Path.Combine(InstallDir, "nexuswpp.exe"),
                InstallDir,
                Path.Combine(InstallDir, "icon.ico"),
                "NexusWpp dynamic desktop wallpaper");

            Log("Start Menu shortcut registered: " + shortcutPath);
        }

        private static void RemoveStartMenuShortcut()
        {
            DeleteStartMenuShortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
            DeleteStartMenuShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        }

        private static string GetCommonStartMenuShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "NexusWpp.lnk");
        }

        private static void DeleteStartMenuShortcut(string programsFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(programsFolder)) return;
                string shortcut = Path.Combine(programsFolder, "NexusWpp.lnk");
                if (File.Exists(shortcut)) File.Delete(shortcut);
            }
            catch (Exception ex)
            {
                Log("Start Menu shortcut cleanup failed: " + ex.Message);
            }
        }

        private static void CreateShellShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("WScript.Shell is unavailable.");

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                }

                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                }
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
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = cmd;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try { Process.Start(psi); } catch { }
        }

        // The installer is elevated: starting the wallpaper directly would give it (and its WebView2
        // and file dialogs) the administrator token until sign-out. Ask the desktop shell, which runs
        // as the signed-in user without elevation, to start it instead. No fallback to an elevated
        // launch: if the shell is unavailable, the HKLM Run entry starts it at the next sign-in.
        private static bool StartWallpaper()
        {
            string exe = Path.Combine(InstallDir, "nexuswpp.exe");
            try
            {
                UnelevatedShell.Execute(exe, "", InstallDir, UnelevatedShell.SW_SHOWNORMAL);
                Log("Wallpaper started through the desktop shell (not elevated).");
                return true;
            }
            catch (Exception ex)
            {
                Log("Wallpaper not started (desktop shell unavailable): " + ex.Message + ". It starts at the next sign-in.");
                return false;
            }
        }

        // After the process is stopped, Explorer may still show the wallpaper window's last frame.
        // Repaint the interactive user's own wallpaper (same path, setting not written).
        private static void RepaintUserWallpaper()
        {
            try
            {
                const int SPI_GETDESKWALLPAPER = 0x0073, SPI_SETDESKWALLPAPER = 0x0014;
                string wallpaper = null;
                string sid = GetInteractiveUserSid();
                if (sid != null)
                {
                    using (RegistryKey desktop = Registry.Users.OpenSubKey(sid + @"\Control Panel\Desktop", false))
                    {
                        if (desktop != null) wallpaper = Convert.ToString(desktop.GetValue("Wallpaper"));
                    }
                }
                StringBuilder current = new StringBuilder(1024);
                if (wallpaper == null && SystemParametersInfo(SPI_GETDESKWALLPAPER, current.Capacity, current, 0))
                {
                    wallpaper = current.ToString();
                }
                if (wallpaper != null)
                {
                    SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, new StringBuilder(wallpaper), 0);
                    Log("Desktop wallpaper repainted.");
                }
            }
            catch (Exception ex)
            {
                Log("Wallpaper repaint failed: " + ex.Message);
            }
        }

        // %LOCALAPPDATA%\NexusWpp of the signed-in user (custom logo, logs, WebView2 cache). It is a
        // user-writable folder, so it is removed by the user's own unelevated shell: an elevated
        // delete there could be redirected elsewhere through links.
        private static void RemoveInteractiveUserData()
        {
            try
            {
                UnelevatedShell.Execute(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/d /c rmdir /s /q \"%LOCALAPPDATA%\\NexusWpp\"", Environment.SystemDirectory, UnelevatedShell.SW_HIDE);
                Log("User data removal requested through the desktop shell.");
            }
            catch (Exception ex)
            {
                Log("User data not removed (desktop shell unavailable): " + ex.Message);
            }
        }

        // Owner of the Explorer shell in this session: the person signed in at this desktop.
        private static string GetInteractiveUserSid()
        {
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Handle, SessionId FROM Win32_Process WHERE Name = 'explorer.exe' AND SessionId = " + session.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        ManagementBaseObject result = item.InvokeMethod("GetOwnerSid", null, null);
                        string sid = result != null ? Convert.ToString(result["Sid"]) : null;
                        if (!string.IsNullOrEmpty(sid)) return sid;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Interactive user lookup failed: " + ex.Message);
            }
            return null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int winIni);

        // Authenticode check of a downloaded file: a valid, trusted signature (WinVerifyTrust)
        // whose signer certificate belongs to Microsoft Corporation.
        internal static bool IsSignedByMicrosoft(string path, out string signer)
        {
            signer = "unsigned";
            if (!WinTrust.Verify(path)) return false;
            try
            {
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                {
                    signer = certificate.Subject;
                    string commonName = certificate.GetNameInfo(X509NameType.SimpleName, false);
                    return string.Equals(commonName, "Microsoft Corporation", StringComparison.Ordinal) &&
                        certificate.Subject.IndexOf("O=Microsoft Corporation", StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception ex)
            {
                signer = ex.Message;
                return false;
            }
        }

        private static bool installDirReady;

        private static string LogPath
        {
            get
            {
                return installDirReady
                    ? Path.Combine(InstallDir, "install.log")
                    : Path.Combine(Path.GetTempPath(), "NexusWppInstall.log");
            }
        }

        // Never creates C:\nexuswpp: until the folder is prepared with its secure ACL, the log goes to %TEMP%.
        private static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("s") + "] " + message + Environment.NewLine;
            try
            {
                File.AppendAllText(LogPath, line);
            }
            catch
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "NexusWppInstall.log"), line); }
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

    // WinVerifyTrust with the generic Authenticode policy: the file carries an embedded signature
    // that is intact and chains to a trusted root.
    internal static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

        internal static bool Verify(string path)
        {
            const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2;
            var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = path };
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(file, filePointer, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE, // offline CRL servers must not block the install
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = filePointer,
                    dwStateAction = WTD_STATEACTION_VERIFY
                };
                int result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
                return result == 0;
            }
            finally
            {
                Marshal.DestroyStructure(filePointer, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(filePointer);
            }
        }
    }

    // Runs a program through the desktop Explorer (IShellWindows -> desktop view -> Shell.Application
    // ShellExecute), so it starts as the signed-in user without the installer's administrator token.
    internal static class UnelevatedShell
    {
        internal const int SW_HIDE = 0, SW_SHOWNORMAL = 1;
        private const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, SVGIO_BACKGROUND = 0;
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            int Count { get; }
            [return: MarshalAs(UnmanagedType.IDispatch)] object Item([MarshalAs(UnmanagedType.Struct)] object index);
            [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
            void Register();
            void RegisterPending();
            void Revoke();
            void OnNavigate();
            void OnActivated();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object pvarLoc, [MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
                int swClass, out int phwnd, int swfwOptions);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService([MarshalAs(UnmanagedType.LPStruct)] Guid service, [MarshalAs(UnmanagedType.LPStruct)] Guid riid);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow();
            void ContextSensitiveHelp();
            void InsertMenusSB();
            void SetMenuSB();
            void RemoveMenusSB();
            void SetStatusTextSB();
            void EnableModelessSB();
            void TranslateAcceleratorSB();
            void BrowseObject();
            void GetViewStateStream();
            void GetControlWindow();
            void SendControlMsg();
            IShellView QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellView
        {
            void GetWindow();
            void ContextSensitiveHelp();
            void TranslateAccelerator();
            void EnableModeless();
            void UIActivate();
            void Refresh();
            void CreateViewWindow();
            void DestroyViewWindow();
            void GetCurrentInfo();
            void AddPropertySheetPages();
            void SaveViewState();
            void SelectItem();
            [return: MarshalAs(UnmanagedType.Interface)]
            object GetItemObject(uint uItem, [MarshalAs(UnmanagedType.LPStruct)] Guid riid);
        }

        internal static void Execute(string file, string arguments, string directory, int show)
        {
            Exception failure = null;
            // Shell COM objects expect a single-threaded apartment; bound the wait if Explorer hangs.
            var thread = new Thread(() =>
            {
                try { ExecuteOnSta(file, arguments, directory, show); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(15000)) throw new TimeoutException("The desktop shell did not respond.");
            if (failure != null) throw failure;
        }

        private static void ExecuteOnSta(string file, string arguments, string directory, int show)
        {
            Type shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true);
            var shellWindows = (IShellWindows)Activator.CreateInstance(shellWindowsType);
            object location = 0; // CSIDL_DESKTOP
            object root = null;
            int hwnd;
            object desktop = shellWindows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH);
            if (desktop == null) throw new InvalidOperationException("No desktop shell window.");
            var browser = (IShellBrowser)((IServiceProvider)desktop).QueryService(SID_STopLevelBrowser, typeof(IShellBrowser).GUID);
            IShellView view = browser.QueryActiveShellView();
            object folderView = view.GetItemObject(SVGIO_BACKGROUND, new Guid("00020400-0000-0000-C000-000000000046")); // IDispatch
            object application = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null);
            application.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, application,
                new object[] { file, arguments, directory, "open", show });
        }
    }

    internal interface IProgressReporter
    {
        void Report(int percent, string message);
    }

    internal sealed class NullProgressReporter : IProgressReporter
    {
        public void Report(int percent, string message)
        {
        }
    }

    internal sealed class InstallerProgressForm : Form, IProgressReporter
    {
        private readonly Label titleLabel;
        private readonly Label statusLabel;
        private readonly Label percentLabel;
        private readonly Panel progressTrack;
        private readonly Panel progressFill;
        private readonly Button closeButton;
        private bool completed;

        public int ExitCode { get; private set; }

        public InstallerProgressForm(bool uninstall)
        {
            ExitCode = 1;
            Text = uninstall ? "Desinstallation NexusWpp " + Program.ProductVersion : "Installation NexusWpp " + Program.ProductVersion;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ClientSize = new Size(520, 230);
            BackColor = Color.FromArgb(22, 26, 31);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            titleLabel = new Label();
            titleLabel.AutoSize = false;
            titleLabel.Left = 28;
            titleLabel.Top = 24;
            titleLabel.Width = 464;
            titleLabel.Height = 34;
            titleLabel.Text = uninstall ? "Desinstallation de NexusWpp " + Program.ProductVersion : "Installation de NexusWpp " + Program.ProductVersion;
            titleLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.White;

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Left = 30;
            statusLabel.Top = 72;
            statusLabel.Width = 460;
            statusLabel.Height = 44;
            statusLabel.Text = "Preparation...";
            statusLabel.ForeColor = Color.FromArgb(210, 220, 230);

            progressTrack = new Panel();
            progressTrack.Left = 30;
            progressTrack.Top = 130;
            progressTrack.Width = 460;
            progressTrack.Height = 12;
            progressTrack.BackColor = Color.FromArgb(48, 55, 64);

            progressFill = new Panel();
            progressFill.Left = 0;
            progressFill.Top = 0;
            progressFill.Width = 0;
            progressFill.Height = progressTrack.Height;
            progressFill.BackColor = Color.FromArgb(0, 199, 190);
            progressTrack.Controls.Add(progressFill);

            percentLabel = new Label();
            percentLabel.AutoSize = false;
            percentLabel.Left = 30;
            percentLabel.Top = 150;
            percentLabel.Width = 460;
            percentLabel.Height = 28;
            percentLabel.Text = "0 %";
            percentLabel.TextAlign = ContentAlignment.MiddleRight;
            percentLabel.ForeColor = Color.FromArgb(150, 166, 178);

            closeButton = new Button();
            closeButton.Left = 370;
            closeButton.Top = 178;
            closeButton.Width = 120;
            closeButton.Height = 34;
            closeButton.Text = "Fermer";
            closeButton.Enabled = false;
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderColor = Color.FromArgb(80, 94, 108);
            closeButton.BackColor = Color.FromArgb(36, 43, 51);
            closeButton.ForeColor = Color.White;
            closeButton.Click += delegate { Close(); };

            Controls.Add(titleLabel);
            Controls.Add(statusLabel);
            Controls.Add(progressTrack);
            Controls.Add(percentLabel);
            Controls.Add(closeButton);
        }

        public void Report(int percent, string message)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(Report), percent, message);
                return;
            }

            int safePercent = Math.Max(0, Math.Min(100, percent));
            statusLabel.Text = message;
            percentLabel.Text = safePercent.ToString() + " %";
            progressFill.Width = (int)Math.Round(progressTrack.Width * (safePercent / 100.0));
            progressFill.Refresh();
        }

        public void Complete(bool success, string message, int exitCode)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string, int>(Complete), success, message, exitCode);
                return;
            }

            completed = true;
            ExitCode = exitCode;
            Report(100, message);
            progressFill.BackColor = success ? Color.FromArgb(48, 209, 88) : Color.FromArgb(255, 69, 58);
            titleLabel.Text = success ? "Termine" : "Erreur d'installation";
            closeButton.Enabled = true;
            closeButton.Focus();
            ControlBox = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!completed)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }
    }
}
