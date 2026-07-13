namespace NetworkRoutesConflictResolver.Model;

/// <summary>
/// One personal (ip-list) CIDR that overlaps one or more VPN-added routes.
/// <see cref="ConflictingRoutes"/> holds the intersecting VPN blocks that must be excluded
/// from the personal route so both tunnels can coexist.
/// </summary>
public sealed record RouteConflict(
    Ipv4Cidr PersonalCidr,
    IReadOnlyList<Ipv4Cidr> ConflictingRoutes);
