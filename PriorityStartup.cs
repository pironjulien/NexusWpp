using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace DesktopHtmlHost
{
    // Store build only. Windows starts a package's startup task after every Run entry and its
    // startup delay (about a minute after sign-in); a per-user logon task in Task Scheduler fires
    // with the desktop instead. While it exists the package startup task is turned off, so
    // NexusWpp is launched once, and turned back on when the option is cleared.
    internal static class PriorityStartup
    {
        internal const string TaskName = "NexusWpp Priority Startup";
        const string StartupTaskId = "NexusWppStartup";
        const int TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_INTERACTIVE_TOKEN = 3;

        // App execution alias declared in the package manifest; it survives package updates.
        internal static string LauncherPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\nexuswpp.exe"); }
        }

        internal static bool Available
        {
            get { return Program.IsPackaged && File.Exists(LauncherPath); }
        }

        static Task<StartupTask> PackageStartupTask()
        {
            return StartupTask.GetAsync(StartupTaskId).AsTask();
        }

        static dynamic RootFolder()
        {
            Type scheduler = Type.GetTypeFromProgID("Schedule.Service", true) ?? throw new InvalidOperationException("Task Scheduler is not registered.");
            dynamic service = Activator.CreateInstance(scheduler) ?? throw new InvalidOperationException("Task Scheduler could not be created.");
            service.Connect();
            return service.GetFolder(@"\");
        }

        internal static bool IsEnabled
        {
            get
            {
                try { RootFolder().GetTask(TaskName); return true; }
                catch { return false; }
            }
        }

        internal static string BuildTaskXml(string user, string launcher)
        {
            string u = System.Security.SecurityElement.Escape(user), l = System.Security.SecurityElement.Escape(launcher);
            // Priority 4 is the normal class: the Task Scheduler default (7) would slow the whole wallpaper.
            // Fired at the very second of sign-in, the package can still fail to activate (0xD0004001,
            // seen on 2026-11-06 after a restart): Task Scheduler then retries every minute, three times.
            // A second launch is harmless: the running instance's mutex makes it exit with code 0.
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Author>NexusWpp</Author><Description>Starts the NexusWpp wallpaper with the desktop.</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + u + "</UserId></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + u + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority>" +
                "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + l + "</Command></Exec></Actions></Task>";
        }

        static void RegisterLogonTask()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            RootFolder().RegisterTask(TaskName, BuildTaskXml(user, LauncherPath), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
        }

        internal static async Task EnableAsync()
        {
            if (!Available) throw new InvalidOperationException("The nexuswpp.exe app execution alias is missing.");
            RegisterLogonTask();
            StartupTask startup = await PackageStartupTask();
            if (startup.State == StartupTaskState.Enabled) startup.Disable();
            Program.LogDebug("Priority startup enabled (logon task " + TaskName + ").");
        }

        // Returns false when the user turned NexusWpp off in Settings > Apps > Startup.
        internal static async Task<bool> DisableAsync()
        {
            try { RootFolder().DeleteTask(TaskName, 0); } catch { }
            StartupTask startup = await PackageStartupTask();
            StartupTaskState state = startup.State == StartupTaskState.Disabled ? await startup.RequestEnableAsync() : startup.State;
            Program.LogDebug("Priority startup disabled; package startup task " + state + ".");
            return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy;
        }

        // At launch: a logon task whose alias is gone (alias turned off in Settings) would start
        // nothing, and the package startup task must stay off while the logon task exists.
        internal static async Task ReconcileAsync()
        {
            if (!Program.IsPackaged) return;
            try
            {
                bool task = IsEnabled;
                if (task && !File.Exists(LauncherPath)) { await DisableAsync(); return; }
                if (!task) return;
                // Rewritten at each launch so a task made by an earlier version gets the current settings.
                RegisterLogonTask();
                StartupTask startup = await PackageStartupTask();
                if (startup.State == StartupTaskState.Enabled) startup.Disable();
            }
            catch (Exception ex)
            {
                Program.LogDebug("Priority startup check failed: " + ex.Message);
            }
        }
    }
}
