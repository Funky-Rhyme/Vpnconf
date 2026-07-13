using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Engine;

/// <summary>
/// Finds conflicts between VPN-added routes and the personal ip-list: a conflict is a personal
/// CIDR that overlaps one or more VPN routes. Pure and deterministic.
/// </summary>
public sealed class ConflictDetector
{
    /// <summary>
    /// Routes present in <paramref name="after"/> but not in <paramref name="before"/>, compared
    /// by destination block. This is the set the VPN added on connect (before/after workflow).
    /// </summary>
    public IReadOnlyList<RouteEntry> ComputeAddedRoutes(
        IReadOnlyList<RouteEntry> before,
        IReadOnlyList<RouteEntry> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeDestinations = new HashSet<Ipv4Cidr>();
        foreach (var route in before)
        {
            beforeDestinations.Add(route.Destination);
        }

        var added = new List<RouteEntry>();
        var seen = new HashSet<Ipv4Cidr>();
        foreach (var route in after)
        {
            if (!beforeDestinations.Contains(route.Destination) && seen.Add(route.Destination))
            {
                added.Add(route);
            }
        }

        return added;
    }

    /// <summary>
    /// For each personal CIDR, collects the VPN routes that overlap it. Personal CIDRs with no
    /// overlap are omitted. Output is ordered by personal CIDR for reproducibility.
    /// </summary>
    public IReadOnlyList<RouteConflict> DetectConflicts(
        IEnumerable<Ipv4Cidr> vpnRoutes,
        IReadOnlyList<CidrEntry> ipList)
    {
        ArgumentNullException.ThrowIfNull(vpnRoutes);
        ArgumentNullException.ThrowIfNull(ipList);

        // Sort VPN routes by start address so we can advance a window instead of rescanning.
        var routes = new List<Ipv4Cidr>(vpnRoutes);
        routes.Sort();

        var personal = new List<Ipv4Cidr>(ipList.Count);
        foreach (var entry in ipList)
        {
            personal.Add(entry.Cidr);
        }

        personal.Sort();

        var conflicts = new List<RouteConflict>();
        var windowStart = 0;

        foreach (var cidr in personal)
        {
            // Everything ending before this personal block can never overlap it (or any later one).
            while (windowStart < routes.Count && routes[windowStart].Last < cidr.First)
            {
                windowStart++;
            }

            List<Ipv4Cidr>? overlapping = null;
            for (var i = windowStart; i < routes.Count && routes[i].First <= cidr.Last; i++)
            {
                if (routes[i].Overlaps(cidr))
                {
                    (overlapping ??= []).Add(routes[i]);
                }
            }

            if (overlapping is not null)
            {
                conflicts.Add(new RouteConflict(cidr, overlapping));
            }
        }

        return conflicts;
    }
}
