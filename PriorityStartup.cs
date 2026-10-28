using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;

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

        // The split WinMetadata files lack the unified "Windows" assembly that the framework's
        // await extensions expect, so WinRT operations are bridged to a Task here.
        static Task<T> Run<T>(IAsyncOperation<T> operation)
        {
            var result = new TaskCompletionSource<T>();
            operation.Completed = (op, status) =>
            {
                if (status == AsyncStatus.Completed) result.TrySetResult(op.GetResults());
                else result.TrySetException(op.ErrorCode ?? new OperationCanceledException());
            };
            return result.Task;
        }

        static Task<StartupTask> PackageStartupTask()
        {
            return Run(StartupTask.GetAsync(StartupTaskId));
        }

        static dynamic RootFolder()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
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
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Author>NexusWpp</Author><Description>Starts the NexusWpp wallpaper with the desktop.</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + u + "</UserId></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + u + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority>" +
                "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + l + "</Command></Exec></Actions></Task>";
        }

        internal static async Task EnableAsync()
        {
            if (!Available) throw new InvalidOperationException("The nexuswpp.exe app execution alias is missing.");
            string user = WindowsIdentity.GetCurrent().Name;
            RootFolder().RegisterTask(TaskName, BuildTaskXml(user, LauncherPath), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
            StartupTask startup = await PackageStartupTask();
            if (startup.State == StartupTaskState.Enabled) startup.Disable();
            Program.LogDebug("Priority startup enabled (logon task " + TaskName + ").");
        }

        // Returns false when the user turned NexusWpp off in Settings > Apps > Startup.
        internal static async Task<bool> DisableAsync()
        {
            try { RootFolder().DeleteTask(TaskName, 0); } catch { }
            StartupTask startup = await PackageStartupTask();
            StartupTaskState state = startup.State == StartupTaskState.Disabled ? await Run(startup.RequestEnableAsync()) : startup.State;
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
