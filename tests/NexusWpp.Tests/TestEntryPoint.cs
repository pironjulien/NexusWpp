using System.Linq;

namespace NexusWpp.Tests;

// The xUnit v3 entry point (same code as the one xUnit generates), with two additions:
// - the host's own startup configuration (DPI per monitor v2, visual styles) before any window, as Program.Main does;
// - "--parent-helper": the host test starts this executable as a stand-in for Explorer that it then kills;
// - "--cover-helper": the same, as a stand-in for an application window on another virtual desktop.
static class TestEntryPoint
{
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 1 && args[0] == HostRegression.ParentHelperArgument)
            return HostRegression.RunParentHelper();
        if (args.Length == 1 && args[0] == HostRegression.CoverHelperArgument)
            return HostRegression.RunCoverHelper();
        if (args.Any(arg => arg == "--server" || arg == "--internal-msbuild-node"))
            return Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult();
        return Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }
}
