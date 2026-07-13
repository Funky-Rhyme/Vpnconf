using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Infrastructure.Routes;

/// <summary>
/// Supplies the live routing table already normalized to <see cref="RouteEntry"/> (including a
/// reliable interface index). Distinct from <see cref="IRouteSource"/>, which yields raw text
/// dumps for the file-based workflow.
/// </summary>
public interface IRouteTableProvider
{
    Task<IReadOnlyList<RouteEntry>> GetRoutesAsync(CancellationToken cancellationToken = default);
}
