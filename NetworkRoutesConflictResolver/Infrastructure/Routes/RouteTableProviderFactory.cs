using System.Runtime.InteropServices;

namespace NetworkRoutesConflictResolver.Infrastructure.Routes;

/// <summary>
/// Selects the live route provider for the current OS. Live collection is implemented for Windows
/// and Linux; macOS should use the file-based <c>analyze</c> workflow until its native provider
/// lands (staged roadmap, see .claude/tasks/live-routes-linux-macos).
/// </summary>
public static class RouteTableProviderFactory
{
    public static IRouteTableProvider Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsRouteTableProvider();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxRouteTableProvider();
        }

        throw new PlatformNotSupportedException(
            "Live route collection is currently implemented for Windows and Linux only. " +
            "On macOS, capture route dumps to files and use the 'analyze' command instead.");
    }
}
