using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopHtmlHost;
using Microsoft.Web.WebView2.Core;

// Host lifecycle and placement, compiled with the host sources. Only hidden or off-screen
// windows owned by this process are created; the desktop, Explorer and the user's log are untouched.
static class HostRegression
{
    static int failures;

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }

    const int GWL_STYLE = -16, WS_CHILD = 0x40000000, WS_POPUP = unchecked((int)0x80000000);

    static void Check(bool condition, string label)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition) failures++;
    }

    static Rectangle RectOf(IntPtr window)
    {
        NativeRect r;
        GetWindowRect(window, out r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    // Hidden top-level popup, like WorkerW spanning the virtual screen.
    sealed class Popup : NativeWindow, IDisposable
    {
        public Popup(Rectangle bounds)
        {
            CreateHandle(new CreateParams { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, Style = WS_POPUP });
        }
        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }

    // Reproduces the host attach: a top-level window parked off screen becomes a WS_CHILD.
    static void Reparent(IntPtr child, IntPtr parent)
    {
        SetParent(child, parent);
        SetWindowLong(child, GWL_STYLE, (GetWindowLong(child, GWL_STYLE) | WS_CHILD) & ~WS_POPUP);
    }

    static void Placement()
    {
        var parents = new Dictionary<string, Rectangle>
        {
            { "monitor left of primary", new Rectangle(-1920, 0, 3840, 1080) },
            { "monitor above primary", new Rectangle(0, -1080, 1920, 2160) },
            { "single monitor", new Rectangle(0, 0, 1920, 1080) },
            { "mixed negative origin", new Rectangle(-2560, -360, 4480, 1440) }
        };
        foreach (var item in parents)
        {
            using (var parent = new Popup(item.Value))
            using (var child = new Popup(new Rectangle(-32000, -32000, 100, 100)))
            {
                Check(RectOf(parent.Handle) == item.Value, "hidden parent placed for " + item.Key);
                Reparent(child.Handle, parent.Handle);
                Check(DesktopForm.PlaceOverScreenRect(child.Handle, item.Value), "child placement accepted for " + item.Key);
                Check(RectOf(child.Handle) == RectOf(parent.Handle), "child covers its desktop parent exactly: " + item.Key);
            }
        }
        using (var topLevel = new Popup(new Rectangle(-32000, -32000, 100, 100)))
        {
            var target = new Rectangle(-1920, -200, 3840, 1280);
            DesktopForm.PlaceOverScreenRect(topLevel.Handle, target);
            Check(RectOf(topLevel.Handle) == target, "before attach, a top-level window uses screen coordinates");
        }
    }

    static void Layout()
    {
        Check(DesktopForm.BuildLayoutMessage(new Rectangle(-1920, 0, 3840, 1080), new Rectangle(0, 0, 1920, 1080)) ==
            "{\"control\":\"LAYOUT\",\"window\":{\"w\":3840,\"h\":1080},\"primary\":{\"x\":1920,\"y\":0,\"w\":1920,\"h\":1080}}",
            "layout message gives the primary monitor relative to the window");
        Check(DesktopForm.BuildLayoutMessage(new Rectangle(0, 0, 2560, 1440), new Rectangle(0, 0, 2560, 1440)) ==
            "{\"control\":\"LAYOUT\",\"window\":{\"w\":2560,\"h\":1440},\"primary\":{\"x\":0,\"y\":0,\"w\":2560,\"h\":1440}}",
            "single monitor layout equals the whole window");
    }

    static void Recovery()
    {
        Check(DesktopForm.RecoveryDelayMs(0) == 0 && DesktopForm.RecoveryDelayMs(1) == 1000 && DesktopForm.RecoveryDelayMs(2) == 2000 &&
            DesktopForm.RecoveryDelayMs(6) == 32000 && DesktopForm.RecoveryDelayMs(7) == 60000 && DesktopForm.RecoveryDelayMs(40) == 60000,
            "recovery is immediate first, then backs off up to 60 s");
        var disposedCore = new InvalidOperationException("CoreWebView2 members cannot be accessed after the WebView2 control is disposed.");
        Check(DesktopForm.ShouldRecoverAfterPostFailure(disposedCore, 1), "disposed CoreWebView2 recovers on the first failed post");
        Check(DesktopForm.ShouldRecoverAfterPostFailure(new ObjectDisposedException("WebView2"), 1), "disposed control recovers on the first failed post");
        var busy = new InvalidOperationException("transient");
        Check(!DesktopForm.ShouldRecoverAfterPostFailure(busy, DesktopForm.MaxFailedPosts - 1), "isolated failed posts are tolerated");
        Check(DesktopForm.ShouldRecoverAfterPostFailure(busy, DesktopForm.MaxFailedPosts), "five consecutive failed posts trigger recovery");

        Check(DesktopForm.ClassifyProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited) == DesktopForm.ProcessFailureAction.LogOnly &&
            DesktopForm.ClassifyProcessFailure(CoreWebView2ProcessFailedKind.UtilityProcessExited) == DesktopForm.ProcessFailureAction.LogOnly,
            "GPU and utility process exits keep the scene and histories");
        Check(DesktopForm.ClassifyProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessExited) == DesktopForm.ProcessFailureAction.Reload &&
            DesktopForm.ClassifyProcessFailure(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) == DesktopForm.ProcessFailureAction.Reload,
            "renderer exit or hang reloads the page");
        Check(DesktopForm.ClassifyProcessFailure(CoreWebView2ProcessFailedKind.BrowserProcessExited) == DesktopForm.ProcessFailureAction.RecreateEnvironment,
            "browser process exit recreates the environment");
        Check(DesktopForm.ClassifyProcessFailure(CoreWebView2ProcessFailedKind.FrameRenderProcessExited) == DesktopForm.ProcessFailureAction.LogOnly,
            "frame renderer exit is only logged");
    }

    static void ShellEvents()
    {
        int restarts = 0;
        var display = new List<bool>();
        using (var window = new ShellEventWindow(() => restarts++, on => display.Add(on)))
        {
            Check(window.DisplayNotificationsRegistered, "display power notifications registered");
            Application.DoEvents();
            display.Clear(); // Windows reports the current state right after registration.
            Check(ShellEventWindow.TaskbarCreatedMessage != 0, "TaskbarCreated message registered");
            SendMessage(window.Handle, unchecked((int)ShellEventWindow.TaskbarCreatedMessage), IntPtr.Zero, IntPtr.Zero);
            Check(restarts == 1, "Explorer restart broadcast reaches the host");
            foreach (var item in new[] { Tuple.Create(ShellEventWindow.ConsoleDisplayState, 0), Tuple.Create(ShellEventWindow.ConsoleDisplayState, 1),
                Tuple.Create(ShellEventWindow.ConsoleDisplayState, 2), Tuple.Create(ShellEventWindow.MonitorPowerOn, 0), Tuple.Create(Guid.NewGuid(), 0) })
            {
                IntPtr setting = ShellEventWindow.AllocateDisplayPowerSetting(item.Item1, item.Item2);
                try { SendMessage(window.Handle, ShellEventWindow.WM_POWERBROADCAST, (IntPtr)ShellEventWindow.PBT_POWERSETTINGCHANGE, setting); }
                finally { Marshal.FreeHGlobal(setting); }
            }
            Check(string.Join(",", display) == "False,True,True,False", "display off pauses; on and dimmed resume; unrelated settings ignored");
        }
    }

    static void Visibility()
    {
        Check(DesktopVisibility.GetMonitorWorkAreas().Count == Screen.AllScreens.Length, "every monitor work area enumerated");
        IntPtr before = GetThreadDpiAwarenessContext();
        string reason;
        DesktopVisibility.ShouldSuspend(IntPtr.Zero, out reason);
        Check(AreDpiAwarenessContextsEqual(before, GetThreadDpiAwarenessContext()), "coverage check restores the thread DPI context");
        Check(!string.IsNullOrEmpty(reason), "coverage check reports a reason");
    }

    // Stand-in for DesktopForm: same contract, no WebView2, tray icon or desktop attach.
    sealed class FakeWallpaper : Form, IDesktopHostWindow
    {
        bool closing;
        public bool HandleLost { get; private set; }
        public FakeWallpaper()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(100, 100);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; base.OnFormClosing(e); }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (!closing && !RecreatingHandle) HandleLost = true;
            base.OnHandleDestroyed(e);
        }
    }

    static void ParentDestroyed()
    {
        var forms = new List<FakeWallpaper>();
        bool exitedByClose = false;
        DesktopHostContext context = null;
        context = new DesktopHostContext(() => { var form = new FakeWallpaper(); forms.Add(form); return form; });
        int step = 0;
        var timer = new Timer { Interval = 100 };
        var started = DateTime.Now;
        timer.Tick += delegate
        {
            double elapsed = (DateTime.Now - started).TotalMilliseconds;
            if (step == 0)
            {
                step = 1;
                using (var parent = new Popup(new Rectangle(-1920, 0, 3840, 1080)))
                {
                    Reparent(forms[0].Handle, parent.Handle);
                } // Destroying the parent destroys the attached wallpaper window, as Explorer does on exit.
                Check(forms[0].HandleLost && !forms[0].IsHandleCreated, "parent destruction destroys the wallpaper window");
            }
            else if (step == 1 && forms.Count == 2)
            {
                step = 2;
                Check(forms[0].IsDisposed, "lost wallpaper form is disposed");
                Check(ReferenceEquals(context.CurrentForm, forms[1]) && forms[1].Visible, "a new wallpaper form is created and shown");
                forms[1].Close();
            }
            else if (elapsed > 10000)
            {
                timer.Stop();
                Check(false, "host context reacts within 10 s");
                context.ExitThread();
            }
        };
        context.ThreadExit += delegate { exitedByClose = step == 2; timer.Stop(); };
        timer.Start();
        Application.Run(context);
        Check(exitedByClose && forms.Count == 2, "a requested close ends the message loop");
        context.Dispose();
    }

    [STAThread]
    static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : Path.GetTempPath();
        Directory.CreateDirectory(output);
        Program.LogPathOverride = Path.Combine(output, "host-regression.log");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        try
        {
            Placement();
            Layout();
            Recovery();
            ShellEvents();
            Visibility();
            ParentDestroyed();
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error);
            failures++;
        }
        Console.WriteLine(failures == 0 ? "RESULT host regressions passed" : "RESULT " + failures + " host regressions failed");
        return failures == 0 ? 0 : 1;
    }
}
