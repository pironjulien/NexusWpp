using System;
using System.Runtime.InteropServices;

namespace NexusWpp.Tests;

/// <summary>CPU time (user + kernel) of the calling thread: a cost that does not grow when other processes hold the
/// cores, unlike a stopwatch.</summary>
internal static partial class ThreadCpu
{
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    /// <summary>Microseconds of CPU used so far by the calling thread.</summary>
    internal static long Microseconds()
    {
        if (!GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user))
            throw new InvalidOperationException("GetThreadTimes failed: " + Marshal.GetLastPInvokeError());
        return (kernel + user) / 10;  // 100-nanosecond units
    }
}
