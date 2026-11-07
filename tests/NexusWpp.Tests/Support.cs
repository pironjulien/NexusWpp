using System.Linq;
using System.Reflection;

// The host's debug log, as the host sources compiled here call it: tests write nothing.
namespace DesktopHtmlHost
{
    static class Program
    {
        internal static void LogDebug(string _) { }
    }
}

namespace NexusWpp.Tests
{
    // The repository these tests were built from (source checks read its files).
    static class Repository
    {
        internal static readonly string Root = typeof(Repository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "NexusWpp.Repository").Value!;
    }
}