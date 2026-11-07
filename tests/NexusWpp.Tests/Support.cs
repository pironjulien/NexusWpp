using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;

// Harness output (PASS / FAIL lines) goes to each test's own output.
[assembly: CaptureConsole]

namespace NexusWpp.Tests
{
    // The repository these tests were built from (source checks read its files, harnesses load its page).
    static class Repository
    {
        internal static readonly string Root = typeof(Repository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "NexusWpp.Repository").Value!;

        // A fresh folder under work\ (ignored by Git) for one harness's images, logs and results.
        internal static string Output(params string[] parts)
        {
            string path = Path.Combine([Root, "work", .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    // WinForms and WebView2 harnesses: one STA thread per run, as in the host's UI thread. Their windows are off screen
    // and never activated (WS_EX_NOACTIVATE), so a run never takes the foreground or the keyboard.
    static partial class Sta
    {
        [LibraryImport("user32.dll")]
        private static partial IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        private static readonly IntPtr DpiUnaware = new(-1); // DPI_AWARENESS_CONTEXT_UNAWARE

        // dpiUnaware: the page harnesses set the device scale themselves through DevTools emulation, so their window must
        // be one CSS pixel per device pixel whatever the scale of the PC's screen (the per-monitor host mode would scale it
        // a second time). Windows created on this thread are then DPI unaware; the process keeps the host's mode.
        internal static int Run(Func<int> harness, bool dpiUnaware = false)
        {
            int result = -1;
            ExceptionDispatchInfo? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (dpiUnaware && SetThreadDpiAwarenessContext(DpiUnaware) == IntPtr.Zero)
                        throw new InvalidOperationException("The harness thread could not be made DPI unaware.");
                    result = harness();
                }
                catch (Exception exception) { error = ExceptionDispatchInfo.Capture(exception); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            error?.Throw();
            return result;
        }
    }

    // One collection for every window harness: they run one after the other, after the parallel unit tests, like the
    // separate programs they were.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WindowHarnesses
    {
        internal const string Name = "Window harnesses";
    }
}
