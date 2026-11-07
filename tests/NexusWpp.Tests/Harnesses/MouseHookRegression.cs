using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Exercises the built host without starting its wallpaper or injecting user input.
internal static class MouseHookRegression
{
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static Type? host;

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }
    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [DllImport("comctl32.dll")]
    static extern void InitCommonControls();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]
    static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    sealed class Receiver : NativeWindow, IDisposable
    {
        public readonly List<string> Messages = new List<string>();
        public Receiver()
        {
            CreateHandle(new CreateParams { Caption = "NexusWpp input regression", Width = 200, Height = 100 });
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x201 || message.Msg == 0x202)
            {
                long coordinates = message.LParam.ToInt64();
                Messages.Add(message.Msg + ":" + unchecked((short)coordinates) + ":" + unchecked((short)(coordinates >> 16)));
            }
            base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }

    sealed class ChildWindow : NativeWindow, IDisposable
    {
        public ChildWindow(IntPtr parent)
        {
            CreateHandle(new CreateParams { Parent = parent, Style = 0x40000000 /* WS_CHILD */, Width = 50, Height = 50 });
        }
        public void Dispose() { DestroyHandle(); }
    }

    // MSLLHOOKSTRUCT as Windows passes it to WH_MOUSE_LL.
    static IntPtr HookEvent(int x, int y)
    {
        IntPtr data = Marshal.AllocHGlobal(32);
        for (int offset = 0; offset < 32; offset += 4) Marshal.WriteInt32(data, offset, 0);
        Marshal.WriteInt32(data, 0, x);
        Marshal.WriteInt32(data, 4, y);
        return data;
    }

    // Private members of the built host, read by reflection; a missing one fails the run.
    static Type Host { get { return host ?? throw new InvalidOperationException("The host assembly is not loaded."); } }
    static MethodInfo HostMethod(string name) { return Host.GetMethod(name, PrivateStatic) ?? throw new MissingMethodException(Host.FullName, name); }
    static FieldInfo HostField(string name) { return Host.GetField(name, PrivateStatic) ?? throw new MissingFieldException(Host.FullName, name); }
    static object? Invoke(string method, params object?[] args) { return HostMethod(method).Invoke(null, args); }
    static T Invoke<T>(string method, params object?[] args) { return (T)(Invoke(method, args) ?? throw new InvalidOperationException(method + " returned nothing.")); }
    static object? Field(string name) { return HostField(name).GetValue(null); }
    static T Field<T>(string name) { return (T)(Field(name) ?? throw new InvalidOperationException(name + " is null.")); }
    static void Check([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
    static bool WaitUntil(Func<bool> condition)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 2000) Thread.Sleep(1);
        return condition();
    }

    // Run by HarnessTests on an STA thread, after the host's startup configuration (TestEntryPoint).
    internal static int Run()
    {
        try
        {
            // The host sources are compiled into this test assembly.
            host = typeof(MouseHookRegression).Assembly.GetType("DesktopHtmlHost.DesktopForm", true);
            // The host's own startup configuration ran before any window (TestEntryPoint, same DPI mode as
            // NexusWpp.csproj, see HostSourceTests): hook points, window rectangles and Chromium client
            // coordinates are then all physical pixels, as in the running wallpaper.
            Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)), "host starts per-monitor v2 DPI aware");
            // These paths must not dereference a pointer or access a UI control.
            Check(Invoke<IntPtr>("HookCallback", -1, (IntPtr)0x201, IntPtr.Zero) == IntPtr.Zero, "negative hook code passes through");
            Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x200, IntPtr.Zero) == IntPtr.Zero, "motion passes through without reading event data");
            Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x20A, IntPtr.Zero) == IntPtr.Zero, "wheel passes through without reading event data");
            Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x201, IntPtr.Zero) == IntPtr.Zero, "absent routing does not swallow a click");

            IntPtr destroyed;
            using (Receiver receiver = new Receiver())
            {
                destroyed = receiver.Handle;
                foreach (Point local in new[] { new Point { X = 23, Y = 37 }, new Point { X = -9, Y = -7 } })
                {
                    Point screen = local;
                    Check(ClientToScreen(receiver.Handle, ref screen), "test receiver screen coordinates");
                    Check(Invoke<bool>("ForwardMouseClick", receiver.Handle, screen.X, screen.Y, (uint)0x201), "button down accepted by native target");
                    Check(Invoke<bool>("ForwardMouseClick", receiver.Handle, screen.X, screen.Y, (uint)0x202), "button up accepted by native target");
                }
                Check(WaitUntil(delegate { Application.DoEvents(); return receiver.Messages.Count == 4; }), "posted clicks delivered without synchronous UI invocation");
                Check(string.Join(",", receiver.Messages.ToArray()) == "513:23:37,514:23:37,513:-9:-7,514:-9:-7", "click order and signed client coordinates preserved");
            }
            Check(!Invoke<bool>("ForwardMouseClick", destroyed, 0, 0, (uint)0x201), "destroyed target does not swallow a click");

            // Hit-test rule: only the wallpaper and the desktop layers, never other windows of this process.
            using (Receiver ownWindow = new Receiver())
            using (Receiver hostWindow = new Receiver())
            using (ChildWindow hostChild = new ChildWindow(hostWindow.Handle))
            {
                IntPtr render = hostChild.Handle;
                Check(!Invoke<bool>("ShouldForwardHit", ownWindow.Handle, hostWindow.Handle, render), "own top-level window above the panel keeps its click");
                Check(!Invoke<bool>("ShouldForwardHit", IntPtr.Zero, hostWindow.Handle, render), "no hit window is not forwarded");
                Check(Invoke<bool>("ShouldForwardHit", render, hostWindow.Handle, render), "render window is forwarded");
                Check(Invoke<bool>("ShouldForwardHit", hostChild.Handle, hostWindow.Handle, IntPtr.Zero), "child of the wallpaper host is forwarded");
                InitCommonControls();
                IntPtr desktopIcons = CreateWindowEx(0, "SysListView32", "", 0, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                try { Check(desktopIcons != IntPtr.Zero && Invoke<bool>("ShouldForwardHit", desktopIcons, hostWindow.Handle, render), "desktop icon list (SysListView32) is forwarded"); }
                finally { DestroyWindow(desktopIcons); }
            }

            // Button pairing: an up follows its down, whatever the panel bounds say.
            using (Receiver target = new Receiver())
            {
                Point origin = new Point();
                ClientToScreen(target.Handle, ref origin);
                System.Drawing.Rectangle panel = new System.Drawing.Rectangle(origin.X, origin.Y, 100, 50);
                Type snapshotType = Host.GetNestedType("MouseRoutingSnapshot", BindingFlags.NonPublic)
                    ?? throw new MissingMemberException(Host.FullName, "MouseRoutingSnapshot");
                object? snapshot = Activator.CreateInstance(snapshotType, BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new object[] { panel, target.Handle, target.Handle }, null);
                HostField("mouseRouting").SetValue(null, snapshot);
                IntPtr outside = HookEvent(panel.Right + 400, panel.Bottom + 400), inside = HookEvent(panel.X + 10, panel.Y + 10);
                try
                {
                    Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x201, outside) == IntPtr.Zero, "press outside the panel passes through");
                    Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x202, inside) == IntPtr.Zero, "release inside the panel after an outside press is not swallowed");
                    HostField("pairedRenderWindow").SetValue(null, target.Handle);
                    Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x202, outside) == (IntPtr)1, "release after a forwarded press follows it outside the panel");
                    Check(Field<IntPtr>("pairedRenderWindow") == IntPtr.Zero, "pairing ends with the release");
                    Check(Invoke<IntPtr>("HookCallback", 0, (IntPtr)0x202, outside) == IntPtr.Zero, "a second release is not swallowed");
                    Check(WaitUntil(delegate { Application.DoEvents(); return target.Messages.Count == 1; }) && target.Messages[0].StartsWith("514:", StringComparison.Ordinal),
                        "only the paired release reaches Chromium");
                }
                finally
                {
                    HostField("mouseRouting").SetValue(null, null);
                    Marshal.FreeHGlobal(outside);
                    Marshal.FreeHGlobal(inside);
                }
            }

            System.Drawing.Rectangle client = new System.Drawing.Rectangle(0, 0, 1920, 1080);
            Check(Invoke<System.Drawing.Rectangle>("ClipPanelBounds", new System.Drawing.Rectangle(1800, 1000, 300, 200), client) == new System.Drawing.Rectangle(1800, 1000, 120, 80), "panel bounds are clipped to the client area");
            Check((Invoke<System.Drawing.Rectangle>("ClipPanelBounds", new System.Drawing.Rectangle(2000, 0, 50, 50), client)).IsEmpty, "panel outside the client area routes nothing");
            Check((Invoke<System.Drawing.Rectangle>("ClipPanelBounds", new System.Drawing.Rectangle(10, 10, 0, 40), client)).IsEmpty, "empty panel routes nothing");

            for (int cycle = 0; cycle < 10; cycle++)
            {
                Invoke("StartMouseHook");
                Check(WaitUntil(delegate { return Field<IntPtr>("hookId") != IntPtr.Zero; }), "native hook installed");
                Check(unchecked((uint)Field<int>("mouseHookThreadId")) != GetCurrentThreadId(), "hook serviced independently of caller UI thread");
                Thread hookThread = Field<Thread>("mouseHookThread");
                Invoke("StopMouseHook");
                Check(hookThread.Join(2000) && Field<IntPtr>("hookId") == IntPtr.Zero, "hook removed and message pump stopped");
            }
            for (int cycle = 0; cycle < 10; cycle++)
            {
                Invoke("StartMouseHook");
                Thread hookThread = Field<Thread>("mouseHookThread");
                Invoke("StopMouseHook");
                Check(hookThread.Join(2000) && Field<IntPtr>("hookId") == IntPtr.Zero, "immediate shutdown cannot strand a hook");
            }
            // Paused runtime: no global hook. Resume reinstalls it, even right after the pause.
            Invoke("StartMouseHook");
            Check(WaitUntil(delegate { return Field<IntPtr>("hookId") != IntPtr.Zero; }), "hook installed while visible");
            for (int cycle = 0; cycle < 5; cycle++)
            {
                Thread running = Field<Thread>("mouseHookThread");
                Invoke("UpdateMouseHookForRuntime", true);
                Check(running.Join(2000) && Field<IntPtr>("hookId") == IntPtr.Zero, "hook absent while paused");
                Invoke("UpdateMouseHookForRuntime", false);
                Check(WaitUntil(delegate { return Field<IntPtr>("hookId") != IntPtr.Zero; }), "resume reinstalls the hook");
                Invoke("UpdateMouseHookForRuntime", true);
                Invoke("UpdateMouseHookForRuntime", false);
                Check(WaitUntil(delegate { return Field<IntPtr>("hookId") != IntPtr.Zero && (Field<Thread>("mouseHookThread")).IsAlive; }), "immediate pause and resume keeps a hook installed");
            }
            Thread last = Field<Thread>("mouseHookThread");
            Invoke("StopMouseHook");
            Check(last.Join(2000) && Field<IntPtr>("hookId") == IntPtr.Zero, "final stop removes the hook");
            Check(Field("mouseHookError") == null, "no callback or native lifecycle errors");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (host != null) Invoke("StopMouseHook"); }
    }
}
