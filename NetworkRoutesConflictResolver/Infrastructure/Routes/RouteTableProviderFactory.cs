using System.Runtime.InteropServices;

namespace NetworkRoutesConflictResolver.Infrastructure.Routes;

/// <summary>
/// Selects the live route provider for the current OS. Live collection is currently implemented
/// for Windows; other platforms should use the file-based <c>analyze</c> workflow until their
/// native providers land (staged roadmap).
/// </summary>
public static class RouteTableProviderFactory
{
    public static IRouteTableProvider Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsRouteTableProvider();
        }

        throw new PlatformNotSupportedException(
            "Live route collection is currently implemented for Windows only. " +
            "On macOS/Linux, capture route dumps to files and use the 'analyze' command instead.");
    }
}
